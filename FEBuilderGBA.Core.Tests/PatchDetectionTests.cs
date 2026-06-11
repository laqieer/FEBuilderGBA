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

        // ---------------------------------------------------------------
        // OPClassReel patch detectors (added for gap-sweep #419).
        //
        // - OPClassReelAnimationIDOver255 has ONLY an FE8J signature in
        //   PatchUtil; FE8U must always return false.
        // - OPClassReelSort has BOTH FE8J and FE8U signatures; tests
        //   below assert both versions detect the patch correctly.
        //   (Copilot bot review thread PRRT_kwDOH0Mc1M6ETZ4g on PR #544
        //   flagged the previous wording as misleading.)
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a minimal FE8J ROM (BE8J01 signature, 0x1100000 bytes
        /// so Rom.LoadLow assigns ROMFE8JP). Optionally plants the patch
        /// signature bytes at the well-known addresses.
        /// </summary>
        static ROM MakeFe8jRom(bool plantOver255 = false, bool plantReelSort = false)
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            if (plantOver255)
            {
                // Source: PatchUtil.OPClassReelAnimationIDOver255Low — FE8J @ 0xB86B0 / {0x59, 0x8A}.
                data[0xB86B0] = 0x59;
                data[0xB86B0 + 1] = 0x8A;
            }
            if (plantReelSort)
            {
                // Source: PatchUtil.OPClassReelSortPatchLow — FE8J @ 0xB8C80 / {0x04, 0x4B, 0x1B, 0x68}.
                data[0xB8C80] = 0x04;
                data[0xB8C80 + 1] = 0x4B;
                data[0xB8C80 + 2] = 0x1B;
                data[0xB8C80 + 3] = 0x68;
            }
            rom.LoadLow("synthetic-fe8j.gba", data, "BE8J01");
            return rom;
        }

        /// <summary>Build a minimal FE8U ROM (BE8E01) — Over255 must always be false here.</summary>
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            var data = new byte[0x1100000];
            rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
            return rom;
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(null));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8J_PatchAbsent_ReturnsFalse()
        {
            var rom = MakeFe8jRom(plantOver255: false);
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8J_PatchPresent_ReturnsTrue()
        {
            var rom = MakeFe8jRom(plantOver255: true);
            Assert.True(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelAnimationIDOver255Detect_FE8U_AlwaysFalse()
        {
            // The signature only matches "FE8J" — even if we synthetically
            // wrote 0x59, 0x8A at 0xB86B0 on an FE8U ROM, the version
            // filter rejects it.
            var rom = MakeFe8uRom();
            rom.Data[0xB86B0] = 0x59;
            rom.Data[0xB86B0 + 1] = 0x8A;
            Assert.False(PatchDetection.OPClassReelAnimationIDOver255Detect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(null));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8J_PatchAbsent_ReturnsFalse()
        {
            var rom = MakeFe8jRom(plantReelSort: false);
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8J_PatchPresent_ReturnsTrue()
        {
            var rom = MakeFe8jRom(plantReelSort: true);
            Assert.True(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8U_PatchPresent_ReturnsTrue()
        {
            // OPClassReelSort has both FE8J and FE8U entries (FE8U @ 0xB40EC).
            var rom = MakeFe8uRom();
            rom.Data[0xB40EC] = 0x04;
            rom.Data[0xB40EC + 1] = 0x4B;
            rom.Data[0xB40EC + 2] = 0x1B;
            rom.Data[0xB40EC + 3] = 0x68;
            Assert.True(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        [Fact]
        public void OPClassReelSortPatchDetect_FE8U_PatchAtJpOffset_StillFalse()
        {
            // Writing the FE8J signature at the FE8J offset on an FE8U
            // ROM must NOT match — the version filter rejects it.
            var rom = MakeFe8uRom();
            rom.Data[0xB8C80] = 0x04;
            rom.Data[0xB8C80 + 1] = 0x4B;
            rom.Data[0xB8C80 + 2] = 0x1B;
            rom.Data[0xB8C80 + 3] = 0x68;
            Assert.False(PatchDetection.OPClassReelSortPatchDetect(rom));
        }

        // CSkillSys "class skill extends" detector tests moved to
        // SkillSystemPatchScannerTests.cs (single source of truth — see
        // PatchDetection.cs comment block).

        // ---------------------------------------------------------------
        // AntiHuffman (un-Huffman) patch detector (#1028 Slice D).
        // Table-driven coverage of all six per-version signatures plus the
        // wrong-version + out-of-range negatives. Signatures source:
        // PatchDetection.AntiHuffmanTable (mirrors WF
        // PatchUtil.SearchAntiHuffmanPatch_Low).
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a minimal ROM for an AntiHuffman version signature, optionally
        /// planting the patch bytes at the given offset.
        /// </summary>
        static ROM MakeAntiHuffmanRom(string signature, uint? plantAddr = null, byte[]? plantBytes = null)
        {
            var rom = new ROM();
            // FE6 only needs 0x800000; all others need 0x1000000. Use the
            // larger size unconditionally so every AntiHuffman addr fits.
            var data = new byte[0x1000000];
            if (plantAddr != null && plantBytes != null)
            {
                for (int i = 0; i < plantBytes.Length; i++)
                    data[plantAddr.Value + i] = plantBytes[i];
            }
            rom.LoadLow("synthetic-antihuffman.gba", data, signature);
            return rom;
        }

        [Fact]
        public void SearchAntiHuffmanPatch_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.SearchAntiHuffmanPatch((ROM?)null));
        }

        [Theory]
        // ver signature, addr, the exact signature bytes (from AntiHuffmanTable).
        [InlineData("AFEJ01", 0x384c, new byte[] { 0x03, 0xB5, 0x02, 0xB0 })] // FE6
        [InlineData("AE7J01", 0x13324, new byte[] { 0x02, 0x49, 0x28, 0x1C })] // FE7J
        [InlineData("AE7E01", 0x12C6C, new byte[] { 0x02, 0x49, 0x28, 0x1C })] // FE7U
        [InlineData("BE8J01", 0x2af4, new byte[] { 0x00, 0xB5, 0xC2, 0x0F })] // FE8J
        [InlineData("BE8E01", 0x2BA4, new byte[] { 0x00, 0xB5, 0xC2, 0x0F })] // FE8U normal
        [InlineData("BE8E01", 0x2ba4, new byte[] { 0x78, 0x47, 0xC0, 0x46 })] // FE8U snake1
        public void SearchAntiHuffmanPatch_SignaturePresent_ReturnsTrue(string sig, uint addr, byte[] bytes)
        {
            var rom = MakeAntiHuffmanRom(sig, addr, bytes);
            Assert.True(PatchDetection.SearchAntiHuffmanPatch(rom));
        }

        [Theory]
        [InlineData("AFEJ01")] // FE6
        [InlineData("AE7J01")] // FE7J
        [InlineData("AE7E01")] // FE7U
        [InlineData("BE8J01")] // FE8J
        [InlineData("BE8E01")] // FE8U
        public void SearchAntiHuffmanPatch_SignatureAbsent_ReturnsFalse(string sig)
        {
            // Clean ROM (all zero bytes) — no signature planted.
            var rom = MakeAntiHuffmanRom(sig);
            Assert.False(PatchDetection.SearchAntiHuffmanPatch(rom));
        }

        [Fact]
        public void SearchAntiHuffmanPatch_FE8U_Snake1Present_ReturnsTrue()
        {
            // Both FE8U signatures live at 0x2BA4; ensure the snake1 variant
            // (0x78,0x47,0xC0,0x46) is detected independently of the normal one.
            var rom = MakeAntiHuffmanRom("BE8E01", 0x2ba4, new byte[] { 0x78, 0x47, 0xC0, 0x46 });
            Assert.True(PatchDetection.SearchAntiHuffmanPatch(rom));
        }

        [Fact]
        public void SearchAntiHuffmanPatch_WrongVersion_SignatureIgnored_ReturnsFalse()
        {
            // Plant the FE8U signature bytes at the FE8U offset, but on an FE6
            // ROM — the version filter must reject it (no FE6 row matches
            // {0x00,0xB5,0xC2,0x0F} at 0x2BA4).
            var rom = MakeAntiHuffmanRom("AFEJ01", 0x2BA4, new byte[] { 0x00, 0xB5, 0xC2, 0x0F });
            Assert.False(PatchDetection.SearchAntiHuffmanPatch(rom));
        }

        [Fact]
        public void SearchAntiHuffmanPatch_UnknownVersion_ReturnsFalse()
        {
            // A ROM whose version (NAZO / ROMFE0) matches NONE of the six
            // AntiHuffman signature rows — the version filter rejects every row,
            // so no signature window is ever read and the result is false. This is
            // the path a too-small / unrecognized ROM takes (out-of-range for the
            // signature table): the addr is never read because the version never
            // matches.
            var rom = new ROM();
            rom.LoadLow("synthetic-nazo.gba", new byte[0x1000000], "NAZO");
            Assert.NotNull(rom.RomInfo);
            Assert.False(PatchDetection.SearchAntiHuffmanPatch(rom));
        }

        [Fact]
        public void SearchAntiHuffmanPatch_AddrBeyondData_ReturnsFalseNoThrow()
        {
            // Out-of-range guard: getBinaryData clamps the read window to
            // Data.Length, so even if the planted bytes landed past the signature
            // window the comparison fails safely. Here we plant the FE8U signature
            // bytes at a WRONG (later) offset; the fixed 0x2BA4 read sees zeroes,
            // so the detector returns false without throwing.
            var rom = MakeAntiHuffmanRom("BE8E01", 0x500000, new byte[] { 0x00, 0xB5, 0xC2, 0x0F });
            Assert.False(PatchDetection.SearchAntiHuffmanPatch(rom));
        }
    }
}
