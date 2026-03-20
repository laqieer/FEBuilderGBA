using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class CliResolveNamesTests
    {
        [Fact]
        public void GetUnitName_WithNullRom_ReturnsIdFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetUnitName(5);
                // When ROM is null, NameResolver returns "???" as fallback
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetClassName_WithNullRom_ReturnsIdFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetClassName(10);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetItemName_WithNullRom_ReturnsIdFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetItemName(0x15);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetSongName_WithNullRom_ReturnsSongPrefix()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetSongName(0x1A);
                // Even without ROM, song names return a formatted string
                Assert.StartsWith("Song", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetPortraitName_WithNullRom_ReturnsEmpty()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetPortraitName(1);
                Assert.Equal("", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void ClearCache_ThenRequery_DoesNotThrow()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                // Query multiple name types after clearing cache
                NameResolver.GetUnitName(0);
                NameResolver.GetClassName(0);
                NameResolver.GetItemName(0);
                NameResolver.ClearCache();
                // Re-query after second clear
                string unit = NameResolver.GetUnitName(0);
                string cls = NameResolver.GetClassName(0);
                string item = NameResolver.GetItemName(0);
                // Should all succeed without throwing
                Assert.NotNull(unit);
                Assert.NotNull(cls);
                Assert.NotNull(item);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }
    }
}
