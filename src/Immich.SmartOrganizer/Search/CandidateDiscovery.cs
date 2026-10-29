using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Immich;

namespace Immich.SmartOrganizer.Search;

internal sealed class CandidateDiscovery(ImmichApiClient client)
{
    public async Task<SearchDiscoveryResult> DiscoverAsync(
        IReadOnlyList<AppConfiguration.CategoryOptions> categories,
        int batchSize,
        int maxAssets,
        bool includeVideos = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be between 1 and 500.");
        }

        if (maxAssets < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAssets), maxAssets, "Maximum assets must be at least 1.");
        }

        List<SearchPlan> plans = [];
        foreach (var category in categories)
        {
            if (!category.Enabled)
            {
                continue;
            }

            plans.AddRange(category.Search.Contextual
                .Where(static query => !string.IsNullOrWhiteSpace(query))
                .Select(query => new SearchPlan(category.Name, SearchEvidenceKind.Contextual, query)));
            plans.AddRange(category.Search.Ocr
                .Where(static query => !string.IsNullOrWhiteSpace(query))
                .Select(query => new SearchPlan(category.Name, SearchEvidenceKind.Ocr, query)));
        }

        CandidateAccumulator candidates = new(maxAssets);
        List<string> partialReasons = [];
        for (var index = 0; index < plans.Count; index++)
        {
            if (candidates.Count >= maxAssets)
            {
                AddPartialReason(partialReasons, "The configured asset limit stopped remaining search requests.");
                break;
            }

            var plan = plans[index];
            var effectiveSize = Math.Min(batchSize, maxAssets - candidates.Count);
            try
            {
                if (plan.Kind == SearchEvidenceKind.Contextual)
                {
                    var page = await client.SearchSmartAsync(
                        plan.Query,
                        effectiveSize,
                        includeVideos,
                        cancellationToken);
                    var wasTruncated = candidates.AddPage(page.AssetIds, plan);
                    if (wasTruncated
                        || page.Count >= effectiveSize
                        || !string.IsNullOrEmpty(page.NextCursor)
                        || !string.IsNullOrEmpty(page.NextPage))
                    {
                        AddPartialReason(partialReasons, "Contextual search reached its result window, which has no supported continuation.");
                    }

                    continue;
                }

                await DiscoverOcrQueryAsync(
                    client,
                    candidates,
                    partialReasons,
                    plan,
                    batchSize,
                    maxAssets,
                    includeVideos,
                    cancellationToken);
            }
            catch (ImmichApiException exception)
                when (exception.Kind is ImmichApiFailureKind.Connectivity or ImmichApiFailureKind.Transient)
            {
                AddPartialReason(partialReasons, "One or more search requests failed after bounded retries.");
            }
        }

        return new SearchDiscoveryResult(
            candidates.ToAssets(),
            partialReasons.Count > 0,
            partialReasons);
    }

    private static async Task DiscoverOcrQueryAsync(
        ImmichApiClient client,
        CandidateAccumulator candidates,
        List<string> partialReasons,
        SearchPlan plan,
        int batchSize,
        int maxAssets,
        bool includeVideos,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        HashSet<string> visitedCursors = new(StringComparer.Ordinal);
        while (true)
        {
            var effectiveSize = Math.Min(batchSize, maxAssets - candidates.Count);
            var page = await client.SearchMetadataAsync(
                plan.Query,
                effectiveSize,
                cursor,
                includeVideos,
                cancellationToken);
            var wasTruncated = candidates.AddPage(page.AssetIds, plan);

            if (wasTruncated || page.AssetIds.Count > effectiveSize)
            {
                AddPartialReason(partialReasons, "The configured asset limit truncated search results.");
                return;
            }

            if (!string.IsNullOrEmpty(page.NextPage))
            {
                AddPartialReason(partialReasons, "Immich returned only a deprecated continuation token for metadata search.");
                return;
            }

            if (string.IsNullOrEmpty(page.NextCursor))
            {
                return;
            }

            if (candidates.Count >= maxAssets)
            {
                AddPartialReason(partialReasons, "The configured asset limit stopped remaining search pages.");
                return;
            }

            if (!visitedCursors.Add(page.NextCursor))
            {
                throw new ImmichApiException(
                    ImmichApiFailureKind.IncompatibleApi,
                    "Immich repeated a metadata search cursor; pagination was stopped to avoid an unbounded request loop.");
            }

            cursor = page.NextCursor;
        }
    }

    private static void AddPartialReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }

    private sealed record SearchPlan(string Category, SearchEvidenceKind Kind, string Query);

    private sealed class CandidateAccumulator(int maxAssets)
    {
        private readonly Dictionary<string, MutableAsset> _assets = new(StringComparer.Ordinal);
        private readonly List<MutableAsset> _orderedAssets = [];

        public int Count => _assets.Count;

        public bool AddPage(
            IReadOnlyList<string> assetIds,
            SearchPlan plan)
        {
            var truncated = false;
            CandidateEvidence evidence = new(plan.Category, plan.Kind, plan.Query);
            foreach (var assetId in assetIds)
            {
                if (_assets.TryGetValue(assetId, out var existing))
                {
                    existing.AddEvidence(evidence);
                    continue;
                }

                if (_assets.Count >= maxAssets)
                {
                    truncated = true;
                    continue;
                }

                MutableAsset asset = new(assetId);
                asset.AddEvidence(evidence);
                _assets.Add(assetId, asset);
                _orderedAssets.Add(asset);
            }

            return truncated;
        }

        public DiscoveredAsset[] ToAssets() =>
            _orderedAssets
                .Select(static asset => new DiscoveredAsset(asset.Id, asset.Evidence))
                .ToArray();

        private sealed class MutableAsset(string id)
        {
            private readonly HashSet<CandidateEvidence> _evidenceSet = [];
            private readonly List<CandidateEvidence> _evidence = [];

            public string Id { get; } = id;

            public IReadOnlyList<CandidateEvidence> Evidence => _evidence;

            public void AddEvidence(CandidateEvidence evidence)
            {
                if (_evidenceSet.Add(evidence))
                {
                    _evidence.Add(evidence);
                }
            }
        }
    }
}
