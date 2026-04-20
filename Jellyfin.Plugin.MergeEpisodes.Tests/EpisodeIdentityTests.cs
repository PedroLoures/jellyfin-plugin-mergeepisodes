// ═══════════════════════════════════════════════════════════════════════════════
// EpisodeIdentityTests.cs
// ═══════════════════════════════════════════════════════════════════════════════
// Tests for the episode identity extraction regex — the foundation of the merge
// logic. The "base identity" is everything in a filename up to and including the
// SxxExx identifier (e.g., "Show Name S01E01"). Quality tags, codec info, and
// episode titles that follow are stripped, so different versions of the same
// episode produce the same identity and get grouped for merging.
//
// Supported patterns:
//   • Standard: "Show S01E01 - 720p.mkv" → "Show S01E01"
//   • Multi-episode: "Show S01E01E02 - 720p.mkv" → "Show S01E01E02"
//   • Dash multi: "Show S01E01-E02 - 720p.mkv" → "Show S01E01-E02"
//   • N-separator: "Show S01E01n02 - 720p.mkv" → "Show S01E01n02"
//   • Case-insensitive: "show s01e01" matches "Show S01E01"
//   • No match: files without SxxExx → null (skipped during merge)
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.MergeEpisodes.Tests
{
    /// <summary>
    /// Tests for <see cref="MergeEpisodesManager.GetBaseIdentity"/> — the regex-based
    /// identity extractor that determines which episodes are "the same" and should be merged.
    /// </summary>
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
        // === Jellyfin documentation format: Series Name (Year) SxxExx Episode Title.ext ===
        [InlineData("/tv/Series Name A (2010)/Season 01/Series Name A (2010) S01E03.mkv", "Series Name A (2010) S01E03")]
        [InlineData("/tv/Series Name A (2021)/Season 1/Series Name A (2021) S01E01 Title.avi", "Series Name A (2021) S01E01")]
        [InlineData("/tv/Awesome TV Show (2024)/Season 1/Awesome TV Show (2024) S01E01 episode name.mp4", "Awesome TV Show (2024) S01E01")]
        // 3D tags with dots (Jellyfin 3D naming convention)
        [InlineData("/tv/Series Name A (2022)/Season 01/Series Name A (2022) S01E01 Some Episode.3d.ftab.mp4", "Series Name A (2022) S01E01")]
        [InlineData("/tv/Series Name A (2022)/Season 01/Series Name A (2022) S01E03 Yet another episode.3d.hsbs.mp4", "Series Name A (2022) S01E03")]
        // Metadata provider ID in series name
        [InlineData("/tv/Jellyfin Documentary (2030) [imdbid-tt00000000]/Season 01/Jellyfin Documentary (2030) [imdbid-tt00000000] S01E01.mkv", "Jellyfin Documentary (2030) [imdbid-tt00000000] S01E01")]
        public void StandardEpisode_ExtractsIdentity(string path, string expected)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetBaseIdentity(episode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("/tv/Show S01E01E02 - 720p.mkv", "Show S01E01E02")]
        [InlineData("/tv/Show S01E01-E02 - 720p.mkv", "Show S01E01-E02")]
        [InlineData("/tv/Show S01E01n02 Nome de Epi - 720p.mkv", "Show S01E01n02")]
        // Multi-episode with episode name after identifier
        [InlineData("/tv/Show S01E01E02 Pilot Part 1 and 2 - 1080p.mkv", "Show S01E01E02")]
        [InlineData("/tv/Show S01E01-E02 Pilot - BluRay.mkv", "Show S01E01-E02")]
        // === Jellyfin documentation format: multi-episode ===
        [InlineData("/tv/Series Name A (2010)/Season 01/Series Name A (2010) S01E01-E02.mkv", "Series Name A (2010) S01E01-E02")]
        [InlineData("/tv/Series Name B (2018)/Season 02/Series Name B (2018) S02E01-E02.mkv", "Series Name B (2018) S02E01-E02")]
        public void MultiEpisode_ExtractsFullIdentity(string path, string expected)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetBaseIdentity(episode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("/tv/Show Name S01E05 - 720p.mkv", "/tv/Show Name S01E05 1080p.mkv")]
        [InlineData("/tv/My Show S02E10 - HEVC.mkv", "/tv/My Show S02E10 BluRay.mkv")]
        // Episode name present in one but not the other — still same identity
        [InlineData("/tv/Show S01E05 The Beginning - 720p.mkv", "/tv/Show S01E05 - 1080p.mkv")]
        [InlineData("/tv/Show S01E05 The Beginning - 720p.mkv", "/tv/Show S01E05 The Beginning - 1080p.mkv")]
        // === Jellyfin doc format: same episode, different quality/3D variants ===
        [InlineData(
            "/tv/Series Name A (2022)/Season 01/Series Name A (2022) S01E01 Some Episode.3d.ftab.mp4",
            "/tv/Series Name A (2022)/Season 01/Series Name A (2022) S01E01 Some Episode.mkv")]
        [InlineData(
            "/tv/Awesome TV Show (2024)/Season 1/Awesome TV Show (2024) S01E01 episode name - 720p.mkv",
            "/tv/Awesome TV Show (2024)/Season 1/Awesome TV Show (2024) S01E01 episode name - 1080p.mkv")]
        public void DifferentQualities_ProduceSameIdentity(string path1, string path2)
        {
            var ep1 = CreateEpisode(path1);
            var ep2 = CreateEpisode(path2);

            var id1 = MergeEpisodesManager.GetBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetBaseIdentity(ep2);

            Assert.NotNull(id1);
            Assert.Equal(id1, id2, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/tv/Show Name S01E01 - 720p.mkv", "/tv/Show Name S01E02 720p.mkv")]
        public void DifferentEpisodes_ProduceDifferentIdentity(string path1, string path2)
        {
            var ep1 = CreateEpisode(path1);
            var ep2 = CreateEpisode(path2);

            var id1 = MergeEpisodesManager.GetBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetBaseIdentity(ep2);

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
            var result = MergeEpisodesManager.GetBaseIdentity(episode);
            Assert.Null(result);
        }

        [Fact]
        public void CaseInsensitive_SameIdentity()
        {
            var ep1 = CreateEpisode("/tv/Show s01e05 - 720p.mkv");
            var ep2 = CreateEpisode("/tv/Show S01E05 - 1080p.mkv");

            var id1 = MergeEpisodesManager.GetBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetBaseIdentity(ep2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            // The regex is case-insensitive for matching, but the extracted text preserves case.
            // Grouping in MergeEpisodesAsync uses StringComparer.OrdinalIgnoreCase.
            Assert.Equal(id1, id2, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Specials in Season 00 use S00Exx numbering per Jellyfin docs.
        /// These should extract identity just like regular episodes.
        /// </summary>
        [Theory]
        [InlineData("/tv/Series Name A (2010)/Season 00/Series Name A S00E01.mkv", "Series Name A S00E01")]
        [InlineData("/tv/Series Name A (2010)/Season 00/Series Name A S00E02.mkv", "Series Name A S00E02")]
        public void Specials_S00Exx_ExtractsIdentity(string path, string expected)
        {
            var episode = CreateEpisode(path);
            var result = MergeEpisodesManager.GetBaseIdentity(episode);
            Assert.Equal(expected, result);
        }

        /// <summary>
        /// Files with -part suffixes (multi-part episodes per Jellyfin docs) should NOT
        /// merge together — they have different SxxExx identities or the part suffix
        /// differentiates them. Jellyfin docs state "This does not work with multiple versions or merging."
        /// </summary>
        [Fact]
        public void MultiPartFiles_ProduceSameIdentity_ButDocsWarnAgainstMerging()
        {
            // "Series Name A (2025) S01E01-part-1.mkv" and "Series Name A (2025) S01E01-part-2.mkv"
            // The regex stops at S01E01 — the "-part-1" suffix is NOT part of the identity.
            // Both files produce the same identity, meaning they WOULD be grouped for merging.
            // This is documented as unsupported by Jellyfin — users should not use multi-part
            // files with this plugin.
            var ep1 = CreateEpisode("/tv/Series Name A (2025)/Season 1/Series Name A (2025) S01E01-part-1.mkv");
            var ep2 = CreateEpisode("/tv/Series Name A (2025)/Season 1/Series Name A (2025) S01E01-part-2.mkv");

            var id1 = MergeEpisodesManager.GetBaseIdentity(ep1);
            var id2 = MergeEpisodesManager.GetBaseIdentity(ep2);

            // Both produce the same identity — this is a known limitation
            Assert.Equal(id1, id2, StringComparer.OrdinalIgnoreCase);
        }
    }
}
