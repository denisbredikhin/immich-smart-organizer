using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Search;

namespace Immich.SmartOrganizer.Classification;

internal sealed class ClassificationEngine
{
    public static IReadOnlyList<AssetClassification> Classify(
        IReadOnlyList<DiscoveredAsset> assets,
        IReadOnlyList<AppConfiguration.CategoryOptions> categories,
        string ambiguousAssetPolicy)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentException.ThrowIfNullOrWhiteSpace(ambiguousAssetPolicy);

        if (ambiguousAssetPolicy is not ("review" or "skip"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ambiguousAssetPolicy),
                ambiguousAssetPolicy,
                "Ambiguous asset policy must be 'review' or 'skip'.");
        }

        List<AssetClassification> classifications = new(assets.Count);
        foreach (var asset in assets)
        {
            List<CategoryEvaluation> evaluations = [];
            foreach (var category in categories)
            {
                if (!category.Enabled)
                {
                    continue;
                }

                if (category.Name is null || category.Match is null)
                {
                    throw new InvalidOperationException("Validated categories must have a name and match conditions.");
                }

                var categoryEvidence = asset.Evidence
                    .Where(evidence => string.Equals(
                        evidence.Category,
                        category.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                evaluations.Add(EvaluateCategory(category, categoryEvidence));
            }

            var matches = evaluations.Where(static evaluation => evaluation.IsMatch).ToArray();
            var status = matches.Length switch
            {
                0 => ClassificationStatus.Unmatched,
                1 => ClassificationStatus.Classified,
                _ when ambiguousAssetPolicy == "review" => ClassificationStatus.NeedsReview,
                _ => ClassificationStatus.SkippedAsAmbiguous
            };
            classifications.Add(new AssetClassification(asset, status, matches, evaluations));
        }

        return classifications;
    }

    private static CategoryEvaluation EvaluateCategory(
        AppConfiguration.CategoryOptions category,
        IReadOnlyList<CandidateEvidence> evidence)
    {
        var match = category.Match!;
        List<string> matchedConditions = [];
        List<string> unmetConditions = [];

        var allMatched = true;
        foreach (var condition in match.All!)
        {
            var result = EvaluateCondition(condition, evidence);
            if (result.IsMatch)
            {
                matchedConditions.AddRange(result.Explanations);
            }
            else
            {
                allMatched = false;
                unmetConditions.Add($"all: {result.FailureReason}");
            }
        }

        var anyMatched = match.Any!.Count == 0;
        List<string> failedAnyConditions = [];
        foreach (var condition in match.Any)
        {
            var result = EvaluateCondition(condition, evidence);
            if (result.IsMatch)
            {
                anyMatched = true;
                matchedConditions.AddRange(result.Explanations);
            }
            else
            {
                failedAnyConditions.Add(result.FailureReason);
            }
        }

        if (!anyMatched)
        {
            unmetConditions.Add(
                $"any: none of the configured conditions matched ({string.Join("; ", failedAnyConditions)})");
        }

        var notMatched = true;
        if (match.Not is not null)
        {
            var result = EvaluateCondition(match.Not, evidence);
            notMatched = !result.IsMatch;
            if (notMatched)
            {
                matchedConditions.Add($"not ({result.FailureReason})");
            }
            else
            {
                unmetConditions.Add($"not: condition matched ({string.Join(", ", result.Explanations)})");
            }
        }

        var excludedBy = match.Exclude!
            .Select(condition => EvaluateCondition(condition, evidence))
            .FirstOrDefault(static result => result.IsMatch);
        var isExcluded = excludedBy is not null;
        if (excludedBy is not null)
        {
            unmetConditions.Add($"exclude: matched {string.Join(", ", excludedBy.Explanations)}");
        }

        return new CategoryEvaluation(
            category,
            allMatched && anyMatched && notMatched && !isExcluded,
            isExcluded,
            matchedConditions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            unmetConditions);
    }

    private static ConditionEvaluation EvaluateCondition(
        AppConfiguration.MatchCondition condition,
        IReadOnlyList<CandidateEvidence> evidence)
    {
        if (condition.ContextualAny is { } contextualTerms)
        {
            return EvaluateTerms(
                contextualTerms,
                evidence,
                SearchEvidenceKind.Contextual,
                requireAll: false,
                "contextualAny");
        }

        if (condition.OcrAny is { } ocrAnyTerms)
        {
            return EvaluateTerms(
                ocrAnyTerms,
                evidence,
                SearchEvidenceKind.Ocr,
                requireAll: false,
                "ocrAny");
        }

        if (condition.OcrAll is { } ocrAllTerms)
        {
            return EvaluateTerms(
                ocrAllTerms,
                evidence,
                SearchEvidenceKind.Ocr,
                requireAll: true,
                "ocrAll");
        }

        throw new InvalidOperationException("Validated match conditions must contain exactly one operator.");
    }

    private static ConditionEvaluation EvaluateTerms(
        IReadOnlyList<string> terms,
        IReadOnlyList<CandidateEvidence> evidence,
        SearchEvidenceKind kind,
        bool requireAll,
        string operatorName)
    {
        List<string> matchedTerms = [];
        List<string> missingTerms = [];
        foreach (var term in terms)
        {
            if (evidence.Any(item =>
                item.Kind == kind
                && string.Equals(item.Query, term, StringComparison.OrdinalIgnoreCase)))
            {
                matchedTerms.Add(term);
            }
            else
            {
                missingTerms.Add(term);
            }
        }

        var isMatch = requireAll ? missingTerms.Count == 0 : matchedTerms.Count > 0;
        var explanations = matchedTerms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(term => $"{operatorName}={term}")
            .ToArray();
        var failureReason = isMatch
            ? string.Empty
            : requireAll
                ? $"{operatorName} missing [{string.Join(", ", missingTerms)}]"
                : $"{operatorName} expected one of [{string.Join(", ", terms)}]";

        return new ConditionEvaluation(isMatch, explanations, failureReason);
    }

    internal sealed record AssetClassification(
        DiscoveredAsset Asset,
        ClassificationStatus Status,
        IReadOnlyList<CategoryEvaluation> MatchingCategories,
        IReadOnlyList<CategoryEvaluation> CategoryEvaluations);

    internal sealed record CategoryEvaluation(
        AppConfiguration.CategoryOptions Category,
        bool IsMatch,
        bool IsExcluded,
        IReadOnlyList<string> MatchedConditions,
        IReadOnlyList<string> UnmetConditions);

    private sealed record ConditionEvaluation(
        bool IsMatch,
        IReadOnlyList<string> Explanations,
        string FailureReason);
}

internal enum ClassificationStatus
{
    Unmatched,
    Classified,
    NeedsReview,
    SkippedAsAmbiguous
}
