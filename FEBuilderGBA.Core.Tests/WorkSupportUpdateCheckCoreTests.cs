using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Offline tests for the work-support update-availability check (#1196). All
    /// network/filesystem touches are injected, so these run without a network.
    /// </summary>
    public class WorkSupportUpdateCheckCoreTests
    {
        static Dictionary<string, string> Lines(params (string, string)[] kv)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        [Fact]
        public void Check_NullLines_ReturnsError()
        {
            var r = WorkSupportUpdateCheckCore.Check(null, "rom.gba",
                _ => "", _ => null, _ => DateTime.Now);
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Error, r);
        }

        [Fact]
        public void Check_MissingCheckUrl_ReturnsError()
        {
            var r = WorkSupportUpdateCheckCore.Check(Lines(("CHECK_REGEX", "x")), "rom.gba",
                _ => "", _ => null, _ => DateTime.Now);
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Error, r);
        }

        [Fact]
        public void Check_MissingCheckRegex_ReturnsError()
        {
            var r = WorkSupportUpdateCheckCore.Check(Lines(("CHECK_URL", "http://x")), "rom.gba",
                _ => "", _ => null, _ => DateTime.Now);
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Error, r);
        }

        [Fact]
        public void Check_RemoteNewerThanRom_ReturnsUpdateable()
        {
            // CHECK_REGEX extracts a yyyyMMdd date newer than the ROM's mtime.
            var lines = Lines(("CHECK_URL", "http://example.com/v"), ("CHECK_REGEX", @"ver=(\d{8})"));
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => "ver=20300101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2020, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Updateable, r);
        }

        [Fact]
        public void Check_RemoteOlderThanRom_ReturnsLatest()
        {
            var lines = Lines(("CHECK_URL", "http://example.com/v"), ("CHECK_REGEX", @"ver=(\d{8})"));
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => "ver=20100101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2030, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Latest, r);
        }

        [Fact]
        public void Check_RegexNoMatch_ReturnsError()
        {
            var lines = Lines(("CHECK_URL", "http://example.com/v"), ("CHECK_REGEX", @"ver=(\d{8})"));
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => "nothing here",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2020, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Error, r);
        }

        [Fact]
        public void Check_HttpGetThrows_ReturnsLatest()
        {
            var lines = Lines(("CHECK_URL", "http://example.com/v"), ("CHECK_REGEX", @"ver=(\d{8})"));
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => throw new Exception("network down"),
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2020, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Latest, r);
        }

        [Fact]
        public void Check_DirectUrlRegex_UsesHeadLastModified()
        {
            // @DIRECT_URL skips the HTML fetch; match = url, which is itself a URL,
            // so the HEAD Last-Modified header drives the date.
            var lines = Lines(("CHECK_URL", "http://example.com/file.ups"), ("CHECK_REGEX", "@DIRECT_URL"));
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => "",
                httpHeadLastModified: _ => "Thu, 01 Jan 2099 00:00:00 GMT",
                romDateTime: _ => new DateTime(2020, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Updateable, r);
        }

        [Fact]
        public void Check_ExtractedUrl_HeadProbesExtractedUrl_NotCheckUrl()
        {
            // CHECK_REGEX extracts a DIFFERENT direct-download URL from the page;
            // the HEAD Last-Modified probe must target the extracted URL, not the
            // original CHECK_URL listing page.
            const string checkUrl = "http://example.com/list";
            const string extractedUrl = "http://cdn.example.com/build.ups";
            var lines = Lines(("CHECK_URL", checkUrl), ("CHECK_REGEX", @"href=""(http://cdn[^""]+)"""));

            string probedUrl = "";
            var r = WorkSupportUpdateCheckCore.Check(lines, "rom.gba",
                httpGet: _ => $"<a href=\"{extractedUrl}\">dl</a>",
                httpHeadLastModified: u => { probedUrl = u; return "Thu, 01 Jan 2099 00:00:00 GMT"; },
                romDateTime: _ => new DateTime(2020, 1, 1));

            Assert.Equal(extractedUrl, probedUrl);
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Updateable, r);
        }

        [Fact]
        public void TryParseUnixTime_ValidEpoch_Parses()
        {
            // 2030-01-01 UTC in unix seconds.
            uint epoch = (uint)(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)).ToUnixTimeSeconds();
            bool ok = WorkSupportUpdateCheckCore.TryParseUnixTime(epoch.ToString(), out DateTime dt);
            Assert.True(ok);
            Assert.True(dt.Year >= 2029);
        }

        [Fact]
        public void TryParseUnixTime_NonNumeric_ReturnsFalse()
        {
            bool ok = WorkSupportUpdateCheckCore.TryParseUnixTime("not-a-number", out _);
            Assert.False(ok);
        }

        [Fact]
        public void IsUrl_DetectsHttpAndHttps()
        {
            Assert.True(WorkSupportUpdateCheckCore.IsUrl("http://x"));
            Assert.True(WorkSupportUpdateCheckCore.IsUrl("https://x"));
            Assert.False(WorkSupportUpdateCheckCore.IsUrl("20300101"));
            Assert.False(WorkSupportUpdateCheckCore.IsUrl(""));
        }
    }
}
