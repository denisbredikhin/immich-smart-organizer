using Immich.SmartOrganizer.Classification;

namespace Immich.SmartOrganizer.Actions;

internal static class DryRunActionPlanner
{
    public static ActionPlan Create(
        IReadOnlyList<ClassificationEngine.AssetClassification> classifications,
        int maxArchiveActions)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentOutOfRangeException.ThrowIfNegative(maxArchiveActions);

        List<PlannedAssetActions> actions = [];
        var archiveActions = 0;
        var archiveActionsLimited = 0;

        foreach (var classification in classifications)
        {
            if (classification.Status != ClassificationStatus.Classified)
            {
                continue;
            }

            var category = classification.MatchingCategories[0].Category;
            if (category.Actions is null)
            {
                throw new InvalidOperationException("Validated categories must have action options.");
            }

            var archive = false;
            var wasArchiveLimited = false;
            if (category.Actions.Archive)
            {
                if (archiveActions < maxArchiveActions)
                {
                    archive = true;
                    archiveActions++;
                }
                else
                {
                    wasArchiveLimited = true;
                    archiveActionsLimited++;
                }
            }

            if (category.Actions.Album is null
                && category.Actions.Tag is null
                && !archive
                && !wasArchiveLimited)
            {
                continue;
            }

            actions.Add(new PlannedAssetActions(
                classification.Asset.Id,
                category.Name,
                category.Actions.Album,
                category.Actions.Tag,
                archive,
                wasArchiveLimited));
        }

        return new ActionPlan(actions, archiveActionsLimited);
    }

    internal sealed record ActionPlan(
        IReadOnlyList<PlannedAssetActions> Actions,
        int ArchiveActionsLimited)
    {
        public int AlbumAdditionsPlanned => Actions.Count(static action => action.AlbumName is not null);

        public int TagApplicationsPlanned => Actions.Count(static action => action.TagName is not null);

        public int ArchiveActionsPlanned => Actions.Count(static action => action.Archive);
    }

    internal sealed record PlannedAssetActions(
        string AssetId,
        string CategoryName,
        string? AlbumName,
        string? TagName,
        bool Archive,
        bool ArchiveLimited);
}
