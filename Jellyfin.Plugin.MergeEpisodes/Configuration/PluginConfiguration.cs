using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MergeEpisodes.Configuration
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            LocationsExcluded = new List<string>();
        }

        /// <summary>
        /// Gets the library paths excluded from merging.
        /// </summary>
        public IList<string> LocationsExcluded { get; }

        /// <summary>
        /// Gets or sets a value indicating whether episodes should be automatically merged after library scans.
        /// </summary>
        public bool AutoMergeAfterLibraryScan { get; set; }
    }
}
