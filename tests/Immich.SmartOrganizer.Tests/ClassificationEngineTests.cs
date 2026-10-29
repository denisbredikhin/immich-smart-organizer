using Immich.SmartOrganizer.Actions;
using Immich.SmartOrganizer.Classification;
using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Search;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ClassificationEngineTests
{
    [TestMethod]
    public void Classify_applies_all_any_not_and_exclude_conditions_to_query_evidence()
    {
        var category = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                All = [new AppConfiguration.MatchCondition { ContextualAny = ["receipt"] }],
                Any =
                [
                    new AppConfiguration.MatchCondition { OcrAny = ["kassabon"] },
                    new AppConfiguration.MatchCondition { OcrAny = ["invoice"] }
                ],
                Not = new AppConfiguration.MatchCondition { OcrAny = ["advertisement"] },
                Exclude = [new AppConfiguration.MatchCondition { OcrAny = ["personal"] }]
            });
        var asset = CreateAsset(
            ("receipts", SearchEvidenceKind.Contextual, "Receipt"),
            ("receipts", SearchEvidenceKind.Ocr, "INVOICE"));

        var result = ClassificationEngine.Classify([asset], [category], "review");

        Assert.AreEqual(ClassificationStatus.Classified, result[0].Status);
        Assert.AreSequenceEqual(
            ["contextualAny=receipt", "ocrAny=invoice", "not (ocrAny expected one of [advertisement])"],
            result[0].MatchingCategories[0].MatchedConditions);
    }

    [TestMethod]
    public void Classify_requires_every_ocrAll_query_and_matches_queries_case_insensitively()
    {
        var category = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAll = ["kassabon", "btw"] }]
            });
        var incomplete = CreateAsset(("receipts", SearchEvidenceKind.Ocr, "KASSABON"));
        var complete = CreateAsset(
            ("receipts", SearchEvidenceKind.Ocr, "KASSABON"),
            ("receipts", SearchEvidenceKind.Ocr, "BTW"));

        var results = ClassificationEngine.Classify([incomplete, complete], [category], "review");

        Assert.AreEqual(ClassificationStatus.Unmatched, results[0].Status);
        Assert.AreEqual(ClassificationStatus.Classified, results[1].Status);
        Assert.Contains("ocrAll missing [btw]", results[0].CategoryEvaluations[0].UnmetConditions[0]);
    }

    [TestMethod]
    public void Classify_does_not_treat_a_partial_query_string_as_matching_evidence()
    {
        var category = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }]
            });
        var asset = CreateAsset(("receipts", SearchEvidenceKind.Ocr, "restaurant receipt"));

        var result = ClassificationEngine.Classify([asset], [category], "review");

        Assert.AreEqual(ClassificationStatus.Unmatched, result[0].Status);
    }

    [TestMethod]
    public void Classify_does_not_use_evidence_from_another_category()
    {
        var receipts = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }]
            });
        var invoices = CreateCategory(
            "invoices",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["invoice"] }]
            });
        var asset = CreateAsset(("receipts", SearchEvidenceKind.Ocr, "invoice"));

        var result = ClassificationEngine.Classify([asset], [receipts, invoices], "review");

        Assert.AreEqual(ClassificationStatus.Unmatched, result[0].Status);
        Assert.IsEmpty(result[0].MatchingCategories);
    }

    [TestMethod]
    [DataRow("advertisement", "not: condition matched")]
    [DataRow("personal", "exclude: matched ocrAny=personal")]
    public void Classify_rejects_not_and_excluded_evidence(
        string blockedQuery,
        string expectedReason)
    {
        var category = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }],
                Not = new AppConfiguration.MatchCondition { OcrAny = ["advertisement"] },
                Exclude = [new AppConfiguration.MatchCondition { OcrAny = ["personal"] }]
            });
        var asset = CreateAsset(
            ("receipts", SearchEvidenceKind.Ocr, "receipt"),
            ("receipts", SearchEvidenceKind.Ocr, blockedQuery));

        var result = ClassificationEngine.Classify([asset], [category], "review");

        Assert.Contains(
            reason => reason.Contains(expectedReason, StringComparison.Ordinal),
            result[0].CategoryEvaluations[0].UnmetConditions);
        Assert.AreEqual(ClassificationStatus.Unmatched, result[0].Status);
        Assert.IsEmpty(result[0].MatchingCategories);
    }

    [TestMethod]
    [DataRow("review", "NeedsReview")]
    [DataRow("skip", "SkippedAsAmbiguous")]
    public void Classify_routes_multiple_category_matches_according_to_policy(
        string policy,
        string expectedStatus)
    {
        var receipts = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }]
            },
            album: "Receipts",
            tag: "Documents/Receipts",
            archive: true);
        var invoices = CreateCategory(
            "invoices",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["invoice"] }]
            },
            album: "Invoices",
            tag: "Documents/Invoices",
            archive: true);
        var asset = CreateAsset(
            ("receipts", SearchEvidenceKind.Ocr, "receipt"),
            ("invoices", SearchEvidenceKind.Ocr, "invoice"));

        var result = ClassificationEngine.Classify([asset], [receipts, invoices], policy);

        Assert.AreEqual(expectedStatus, result[0].Status.ToString());
        Assert.HasCount(2, result[0].MatchingCategories);
        Assert.IsEmpty(DryRunActionPlanner.Create(result, maxArchiveActions: 10).Actions);
    }

    [TestMethod]
    public void Action_planner_creates_only_unambiguous_plans_and_enforces_archive_limit()
    {
        var receipts = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }]
            },
            album: "Receipts",
            tag: "Documents/Receipts",
            archive: true);
        var invoices = CreateCategory(
            "invoices",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["invoice"] }]
            },
            album: "Invoices",
            archive: true);
        var assets = new[]
        {
            CreateAsset(("receipts", SearchEvidenceKind.Ocr, "receipt")),
            CreateAsset(("invoices", SearchEvidenceKind.Ocr, "invoice"))
        };
        var classifications = ClassificationEngine.Classify(assets, [receipts, invoices], "review");

        var plan = DryRunActionPlanner.Create(classifications, maxArchiveActions: 1);

        Assert.HasCount(2, plan.Actions);
        Assert.AreEqual(2, plan.AlbumAdditionsPlanned);
        Assert.AreEqual(1, plan.TagApplicationsPlanned);
        Assert.AreEqual(1, plan.ArchiveActionsPlanned);
        Assert.AreEqual(1, plan.ArchiveActionsLimited);
        Assert.IsTrue(plan.Actions[1].ArchiveLimited);
    }

    [TestMethod]
    public void Action_planner_does_not_plan_archive_when_category_action_is_disabled()
    {
        var category = CreateCategory(
            "receipts",
            new AppConfiguration.MatchOptions
            {
                Any = [new AppConfiguration.MatchCondition { OcrAny = ["receipt"] }]
            });
        var asset = CreateAsset(("receipts", SearchEvidenceKind.Ocr, "receipt"));
        var classifications = ClassificationEngine.Classify([asset], [category], "review");

        var plan = DryRunActionPlanner.Create(classifications, maxArchiveActions: 25);

        Assert.IsEmpty(plan.Actions);
        Assert.AreEqual(0, plan.ArchiveActionsPlanned);
    }

    private static AppConfiguration.CategoryOptions CreateCategory(
        string name,
        AppConfiguration.MatchOptions match,
        string? album = null,
        string? tag = null,
        bool archive = false) =>
        new()
        {
            Name = name,
            Enabled = true,
            Search = new(),
            Match = match,
            Actions = new AppConfiguration.ActionOptions
            {
                Album = album,
                Tag = tag,
                Archive = archive
            }
        };

    private static DiscoveredAsset CreateAsset(
        params (string Category, SearchEvidenceKind Kind, string Query)[] evidence) =>
        new(
            Guid.NewGuid().ToString("N"),
            evidence.Select(static item => new CandidateEvidence(item.Category, item.Kind, item.Query)).ToArray());
}
