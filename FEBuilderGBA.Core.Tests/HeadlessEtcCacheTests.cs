using Xunit;
using FEBuilderGBA;
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Core.Tests
{
    public class HeadlessEtcCacheTests
    {
        [Fact]
        public void Update_And_Read_Works()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "test comment");

            Assert.True(cache.CheckFast(0x100));
            Assert.Equal("test comment", cache.At(0x100));
            Assert.Equal("test comment", cache.S_At(0x100));
        }

        [Fact]
        public void TryGetValue_ReturnsFalse_WhenMissing()
        {
            var cache = new HeadlessEtcCache();
            Assert.False(cache.TryGetValue(0x999, out string _));
        }

        [Fact]
        public void At_ReturnsDefault_WhenMissing()
        {
            var cache = new HeadlessEtcCache();
            Assert.Equal("default", cache.At(0x999, "default"));
            Assert.Equal("", cache.S_At(0x999));
        }

        [Fact]
        public void Remove_Works()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "test");
            Assert.True(cache.CheckFast(0x100));

            cache.Remove(0x100);
            Assert.False(cache.CheckFast(0x100));
        }

        [Fact]
        public void RemoveRange_Works()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "a");
            cache.Update(0x200, "b");
            cache.Update(0x300, "c");

            cache.RemoveRange(0x100, 0x250);

            Assert.False(cache.CheckFast(0x100));
            Assert.False(cache.CheckFast(0x200));
            Assert.True(cache.CheckFast(0x300));
        }

        [Fact]
        public void RemoveOverRange_Works()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "a");
            cache.Update(0x200, "b");
            cache.Update(0x300, "c");

            cache.RemoveOverRange(0x200);

            Assert.True(cache.CheckFast(0x100));
            Assert.False(cache.CheckFast(0x200));
            Assert.False(cache.CheckFast(0x300));
        }

        [Fact]
        public void ImplementsIEtcCache()
        {
            IEtcCache cache = new HeadlessEtcCache();
            Assert.NotNull(cache);
        }

        [Fact]
        public void SnapshotRanges_RestoresExactValuesIncludingEmpty()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "");
            cache.Update(0x180, "inside");
            cache.Update(0x300, "outside");
            var ranges = new[] { new EtcCacheRange(0x100, 0x100u) };

            Assert.True(cache.TryCaptureRanges(ranges, out var snapshot));
            cache.Update(0x100, "changed");
            cache.Remove(0x180);
            cache.Update(0x1F0, "new");

            Assert.True(cache.TryRestoreRanges(snapshot));
            Assert.True(cache.TryGetValue(0x100, out string empty));
            Assert.Equal("", empty);
            Assert.Equal("inside", cache.At(0x180));
            Assert.False(cache.CheckFast(0x1F0));
            Assert.Equal("outside", cache.At(0x300));
        }

        [Fact]
        public void CoreEtcCache_SnapshotRoundTrip_PreservesEmptyValue()
        {
            var seed = new HeadlessEtcCache();
            seed.Update(0x100, "");
            seed.Update(0x180, "value");
            Assert.True(seed.TryCaptureAll(out var seedSnapshot));

            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = Path.GetTempPath();
                var cache = new EtcCache(
                    "snapshot-test-" + Guid.NewGuid().ToString("N"));
                Assert.True(cache.TryRestoreAll(seedSnapshot));
                Assert.True(cache.TryCaptureAll(out var captured));

                Assert.True(captured.Entries.ContainsKey(0x100));
                Assert.Equal("", captured.Entries[0x100]);
                Assert.Equal("value", captured.Entries[0x180]);

                var ranges =
                    new[] { new EtcCacheRange(0x100, 0x100u) };
                Assert.True(
                    cache.TryCaptureRanges(ranges, out var rangeSnapshot));
                cache.Remove(0x100);
                cache.Update(0x180, "changed");
                cache.Update(0x1F0, "new");
                Assert.True(cache.TryRestoreRanges(rangeSnapshot));
                Assert.True(cache.TryGetValue(0x100, out string empty));
                Assert.Equal("", empty);
                Assert.Equal("value", cache.At(0x180));
                Assert.False(cache.CheckFast(0x1F0));

                cache.Update(0x200, "destination");
                cache.RepointEtcData(0x180, 1, 0x200);
                Assert.Equal("value", cache.At(0x200));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void RepointEtcData_SourceEntryWinsDestinationCollision()
        {
            var cache = new HeadlessEtcCache();
            cache.Update(0x100, "moved");
            cache.Update(0x200, "destination");

            cache.RepointEtcData(0x100, 1, 0x200);

            Assert.False(cache.CheckFast(0x100));
            Assert.Equal("moved", cache.At(0x200));
        }
    }
}
