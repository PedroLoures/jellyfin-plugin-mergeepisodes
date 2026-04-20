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
    }
}
