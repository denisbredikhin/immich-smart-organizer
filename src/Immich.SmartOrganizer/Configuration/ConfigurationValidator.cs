using System.Globalization;

namespace Immich.SmartOrganizer.Configuration;

internal static class ConfigurationValidator
{
    public static ConfigurationValidationResult Validate(
        AppConfiguration configuration,
        string configurationPath,
        ICommandEnvironment environment)
    {
        string[] errors =
        [
            .. ValidateImmich(configuration.Immich, environment),
            .. ValidateScanner(configuration.Scanner),
            .. ValidateProcessing(configuration.Processing),
            .. ValidateCategories(configuration.Categories),
            .. ValidateLogging(configuration.Logging),
        ];
        var stateValidation = ValidateStatePath(
            configuration.State.Path,
            configurationPath);

        return new ConfigurationValidationResult(
            [.. errors, .. stateValidation.Errors],
            stateValidation.StateDatabasePath);
    }

    private static IEnumerable<string> ValidateImmich(
        AppConfiguration.ImmichOptions immich,
        ICommandEnvironment environment)
    {
        if (!Uri.TryCreate(immich.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            yield return "immich.url must be an absolute HTTP or HTTPS URL without embedded credentials, query, or fragment.";
        }

        if (string.IsNullOrWhiteSpace(immich.ApiKeyEnv)
            || !IsEnvironmentVariableName(immich.ApiKeyEnv))
        {
            yield return "immich.apiKeyEnv must be a valid environment variable name.";
        }
        else if (string.IsNullOrWhiteSpace(environment.GetEnvironmentVariable(immich.ApiKeyEnv)))
        {
            yield return $"Environment variable '{immich.ApiKeyEnv}' is not set or is empty.";
        }
    }

    private static IEnumerable<string> ValidateScanner(AppConfiguration.ScannerOptions scanner)
    {
        if (scanner.Mode is not ("incremental" or "full"))
        {
            yield return "scanner.mode must be 'incremental' or 'full'.";
        }

        foreach (var error in ValidateRange(scanner.BatchSize, 1, 500, "scanner.batchSize")
            .Concat(ValidateRange(scanner.Concurrency, 1, 8, "scanner.concurrency"))
            .Concat(ValidateRange(scanner.LookbackDays, 1, 3650, "scanner.lookbackDays")))
        {
            yield return error;
        }
    }

    private static IEnumerable<string> ValidateProcessing(
        AppConfiguration.ProcessingOptions processing)
    {
        if (processing.DryRun is null)
        {
            yield return "processing.dryRun cannot be null.";
        }

        foreach (var error in ValidateRange(processing.MaxAssetsPerRun, 1, 100_000, "processing.maxAssetsPerRun")
            .Concat(ValidateRange(processing.MaxArchiveActionsPerRun, 0, 1_000, "processing.maxArchiveActionsPerRun"))
            .Concat(ValidateRange(processing.MaxErrorsPerRun, 1, 1_000, "processing.maxErrorsPerRun")))
        {
            yield return error;
        }

        if (processing.AmbiguousAssetPolicy is not ("review" or "skip"))
        {
            yield return "processing.ambiguousAssetPolicy must be 'review' or 'skip'.";
        }
    }

    private static IEnumerable<string> ValidateCategories(
        List<AppConfiguration.CategoryOptions> categories)
    {
        if (categories.Count == 0)
        {
            yield return "At least one category is required.";
            yield break;
        }

        HashSet<string> categoryNames = new(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < categories.Count; index++)
        {
            var category = categories[index];
            var prefix = $"categories[{index}]";

            if (string.IsNullOrWhiteSpace(category.Name))
            {
                yield return $"{prefix}.name is required.";
            }
            else if (category.Name != category.Name.Trim())
            {
                yield return $"{prefix}.name cannot start or end with whitespace.";
            }
            else if (!categoryNames.Add(category.Name))
            {
                yield return $"Category name '{category.Name}' is duplicated.";
            }

            foreach (var error in ValidateSearch(category.Search, prefix)
                .Concat(ValidateMatch(category.Match, prefix)))
            {
                yield return error;
            }

            if (category.Enabled)
            {
                foreach (var error in ValidateMatchQueries(category.Search, category.Match, prefix))
                {
                    yield return error;
                }
            }

            foreach (var error in ValidateActions(category.Actions, prefix))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> ValidateMatchQueries(
        AppConfiguration.SearchOptions search,
        AppConfiguration.MatchOptions match,
        string prefix)
    {
        var contextualQueries = search.Contextual;
        var ocrQueries = search.Ocr;
        foreach (var error in ValidateConditionQueries(match.All, $"{prefix}.match.all", contextualQueries, ocrQueries)
            .Concat(ValidateConditionQueries(match.Any, $"{prefix}.match.any", contextualQueries, ocrQueries))
            .Concat(ValidateConditionQueries(match.Exclude, $"{prefix}.match.exclude", contextualQueries, ocrQueries)))
        {
            yield return error;
        }

        if (match.Not is not null)
        {
            foreach (var error in ValidateConditionQueries(
                [match.Not],
                $"{prefix}.match.not",
                contextualQueries,
                ocrQueries))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> ValidateConditionQueries(
        List<AppConfiguration.MatchCondition> conditions,
        string path,
        IReadOnlyList<string> contextualQueries,
        IReadOnlyList<string> ocrQueries)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var conditionPath = $"{path}[{index}]";
            foreach (var error in ValidateConditionQueryTerms(
                condition.ContextualAny,
                $"{conditionPath}.contextualAny",
                "search.contextual",
                contextualQueries)
                .Concat(ValidateConditionQueryTerms(
                    condition.OcrAny,
                    $"{conditionPath}.ocrAny",
                    "search.ocr",
                    ocrQueries))
                .Concat(ValidateConditionQueryTerms(
                    condition.OcrAll,
                    $"{conditionPath}.ocrAll",
                    "search.ocr",
                    ocrQueries)))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> ValidateConditionQueryTerms(
        IReadOnlyList<string>? terms,
        string conditionPath,
        string searchPath,
        IReadOnlyList<string> searchQueries)
    {
        if (terms is null)
        {
            yield break;
        }

        foreach (var term in terms)
        {
            if (!string.IsNullOrWhiteSpace(term)
                && !searchQueries.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"{conditionPath} term '{term}' must also appear in {searchPath}; "
                    + "classification uses exact search-query evidence.";
            }
        }
    }

    private static IEnumerable<string> ValidateSearch(
        AppConfiguration.SearchOptions search,
        string prefix)
    {
        foreach (var error in ValidateTerms(search.Contextual, $"{prefix}.search.contextual", required: false)
            .Concat(ValidateTerms(search.Ocr, $"{prefix}.search.ocr", required: false)))
        {
            yield return error;
        }

        if (search.Contextual is not { Count: > 0 } && search.Ocr is not { Count: > 0 })
        {
            yield return $"{prefix}.search must contain at least one contextual or OCR query.";
        }
    }

    private static IEnumerable<string> ValidateMatch(
        AppConfiguration.MatchOptions match,
        string prefix)
    {
        foreach (var error in ValidateConditionList(match.All, $"{prefix}.match.all")
            .Concat(ValidateConditionList(match.Any, $"{prefix}.match.any"))
            .Concat(ValidateConditionList(match.Exclude, $"{prefix}.match.exclude")))
        {
            yield return error;
        }

        if (match.Not is not null)
        {
            foreach (var error in ValidateCondition(match.Not, $"{prefix}.match.not"))
            {
                yield return error;
            }
        }

        if (match.All.Count == 0 && match.Any.Count == 0)
        {
            yield return $"{prefix}.match must contain at least one positive condition in 'all' or 'any'.";
        }
    }

    private static IEnumerable<string> ValidateConditionList(
        List<AppConfiguration.MatchCondition> conditions,
        string path)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            foreach (var error in ValidateCondition(condition, $"{path}[{index}]"))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> ValidateCondition(
        AppConfiguration.MatchCondition condition,
        string path)
    {
        foreach (var error in ValidateOperator(condition.ContextualAny, $"{path}.contextualAny")
            .Concat(ValidateOperator(condition.OcrAny, $"{path}.ocrAny"))
            .Concat(ValidateOperator(condition.OcrAll, $"{path}.ocrAll")))
        {
            yield return error;
        }

        var configuredOperators = (condition.ContextualAny is not null ? 1 : 0)
            + (condition.OcrAny is not null ? 1 : 0)
            + (condition.OcrAll is not null ? 1 : 0);
        if (configuredOperators != 1)
        {
            yield return $"{path} must specify exactly one supported condition operator.";
        }
    }

    private static IEnumerable<string> ValidateOperator(
        List<string>? terms,
        string path)
    {
        if (terms is null)
        {
            return [];
        }

        return ValidateTerms(terms, path, required: true);
    }

    private static IEnumerable<string> ValidateActions(
        AppConfiguration.ActionOptions actions,
        string prefix)
    {
        if (actions.Album is not null && string.IsNullOrWhiteSpace(actions.Album))
        {
            yield return $"{prefix}.actions.album cannot be empty.";
        }

        if (actions.Tag is not null && string.IsNullOrWhiteSpace(actions.Tag))
        {
            yield return $"{prefix}.actions.tag cannot be empty.";
        }
        else if (actions.Tag is not null && actions.Tag.Split('/').Any(string.IsNullOrWhiteSpace))
        {
            yield return $"{prefix}.actions.tag must contain non-empty hierarchy segments.";
        }
    }

    private static IEnumerable<string> ValidateLogging(AppConfiguration.LoggingOptions logging)
    {
        string[] levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];
        if (!levels.Contains(logging.Level, StringComparer.OrdinalIgnoreCase))
        {
            yield return "logging.level must be one of Trace, Debug, Information, Warning, Error, Critical, or None.";
        }

        if (!string.Equals(logging.Format, "text", StringComparison.OrdinalIgnoreCase))
        {
            yield return "logging.format must be 'text'.";
        }
    }

    private static ConfigurationValidationResult ValidateStatePath(
        string configuredPath,
        string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new ConfigurationValidationResult(["state.path is required and cannot be empty."], null);
        }

        string fullPath;
        try
        {
            var configurationDirectory = Path.GetDirectoryName(configurationPath)
                ?? Directory.GetCurrentDirectory();
            fullPath = Path.GetFullPath(configuredPath, configurationDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ConfigurationValidationResult([$"state.path is invalid: {exception.Message}"], null);
        }

        if (Directory.Exists(fullPath))
        {
            return new ConfigurationValidationResult(["state.path must point to a database file, not a directory."], null);
        }

        var stateDirectory = Path.GetDirectoryName(fullPath);
        if (stateDirectory is null || !Directory.Exists(stateDirectory))
        {
            return new ConfigurationValidationResult(
                [$"The directory for state.path does not exist: '{stateDirectory ?? fullPath}'."],
                null);
        }

        try
        {
            if (File.Exists(fullPath))
            {
                using FileStream database = new(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            }

            var probePath = Path.Combine(stateDirectory, $".immich-smart-organizer-{Guid.NewGuid():N}.tmp");
            using FileStream probe = new(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return new ConfigurationValidationResult(
                [$"The directory or file for state.path is not writable: '{fullPath}'."],
                null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ConfigurationValidationResult(
                [$"The directory or file for state.path is not writable: '{fullPath}'."],
                null);
        }

        return new ConfigurationValidationResult([], fullPath);
    }

    private static IEnumerable<string> ValidateTerms(
        List<string>? terms,
        string path,
        bool required)
    {
        if (terms is null)
        {
            yield return $"{path} cannot be null.";
            yield break;
        }

        if (required && terms.Count == 0)
        {
            yield return $"{path} must contain at least one term.";
        }

        for (var index = 0; index < terms.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(terms[index]))
            {
                yield return $"{path}[{index}] cannot be empty.";
            }
        }
    }

    private static IEnumerable<string> ValidateRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{name} must be between {minimum} and {maximum}.");
        }
    }

    private static bool IsEnvironmentVariableName(string value)
    {
        if (value.Length == 0 || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}

internal sealed record ConfigurationValidationResult(
    IReadOnlyList<string> Errors,
    string? StateDatabasePath);
