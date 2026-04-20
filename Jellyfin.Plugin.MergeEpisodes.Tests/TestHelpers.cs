using System;
using System.IO;
using Moq;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    /// <summary>
    /// Shared test utilities for the Merge Episodes test suite.
    /// </summary>
    internal static class TestHelpers
    {
        /// <summary>
        /// Ensures <see cref="Plugin.Instance"/> is initialised with a minimal mock
        /// configuration. Safe to call multiple times — only the first call creates the instance.
        /// </summary>
        internal static void EnsurePluginInstance()
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
    }
}
