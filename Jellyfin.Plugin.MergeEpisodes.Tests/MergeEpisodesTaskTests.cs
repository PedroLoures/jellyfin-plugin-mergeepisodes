// ═══════════════════════════════════════════════════════════════════════════════
// MergeEpisodesTaskTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the MergeEpisodesTask scheduled task, available in Jellyfin's
// Scheduled Tasks UI for manual execution.
//
// Key behaviors tested:
//   1. Task calls MergeEpisodesAsync and forwards progress
//   2. Task metadata (Name, Key, Description, Category) is correct
//   3. No default triggers (manual-only)
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
    /// merges duplicate episodes. Available for manual execution in Jellyfin's dashboard.
    /// </summary>
    public class MergeEpisodesTaskTests
    {
        private readonly Mock<ILogger<MergeEpisodesTask>> _logger;
        private readonly Mock<IEpisodeMergeService> _mergeService;

        public MergeEpisodesTaskTests()
        {
            TestHelpers.EnsurePluginInstance();
            _logger = new Mock<ILogger<MergeEpisodesTask>>();
            _mergeService = new Mock<IEpisodeMergeService>();
        }

        // ── Execution ───────────────────────────────────────────────────────────

        /// <summary>
        /// Task should always call MergeEpisodesAsync when executed.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_CallsMerge()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object);

            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .ReturnsAsync(new OperationResult(3, 0, new List<string>().AsReadOnly()));

            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

            _mergeService.Verify(
                s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that the progress instance is passed through to MergeEpisodesAsync.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_PassesProgressToService()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object);

            IProgress<double>? capturedProgress = null;
            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .Callback<IProgress<double>?>(p => capturedProgress = p)
                .ReturnsAsync(new OperationResult(0, 0, new List<string>().AsReadOnly()));

            var progress = new Progress<double>();
            await task.ExecuteAsync(progress, CancellationToken.None);

            Assert.NotNull(capturedProgress);
        }

        /// <summary>
        /// When Jellyfin cancels the task via its CancellationToken,
        /// the task should forward that to CancelRunningOperation on the service.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_CancellationToken_CallsCancelRunningOperation()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object);

            using var cts = new CancellationTokenSource();

            // Make MergeEpisodesAsync block until we cancel
            var tcs = new TaskCompletionSource<OperationResult>();
            _mergeService
                .Setup(s => s.MergeEpisodesAsync(It.IsAny<IProgress<double>?>()))
                .Returns(tcs.Task);

            var executeTask = task.ExecuteAsync(new Progress<double>(), cts.Token);

            // Cancel the framework token
            cts.Cancel();

            // CancelRunningOperation should have been called via the token registration
            _mergeService.Verify(s => s.CancelRunningOperation(), Times.Once);

            // Let the task complete
            tcs.SetResult(new OperationResult(0, 0, new List<string>().AsReadOnly()));
            await executeTask;
        }

        // ── Task Metadata ───────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the task exposes correct metadata for the Jellyfin scheduled task UI.
        /// </summary>
        [Fact]
        public void TaskMetadata_HasCorrectValues()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object);

            Assert.Equal("Merge All Episodes", task.Name);
            Assert.Equal("MergeEpisodesTask", task.Key);
            Assert.Equal("Merge Episodes", task.Category);
            Assert.False(string.IsNullOrWhiteSpace(task.Description));
        }

        // ── Default Triggers ────────────────────────────────────────────────────

        /// <summary>
        /// Task has no default triggers — it's manual-only.
        /// Users can add custom triggers via the Jellyfin dashboard.
        /// </summary>
        [Fact]
        public void GetDefaultTriggers_ReturnsEmpty()
        {
            var task = new MergeEpisodesTask(_logger.Object, _mergeService.Object);

            var triggers = task.GetDefaultTriggers().ToList();

            Assert.Empty(triggers);
        }
    }
}
