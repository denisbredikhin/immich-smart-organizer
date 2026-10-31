using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Immich;
using Immich.SmartOrganizer.Search;
using Immich.SmartOrganizer.State;

namespace Immich.SmartOrganizer.Actions;

internal sealed class ActionExecutor(ImmichApiClient client, ScanStateStore state)
{
    public static bool IsArchiveSafe(AppConfiguration.CategoryOptions category) =>
        category.Match.All.Any(static condition => condition.ContextualAny is { Count: > 0 })
        && category.Match.All.Any(static condition =>
            condition.OcrAny is { Count: > 0 } || condition.OcrAll is { Count: > 0 });

    public static string Fingerprint(AppConfiguration configuration, string? selectedCategory) =>
        Hash(JsonSerializer.Serialize(new
        {
            configuration.Categories,
            configuration.Processing.AmbiguousAssetPolicy,
            configuration.Scanner,
            selectedCategory
        }));

    public async Task<ExecutionResult> ExecuteAsync(
        DryRunActionPlanner.ActionPlan plan,
        IReadOnlyList<DiscoveredAsset> assets,
        string userId,
        string fingerprint,
        int maxArchiveActions,
        int maxErrors,
        bool full,
        CancellationToken cancellationToken)
    {
        var mutations = 0;
        var albumAdditions = 0;
        var tagApplications = 0;
        var archives = 0;
        var skipped = 0;
        var limited = 0;
        var archiveAttempts = 0;
        List<string> errors = [];
        Dictionary<string, string> albums = new(StringComparer.Ordinal);
        Dictionary<string, string> tags = new(StringComparer.Ordinal);
        var evidenceById = assets.ToDictionary(static asset => asset.Id, StringComparer.Ordinal);

        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (errors.Count >= maxErrors)
            {
                break;
            }

            var assetFingerprint = Hash(fingerprint + JsonSerializer.Serialize(
                evidenceById[action.AssetId].Evidence.OrderBy(static evidence => evidence.Category, StringComparer.Ordinal)
                    .ThenBy(static evidence => evidence.Kind).ThenBy(static evidence => evidence.Query, StringComparer.Ordinal)));
            ImmichApiClient.AssetState? current = null;
            try
            {
                current = await client.GetAssetStateAsync(action.AssetId, cancellationToken);
                if (current.OwnerId != userId || current.Visibility is "hidden" or "locked")
                {
                    throw new ImmichApiException(ImmichApiFailureKind.Authentication,
                        "An action candidate is not owned by the authenticated user or has protected visibility.");
                }

                if (!full && state.IsCompleted(action.AssetId, assetFingerprint, current.UpdatedAt))
                {
                    skipped++;
                    continue;
                }

                state.BeginAsset(action.AssetId, assetFingerprint, current.UpdatedAt, action.CategoryName, full);
                if (action.AlbumName is not null)
                {
                    var key = "album:" + action.AlbumName;
                    if (!state.IsActionCompleted(action.AssetId, key))
                    {
                        state.SetAction(action.AssetId, key, false);
                        if (!albums.TryGetValue(action.AlbumName, out var albumId))
                        {
                            albumId = await client.FindOrCreateAlbumAsync(action.AlbumName, userId, cancellationToken);
                            albums.Add(action.AlbumName, albumId);
                        }

                        if (await client.AddToAlbumAsync(albumId, action.AssetId, cancellationToken))
                        {
                            albumAdditions++;
                            mutations++;
                        }

                        state.SetAction(action.AssetId, key, true);
                    }
                }

                if (action.TagName is not null)
                {
                    var key = "tag:" + action.TagName;
                    if (!state.IsActionCompleted(action.AssetId, key))
                    {
                        state.SetAction(action.AssetId, key, false);
                        if (!tags.TryGetValue(action.TagName, out var tagId))
                        {
                            tagId = await client.FindOrCreateTagAsync(action.TagName, cancellationToken);
                            tags.Add(action.TagName, tagId);
                        }

                        if (await client.ApplyTagAsync(tagId, action.AssetId, cancellationToken))
                        {
                            tagApplications++;
                            mutations++;
                        }

                        state.SetAction(action.AssetId, key, true);
                    }
                }

                var complete = true;
                if (action.Archive)
                {
                    // Reconcile visibility before archival, including recovery from an interrupted response.
                    current = await client.GetAssetStateAsync(action.AssetId, cancellationToken);
                    if (current.OwnerId != userId || current.Visibility is "hidden" or "locked")
                    {
                        throw new ImmichApiException(ImmichApiFailureKind.Authentication, "Archive ownership or visibility changed during execution.");
                    }

                    if (current.Visibility != "archive")
                    {
                        if (archiveAttempts >= maxArchiveActions)
                        {
                            limited++;
                            complete = false;
                        }
                        else
                        {
                            archiveAttempts++;
                            state.SetAction(action.AssetId, "archive", false);
                            await client.ArchiveAsync(action.AssetId, cancellationToken);
                            mutations++;
                            current = await client.GetAssetStateAsync(action.AssetId, cancellationToken);
                            if (current.Visibility != "archive")
                            {
                                throw new ImmichApiException(ImmichApiFailureKind.IncompatibleApi, "Immich did not confirm archive visibility after the update.");
                            }

                            archives++;
                            state.SetAction(action.AssetId, "archive", true);
                        }
                    }
                    else
                    {
                        state.SetAction(action.AssetId, "archive", true);
                    }
                }

                current = await client.GetAssetStateAsync(action.AssetId, cancellationToken);
                state.FinishAsset(action.AssetId, current.UpdatedAt, complete);
            }
            catch (ImmichApiException exception)
            {
                errors.Add(exception.Message);
                if (current is not null)
                {
                    state.FinishAsset(action.AssetId, current.UpdatedAt, false, exception.Message);
                }

                if (exception.Kind is ImmichApiFailureKind.Authentication or ImmichApiFailureKind.IncompatibleApi)
                {
                    break;
                }
            }
        }

        return new ExecutionResult(mutations, albumAdditions, tagApplications, archives, skipped, limited, errors);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal sealed record ExecutionResult(
        int Mutations, int AlbumAdditions, int TagApplications, int Archives,
        int Skipped, int ArchiveLimited, IReadOnlyList<string> Errors);
}
