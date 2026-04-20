using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MergeEpisodes
{
    /// <summary>
    /// Defines the contract for episode merge and split operations.
    /// </summary>
    public interface IEpisodeMergeService
    {
        /// <summary>
        /// Scans all episodes and merges duplicates sharing the same SxxExx identity.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        Task<OperationResult> MergeEpisodesAsync(IProgress<double>? progress);

        /// <summary>
        /// Splits all primary merged episodes back into individual items.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        Task<OperationResult> SplitEpisodesAsync(IProgress<double>? progress);

        /// <summary>
        /// Splits ALL episodes that have any merge state (primary or secondary).
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>The operation result.</returns>
        Task<OperationResult> SplitAllEpisodesAsync(IProgress<double>? progress);

        /// <summary>
        /// Cancels any currently running merge or split operation.
        /// </summary>
        void CancelRunningOperation();

        /// <summary>
        /// Extracts the base identity (show name + SxxExx) from an episode's file name.
        /// </summary>
        /// <param name="episode">The episode to extract identity from.</param>
        /// <returns>The base identity string, or null if it cannot be determined.</returns>
        string? GetEpisodeBaseIdentity(Episode episode);
    }
}
