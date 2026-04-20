using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            LocationsIncluded = new List<string>();
        }

        /// <summary>
        /// Gets or sets the library paths included for merging.
        /// An empty list means all libraries are included (default for fresh installs).
        /// </summary>
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin XML serializer requires a setter for deserialization.")]
        public IList<string> LocationsIncluded { get; set; }
    }
}
