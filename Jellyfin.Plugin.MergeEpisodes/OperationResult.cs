using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MergeEpisodes
{
    /// <summary>
    /// Result of a merge or split operation.
    /// </summary>
    /// <param name="Succeeded">Number of items successfully processed.</param>
    /// <param name="Failed">Number of items that failed.</param>
    /// <param name="FailedItems">Names of items that failed.</param>
    public record OperationResult(int Succeeded, int Failed, ReadOnlyCollection<string> FailedItems);
}
