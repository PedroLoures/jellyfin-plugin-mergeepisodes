using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeEpisodes.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that automatically merges duplicate episodes in the library.
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
            if (Plugin.Instance?.Configuration.AutoMergeAfterLibraryScan != true)
            {
                _logger.LogDebug("Automatic merge after library scan is disabled, skipping");
                progress.Report(100);
                return;
            }

            _logger.LogInformation("Starting scheduled merge episodes task");

            var result = await _mergeService.MergeEpisodesAsync(progress).ConfigureAwait(false);

            _logger.LogInformation(
                "Scheduled merge episodes task finished. Succeeded: {Succeeded}, Failed: {Failed}",
                result.Succeeded,
                result.Failed);
        }

        /// <inheritdoc/>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run after library scan completes
            return [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks }];
        }
    }
}
