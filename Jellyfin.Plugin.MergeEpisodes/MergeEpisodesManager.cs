using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeEpisodes
{
    /// <summary>
    /// Manages merging and splitting of duplicate episodes.
    /// </summary>
    public sealed class MergeEpisodesManager : IEpisodeMergeService, IDisposable
    {
        // Captures everything up through the SxxExx identifier (supporting multi-digit
        // season/episode and multi-episode variants like E01E02, E01n02, E01-E02, etc.)
        // Stops before the first space or dot that follows, so quality tags are excluded.
        private static readonly Regex EpisodeIdentityRegex =
            new(@"^(.+?S\d+E\d+(?:(?:E|-E|n)\d+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly object _lock = new();
        private readonly SemaphoreSlim _operationGuard = new(1, 1);
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<MergeEpisodesManager> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly LibraryQueryService _queryService;

        private CancellationTokenSource? _cts;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeEpisodesManager"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="configService">The configuration service.</param>
        /// <param name="queryService">The library query service.</param>
        public MergeEpisodesManager(
            ILibraryManager libraryManager,
            ILogger<MergeEpisodesManager> logger,
            IFileSystem fileSystem,
            ConfigurationService configService,
            LibraryQueryService queryService)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _configService = configService;
            _queryService = queryService;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Dispose();
            _operationGuard.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public void CancelRunningOperation()
        {
            lock (_lock)
            {
                _cts?.Cancel();
            }
        }

        /// <inheritdoc />
        public string? GetEpisodeBaseIdentity(Episode episode)
        {
            return GetBaseIdentity(episode);
        }

        /// <summary>
        /// Extracts the base identity (show name + SxxExx) from an episode's file name.
        /// Static convenience method for use without an instance.
        /// </summary>
        /// <param name="episode">The episode to extract identity from.</param>
        /// <returns>The base identity string, or null if it cannot be determined.</returns>
        public static string? GetBaseIdentity(Episode episode)
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

        /// <summary>
        /// Scans all episodes and merges duplicates sharing the same SxxExx identity.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        public async Task<OperationResult> MergeEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = await BeginOperationAsync().ConfigureAwait(false);
            _logger.LogInformation("Scanning for repeated episodes");

            var duplicateEpisodes = _queryService.GetEligibleEpisodes()
                .GroupBy(e => GetBaseIdentity(e), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key is not null && g.Count() > 1)
                .ToList();

            _logger.LogInformation("Found {Count} episode groups to merge", duplicateEpisodes.Count);

            var current = 0;
            var failedItems = new List<string>();
            foreach (var e in duplicateEpisodes)
            {
                // Check cancellation BETWEEN groups — never mid-transaction
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                var percent = current / (double)duplicateEpisodes.Count * 100;
                progress?.Report((int)percent);

                // Acquire guard for the atomic write — this prevents partial state on shutdown
                await _operationGuard.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _logger.LogInformation("Merging {Key}", e.Key);
                    await MergeEpisodeVersions(
                        e.Select(e => e.Id).ToList()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failedItems.Add(e.Key!);
                    _logger.LogError(ex, "Failed to merge {Key}", e.Key);
                }
                finally
                {
                    _operationGuard.Release();
                }
            }

            var succeeded = current - failedItems.Count;
            _logger.LogInformation(
                "Merge complete: {Succeeded} succeeded, {Failed} failed",
                succeeded,
                failedItems.Count);
            progress?.Report(100);
            return new OperationResult(succeeded, failedItems.Count, failedItems.AsReadOnly());
        }

        // NOTE: The LinkedAlternateVersions/PrimaryVersionId filtering in SplitEpisodesAsync
        // and SplitAllEpisodesAsync cannot be unit tested because Video.LinkedAlternateVersions
        // is non-virtual and its backing field is managed internally by Jellyfin.
        // These code paths must be verified manually against a running Jellyfin instance.

        /// <summary>
        /// Splits all primary merged episodes back into individual items.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        public async Task<OperationResult> SplitEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = await BeginOperationAsync().ConfigureAwait(false);

            // Only target primary versions
            // so processing secondary items would be redundant lookups.
            var primaryEpisodes = _queryService.GetEligibleEpisodes()
                .Where(e => e.LinkedAlternateVersions.Length > 0)
                .ToList();

            _logger.LogInformation("Found {Count} merged episodes to split", primaryEpisodes.Count);

            return await SplitEpisodeList(primaryEpisodes, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Splits ALL episodes that have any merge state (primary or secondary),
        /// intended as a deep clean to fix issues left by older plugin versions.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        public async Task<OperationResult> SplitAllEpisodesAsync(IProgress<double>? progress)
        {
            var cancellationToken = await BeginOperationAsync().ConfigureAwait(false);

            var allMergedEpisodes = _queryService.GetEligibleEpisodes()
                .Where(e => e.LinkedAlternateVersions.Length > 0 || e.PrimaryVersionId != null)
                .ToList();

            _logger.LogInformation("Found {Count} episodes with merge state to split (deep clean)", allMergedEpisodes.Count);

            return await SplitEpisodeList(allMergedEpisodes, progress, cancellationToken).ConfigureAwait(false);
        }

        private CancellationToken BeginOperation()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                return _cts.Token;
            }
        }

        /// <summary>
        /// Acquires the operation guard, ensuring only one operation runs at a time.
        /// If another operation is already running, this will wait for it to complete
        /// its current atomic unit before cancellation takes effect.
        /// </summary>
        /// <returns>The cancellation token for the new operation.</returns>
        private async Task<CancellationToken> BeginOperationAsync()
        {
            var token = BeginOperation();

            // Wait for any in-progress atomic write to finish before proceeding.
            // This ensures the previous operation completes its current episode cleanly.
            await _operationGuard.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _operationGuard.Release();

            // Re-check: if another call came in while we waited, our token may already be cancelled.
            token.ThrowIfCancellationRequested();
            return token;
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
                // Check cancellation BETWEEN episodes — never mid-transaction
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                var percent = current / (double)episodes.Count * 100;
                progress?.Report((int)percent);

                // Acquire guard for the atomic write
                await _operationGuard.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _logger.LogInformation("Splitting {Name} ({SeriesName})", e.Name, e.SeriesName);
                    await DeleteAlternateSources(e.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failedItems.Add($"{e.SeriesName} - {e.Name}");
                    _logger.LogError(ex, "Failed to split {Name} ({SeriesName})", e.Name, e.SeriesName);
                }
                finally
                {
                    _operationGuard.Release();
                }
            }

            var succeeded = current - failedItems.Count;
            _logger.LogInformation(
                "Split complete: {Succeeded} succeeded, {Failed} failed",
                succeeded,
                failedItems.Count);
            progress?.Report(100);
            return new OperationResult(succeeded, failedItems.Count, failedItems.AsReadOnly());
        }

        private async Task MergeEpisodeVersions(List<Guid> ids)
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
                i.MediaSourceCount > 1 && string.IsNullOrEmpty(i.PrimaryVersionId));
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

            // Collect all new linked children and their existing sub-links
            foreach (var item in itemsToLink)
            {
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
            }

            // STEP 1: Update primary FIRST — so it knows about all children.
            // If we crash after this, children appear as duplicates (harmless, self-healing).
            primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary.ToArray();
            await primaryVersion
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);

            // STEP 2: Now set PrimaryVersionId on children and clear their own linked lists.
            foreach (var item in itemsToLink)
            {
                item.SetPrimaryVersionId(
                    primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture));

                if (item.LinkedAlternateVersions.Length > 0)
                {
                    item.LinkedAlternateVersions = [];
                }

                await item.UpdateToRepositoryAsync(
                    ItemUpdateType.MetadataEdit,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task DeleteAlternateSources(Guid itemId)
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

            // Capture linked items before clearing
            var linkedItems = item.GetLinkedAlternateVersions();

            // STEP 1: Clear primary FIRST — so it no longer references children.
            // If we crash after this, children have stale PrimaryVersionId pointing
            // to a primary that doesn't list them — Jellyfin treats them as standalone.
            item.LinkedAlternateVersions = [];
            item.SetPrimaryVersionId(null);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);

            // STEP 2: Clear children's back-references.
            foreach (var link in linkedItems)
            {
                link.SetPrimaryVersionId(null);
                link.LinkedAlternateVersions = [];

                await link.UpdateToRepositoryAsync(
                    ItemUpdateType.MetadataEdit,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
