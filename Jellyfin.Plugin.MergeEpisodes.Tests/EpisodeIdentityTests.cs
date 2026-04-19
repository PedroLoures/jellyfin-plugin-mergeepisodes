using System;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    public class EpisodeIdentityTests
    {
        private static Episode CreateEpisode(string path)
        {
            return new Episode { Path = path };
        }

        [Theory]
        [InlineData("/tv/Show Name S01E01 - 720p.mkv", "Show Name S01E01")]
        [InlineData("/tv/Show Name S01E01.mkv", "Show Name S01E01")]
        [InlineData("/tv/My Show S02E15 - 1080p - HEVC.mkv", "My Show S02E15")]
        [InlineData("/tv/Show S10E100 Nome Do Episódio - BluRay.mkv", "Show S10E100")]
        // Episode name after SxxExx (should be excluded from identity)
        [InlineData("/tv/Show S01E05 The Beginning - 720p.mkv", "Show S01E05")]
        [InlineData("/tv/Show S01E05 The Beginning.mkv", "Show S01E05")]
        // Bare SxxExx with no tags at all
        [InlineData("/tv/Show S01E01", "Show S01E01")]
        public void StandardEpisode_ExtractsIdentity(string path, string expected)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetEpisodeBaseIdentity(episode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("/tv/Show S01E01E02 - 720p.mkv", "Show S01E01E02")]
        [InlineData("/tv/Show S01E01-E02 - 720p.mkv", "Show S01E01-E02")]
        [InlineData("/tv/Show S01E01n02 Nome de Epi - 720p.mkv", "Show S01E01n02")]
        // Multi-episode with episode name after identifier
        [InlineData("/tv/Show S01E01E02 Pilot Part 1 and 2 - 1080p.mkv", "Show S01E01E02")]
        [InlineData("/tv/Show S01E01-E02 Pilot - BluRay.mkv", "Show S01E01-E02")]
        public void MultiEpisode_ExtractsFullIdentity(string path, string expected)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetEpisodeBaseIdentity(episode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("/tv/Show Name S01E05 - 720p.mkv", "/tv/Show Name S01E05 1080p.mkv")]
        [InlineData("/tv/My Show S02E10 - HEVC.mkv", "/tv/My Show S02E10 BluRay.mkv")]
        // Episode name present in one but not the other — still same identity
        [InlineData("/tv/Show S01E05 The Beginning - 720p.mkv", "/tv/Show S01E05 - 1080p.mkv")]
        [InlineData("/tv/Show S01E05 The Beginning - 720p.mkv", "/tv/Show S01E05 The Beginning - 1080p.mkv")]
        public void DifferentQualities_ProduceSameIdentity(string path1, string path2)
        {
            var ep1 = CreateEpisode(path1);
            var ep2 = CreateEpisode(path2);

            var id1 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep2);

            Assert.NotNull(id1);
            Assert.Equal(id1, id2, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/tv/Show Name S01E01 - 720p.mkv", "/tv/Show Name S01E02 720p.mkv")]
        public void DifferentEpisodes_ProduceDifferentIdentity(string path1, string path2)
        {
            var ep1 = CreateEpisode(path1);
            var ep2 = CreateEpisode(path2);

            var id1 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            Assert.NotEqual(id1, id2, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/tv/random_video.mkv")]
        [InlineData("/tv/no_episode_tag.mkv")]
        [InlineData("/tv/movie 2024.mkv")]
        public void NoSxxExx_ReturnsNull(string path)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetEpisodeBaseIdentity(episode);
            Assert.Null(result);
        }

        [Fact]
        public void CaseInsensitive_SameIdentity()
        {
            var ep1 = CreateEpisode("/tv/Show s01e05 - 720p.mkv");
            var ep2 = CreateEpisode("/tv/Show S01E05 - 1080p.mkv");

            var id1 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetEpisodeBaseIdentity(ep2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            // The regex is case-insensitive for matching, but the extracted text preserves case.
            // Grouping in MergeEpisodesAsync uses StringComparer.OrdinalIgnoreCase.
            Assert.Equal(id1, id2, StringComparer.OrdinalIgnoreCase);
        }
    }
}
