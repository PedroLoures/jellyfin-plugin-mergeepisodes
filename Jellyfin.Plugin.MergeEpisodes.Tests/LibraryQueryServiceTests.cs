// ═══════════════════════════════════════════════════════════════════════════════
// LibraryQueryServiceTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the LibraryQueryService — the dedicated service for querying episodes
// from the Jellyfin library. Follows the LanguageTags "LibraryQueryService" pattern
// where library querying is extracted into its own service for testability.
//
// Key behaviors tested:
//   1. Returns all episodes when no exclusions are configured
//   2. Filters out episodes in excluded library locations
//   3. Returns empty when library has no episodes
//   4. IsInExcludedLibrary correctly checks the config + filesystem
//   5. IsEligible inverts IsInExcludedLibrary
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    /// <summary>
    /// Tests for <see cref="LibraryQueryService"/>, verifying episode querying
    /// and library exclusion filtering logic.
    /// </summary>
    public class LibraryQueryServiceTests
    {
        private readonly Mock<ILibraryManager> _libraryManager;
        private readonly Mock<IFileSystem> _fileSystem;
        private readonly Mock<ILogger<LibraryQueryService>> _logger;
        private readonly LibraryQueryService _service;

        public LibraryQueryServiceTests()
        {
            EnsurePluginInstance();
            _libraryManager = new Mock<ILibraryManager>();
            _fileSystem = new Mock<IFileSystem>();
            _logger = new Mock<ILogger<LibraryQueryService>>();
            _service = new LibraryQueryService(
                _libraryManager.Object,
                _fileSystem.Object,
                new ConfigurationService(),
                _logger.Object);
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

        // ═══════════════════════════════════════════════════════════════════════
        // SECTION: GetEligibleEpisodes — querying and filtering
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// When the library has no episodes, GetEligibleEpisodes returns an empty list.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_EmptyLibrary_ReturnsEmpty()
        {
            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem>());

            var result = _service.GetEligibleEpisodes();

            Assert.Empty(result);
        }

        /// <summary>
        /// When no exclusions are configured, all episodes are returned.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_NoExclusions_ReturnsAll()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            var ep1 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E02.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            var result = _service.GetEligibleEpisodes();

            Assert.Equal(2, result.Count);
        }

        /// <summary>
        /// Episodes in excluded library locations are filtered out.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_WithExclusions_FiltersExcludedPaths()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            Plugin.Instance.Configuration.LocationsExcluded.Add("/anime");

            var ep1 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/anime/Show S01E01.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            // Only the anime path is a subpath of /anime
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/anime", "/anime/Show S01E01.mkv"))
                .Returns(true);
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/anime", "/tv/Show S01E01.mkv"))
                .Returns(false);

            var result = _service.GetEligibleEpisodes();

            // Only ep1 should be returned (ep2 is excluded)
            Assert.Single(result);
            Assert.Equal(ep1.Id, result[0].Id);

            // Cleanup
            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SECTION: IsInExcludedLibrary — path exclusion checks
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// When no exclusions are configured, no item is considered excluded.
        /// </summary>
        [Fact]
        public void IsInExcludedLibrary_NoExclusions_ReturnsFalse()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            var ep = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            Assert.False(_service.IsInExcludedLibrary(ep));
        }

        /// <summary>
        /// An item whose path is within an excluded location is correctly identified.
        /// </summary>
        [Fact]
        public void IsInExcludedLibrary_PathInExcluded_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            Plugin.Instance.Configuration.LocationsExcluded.Add("/excluded");

            var ep = new Episode { Id = Guid.NewGuid(), Path = "/excluded/Show S01E01.mkv" };
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/excluded", "/excluded/Show S01E01.mkv"))
                .Returns(true);

            Assert.True(_service.IsInExcludedLibrary(ep));

            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }

        /// <summary>
        /// IsEligible is the inverse of IsInExcludedLibrary.
        /// </summary>
        [Fact]
        public void IsEligible_NotExcluded_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            var ep = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            Assert.True(_service.IsEligible(ep));
        }

        /// <summary>
        /// An item with a null Path (corrupted or virtual) should not be considered excluded
        /// and must not throw a NullReferenceException in ContainsSubPath.
        /// </summary>
        [Fact]
        public void IsInExcludedLibrary_NullPath_ReturnsFalse()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            Plugin.Instance.Configuration.LocationsExcluded.Add("/excluded");

            var ep = new Episode { Id = Guid.NewGuid(), Path = null! };

            // Should return false (not excluded) rather than throwing
            Assert.False(_service.IsInExcludedLibrary(ep));

            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }

        /// <summary>
        /// GetEligibleEpisodes should not crash if the library returns episodes with null paths.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_EpisodeWithNullPath_DoesNotThrow()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            Plugin.Instance.Configuration.LocationsExcluded.Add("/excluded");

            var ep1 = new Episode { Id = Guid.NewGuid(), Path = null! };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            _fileSystem
                .Setup(fs => fs.ContainsSubPath(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            // Should not throw; both episodes are eligible (null path is not "excluded")
            var result = _service.GetEligibleEpisodes();
            Assert.Equal(2, result.Count);

            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }
    }
}
