using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeEpisodes
{
    /// <summary>
    /// Service for querying episodes from the Jellyfin library.
    /// Handles filtering by eligibility (e.g., included library locations).
    /// Follows the LanguageTags LibraryQueryService pattern for testability.
    /// </summary>
    public class LibraryQueryService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly ILogger<LibraryQueryService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryQueryService"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the library manager.</param>
        /// <param name="fileSystem">Instance of the file system.</param>
        /// <param name="configService">Instance of the configuration service.</param>
        /// <param name="logger">Instance of the logger.</param>
        public LibraryQueryService(
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            ConfigurationService configService,
            ILogger<LibraryQueryService> logger)
        {
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// Gets all eligible episodes from the library, filtering to only include
        /// user-configured locations. Only non-virtual, recursive episodes are returned.
        /// An empty include list means all libraries are eligible.
        /// </summary>
        /// <returns>List of eligible episodes.</returns>
        public ReadOnlyCollection<Episode> GetEligibleEpisodes()
        {
            var episodes = _libraryManager
                .GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Episode],
                    IsVirtualItem = false,
                    Recursive = true,
                })
                .OfType<Episode>()
                .Where(IsEligible)
                .ToList();

            _logger.LogDebug("Found {Count} eligible episodes in library", episodes.Count);
            return episodes.AsReadOnly();
        }

        /// <summary>
        /// Determines whether an item is eligible for merge/split operations.
        /// An item is eligible if the included-paths list is empty (all included)
        /// or if the item resides within one of the included paths.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is eligible; false otherwise.</returns>
        public bool IsEligible(BaseItem item)
        {
            return IsInIncludedLibrary(item);
        }

        /// <summary>
        /// Checks whether an item's path falls within a user-configured included location.
        /// An empty include list means all libraries are included.
        /// Uses the IFileSystem.ContainsSubPath method for platform-aware path comparison.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is in an included library (or no filter is set); false otherwise.</returns>
        public bool IsInIncludedLibrary(BaseItem item)
        {
            var included = _configService.LocationsIncluded;

            // Empty list = all libraries included (fresh install / no filter)
            if (included.Count == 0)
            {
                return true;
            }

            if (item.Path is null)
            {
                return false;
            }

            return included.Any(s => _fileSystem.ContainsSubPath(s, item.Path));
        }
    }
}
