using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchDetectionTests : IDisposable
    {
        readonly ROM? _savedRom;

        public PatchDetectionTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            PatchDetection.ClearAllCaches();
        }

        [Fact]
        public void SearchPriorityCode_NullROM_ReturnsLAT1()
        {
            CoreState.ROM = null;
            var result = PatchDetection.SearchPriorityCode();
            Assert.Equal(PatchDetection.PRIORITY_CODE.LAT1, result);
        }

        [Fact]
        public void SearchDrawFontPatch_NullROM_ReturnsNO()
        {
            CoreState.ROM = null;
            PatchDetection.ClearCacheDrawFont();
            var result = PatchDetection.SearchDrawFontPatch();
            Assert.Equal(PatchDetection.draw_font_enum.NO, result);
        }

        [Fact]
        public void SearchPatchBool_NullROM_ReturnsFalse()
        {
            CoreState.ROM = null;
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt
                {
                    name = "Test", ver = "FE8U", addr = 0x100,
                    data = new byte[] { 0x00 }
                }
            };
            Assert.False(PatchDetection.SearchPatchBool(table));
        }

        [Fact]
        public void SearchPatch_NullROM_ReturnsDefault()
        {
            CoreState.ROM = null;
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt
                {
                    name = "Test", ver = "FE8U", addr = 0x100,
                    data = new byte[] { 0x00 }
                }
            };
            var result = PatchDetection.SearchPatch(table);
            Assert.Equal(0u, result.addr);
        }

        [Fact]
        public void SearchPriorityCode_WithRomParam_NullROM_ReturnsSJIS()
        {
            // The ROM-parameter overload returns SJIS for null
            var result = PatchDetection.SearchPriorityCode(null);
            Assert.Equal(PatchDetection.PRIORITY_CODE.SJIS, result);
        }

        [Fact]
        public void ClearAllCaches_ResetsDrawFontAndTextEngine()
        {
            PatchDetection.ClearAllCaches();
            // After clearing, no-arg overloads should re-evaluate
            // With null ROM, they return safe defaults
            CoreState.ROM = null;
            Assert.Equal(PatchDetection.draw_font_enum.NO, PatchDetection.SearchDrawFontPatch());
        }
    }
}
