using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeEpisodes.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that merges duplicate episodes in the library.
    /// Available in Jellyfin's Scheduled Tasks UI for manual execution.
    /// </summary>
    public class MergeEpisodesTask : IScheduledTask
    {
        private readonly ILogger<MergeEpisodesTask> _logger;
        private readonly IEpisodeMergeService _mergeService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeEpisodesTask"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{MergeEpisodesTask}"/> interface.</param>
        /// <param name="mergeService">Instance of the <see cref="IEpisodeMergeService"/> interface.</param>
        public MergeEpisodesTask(
            ILogger<MergeEpisodesTask> logger,
            IEpisodeMergeService mergeService)
        {
            _logger = logger;
            _mergeService = mergeService;
        }

        /// <inheritdoc/>
        public string Name => "Merge All Episodes";

        /// <inheritdoc/>
        public string Key => "MergeEpisodesTask";

        /// <inheritdoc/>
        public string Description => "Scans the library and merges duplicate episodes into single entries with multiple versions.";

        /// <inheritdoc/>
        public string Category => "Merge Episodes";

        /// <inheritdoc/>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting scheduled merge episodes task");

            // Wire the framework's cancellation token to the merge service so
            // Jellyfin can stop this task through its normal mechanism.
            using var registration = cancellationToken.Register(() => _mergeService.CancelRunningOperation());

            var result = await _mergeService.MergeEpisodesAsync(progress).ConfigureAwait(false);

            _logger.LogInformation(
                "Scheduled merge episodes task finished. Succeeded: {Succeeded}, Failed: {Failed}",
                result.Succeeded,
                result.Failed);
        }

        /// <inheritdoc/>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // No automatic triggers. Users can run this manually from the Scheduled Tasks UI
            // or add custom triggers via the Jellyfin dashboard.
            return [];
        }
    }
}
