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
        /// Gets or sets the library paths included for merging.
        /// An empty list means nothing is included (user must select paths after install).
        /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only — Jellyfin XML serializer requires a setter
#pragma warning disable CA1002 // Do not expose generic lists — Jellyfin XML serializer requires concrete List<T>
        public List<string> LocationsIncluded { get; set; } = [];
#pragma warning restore CA1002
#pragma warning restore CA2227
    }
}
