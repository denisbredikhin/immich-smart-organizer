namespace Immich.SmartOrganizer.Search;

internal sealed record SearchDiscoveryResult(
    IReadOnlyList<DiscoveredAsset> Assets,
    bool IsPartial,
    IReadOnlyList<string> PartialReasons);
