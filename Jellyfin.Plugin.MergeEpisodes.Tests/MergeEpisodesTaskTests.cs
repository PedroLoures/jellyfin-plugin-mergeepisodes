// ═══════════════════════════════════════════════════════════════════════════════
// MergeEpisodesTaskTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the MergeEpisodesTask scheduled task, which runs automatically
// on a 24-hour interval when the "Auto Merge After Library Scan" config option
// is enabled.
//
// Key behaviors tested:
//   1. Task skips execution entirely when AutoMergeAfterLibraryScan is disabled
//   2. Task calls MergeEpisodesAsync when flag is enabled
//   3. Task reports progress correctly (100% on skip, delegates during execution)
//   4. Task metadata (Name, Key, Description, Category) is correct
//   5. Default trigger is a 24-hour interval
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MergeEpisodes.ScheduledTasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    /// <summary>
    /// Tests for <see cref="MergeEpisodesTask"/>, the scheduled task that
    /// automatically merges duplicate episodes after library scans.
    /// Verifies the task correctly respects the AutoMergeAfterLibraryScan flag.
    /// </summary>
    public class MergeEpisodesTaskTests
    {
        private readonly Mock<ILogger<MergeEpisodesTask>> _logger;
        private readonly Mock<IEpisodeMergeService> _mergeService;
        private readonly ConfigurationService _configService;

        public MergeEpisodesTaskTests()
        {
            EnsurePluginInstance();
            _logger = new Mock<ILogger<MergeEpisodesTask>>();
            _mergeService = new Mock<IEpisodeMergeService>();
            _configService = new ConfigurationService();
        }

        private static void EnsurePluginInstance()
        {
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

            _ = new Plugin(appPaths.Object, xmlSerializer.Object);
        }

        // ── Flag Checking (Auto-Merge Disabled) ─────────────────────────────────

        /// <summary>
        /// When AutoMergeAfterLibraryScan is disabled (default), the task should
        /// skip entirely and NEVER call MergeEpisodesAsync. This prevents unwanted
        /// automatic merging for users who haven't opted in.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FlagDisabled_SkipsMerge()
        {
            // Arrange: ensure flag is OFF (the default state)
            Plugin.Instance!.Configuration.AutoMergeAfterLibraryScan = false;
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);
            var progress = new Progress<double>();

            // Act
            await task.ExecuteAsync(progress, CancellationToken.None);

            // Assert: MergeEpisodesAsync was never called
            _mergeService.Verify(
                s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()),
                Times.Never,
                "MergeEpisodesAsync should NOT be called when the auto-merge flag is disabled");
        }

        /// <summary>
        /// When the flag is disabled and the task skips, it should still report
        /// 100% progress so the Jellyfin UI doesn't show it as "stuck".
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FlagDisabled_ReportsProgress100()
        {
            // Arrange
            Plugin.Instance!.Configuration.AutoMergeAfterLibraryScan = false;
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);
            var progressValues = new List<double>();
            var progress = new Progress<double>(v => progressValues.Add(v));

            // Act
            await task.ExecuteAsync(progress, CancellationToken.None);

            // Allow progress callback to fire (it's async)
            await Task.Delay(50);

            // Assert: should have reported 100%
            Assert.Contains(100.0, progressValues);
        }

        // ── Flag Checking (Auto-Merge Enabled) ──────────────────────────────────

        /// <summary>
        /// When AutoMergeAfterLibraryScan is enabled, the task should invoke
        /// MergeEpisodesAsync to perform the actual merge operation.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FlagEnabled_CallsMerge()
        {
            // Arrange: enable the flag
            Plugin.Instance!.Configuration.AutoMergeAfterLibraryScan = true;
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);

            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .ReturnsAsync(new OperationResult(3, 0, new List<string>().AsReadOnly()));

            var progress = new Progress<double>();

            // Act
            await task.ExecuteAsync(progress, CancellationToken.None);

            // Assert: MergeEpisodesAsync was called exactly once
            _mergeService.Verify(
                s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()),
                Times.Once,
                "MergeEpisodesAsync should be called exactly once when the auto-merge flag is enabled");

            // Cleanup
            Plugin.Instance.Configuration.AutoMergeAfterLibraryScan = false;
        }

        /// <summary>
        /// Verifies that the progress instance is passed through to MergeEpisodesAsync
        /// so users can see real-time progress in the Jellyfin dashboard.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FlagEnabled_PassesProgressToService()
        {
            // Arrange
            Plugin.Instance!.Configuration.AutoMergeAfterLibraryScan = true;
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);

            IProgress<double>? capturedProgress = null;
            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .Callback<IProgress<double>?>(p => capturedProgress = p)
                .ReturnsAsync(new OperationResult(0, 0, new List<string>().AsReadOnly()));

            var progress = new Progress<double>();

            // Act
            await task.ExecuteAsync(progress, CancellationToken.None);

            // Assert: the progress instance was forwarded
            Assert.NotNull(capturedProgress);

            // Cleanup
            Plugin.Instance.Configuration.AutoMergeAfterLibraryScan = false;
        }

        // ── Task Metadata ───────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the task exposes correct metadata for the Jellyfin scheduled task UI.
        /// </summary>
        [Fact]
        public void TaskMetadata_HasCorrectValues()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);

            Assert.Equal("Merge All Episodes", task.Name);
            Assert.Equal("MergeEpisodesTask", task.Key);
            Assert.Equal("Merge Episodes", task.Category);
            Assert.False(string.IsNullOrWhiteSpace(task.Description));
        }

        // ── Default Triggers ────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the task has a default 24-hour interval trigger,
        /// so it runs periodically after library scans complete.
        /// </summary>
        [Fact]
        public void GetDefaultTriggers_Returns24HourInterval()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);

            var triggers = task.GetDefaultTriggers().ToList();

            // Should have exactly one trigger
            Assert.Single(triggers);

            // The trigger should be an interval trigger with 24-hour period
            var trigger = triggers[0];
            Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
            Assert.Equal(TimeSpan.FromHours(24).Ticks, trigger.IntervalTicks);
        }

        // ── Edge Cases ──────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies that if the flag changes between task creation and execution,
        /// the task uses the current value at execution time (not cached).
        /// This is critical because users may toggle the setting while the task
        /// is already scheduled.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FlagChangedAfterConstruction_UsesCurrentValue()
        {
            // Arrange: start with flag disabled
            Plugin.Instance!.Configuration.AutoMergeAfterLibraryScan = false;
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object, _configService);

            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .ReturnsAsync(new OperationResult(1, 0, new List<string>().AsReadOnly()));

            // Enable the flag AFTER task was constructed
            Plugin.Instance.Configuration.AutoMergeAfterLibraryScan = true;

            // Act
            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

            // Assert: should have called merge because flag is now true
            _mergeService.Verify(
                s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()),
                Times.Once);

            // Cleanup
            Plugin.Instance.Configuration.AutoMergeAfterLibraryScan = false;
        }
    }
}
