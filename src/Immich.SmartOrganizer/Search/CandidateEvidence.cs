namespace Immich.SmartOrganizer.Search;

internal sealed record CandidateEvidence(
    string Category,
    SearchEvidenceKind Kind,
    string Query);
