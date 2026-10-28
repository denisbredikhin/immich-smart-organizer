namespace Immich.SmartOrganizer.Search;

internal sealed record DiscoveredAsset(
    string Id,
    IReadOnlyList<CandidateEvidence> Evidence);
