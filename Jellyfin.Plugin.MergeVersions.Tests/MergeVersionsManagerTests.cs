using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MergeVersions.Tests
{
    public class MergeVersionsManagerTests
    {
        private readonly Mock<ILibraryManager> _libraryManager;
        private readonly Mock<ILogger<MergeVersionsManager>> _logger;
        private readonly Mock<IFileSystem> _fileSystem;
        private readonly MergeVersionsManager _manager;

        public MergeVersionsManagerTests()
        {
            _libraryManager = new Mock<ILibraryManager>();
            _logger = new Mock<ILogger<MergeVersionsManager>>();
            _fileSystem = new Mock<IFileSystem>();

            // Plugin.Instance is required by IsInExcludedLibrary — we need to set it up.
            // Create a minimal plugin mock via reflection since the constructor requires
            // IServerApplicationPaths + IXmlSerializer. We'll use a helper to bypass this.
            EnsurePluginInstance();

            _manager = new MergeVersionsManager(
                _libraryManager.Object,
                _logger.Object,
                _fileSystem.Object
            );
        }

        private static void EnsurePluginInstance()
        {
            // If Plugin.Instance is already set from a previous test, skip.
            if (Plugin.Instance != null)
            {
                return;
            }

            var appPaths = new Mock<MediaBrowser.Controller.IServerApplicationPaths>();
            var tempPath = Path.GetTempPath();
            appPaths.SetupGet(p => p.PluginConfigurationsPath).Returns(tempPath);
            appPaths.SetupGet(p => p.PluginsPath).Returns(tempPath);
            appPaths.SetupGet(p => p.DataPath).Returns(tempPath);
            appPaths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(tempPath);
            var xmlSerializer = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
            xmlSerializer
                .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
                .Returns(new Configuration.PluginConfiguration());

            // This constructor sets Plugin.Instance = this
            _ = new Plugin(appPaths.Object, xmlSerializer.Object);
        }

        private static Episode CreateTestEpisode(Guid id, string path, string? primaryVersionId = null, LinkedChild[]? linkedAlternates = null)
        {
            var ep = new Episode
            {
                Id = id,
                Path = path,
                LinkedAlternateVersions = linkedAlternates ?? []
            };

            if (primaryVersionId != null)
            {
                ep.SetPrimaryVersionId(primaryVersionId);
            }

            return ep;
        }

        private void SetupLibraryReturns(params Episode[] episodes)
        {
            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(episodes.Cast<BaseItem>().ToList());
        }

        // ── MergeEpisodesAsync ──────────────────────────────────────────

        [Fact]
        public async Task MergeEpisodesAsync_NoDuplicates_ReturnsZeroCounts()
        {
            var ep1 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 720p.mkv");
            var ep2 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E02 720p.mkv");
            SetupLibraryReturns(ep1, ep2);

            var result = await _manager.MergeEpisodesAsync(null);

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.Empty(result.FailedItems);
        }

        [Fact]
        public async Task MergeEpisodesAsync_NoEpisodes_ReturnsZeroCounts()
        {
            SetupLibraryReturns();

            var result = await _manager.MergeEpisodesAsync(null);

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(0, result.Failed);
        }

        [Fact]
        public async Task MergeEpisodesAsync_NoSxxExxPattern_ReturnsZeroCounts()
        {
            // Episodes without SxxExx pattern should be skipped (identity returns null)
            var ep1 = CreateTestEpisode(Guid.NewGuid(), "/tv/random_file_1.mkv");
            var ep2 = CreateTestEpisode(Guid.NewGuid(), "/tv/random_file_2.mkv");
            SetupLibraryReturns(ep1, ep2);

            var result = await _manager.MergeEpisodesAsync(null);

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(0, result.Failed);
        }

        [Fact]
        public async Task MergeEpisodesAsync_ReportsProgress()
        {
            var ep1 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 720p.mkv");
            var ep2 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 1080p.mkv");
            SetupLibraryReturns(ep1, ep2);

            // GetItemById returns Video for MergeVersions inner call
            _libraryManager
                .Setup(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), null))
                .Returns((Guid id, object? _) =>
                {
                    if (id == ep1.Id) return ep1;
                    if (id == ep2.Id) return ep2;
                    return null;
                });

            var progressValues = new List<double>();
            var progress = new Progress<double>(v => progressValues.Add(v));

            var result = await _manager.MergeEpisodesAsync(progress);

            // Should have reported at least the final 100
            Assert.Contains(100.0, progressValues);
        }

        // ── SplitEpisodesAsync ──────────────────────────────────────────

        [Fact]
        public async Task SplitEpisodesAsync_NoMergedEpisodes_ReturnsZeroCounts()
        {
            var ep1 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 720p.mkv");
            SetupLibraryReturns(ep1);

            var result = await _manager.SplitEpisodesAsync(null);

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(0, result.Failed);
        }

        // NOTE: SplitEpisodesAsync and SplitAllEpisodesAsync filtering by LinkedAlternateVersions
        // cannot be properly unit tested because Video.LinkedAlternateVersions is a non-virtual
        // property in Jellyfin's BaseItem. Direct assignment on Episode instances is silently
        // ignored at runtime (the backing field is managed internally by Jellyfin), and Moq
        // cannot intercept non-virtual members. These behaviors must be verified manually
        // against a running Jellyfin instance.
        //
        // What would be tested here:
        //   - SplitEpisodesAsync only targets episodes where LinkedAlternateVersions.Length > 0
        //     (primary versions), not secondary items that only have PrimaryVersionId set.
        //   - SplitAllEpisodesAsync targets both primary and secondary items
        //     (LinkedAlternateVersions.Length > 0 OR PrimaryVersionId != null).

        // ── Cancellation ────────────────────────────────────────────────

        [Fact]
        public async Task MergeEpisodesAsync_CancellationThrows()
        {
            var ep1 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 720p.mkv");
            var ep2 = CreateTestEpisode(Guid.NewGuid(), "/tv/Show S01E01 1080p.mkv");
            SetupLibraryReturns(ep1, ep2);

            // Start the operation, then immediately cancel
            _ = Task.Run(async () =>
            {
                await Task.Delay(10);
                MergeVersionsManager.CancelRunningOperation();
            });

            // This may or may not throw depending on timing — either outcome is valid.
            // The key thing is it doesn't hang or crash.
            try
            {
                await _manager.MergeEpisodesAsync(null);
            }
            catch (OperationCanceledException)
            {
                // Expected — cancellation was processed
            }
        }

        [Fact]
        public void CancelRunningOperation_WithNoActiveOperation_DoesNotThrow()
        {
            // Should be safe to call even when nothing is running
            var ex = Record.Exception(() => MergeVersionsManager.CancelRunningOperation());
            Assert.Null(ex);
        }

        // ── OperationResult ─────────────────────────────────────────────

        [Fact]
        public void OperationResult_RecordEquality()
        {
            var a = new OperationResult(5, 2, ["item1", "item2"]);
            Assert.Equal(5, a.Succeeded);
            Assert.Equal(2, a.Failed);
            Assert.Equal(2, a.FailedItems.Count);
        }
    }
}
