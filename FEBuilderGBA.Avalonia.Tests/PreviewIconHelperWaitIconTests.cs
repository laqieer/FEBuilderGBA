using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #342 (round 6) regression tests for
    /// <see cref="PreviewIconHelper.LoadClassWaitIcon"/>. The earlier rounds
    /// fixed the outer slot/Stretch behaviour, but the producer was still
    /// returning the WRONG sub-rectangle of the LZ77 strip for animType 1
    /// (16x24 cavalier-style icons): WinForms crops the first frame at
    /// <c>Rectangle(0, 8, 16, 24)</c>, whereas the helper was decoding only
    /// the first 2x3 tiles starting at <c>Y=0</c>. Result on screen: the
    /// header padding tile + the top 16 pixels of the rider's body, missing
    /// the bottom 8 pixels (legs). Issue screenshot row 05 "ソシアルナイト"
    /// shows the symptom.
    ///
    /// These tests pin the new contract per-animType and ensure the fix
    /// matches the WinForms crop rectangle exactly without making fragile
    /// "this specific pixel must be non-transparent" assertions about the
    /// underlying sprite art (which would break on future ROM revisions).
    /// </summary>
    [Collection("SharedState")]
    public class PreviewIconHelperWaitIconTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public PreviewIconHelperWaitIconTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        static void EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        // ---------- helpers ----------

        /// <summary>
        /// Discover a wait icon index in the loaded ROM whose animType
        /// matches <paramref name="wantedAnimType"/>. Returns 0 when nothing
        /// matches (the test then skips that scenario gracefully).
        /// </summary>
        static uint FindWaitIconWithAnimType(byte wantedAnimType)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return 0;

            // Scan up to 512 entries (FE8U has ~120; 512 is a comfortable
            // upper bound that still terminates fast on any cartridge).
            const int MAX_SCAN = 512;
            for (int i = 1; i < MAX_SCAN; i++)
            {
                uint entry = baseAddr + (uint)(i * 8);
                if (entry + 8 > (uint)rom.Data.Length) break;

                uint spriteGba = rom.u32(entry + 4);
                if (!U.isPointer(spriteGba)) break;  // table terminator
                byte animType = (byte)rom.u8(entry + 2);
                if (animType == wantedAnimType) return (uint)i;
            }
            return 0;
        }

        /// <summary>
        /// Recompute the WinForms reference crop rectangle for animType 0/1/2
        /// at <c>step=0, height16_limit=false</c> — identical to the table in
        /// the production XML doc. Kept inline in the test so a regression in
        /// the production constants fails the test rather than passing
        /// silently against a shared helper.
        /// </summary>
        static (int x, int y, int w, int h, int stripWidth) WinFormsCrop(byte animType)
        {
            return animType switch
            {
                0 => (0, 0, 16, 16, 16),
                1 => (0, 8, 16, 24, 16),
                2 => (0, 0, 32, 32, 32),
                _ => (0, 0, 16, 16, 16),
            };
        }

        /// <summary>
        /// Render the full LZ77 strip independently via the same Core helper
        /// the production code uses, then crop the WF rectangle manually.
        /// The test compares the production helper's output against THIS
        /// pixel-for-pixel — a true differential against the WF semantics
        /// without leaning on sprite-specific colour values.
        /// </summary>
        static byte[] RenderWinFormsCroppedPixels(uint waitIconIndex, out byte animType, out int outW, out int outH)
        {
            animType = 0; outW = 0; outH = 0;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return null;

            uint entry = baseAddr + waitIconIndex * 8;
            if (entry + 8 > (uint)rom.Data.Length) return null;

            animType = (byte)rom.u8(entry + 2);
            uint spriteGba = rom.u32(entry + 4);
            if (!U.isPointer(spriteGba)) return null;
            uint spriteAddr = U.toOffset(spriteGba);
            if (!U.isSafetyOffset(spriteAddr)) return null;

            uint palAddr = rom.RomInfo.unit_icon_palette_address;
            if (palAddr == 0 || !U.isSafetyOffset(palAddr)) return null;
            byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
            if (palette == null) return null;

            var (cropX, cropY, cropW, cropH, stripWidth) = WinFormsCrop(animType);
            outW = cropW; outH = cropH;

            byte[] stripData = LZ77.decompress(rom.Data, spriteAddr);
            if (stripData == null || stripData.Length == 0) return null;

            // Mirror ImageUtil.CalcHeight with align=8 (4bpp: 2 px/byte).
            int half = stripWidth / 2;
            int stripHeight = stripData.Length / half + (stripData.Length % half != 0 ? 1 : 0);
            int rem = stripHeight % 8;
            if (rem != 0) stripHeight += (8 - rem);
            if (stripHeight <= 0) return null;
            if (cropX + cropW > stripWidth) return null;
            if (cropY + cropH > stripHeight) return null;

            using IImage strip = CoreState.ImageService.Decode4bppTiles(
                stripData, 0, stripWidth, stripHeight, palette);
            if (strip == null) return null;

            byte[] stripIdx = strip.GetPixelData();
            byte[] cropIdx = new byte[cropW * cropH];
            for (int row = 0; row < cropH; row++)
            {
                int srcOff = (cropY + row) * stripWidth + cropX;
                int dstOff = row * cropW;
                Buffer.BlockCopy(stripIdx, srcOff, cropIdx, dstOff, cropW);
            }
            return cropIdx;
        }

        // ---------- per-animType tests ----------

        [Fact]
        public void LoadClassWaitIcon_AnimType0_MatchesWinFormsCrop_16x16()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            EnsureImageService();

            uint id = FindWaitIconWithAnimType(0);
            if (id == 0) { _output.WriteLine("No animType-0 wait icon in this ROM — skipping."); return; }

            using IImage actual = PreviewIconHelper.LoadClassWaitIcon(id);
            Assert.NotNull(actual);
            Assert.Equal(16, actual.Width);
            Assert.Equal(16, actual.Height);

            byte[] expected = RenderWinFormsCroppedPixels(id, out _, out int w, out int h);
            Assert.NotNull(expected);
            Assert.Equal(16, w);
            Assert.Equal(16, h);
            Assert.Equal(expected, actual.GetPixelData());
            _output.WriteLine($"FE8U wait icon 0x{id:X2} animType=0 → 16x16 matches WF crop.");
        }

        [Fact]
        public void LoadClassWaitIcon_AnimType1_MatchesWinFormsCrop_16x24_AtY8()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            EnsureImageService();

            uint id = FindWaitIconWithAnimType(1);
            if (id == 0) { _output.WriteLine("No animType-1 wait icon in this ROM — skipping."); return; }

            using IImage actual = PreviewIconHelper.LoadClassWaitIcon(id);
            Assert.NotNull(actual);
            Assert.Equal(16, actual.Width);
            Assert.Equal(24, actual.Height);

            byte[] expected = RenderWinFormsCroppedPixels(id, out byte at, out int w, out int h);
            Assert.NotNull(expected);
            Assert.Equal((byte)1, at);
            Assert.Equal(16, w);
            Assert.Equal(24, h);
            Assert.Equal(expected, actual.GetPixelData());
            _output.WriteLine($"FE8U wait icon 0x{id:X2} animType=1 → 16x24 matches WF crop at Y=8.");
        }

        [Fact]
        public void LoadClassWaitIcon_AnimType2_MatchesWinFormsCrop_32x32()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            EnsureImageService();

            uint id = FindWaitIconWithAnimType(2);
            if (id == 0) { _output.WriteLine("No animType-2 wait icon in this ROM — skipping."); return; }

            using IImage actual = PreviewIconHelper.LoadClassWaitIcon(id);
            Assert.NotNull(actual);
            Assert.Equal(32, actual.Width);
            Assert.Equal(32, actual.Height);

            byte[] expected = RenderWinFormsCroppedPixels(id, out _, out int w, out int h);
            Assert.NotNull(expected);
            Assert.Equal(32, w);
            Assert.Equal(32, h);
            Assert.Equal(expected, actual.GetPixelData());
            _output.WriteLine($"FE8U wait icon 0x{id:X2} animType=2 → 32x32 matches WF crop.");
        }

        // ---------- strong regression guard: the OLD bad crop must differ ----------

        /// <summary>
        /// For animType 1 specifically, prove the production output differs
        /// from the pre-fix output. Before this fix the helper decoded only
        /// <c>Rectangle(0, 0, 16, 24)</c> from the strip, picking up the
        /// 8-pixel header padding instead of the 8-pixel-offset frame. This
        /// regression test renders BOTH the old and new crop rectangles and
        /// asserts they produce different pixel buffers — so reintroducing
        /// the bug fails the test.
        /// </summary>
        [Fact]
        public void LoadClassWaitIcon_AnimType1_DiffersFromPreFixOrigin()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            EnsureImageService();

            uint id = FindWaitIconWithAnimType(1);
            if (id == 0) { _output.WriteLine("No animType-1 wait icon in this ROM — skipping."); return; }

            // Decode the strip ourselves so we can compute both crops.
            ROM rom = CoreState.ROM;
            uint baseAddr = rom.p32(rom.RomInfo.unit_wait_icon_pointer);
            uint entry = baseAddr + id * 8;
            uint spriteAddr = U.toOffset(rom.u32(entry + 4));
            uint palAddr = rom.RomInfo.unit_icon_palette_address;
            byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
            byte[] stripData = LZ77.decompress(rom.Data, spriteAddr);
            int stripHeight = stripData.Length / 8 + (stripData.Length % 8 != 0 ? 1 : 0);
            int rem = stripHeight % 8;
            if (rem != 0) stripHeight += (8 - rem);

            using IImage strip = CoreState.ImageService.Decode4bppTiles(
                stripData, 0, 16, stripHeight, palette);
            byte[] stripIdx = strip.GetPixelData();

            // Pre-fix crop (the bug): Rectangle(0, 0, 16, 24).
            byte[] preFix = new byte[16 * 24];
            for (int row = 0; row < 24; row++)
                Buffer.BlockCopy(stripIdx, row * 16, preFix, row * 16, 16);

            // Production (post-fix) crop: Rectangle(0, 8, 16, 24).
            using IImage actual = PreviewIconHelper.LoadClassWaitIcon(id);
            Assert.NotNull(actual);
            byte[] postFix = actual.GetPixelData();

            // Must differ — otherwise the bug has snuck back in.
            Assert.NotEqual(preFix, postFix);
            _output.WriteLine($"FE8U wait icon 0x{id:X2} animType=1 post-fix differs from pre-fix crop ✓");
        }

        // ---------- null-safety / edge cases ----------

        [Fact]
        public void LoadClassWaitIcon_OutOfRangeIndex_ReturnsNull()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            EnsureImageService();

            // 0xFFFF is well past any legitimate wait icon table; the entry
            // pointer at offset+4 should fail the U.isPointer guard or fall
            // off the end of ROM.
            using IImage img = PreviewIconHelper.LoadClassWaitIcon(0xFFFF);
            Assert.Null(img);
        }

        [Fact]
        public void LoadClassWaitIcon_NoImageService_ReturnsNull()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping.");
                return;
            }
            // Intentionally do NOT call EnsureImageService — verify the
            // helper bails early when no image service is wired (this used
            // to NRE when LoadROMTiles4bpp accessed CoreState.ImageService).
            var prev = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                using IImage img = PreviewIconHelper.LoadClassWaitIcon(1);
                Assert.Null(img);
            }
            finally
            {
                CoreState.ImageService = prev;
            }
        }
    }
}
