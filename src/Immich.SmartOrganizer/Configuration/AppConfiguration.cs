namespace Immich.SmartOrganizer.Configuration;

internal sealed class AppConfiguration
{
    public required ImmichOptions Immich { get; set; }

    public ScannerOptions Scanner { get; set; } = new();

    public ProcessingOptions Processing { get; set; } = new();

    public required StateOptions State { get; set; }

    public required List<CategoryOptions> Categories { get; set; }

    public LoggingOptions Logging { get; set; } = new();

    internal sealed class ImmichOptions
    {
        public required string Url { get; set; }

        public required string ApiKeyEnv { get; set; }
    }

    internal sealed class ScannerOptions
    {
        public string Mode { get; set; } = "incremental";

        public int BatchSize { get; set; } = 100;

        public int Concurrency { get; set; } = 2;

        public bool IncludeArchived { get; set; } = true;

        public bool IncludeExternalLibraries { get; set; } = true;

        public bool IncludeVideos { get; set; }

        public int LookbackDays { get; set; } = 7;
    }

    internal sealed class ProcessingOptions
    {
        public bool? DryRun { get; set; } = true;

        public int MaxAssetsPerRun { get; set; } = 1000;

        public int MaxArchiveActionsPerRun { get; set; } = 25;

        public int MaxErrorsPerRun { get; set; } = 20;

        public string AmbiguousAssetPolicy { get; set; } = "review";
    }

    internal sealed class StateOptions
    {
        public string Path { get; set; } = "./state.db";
    }

    internal sealed class CategoryOptions
    {
        public required string Name { get; set; }

        public bool Enabled { get; set; } = true;

        public required SearchOptions Search { get; set; }

        public required MatchOptions Match { get; set; }

        public ActionOptions Actions { get; set; } = new();
    }

    internal sealed class SearchOptions
    {
        public List<string> Contextual { get; set; } = [];

        public List<string> Ocr { get; set; } = [];
    }

    internal sealed class MatchOptions
    {
        public List<MatchCondition> All { get; set; } = [];

        public List<MatchCondition> Any { get; set; } = [];

        public MatchCondition? Not { get; set; }

        public List<MatchCondition> Exclude { get; set; } = [];
    }

    internal sealed class MatchCondition
    {
        public List<string>? ContextualAny { get; set; }

        public List<string>? OcrAny { get; set; }

        public List<string>? OcrAll { get; set; }
    }

    internal sealed class ActionOptions
    {
        public string? Album { get; set; }

        public string? Tag { get; set; }

        public bool Archive { get; set; }
    }

    internal sealed class LoggingOptions
    {
        public string Level { get; set; } = "Information";

        public string Format { get; set; } = "text";
    }
}
