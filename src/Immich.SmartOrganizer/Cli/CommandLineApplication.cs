using System.Globalization;
using System.Text;
using Immich.SmartOrganizer.Actions;
using Immich.SmartOrganizer.Classification;
using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Immich;
using Immich.SmartOrganizer.Search;

namespace Immich.SmartOrganizer.Cli;

internal static class CommandLineApplication
{
    private const int Success = 0;
    private const int GeneralFailure = 1;
    private const int InvalidArguments = 2;
    private const int ConnectivityFailure = 3;
    private const int IncompatibleApi = 4;
    private const int PartialScan = 5;
    private const int MaximumScanLimit = 100_000;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        ICommandEnvironment environment,
        Func<HttpClient>? httpClientFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (args is [] or ["--help"] or ["-h"])
        {
            WriteHelp(output);
            return Success;
        }

        if (args is ["--version"] or ["-v"])
        {
            output.WriteLine(ApplicationVersion.Current);
            return Success;
        }

        try
        {
            return args[0] switch
            {
                "init" => await RunInitAsync(args, output, error, environment, cancellationToken),
                "config" => await RunConfigAsync(args, output, error, environment, cancellationToken),
                "scan" => await RunScanAsync(args, output, error, environment, httpClientFactory, cancellationToken),
                _ => WriteInvalidArgument(error, args[0])
            };
        }
        catch (ImmichApiException exception)
        {
            error.WriteLine($"Immich API error: {exception.Message}");
            return exception.Kind switch
            {
                ImmichApiFailureKind.Authentication or ImmichApiFailureKind.Connectivity or ImmichApiFailureKind.Transient
                    => ConnectivityFailure,
                ImmichApiFailureKind.IncompatibleApi => IncompatibleApi,
                _ => GeneralFailure
            };
        }
        catch (ConfigurationException exception)
        {
            error.WriteLine($"Configuration error: {exception.Message}");
            return InvalidArguments;
        }
        catch (IOException exception)
        {
            error.WriteLine($"I/O error: {exception.Message}");
            return GeneralFailure;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"Access error: {exception.Message}");
            return GeneralFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled.");
            return GeneralFailure;
        }
    }

    private static async Task<int> RunInitAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        ICommandEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!TryParseInit(args, out var outputPath, out var parseError))
        {
            error.WriteLine($"Error: {parseError}");
            return InvalidArguments;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(
                outputPath ?? ConfigurationLoader.DefaultConfigurationFileName,
                environment.CurrentDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error.WriteLine($"Error: invalid output path: {exception.Message}");
            return InvalidArguments;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            error.WriteLine($"Error: output directory does not exist: '{directory ?? fullPath}'.");
            return InvalidArguments;
        }

        var template = await ConfigurationTemplate.ReadAsync(cancellationToken);
        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            error.WriteLine($"Error: refusing to overwrite existing file '{fullPath}'.");
            return InvalidArguments;
        }

        await using (stream)
        {
            await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(template.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        output.WriteLine($"Created configuration: {fullPath}");
        return Success;
    }

    private static async Task<int> RunConfigAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        ICommandEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2 || args[1] != "validate")
        {
            error.WriteLine("Error: usage: config validate [--config PATH]");
            return InvalidArguments;
        }

        if (!TryParseConfigPath(args, 2, out var configPath, out var parseError))
        {
            error.WriteLine($"Error: {parseError}");
            return InvalidArguments;
        }

        var loaded = await ConfigurationLoader.LoadAsync(configPath, environment, cancellationToken);
        output.WriteLine("Configuration is valid.");
        output.WriteLine($"Config file: {loaded.ConfigurationPath}");
        output.WriteLine($"State database: {loaded.StateDatabasePath}");
        return Success;
    }

    private static async Task<int> RunScanAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        ICommandEnvironment environment,
        Func<HttpClient>? httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (!TryParseScanOptions(args, out var options, out var parseError))
        {
            error.WriteLine($"Error: {parseError}");
            return InvalidArguments;
        }

        var loaded = await ConfigurationLoader.LoadAsync(options.ConfigurationPath, environment, cancellationToken);
        if (options.Category is not null
            && !loaded.Configuration.Categories.Any(category =>
                string.Equals(category.Name, options.Category, StringComparison.OrdinalIgnoreCase)))
        {
            error.WriteLine($"Error: category '{options.Category}' is not configured.");
            return InvalidArguments;
        }

        var configuration = loaded.Configuration;
        var selectedCategories = configuration.Categories;
        if (options.Category is not null)
        {
            selectedCategories = selectedCategories
                .Where(category => string.Equals(
                    category.Name,
                    options.Category,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var apiKey = environment.GetEnvironmentVariable(configuration.Immich.ApiKeyEnv)!;
        using var httpClient = httpClientFactory?.Invoke() ?? ImmichApiClient.CreateDefaultHttpClient();
        ImmichApiClient client = new(httpClient, configuration.Immich.Url, apiKey);

        var version = await client.GetServerVersionAsync(cancellationToken);
        var scanner = configuration.Scanner;
        var processing = configuration.Processing;
        var maxAssets = Math.Min(
            processing.MaxAssetsPerRun,
            options.Limit ?? int.MaxValue);
        CandidateDiscovery discovery = new(client);
        var result = await discovery.DiscoverAsync(
            selectedCategories,
            scanner.BatchSize,
            maxAssets,
            scanner.IncludeVideos,
            cancellationToken);

        var classifications = ClassificationEngine.Classify(
            result.Assets,
            selectedCategories,
            processing.AmbiguousAssetPolicy);
        var actionPlan = DryRunActionPlanner.Create(
            classifications,
            processing.MaxArchiveActionsPerRun);
        var dryRun = options.DryRun || processing.DryRun != false;
        output.WriteLine($"Immich version: {version}");
        output.WriteLine(options.Full
            ? "Scan mode: full re-evaluation of discoverable candidates"
            : $"Scan mode: candidate discovery (configured mode: {scanner.Mode}; incremental state is not applied in this phase)");
        output.WriteLine($"Dry run: {dryRun.ToString().ToLowerInvariant()}");
        output.WriteLine($"Candidates discovered: {result.Assets.Count}");
        output.WriteLine($"Assets evaluated: {classifications.Count}");
        output.WriteLine("Category matches:");
        foreach (var category in selectedCategories.Where(static category => category.Enabled))
        {
            var count = classifications.Count(classification =>
                classification.MatchingCategories.Any(match =>
                    string.Equals(match.Category.Name, category.Name, StringComparison.OrdinalIgnoreCase)));
            output.WriteLine($"  {category.Name}: {count}");
        }

        output.WriteLine($"Needs review: {classifications.Count(static item => item.Status == ClassificationStatus.NeedsReview)}");
        output.WriteLine($"Ambiguous candidates skipped: {classifications.Count(static item => item.Status == ClassificationStatus.SkippedAsAmbiguous)}");
        output.WriteLine($"Unmatched: {classifications.Count(static item => item.Status == ClassificationStatus.Unmatched)}");
        output.WriteLine($"Album additions planned: {actionPlan.AlbumAdditionsPlanned}");
        output.WriteLine($"Tag applications planned: {actionPlan.TagApplicationsPlanned}");
        output.WriteLine($"Archivals planned: {actionPlan.ArchiveActionsPlanned}");
        if (actionPlan.ArchiveActionsLimited > 0)
        {
            output.WriteLine(
                $"Archivals not planned because processing.maxArchiveActionsPerRun was reached: {actionPlan.ArchiveActionsLimited}");
        }

        output.WriteLine($"Coverage: {(result.IsPartial ? "partial" : "search windows completed")}");
        foreach (var reason in result.PartialReasons)
        {
            output.WriteLine($"Partial coverage: {reason}");
        }

        output.WriteLine("Execution: planning only; library actions are not executed in this phase.");
        output.WriteLine("Mutations executed: 0");
        output.WriteLine("No library changes were made.");
        if (options.Verbose)
        {
            WriteClassificationDetails(output, classifications, actionPlan);
        }

        return result.IsPartial ? PartialScan : Success;
    }

    private static void WriteClassificationDetails(
        TextWriter output,
        IReadOnlyList<ClassificationEngine.AssetClassification> classifications,
        DryRunActionPlanner.ActionPlan actionPlan)
    {
        Dictionary<string, DryRunActionPlanner.PlannedAssetActions> plannedActions =
            actionPlan.Actions.ToDictionary(static action => action.AssetId, StringComparer.Ordinal);

        foreach (var classification in classifications)
        {
            switch (classification.Status)
            {
                case ClassificationStatus.Classified:
                    var match = classification.MatchingCategories[0];
                    output.WriteLine(
                        $"Candidate {classification.Asset.Id}: matched category '{match.Category.Name}'"
                        + $" using {string.Join(", ", match.MatchedConditions)}.");
                    if (plannedActions.TryGetValue(classification.Asset.Id, out var actions))
                    {
                        output.WriteLine($"  Planned actions: {FormatPlannedActions(actions)}");
                    }

                    break;
                case ClassificationStatus.NeedsReview:
                    output.WriteLine(
                        $"Candidate {classification.Asset.Id}: needs review; matched categories: "
                        + string.Join(", ", classification.MatchingCategories.Select(
                            static item => item.Category.Name)));
                    break;
                case ClassificationStatus.SkippedAsAmbiguous:
                    output.WriteLine(
                        $"Candidate {classification.Asset.Id}: skipped because multiple categories matched: "
                        + string.Join(", ", classification.MatchingCategories.Select(
                            static item => item.Category.Name)));
                    break;
                case ClassificationStatus.Unmatched:
                    output.WriteLine($"Candidate {classification.Asset.Id}: no category matched.");
                    foreach (var evaluation in classification.CategoryEvaluations)
                    {
                        var reason = evaluation.UnmetConditions.Count == 0
                            ? "no configured query evidence"
                            : string.Join("; ", evaluation.UnmetConditions);
                        output.WriteLine($"  {evaluation.Category.Name}: {reason}");
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unknown classification status '{classification.Status}'.");
            }
        }
    }

    private static string FormatPlannedActions(DryRunActionPlanner.PlannedAssetActions actions)
    {
        List<string> descriptions = [];
        if (actions.AlbumName is not null)
        {
            descriptions.Add($"add to album '{actions.AlbumName}'");
        }

        if (actions.TagName is not null)
        {
            descriptions.Add($"apply tag '{actions.TagName}'");
        }

        if (actions.Archive)
        {
            descriptions.Add("archive");
        }
        else if (actions.ArchiveLimited)
        {
            descriptions.Add("archive not planned because the per-run limit was reached");
        }

        return descriptions.Count == 0 ? "none" : string.Join(", ", descriptions);
    }

    private static bool TryParseInit(string[] args, out string? outputPath, out string? error)
    {
        outputPath = null;
        error = null;

        if (args.Length == 1)
        {
            return true;
        }

        if (args.Length == 3 && args[1] == "--output" && !string.IsNullOrWhiteSpace(args[2]))
        {
            outputPath = args[2];
            return true;
        }

        error = "usage: init [--output PATH]";
        return false;
    }

    private static bool TryParseConfigPath(
        string[] args,
        int firstOptionIndex,
        out string? configPath,
        out string? error)
    {
        configPath = null;
        error = null;

        for (var index = firstOptionIndex; index < args.Length; index++)
        {
            if (args[index] != "--config" || index + 1 >= args.Length
                || string.IsNullOrWhiteSpace(args[index + 1]) || configPath is not null)
            {
                error = "usage: config validate [--config PATH]";
                return false;
            }

            configPath = args[++index];
        }

        return true;
    }

    private static bool TryParseScanOptions(
        string[] args,
        out ScanOptions options,
        out string? error)
    {
        string? configurationPath = null;
        string? category = null;
        int? limit = null;
        var dryRun = false;
        var full = false;
        var verbose = false;
        error = null;
        HashSet<string> seenOptions = new(StringComparer.Ordinal);

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!seenOptions.Add(option))
            {
                error = $"option '{option}' cannot be specified more than once.";
                options = default;
                return false;
            }

            switch (option)
            {
                case "--config":
                    if (!TryReadOptionValue(args, ref index, out configurationPath))
                    {
                        error = "option '--config' requires a path.";
                    }

                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--full":
                    full = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--limit":
                    if (!TryReadOptionValue(args, ref index, out var limitText)
                        || !int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLimit)
                        || parsedLimit < 1
                        || parsedLimit > MaximumScanLimit)
                    {
                        error = $"option '--limit' must be an integer between 1 and {MaximumScanLimit}.";
                    }
                    else
                    {
                        limit = parsedLimit;
                    }

                    break;
                case "--category":
                    if (!TryReadOptionValue(args, ref index, out category))
                    {
                        error = "option '--category' requires a name.";
                    }

                    break;
                default:
                    error = $"unsupported scan option '{option}'.";
                    break;
            }

            if (error is not null)
            {
                options = default;
                return false;
            }
        }

        options = new ScanOptions(configurationPath, dryRun, full, limit, category, verbose);
        return true;
    }

    private static bool TryReadOptionValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int WriteInvalidArgument(TextWriter error, string argument)
    {
        error.WriteLine($"Error: unsupported argument '{argument}'.");
        error.WriteLine("Use --help to see available options.");
        return InvalidArguments;
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Immich Smart Organizer");
        output.WriteLine("Usage:");
        output.WriteLine("  immich-smart-organizer --help");
        output.WriteLine("  immich-smart-organizer --version");
        output.WriteLine("  immich-smart-organizer init [--output PATH]");
        output.WriteLine("  immich-smart-organizer config validate [--config PATH]");
        output.WriteLine("  immich-smart-organizer scan [--config PATH] [--dry-run] [--full] [--limit N] [--category NAME] [--verbose]");
    }

    private readonly record struct ScanOptions(
        string? ConfigurationPath,
        bool DryRun,
        bool Full,
        int? Limit,
        string? Category,
        bool Verbose);
}
