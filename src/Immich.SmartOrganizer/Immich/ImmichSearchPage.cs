namespace Immich.SmartOrganizer.Immich;

internal sealed record ImmichSearchPage(
    IReadOnlyList<string> AssetIds,
    int Count,
    string? NextCursor,
    string? NextPage);
