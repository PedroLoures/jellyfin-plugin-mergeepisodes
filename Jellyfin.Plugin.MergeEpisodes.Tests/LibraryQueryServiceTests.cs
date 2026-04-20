// ═══════════════════════════════════════════════════════════════════════════════
// LibraryQueryServiceTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the LibraryQueryService — the dedicated service for querying episodes
// from the Jellyfin library. Follows the LanguageTags "LibraryQueryService" pattern
// where library querying is extracted into its own service for testability.
//
// Key behaviors tested:
//   1. Returns all episodes when no inclusions are configured (empty = all)
//   2. Filters out episodes NOT in included library locations
//   3. Returns empty when library has no episodes
//   4. IsInIncludedLibrary correctly checks the config + filesystem
//   5. IsEligible delegates to IsInIncludedLibrary
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
    /// and library inclusion filtering logic.
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
        /// When no inclusions are configured (empty list), all episodes are returned.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_NoInclusions_ReturnsAll()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            var ep1 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E02.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            var result = _service.GetEligibleEpisodes();

            Assert.Equal(2, result.Count);
        }

        /// <summary>
        /// Episodes NOT in included library locations are filtered out.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_WithInclusions_FiltersNonIncludedPaths()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/tv");

            var ep1 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/anime/Show S01E01.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            // Only /tv path matches inclusion
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/tv", "/tv/Show S01E01.mkv"))
                .Returns(true);
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/tv", "/anime/Show S01E01.mkv"))
                .Returns(false);

            var result = _service.GetEligibleEpisodes();

            // Only ep1 should be returned (ep2 is not in an included path)
            Assert.Single(result);
            Assert.Equal(ep1.Id, result[0].Id);

            // Cleanup
            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SECTION: IsInIncludedLibrary — path inclusion checks
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// When no inclusions are configured (empty list), all items are considered included.
        /// </summary>
        [Fact]
        public void IsInIncludedLibrary_NoInclusions_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            var ep = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            Assert.True(_service.IsInIncludedLibrary(ep));
        }

        /// <summary>
        /// An item whose path is within an included location is correctly identified.
        /// </summary>
        [Fact]
        public void IsInIncludedLibrary_PathInIncluded_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/included");

            var ep = new Episode { Id = Guid.NewGuid(), Path = "/included/Show S01E01.mkv" };
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/included", "/included/Show S01E01.mkv"))
                .Returns(true);

            Assert.True(_service.IsInIncludedLibrary(ep));

            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }

        /// <summary>
        /// An item whose path is NOT in any included location is not eligible.
        /// </summary>
        [Fact]
        public void IsInIncludedLibrary_PathNotInIncluded_ReturnsFalse()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/included");

            var ep = new Episode { Id = Guid.NewGuid(), Path = "/other/Show S01E01.mkv" };
            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/included", "/other/Show S01E01.mkv"))
                .Returns(false);

            Assert.False(_service.IsInIncludedLibrary(ep));

            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }

        /// <summary>
        /// IsEligible delegates to IsInIncludedLibrary.
        /// </summary>
        [Fact]
        public void IsEligible_InIncludedLibrary_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            var ep = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            Assert.True(_service.IsEligible(ep));
        }

        /// <summary>
        /// An item with a null Path should be considered not included when a filter is active,
        /// and must not throw a NullReferenceException.
        /// </summary>
        [Fact]
        public void IsInIncludedLibrary_NullPath_WithFilter_ReturnsFalse()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/included");

            var ep = new Episode { Id = Guid.NewGuid(), Path = null! };

            Assert.False(_service.IsInIncludedLibrary(ep));

            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }

        /// <summary>
        /// An item with a null Path should be included when no filter is active (empty list).
        /// </summary>
        [Fact]
        public void IsInIncludedLibrary_NullPath_NoFilter_ReturnsTrue()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();

            var ep = new Episode { Id = Guid.NewGuid(), Path = null! };

            Assert.True(_service.IsInIncludedLibrary(ep));
        }

        /// <summary>
        /// GetEligibleEpisodes should not crash if the library returns episodes with null paths.
        /// </summary>
        [Fact]
        public void GetEligibleEpisodes_EpisodeWithNullPath_DoesNotThrow()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/included");

            var ep1 = new Episode { Id = Guid.NewGuid(), Path = null! };
            var ep2 = new Episode { Id = Guid.NewGuid(), Path = "/tv/Show S01E01.mkv" };

            _libraryManager
                .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { ep1, ep2 });

            _fileSystem
                .Setup(fs => fs.ContainsSubPath("/included", "/tv/Show S01E01.mkv"))
                .Returns(true);

            // Should not throw; ep1 (null path) is excluded by filter, ep2 is included
            var result = _service.GetEligibleEpisodes();
            Assert.Single(result);
            Assert.Equal(ep2.Id, result[0].Id);

            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }
    }
}
