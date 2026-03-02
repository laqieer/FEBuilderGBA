using Xunit;
using FEBuilderGBA;

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
    }
}
