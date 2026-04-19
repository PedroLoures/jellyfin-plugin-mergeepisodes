using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Jellyfin.Plugin.MergeEpisodes
{
    public record OperationResult(int Succeeded, int Failed, List<string> FailedItems);

    public class MergeEpisodesManager
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<MergeEpisodesManager> _logger;
        private readonly IFileSystem _fileSystem;

        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;

        public MergeEpisodesManager(
            ILibraryManager libraryManager,
            ILogger<MergeEpisodesManager> logger,
            IFileSystem fileSystem
        )
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Cancels any currently running merge or split operation.
        /// </summary>
        public static void CancelRunningOperation()
        {
            lock (_lock)
            {
                _cts?.Cancel();
            }
        }

        private static CancellationToken BeginOperation()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                return _cts.Token;
            }
        }

        // Captures everything up through the SxxExx identifier (supporting multi-digit
        // season/episode and multi-episode variants like E01E02, E01n02, E01-E02, etc.)
        // Stops before the first space or dot that follows, so quality tags are excluded.
        private static readonly Regex EpisodeIdentityRegex =
    new(@"^(.+?S\d+E\d+(?:(?:E|-E|n)\d+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static string? GetEpisodeBaseIdentity(Episode episode)
        {
            var fileName = Path.GetFileNameWithoutExtension(episode.Path);
            if (fileName is null)
            {
                return null;
            }

            var match = EpisodeIdentityRegex.Match(fileName);
            return match.Success
                ? match.Groups[1].Value.Trim()
                : null;
        }

        public async Task<OperationResult> MergeEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = BeginOperation();
            _logger.LogInformation("Scanning for repeated episodes");

            var duplicateEpisodes = GetEpisodesFromLibrary()
                .GroupBy(e => GetEpisodeBaseIdentity(e), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key is not null && g.Count() > 1)
                .ToList();

            _logger.LogInformation("Found {Count} episode groups to merge", duplicateEpisodes.Count);

            var current = 0;
            var failedItems = new List<string>();
            foreach (var e in duplicateEpisodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                var percent = current / (double)duplicateEpisodes.Count * 100;
                progress?.Report((int)percent);

                try
                {
                    _logger.LogInformation("Merging {Key}", e.Key);
                    await MergeEpisodeVersions(e.Select(e => e.Id).ToList(), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedItems.Add(e.Key!);
                    _logger.LogError(ex, "Failed to merge {Key}", e.Key);
                }
            }

            var succeeded = current - failedItems.Count;
            _logger.LogInformation("Merge complete: {Succeeded} succeeded, {Failed} failed",
                succeeded, failedItems.Count);
            progress?.Report(100);
            return new OperationResult(succeeded, failedItems.Count, failedItems);
        }

        // NOTE: The LinkedAlternateVersions/PrimaryVersionId filtering in SplitEpisodesAsync
        // and SplitAllEpisodesAsync cannot be unit tested because Video.LinkedAlternateVersions
        // is non-virtual and its backing field is managed internally by Jellyfin.
        // These code paths must be verified manually against a running Jellyfin instance.
        public async Task<OperationResult> SplitEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = BeginOperation();

            // Only target primary versions — splitting a primary already unlinks all its alternates,
            // so processing secondary items would be redundant lookups.
            var primaryEpisodes = GetEpisodesFromLibrary()
                .Where(e => e.LinkedAlternateVersions.Length > 0)
                .ToList();

            _logger.LogInformation("Found {Count} merged episodes to split", primaryEpisodes.Count);

            return await SplitEpisodeList(primaryEpisodes, progress, cancellationToken);
        }

        /// <summary>
        /// Splits ALL episodes that have any merge state (primary or secondary),
        /// intended as a deep clean to fix issues left by older plugin versions.
        /// </summary>
        public async Task<OperationResult> SplitAllEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = BeginOperation();

            var allMergedEpisodes = GetEpisodesFromLibrary()
                .Where(e => e.LinkedAlternateVersions.Length > 0 || e.PrimaryVersionId != null)
                .ToList();

            _logger.LogInformation("Found {Count} episodes with merge state to split (deep clean)", allMergedEpisodes.Count);

            return await SplitEpisodeList(allMergedEpisodes, progress, cancellationToken);
        }

        private async Task<OperationResult> SplitEpisodeList(
            List<Episode> episodes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var current = 0;
            var failedItems = new List<string>();

            foreach (var e in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                var percent = current / (double)episodes.Count * 100;
                progress?.Report((int)percent);

                try
                {
                    _logger.LogInformation("Splitting {Name} ({SeriesName})", e.Name, e.SeriesName);
                    await DeleteAlternateSources(e.Id, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedItems.Add($"{e.SeriesName} - {e.Name}");
                    _logger.LogError(ex, "Failed to split {Name} ({SeriesName})", e.Name, e.SeriesName);
                }
            }

            var succeeded = current - failedItems.Count;
            _logger.LogInformation("Split complete: {Succeeded} succeeded, {Failed} failed",
                succeeded, failedItems.Count);
            progress?.Report(100);
            return new OperationResult(succeeded, failedItems.Count, failedItems);
        }

        private List<Episode> GetEpisodesFromLibrary()
        {
            return _libraryManager
                .GetItemList(
                    new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Episode],
                        IsVirtualItem = false,
                        Recursive = true,
                    }
                )
                .OfType<Episode>()
                .Where(IsEligible)
                .ToList();
        }

        private async Task MergeEpisodeVersions(List<Guid> ids, CancellationToken cancellationToken)
        {
            var items = ids.Select(i => _libraryManager.GetItemById<BaseItem>(i, null))
                .OfType<Video>()
                .OrderBy(i => i.Id)
                .ToList();

            if (items.Count < 2)
            {
                return;
            }

            var primaryVersion = items.FirstOrDefault(i =>
                i.MediaSourceCount > 1 && string.IsNullOrEmpty(i.PrimaryVersionId)
            );
            if (primaryVersion is null)
            {
                primaryVersion = items
                    .OrderBy(i =>
                    {
                        if (i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile)
                        {
                            return 1;
                        }

                        return 0;
                    })
                    .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                    .First();
            }

            var alternateVersionsOfPrimary = primaryVersion
                .LinkedAlternateVersions.Where(l => items.Any(i => i.Path == l.Path))
                .ToList();

            var knownPaths = new HashSet<string>(
                alternateVersionsOfPrimary.Select(l => l.Path),
                StringComparer.OrdinalIgnoreCase);

            var itemsToLink = items.Where(i =>
                !i.Id.Equals(primaryVersion.Id) &&
                !alternateVersionsOfPrimary.Any(l => l.ItemId == i.Id)).ToList();

            if (itemsToLink.Count == 0)
            {
                return;
            }

            foreach (var item in itemsToLink)
            {
                cancellationToken.ThrowIfCancellationRequested();

                item.SetPrimaryVersionId(
                    primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)
                );

                await item.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (knownPaths.Add(item.Path))
                {
                    alternateVersionsOfPrimary.Add(
                        new LinkedChild { Path = item.Path, ItemId = item.Id });
                }

                foreach (var linkedItem in item.LinkedAlternateVersions)
                {
                    if (knownPaths.Add(linkedItem.Path))
                    {
                        alternateVersionsOfPrimary.Add(linkedItem);
                    }
                }

                if (item.LinkedAlternateVersions.Length > 0)
                {
                    item.LinkedAlternateVersions = [];
                    await item.UpdateToRepositoryAsync(
                            ItemUpdateType.MetadataEdit,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }

            primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary.ToArray();
            await primaryVersion
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task DeleteAlternateSources(Guid itemId, CancellationToken cancellationToken)
        {
            var item = _libraryManager.GetItemById<Video>(itemId);
            if (item is null)
            {
                return;
            }

            if (item.LinkedAlternateVersions.Length == 0 && item.PrimaryVersionId != null)
            {
                if (!Guid.TryParse(item.PrimaryVersionId, out var primaryId))
                {
                    return;
                }

                item = _libraryManager.GetItemById<Video>(primaryId);
            }

            if (item is null)
            {
                return;
            }

            foreach (var link in item.GetLinkedAlternateVersions())
            {
                cancellationToken.ThrowIfCancellationRequested();

                link.SetPrimaryVersionId(null);
                link.LinkedAlternateVersions = [];

                await link.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            item.LinkedAlternateVersions = [];
            item.SetPrimaryVersionId(null);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        private bool IsEligible(BaseItem item)
        {
            return !IsInExcludedLibrary(item);
        }

        private bool IsInExcludedLibrary(BaseItem item)
        {
           return Plugin.Instance.PluginConfiguration.LocationsExcluded != null
                  && Plugin.Instance.PluginConfiguration.LocationsExcluded
                    .Any(s => _fileSystem.ContainsSubPath(s, item.Path));
        }
    }
}
