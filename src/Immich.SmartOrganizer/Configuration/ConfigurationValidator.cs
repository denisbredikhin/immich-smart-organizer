using System.Globalization;

namespace Immich.SmartOrganizer.Configuration;

internal static class ConfigurationValidator
{
    public static ConfigurationValidationResult Validate(
        AppConfiguration configuration,
        string configurationPath,
        ICommandEnvironment environment)
    {
        List<string> errors = [];

        ValidateImmich(configuration.Immich, environment, errors);
        ValidateScanner(configuration.Scanner, errors);
        ValidateProcessing(configuration.Processing, errors);
        ValidateCategories(configuration.Categories, errors);
        ValidateLogging(configuration.Logging, errors);
        var stateDatabasePath = ValidateStatePath(
            configuration.State.Path,
            configurationPath,
            errors);

        return new ConfigurationValidationResult(errors, stateDatabasePath);
    }

    private static void ValidateImmich(
        AppConfiguration.ImmichOptions immich,
        ICommandEnvironment environment,
        List<string> errors)
    {
        if (!Uri.TryCreate(immich.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add("immich.url must be an absolute HTTP or HTTPS URL without embedded credentials, query, or fragment.");
        }

        if (string.IsNullOrWhiteSpace(immich.ApiKeyEnv)
            || !IsEnvironmentVariableName(immich.ApiKeyEnv))
        {
            errors.Add("immich.apiKeyEnv must be a valid environment variable name.");
        }
        else if (string.IsNullOrWhiteSpace(environment.GetEnvironmentVariable(immich.ApiKeyEnv)))
        {
            errors.Add($"Environment variable '{immich.ApiKeyEnv}' is not set or is empty.");
        }
    }

    private static void ValidateScanner(AppConfiguration.ScannerOptions scanner, List<string> errors)
    {
        if (scanner.Mode is not ("incremental" or "full"))
        {
            errors.Add("scanner.mode must be 'incremental' or 'full'.");
        }

        ValidateRange(scanner.BatchSize, 1, 500, "scanner.batchSize", errors);
        ValidateRange(scanner.Concurrency, 1, 8, "scanner.concurrency", errors);
        ValidateRange(scanner.LookbackDays, 1, 3650, "scanner.lookbackDays", errors);
    }

    private static void ValidateProcessing(
        AppConfiguration.ProcessingOptions processing,
        List<string> errors)
    {
        if (processing.DryRun is null)
        {
            errors.Add("processing.dryRun cannot be null.");
        }

        ValidateRange(processing.MaxAssetsPerRun, 1, 100_000, "processing.maxAssetsPerRun", errors);
        ValidateRange(
            processing.MaxArchiveActionsPerRun,
            0,
            1_000,
            "processing.maxArchiveActionsPerRun",
            errors);
        ValidateRange(processing.MaxErrorsPerRun, 1, 1_000, "processing.maxErrorsPerRun", errors);

        if (processing.AmbiguousAssetPolicy is not ("review" or "skip"))
        {
            errors.Add("processing.ambiguousAssetPolicy must be 'review' or 'skip'.");
        }
    }

    private static void ValidateCategories(
        List<AppConfiguration.CategoryOptions> categories,
        List<string> errors)
    {
        if (categories.Count == 0)
        {
            errors.Add("At least one category is required.");
            return;
        }

        HashSet<string> categoryNames = new(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < categories.Count; index++)
        {
            var category = categories[index];
            var prefix = $"categories[{index}]";

            if (string.IsNullOrWhiteSpace(category.Name))
            {
                errors.Add($"{prefix}.name is required.");
            }
            else if (category.Name != category.Name.Trim())
            {
                errors.Add($"{prefix}.name cannot start or end with whitespace.");
            }
            else if (!categoryNames.Add(category.Name))
            {
                errors.Add($"Category name '{category.Name}' is duplicated.");
            }

            ValidateSearch(category.Search, prefix, errors);
            ValidateMatch(category.Match, prefix, errors);
            if (category.Enabled)
            {
                ValidateMatchQueries(category.Search, category.Match, prefix, errors);
            }

            ValidateActions(category.Actions, prefix, errors);
        }
    }

    private static void ValidateMatchQueries(
        AppConfiguration.SearchOptions search,
        AppConfiguration.MatchOptions match,
        string prefix,
        List<string> errors)
    {
        var contextualQueries = search.Contextual;
        var ocrQueries = search.Ocr;
        ValidateConditionQueries(match.All, $"{prefix}.match.all", contextualQueries, ocrQueries, errors);
        ValidateConditionQueries(match.Any, $"{prefix}.match.any", contextualQueries, ocrQueries, errors);
        ValidateConditionQueries(match.Exclude, $"{prefix}.match.exclude", contextualQueries, ocrQueries, errors);
        if (match.Not is not null)
        {
            ValidateConditionQueries(
                [match.Not],
                $"{prefix}.match.not",
                contextualQueries,
                ocrQueries,
                errors);
        }
    }

    private static void ValidateConditionQueries(
        List<AppConfiguration.MatchCondition> conditions,
        string path,
        IReadOnlyList<string> contextualQueries,
        IReadOnlyList<string> ocrQueries,
        List<string> errors)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var conditionPath = $"{path}[{index}]";
            ValidateConditionQueryTerms(
                condition.ContextualAny,
                $"{conditionPath}.contextualAny",
                "search.contextual",
                contextualQueries,
                errors);
            ValidateConditionQueryTerms(
                condition.OcrAny,
                $"{conditionPath}.ocrAny",
                "search.ocr",
                ocrQueries,
                errors);
            ValidateConditionQueryTerms(
                condition.OcrAll,
                $"{conditionPath}.ocrAll",
                "search.ocr",
                ocrQueries,
                errors);
        }
    }

    private static void ValidateConditionQueryTerms(
        IReadOnlyList<string>? terms,
        string conditionPath,
        string searchPath,
        IReadOnlyList<string> searchQueries,
        List<string> errors)
    {
        if (terms is null)
        {
            return;
        }

        foreach (var term in terms)
        {
            if (!string.IsNullOrWhiteSpace(term)
                && !searchQueries.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{conditionPath} term '{term}' must also appear in {searchPath}; "
                    + "classification uses exact search-query evidence.");
            }
        }
    }

    private static void ValidateSearch(
        AppConfiguration.SearchOptions search,
        string prefix,
        List<string> errors)
    {
        ValidateTerms(search.Contextual, $"{prefix}.search.contextual", errors, required: false);
        ValidateTerms(search.Ocr, $"{prefix}.search.ocr", errors, required: false);

        if (search.Contextual is not { Count: > 0 } && search.Ocr is not { Count: > 0 })
        {
            errors.Add($"{prefix}.search must contain at least one contextual or OCR query.");
        }
    }

    private static void ValidateMatch(
        AppConfiguration.MatchOptions match,
        string prefix,
        List<string> errors)
    {
        var hasCondition = false;
        ValidateConditionList(match.All, $"{prefix}.match.all", errors, ref hasCondition, countsAsMatch: true);
        ValidateConditionList(match.Any, $"{prefix}.match.any", errors, ref hasCondition, countsAsMatch: true);
        ValidateConditionList(match.Exclude, $"{prefix}.match.exclude", errors, ref hasCondition, countsAsMatch: false);

        if (match.Not is not null)
        {
            ValidateCondition(match.Not, $"{prefix}.match.not", errors);
        }

        if (!hasCondition)
        {
            errors.Add($"{prefix}.match must contain at least one positive condition in 'all' or 'any'.");
        }
    }

    private static void ValidateConditionList(
        List<AppConfiguration.MatchCondition> conditions,
        string path,
        List<string> errors,
        ref bool hasCondition,
        bool countsAsMatch)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            hasCondition |= countsAsMatch;
            var condition = conditions[index];
            ValidateCondition(condition, $"{path}[{index}]", errors);
        }
    }

    private static void ValidateCondition(
        AppConfiguration.MatchCondition condition,
        string path,
        List<string> errors)
    {
        var configuredOperators = 0;
        ValidateOperator(condition.ContextualAny, $"{path}.contextualAny", errors, ref configuredOperators);
        ValidateOperator(condition.OcrAny, $"{path}.ocrAny", errors, ref configuredOperators);
        ValidateOperator(condition.OcrAll, $"{path}.ocrAll", errors, ref configuredOperators);

        if (configuredOperators != 1)
        {
            errors.Add($"{path} must specify exactly one supported condition operator.");
        }
    }

    private static void ValidateOperator(
        List<string>? terms,
        string path,
        List<string> errors,
        ref int configuredOperators)
    {
        if (terms is null)
        {
            return;
        }

        configuredOperators++;
        ValidateTerms(terms, path, errors, required: true);
    }

    private static void ValidateActions(
        AppConfiguration.ActionOptions actions,
        string prefix,
        List<string> errors)
    {
        if (actions.Album is not null && string.IsNullOrWhiteSpace(actions.Album))
        {
            errors.Add($"{prefix}.actions.album cannot be empty.");
        }

        if (actions.Tag is not null && string.IsNullOrWhiteSpace(actions.Tag))
        {
            errors.Add($"{prefix}.actions.tag cannot be empty.");
        }
    }

    private static void ValidateLogging(AppConfiguration.LoggingOptions logging, List<string> errors)
    {
        string[] levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];
        if (!levels.Contains(logging.Level, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("logging.level must be one of Trace, Debug, Information, Warning, Error, Critical, or None.");
        }

        if (!string.Equals(logging.Format, "text", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("logging.format must be 'text'.");
        }
    }

    private static string? ValidateStatePath(
        string configuredPath,
        string configurationPath,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add("state.path is required and cannot be empty.");
            return null;
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
            errors.Add($"state.path is invalid: {exception.Message}");
            return null;
        }

        if (Directory.Exists(fullPath))
        {
            errors.Add("state.path must point to a database file, not a directory.");
            return null;
        }

        var stateDirectory = Path.GetDirectoryName(fullPath);
        if (stateDirectory is null || !Directory.Exists(stateDirectory))
        {
            errors.Add($"The directory for state.path does not exist: '{stateDirectory ?? fullPath}'.");
            return null;
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
            errors.Add($"The directory or file for state.path is not writable: '{fullPath}'.");
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            errors.Add($"The directory or file for state.path is not writable: '{fullPath}'.");
            return null;
        }

        return fullPath;
    }

    private static void ValidateTerms(
        List<string>? terms,
        string path,
        List<string> errors,
        bool required)
    {
        if (terms is null)
        {
            errors.Add($"{path} cannot be null.");
            return;
        }

        if (required && terms.Count == 0)
        {
            errors.Add($"{path} must contain at least one term.");
        }

        for (var index = 0; index < terms.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(terms[index]))
            {
                errors.Add($"{path}[{index}] cannot be empty.");
            }
        }
    }

    private static void ValidateRange(int value, int minimum, int maximum, string name, List<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name} must be between {minimum} and {maximum}."));
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
