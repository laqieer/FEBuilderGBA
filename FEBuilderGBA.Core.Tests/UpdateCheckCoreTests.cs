using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class UpdateCheckCoreTests
    {
        [Fact]
        public void ParseLatestVersionFromReleaseJson_ValidRelease_ReturnsTag()
        {
            Assert.Equal("ver_20260704.04",
                UpdateCheckCore.ParseLatestVersionFromReleaseJson("{\"tag_name\":\"ver_20260704.04\",\"name\":\"release\"}"));
        }

        [Theory]
        [InlineData("{\"message\":\"rate limited\"}")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not json")]
        public void ParseLatestVersionFromReleaseJson_InvalidOrMissingTag_ReturnsEmpty(string json)
        {
            Assert.Equal("", UpdateCheckCore.ParseLatestVersionFromReleaseJson(json));
        }

        [Fact]
        public void BuildResult_OlderCurrent_ReturnsUpdateAvailable()
        {
            var r = UpdateCheckCore.BuildResult("ver_20260101.00", "ver_20260704.04", UpdateCheckCore.ReleasesLatestPageUrl);
            Assert.True(r.CheckSucceeded);
            Assert.True(r.IsUpdateAvailable);
        }

        [Fact]
        public void BuildResult_Equal_ReturnsNoUpdate()
        {
            var r = UpdateCheckCore.BuildResult("ver_20260704.04", "ver_20260704.04", UpdateCheckCore.ReleasesLatestPageUrl);
            Assert.True(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
        }

        [Fact]
        public void BuildResult_NewerCurrent_ReturnsNoUpdate()
        {
            var r = UpdateCheckCore.BuildResult("ver_20270101.00", "ver_20260704.04", UpdateCheckCore.ReleasesLatestPageUrl);
            Assert.True(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("2026070404")]
        public void BuildResult_InvalidLatest_ReturnsFailed(string latest)
        {
            var r = UpdateCheckCore.BuildResult("ver_20260101.00", latest, UpdateCheckCore.ReleasesLatestPageUrl);
            Assert.False(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
            Assert.NotEmpty(r.Error);
        }

        [Theory]
        [InlineData("ver_20260101.00", "20260704.04")]
        [InlineData("20260101.00", "ver_20260704.04")]
        public void BuildResult_VerPrefixAndBareNumeric_CompareCorrectly(string current, string latest)
        {
            var r = UpdateCheckCore.BuildResult(current, latest, UpdateCheckCore.ReleasesLatestPageUrl);
            Assert.True(r.CheckSucceeded);
            Assert.True(r.IsUpdateAvailable);
        }

        [Fact]
        public void CheckLatest_EmptyBody_ReturnsFailed()
        {
            var r = UpdateCheckCore.CheckLatest(_ => "");
            Assert.False(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
            Assert.Contains("Could not reach GitHub", r.Error);
        }

        [Fact]
        public void CheckLatest_EmptyJson_ReturnsFailed()
        {
            var r = UpdateCheckCore.CheckLatest(_ => "{}");
            Assert.False(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
        }

        [Fact]
        public void CheckLatest_ValidNewerTag_ReturnsUpdateAvailable()
        {
            var r = UpdateCheckCore.CheckLatest(_ => "{\"tag_name\":\"ver_20991231.99\"}");
            Assert.True(r.CheckSucceeded);
            Assert.True(r.IsUpdateAvailable);
        }

        [Fact]
        public void CheckLatest_HttpGetThrows_ReturnsFailed()
        {
            var r = UpdateCheckCore.CheckLatest(_ => throw new InvalidOperationException("network down"));
            Assert.False(r.CheckSucceeded);
            Assert.False(r.IsUpdateAvailable);
        }

        [Theory]
        [InlineData("0", "0", "20260705", false)]
        [InlineData("3", "20260704", "20260705", false)]
        [InlineData("3", "20260701", "20260705", true)]
        [InlineData("3", "bad", "20260705", true)]
        [InlineData("bad", "20260701", "20260705", false)]
        public void ShouldAutoCheck_MirrorsWinFormsDayGate(string interval, string last, string today, bool expected)
        {
            Assert.Equal(expected, UpdateCheckCore.ShouldAutoCheck(interval, last, today));
        }
    }
}
