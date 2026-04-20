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
    /// Handles filtering by eligibility (e.g., excluded library locations).
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
        /// Gets all eligible episodes from the library, excluding those in user-configured
        /// excluded locations. Only non-virtual, recursive episodes are returned.
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
        /// An item is ineligible if it resides in an excluded library location.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is eligible; false if excluded.</returns>
        public bool IsEligible(BaseItem item)
        {
            return !IsInExcludedLibrary(item);
        }

        /// <summary>
        /// Checks whether an item's path falls within any user-configured excluded location.
        /// Uses the IFileSystem.ContainsSubPath method for platform-aware path comparison.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is in an excluded library; false otherwise.</returns>
        public bool IsInExcludedLibrary(BaseItem item)
        {
            var excluded = _configService.LocationsExcluded;
            return excluded.Count > 0
                   && excluded.Any(s => _fileSystem.ContainsSubPath(s, item.Path));
        }
    }
}
