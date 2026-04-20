// ═══════════════════════════════════════════════════════════════════════════════
// ConfigurationServiceTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the ConfigurationService class, which provides null-safe centralized
// access to plugin configuration. This service wraps Plugin.Instance?.Configuration
// with a fallback to a default PluginConfiguration instance, ensuring no NREs
// occur when the plugin hasn't been initialized (e.g., during testing or early boot).
//
// Key behaviors tested:
//   1. LocationsIncluded property reflects current config state
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
            TestHelpers.EnsurePluginInstance();
        }

        // ── LocationsIncluded ───────────────────────────────────────────────────

        /// <summary>
        /// Verifies that LocationsIncluded defaults to an empty list,
        /// meaning nothing is included (user must select paths after install).
        /// </summary>
        [Fact]
        public void LocationsIncluded_DefaultsToEmptyList()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            var service = new ConfigurationService();

            Assert.Empty(service.LocationsIncluded);
        }

        /// <summary>
        /// Verifies that when inclusion locations are configured,
        /// the ConfigurationService correctly exposes them.
        /// </summary>
        [Fact]
        public void LocationsIncluded_ReflectsConfiguredLocations()
        {
            Plugin.Instance!.Configuration.LocationsIncluded.Clear();
            Plugin.Instance.Configuration.LocationsIncluded.Add("/mnt/anime");
            Plugin.Instance.Configuration.LocationsIncluded.Add("/mnt/kids");
            var service = new ConfigurationService();

            Assert.Equal(2, service.LocationsIncluded.Count);
            Assert.Contains("/mnt/anime", service.LocationsIncluded);
            Assert.Contains("/mnt/kids", service.LocationsIncluded);

            // Cleanup
            Plugin.Instance.Configuration.LocationsIncluded.Clear();
        }

        /// <summary>
        /// Verifies that modifications to LocationsIncluded are reflected live
        /// (same reference from the underlying config, not a copy).
        /// </summary>
        [Fact]
        public void LocationsIncluded_ReflectsLiveModifications()
        {
            var service = new ConfigurationService();

            // Adding a unique path should be reflected immediately via the service
            var uniquePath = "/test-live-" + Guid.NewGuid().ToString("N");
            Plugin.Instance!.Configuration.LocationsIncluded.Add(uniquePath);
            Assert.Contains(uniquePath, service.LocationsIncluded);

            // Removing it should also be reflected
            Plugin.Instance.Configuration.LocationsIncluded.Remove(uniquePath);
            Assert.DoesNotContain(uniquePath, service.LocationsIncluded);
        }
    }
}
