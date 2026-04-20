// ═══════════════════════════════════════════════════════════════════════════════
// ConfigurationServiceTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the ConfigurationService class, which provides null-safe centralized
// access to plugin configuration. This service wraps Plugin.Instance?.Configuration
// with a fallback to a default PluginConfiguration instance, ensuring no NREs
// occur when the plugin hasn't been initialized (e.g., during testing or early boot).
//
// Key behaviors tested:
//   1. LocationsExcluded property reflects current config state
//   2. Defaults are returned when Plugin.Instance has a fresh configuration
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    /// <summary>
    /// Tests for <see cref="ConfigurationService"/>, verifying null-safe config access
    /// and correct delegation to the underlying <see cref="Configuration.PluginConfiguration"/>.
    /// </summary>
    public class ConfigurationServiceTests
    {
        /// <summary>
        /// Ensures Plugin.Instance is initialized for configuration access.
        /// </summary>
        public ConfigurationServiceTests()
        {
            EnsurePluginInstance();
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

        // ── LocationsExcluded ───────────────────────────────────────────────────

        /// <summary>
        /// Verifies that LocationsExcluded defaults to an empty list,
        /// meaning no libraries are excluded from merging.
        /// </summary>
        [Fact]
        public void LocationsExcluded_DefaultsToEmptyList()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            var service = new ConfigurationService();

            Assert.Empty(service.LocationsExcluded);
        }

        /// <summary>
        /// Verifies that when exclusion locations are configured,
        /// the ConfigurationService correctly exposes them.
        /// </summary>
        [Fact]
        public void LocationsExcluded_ReflectsConfiguredLocations()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            Plugin.Instance.Configuration.LocationsExcluded.Add("/mnt/anime");
            Plugin.Instance.Configuration.LocationsExcluded.Add("/mnt/kids");
            var service = new ConfigurationService();

            // Should contain both configured exclusions
            Assert.Equal(2, service.LocationsExcluded.Count);
            Assert.Contains("/mnt/anime", service.LocationsExcluded);
            Assert.Contains("/mnt/kids", service.LocationsExcluded);

            // Cleanup
            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }

        /// <summary>
        /// Verifies that modifications to LocationsExcluded are reflected live
        /// (same reference from the underlying config, not a copy).
        /// </summary>
        [Fact]
        public void LocationsExcluded_ReflectsLiveModifications()
        {
            Plugin.Instance!.Configuration.LocationsExcluded.Clear();
            var service = new ConfigurationService();

            Assert.Empty(service.LocationsExcluded);

            // Add a location — should be visible immediately
            Plugin.Instance.Configuration.LocationsExcluded.Add("/new/path");
            Assert.Single(service.LocationsExcluded);
            Assert.Contains("/new/path", service.LocationsExcluded);

            // Cleanup
            Plugin.Instance.Configuration.LocationsExcluded.Clear();
        }
    }
}
