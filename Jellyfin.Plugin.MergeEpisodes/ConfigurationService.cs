using Jellyfin.Plugin.MergeEpisodes.Configuration;

namespace Jellyfin.Plugin.MergeEpisodes
{
    /// <summary>
    /// Service for accessing plugin configuration with null-safe fallback.
    /// </summary>
    public class ConfigurationService
    {
        private static readonly PluginConfiguration DefaultConfig = new();

        /// <summary>
        /// Gets the current plugin configuration, or a default instance if unavailable.
        /// </summary>
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? DefaultConfig;

        /// <summary>
        /// Gets the library locations included for merging.
        /// An empty list means nothing is included (user must select paths).
        /// </summary>
        public System.Collections.Generic.IList<string> LocationsIncluded => Config.LocationsIncluded;
    }
}
