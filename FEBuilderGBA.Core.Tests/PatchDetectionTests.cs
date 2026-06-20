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

        // ===============================================================
        // #1261 slice 2ad — four producer patch detectors.
        //
        // - SearchUnitActionReworkPatch: DELEGATE wrap. Each RomInfo subclass
        //   exposes patch_unitaction_rework_hack(out enable_value) = (addr,
        //   expected u32). The patch is present when u32(addr)==expected.
        // - ItemUsingExtendsPatch: FE8U byte table @ 0x28E80.
        // - SearchClassType: FE8U-only CompareByte(0x2AAEC) vs two patterns.
        // - SearchGrowsMod: FE8U Vennou byte table @ 0x02BA2A, then FE8U
        //   whole-ROM grep of the SkillSystems signature.
        // ===============================================================

        static ROM MakeRom(string signature, byte[] data) // helper for the slice-2ad detectors
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("synthetic-2ad.gba", data, signature);
            Assert.True(ok, "LoadLow did not recognize version string: " + signature);
            return rom;
        }

        // ---- SearchUnitActionReworkPatch (delegate wrap) ----

        [Fact]
        public void SearchUnitActionReworkPatch_NullRom_ReturnsFalse()
        {
            Assert.False(PatchDetection.SearchUnitActionReworkPatch((ROM?)null));
        }

        [Fact]
        public void SearchUnitActionReworkPatch_FE8U_PatchAbsent_ReturnsFalse()
        {
            // A zeroed FE8U ROM: u32(patch addr) == 0 != enable_value -> false.
            var rom = MakeRom("BE8E01", new byte[0x1000000]);
            Assert.False(PatchDetection.SearchUnitActionReworkPatch(rom));
        }

        [Fact]
        public void SearchUnitActionReworkPatch_FE8U_PatchPresent_ReturnsTrue()
        {
            // Plant the per-version expected u32 at the per-version address from the
            // RomInfo delegate itself (so this stays faithful even if ROMFE8U's
            // (addr, value) ever changes). FE8U: addr=0x031F08, value=0x4C03B510.
            var rom = MakeRom("BE8E01", new byte[0x1000000]);
            uint expected;
            uint addr = rom.RomInfo.patch_unitaction_rework_hack(out expected);
            Assert.NotEqual(0u, addr);
            // little-endian write of `expected` at `addr`.
            rom.Data[addr + 0] = (byte)(expected & 0xFF);
            rom.Data[addr + 1] = (byte)((expected >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((expected >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((expected >> 24) & 0xFF);
            Assert.True(PatchDetection.SearchUnitActionReworkPatch(rom));
        }

        [Fact]
        public void SearchUnitActionReworkPatch_FE8J_PatchPresent_ReturnsTrue()
        {
            // FE8J has its own (addr, value) — prove the delegate-wrap is version-correct.
            var rom = MakeRom("BE8J01", new byte[0x1100000]);
            uint expected;
            uint addr = rom.RomInfo.patch_unitaction_rework_hack(out expected);
            Assert.NotEqual(0u, addr);
            rom.Data[addr + 0] = (byte)(expected & 0xFF);
            rom.Data[addr + 1] = (byte)((expected >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((expected >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((expected >> 24) & 0xFF);
            Assert.True(PatchDetection.SearchUnitActionReworkPatch(rom));
        }

        // ---- ItemUsingExtendsPatch (FE8U byte table @ 0x28E80) ----

        static readonly byte[] IERSig = new byte[]
        {
            0x03, 0x4B, 0x14, 0x22, 0x50, 0x43, 0x40, 0x18,
            0xC0, 0x18, 0x00, 0x68, 0x70, 0x47, 0x00, 0x00
        };

        [Fact]
        public void ItemUsingExtendsPatch_NullRom_ReturnsNO()
        {
            Assert.Equal(PatchDetection.ItemUsingExtends_extends.NO,
                PatchDetection.ItemUsingExtendsPatch((ROM?)null));
        }

        [Fact]
        public void ItemUsingExtendsPatch_FE8U_PatchAbsent_ReturnsNO()
        {
            var rom = MakeRom("BE8E01", new byte[0x1000000]);
            Assert.Equal(PatchDetection.ItemUsingExtends_extends.NO,
                PatchDetection.ItemUsingExtendsPatch(rom));
        }

        [Fact]
        public void ItemUsingExtendsPatch_FE8U_PatchPresent_ReturnsIER()
        {
            var data = new byte[0x1000000];
            Array.Copy(IERSig, 0, data, 0x28E80, IERSig.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.ItemUsingExtends_extends.IER,
                PatchDetection.ItemUsingExtendsPatch(rom));
        }

        [Fact]
        public void ItemUsingExtendsPatch_FE8J_SigAtFE8UOffset_StillNO()
        {
            // The table only has an FE8U row — the version filter rejects FE8J even
            // with the bytes planted at the FE8U offset.
            var data = new byte[0x1100000];
            Array.Copy(IERSig, 0, data, 0x28E80, IERSig.Length);
            var rom = MakeRom("BE8J01", data);
            Assert.Equal(PatchDetection.ItemUsingExtends_extends.NO,
                PatchDetection.ItemUsingExtendsPatch(rom));
        }

        // ---- SearchClassType (FE8U-only CompareByte @ 0x2AAEC) ----

        static readonly byte[] ClassTypePat1 = new byte[] { 0x00, 0x25, 0x00, 0x28, 0x00, 0xD0, 0x05, 0x1C };
        static readonly byte[] ClassTypePat2 = new byte[] { 0x01, 0x4B, 0xA6, 0xF0, 0xED, 0xFE, 0x01, 0xE0 };

        [Fact]
        public void SearchClassType_NullRom_ReturnsNO()
        {
            Assert.Equal(PatchDetection.class_type_extends.NO,
                PatchDetection.SearchClassType((ROM?)null));
        }

        [Fact]
        public void SearchClassType_FE8U_PatchAbsent_ReturnsNO()
        {
            var rom = MakeRom("BE8E01", new byte[0x1000000]);
            Assert.Equal(PatchDetection.class_type_extends.NO,
                PatchDetection.SearchClassType(rom));
        }

        [Fact]
        public void SearchClassType_FE8U_Pattern1_ReturnsRework()
        {
            var data = new byte[0x1000000];
            Array.Copy(ClassTypePat1, 0, data, 0x2AAEC, ClassTypePat1.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.class_type_extends.SkillSystems_Rework,
                PatchDetection.SearchClassType(rom));
        }

        [Fact]
        public void SearchClassType_FE8U_Pattern2_ReturnsRework()
        {
            var data = new byte[0x1000000];
            Array.Copy(ClassTypePat2, 0, data, 0x2AAEC, ClassTypePat2.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.class_type_extends.SkillSystems_Rework,
                PatchDetection.SearchClassType(rom));
        }

        [Fact]
        public void SearchClassType_FE8J_PatternAtFE8UOffset_StillNO()
        {
            // FE8J is multibyte -> the version-8 && !is_multibyte gate rejects it
            // even with the pattern present at 0x2AAEC.
            var data = new byte[0x1100000];
            Array.Copy(ClassTypePat1, 0, data, 0x2AAEC, ClassTypePat1.Length);
            var rom = MakeRom("BE8J01", data);
            Assert.Equal(PatchDetection.class_type_extends.NO,
                PatchDetection.SearchClassType(rom));
        }

        // ---- SearchGrowsMod (FE8U Vennou table + FE8U SkillSystems grep) ----

        static readonly byte[] VennouSig = new byte[]
        {
            0x4E, 0x46, 0x45, 0x46, 0x60, 0xB4, 0x8B, 0xB0, 0x07, 0x1C, 0xFF, 0xF7, 0xDE, 0xFF, 0x00, 0x06,
            0x00, 0x28, 0x00, 0xD1, 0x8E, 0xE0, 0x78, 0x7A, 0x63, 0x28, 0x00, 0xD8, 0x8A, 0xE0, 0x03, 0x1C,
            0x64, 0x3B, 0x7B, 0x72, 0x38, 0x7A, 0x42, 0x1C, 0x3A, 0x72, 0x38, 0x68, 0x79, 0x68, 0x80, 0x6A,
            0x89, 0x6A, 0x08, 0x43, 0x80, 0x21, 0x09, 0x03, 0x08, 0x40, 0x00, 0x28, 0x04, 0xD0, 0x10, 0x06,
            0x00, 0x16, 0x0A, 0x28, 0x0B, 0xD1, 0x03, 0xE0, 0x10, 0x06, 0x00
        };

        static readonly byte[] SkillSystemsSig = new byte[]
        {
            0x17, 0x49, 0x40, 0x18, 0x22, 0x21, 0x41, 0x5C, 0x01, 0x22, 0x11, 0x42, 0x06, 0xD0, 0xC0, 0x68,
            0x00, 0x28, 0x03, 0xD0, 0x80, 0x57, 0x2D, 0x18, 0x00, 0x2F, 0x02, 0xD0, 0x02, 0x33, 0x08, 0x2B,
            0xE5, 0xDD, 0x20, 0x1C, 0x10, 0x49, 0x0F, 0x4A
        };

        [Fact]
        public void SearchGrowsMod_NullRom_ReturnsNO()
        {
            Assert.Equal(PatchDetection.growth_mod_extends.NO,
                PatchDetection.SearchGrowsMod((ROM?)null));
        }

        [Fact]
        public void SearchGrowsMod_FE8U_PatchAbsent_ReturnsNO()
        {
            var rom = MakeRom("BE8E01", new byte[0x1000000]);
            Assert.Equal(PatchDetection.growth_mod_extends.NO,
                PatchDetection.SearchGrowsMod(rom));
        }

        [Fact]
        public void SearchGrowsMod_FE8U_VennouTablePresent_ReturnsVennou()
        {
            var data = new byte[0x1000000];
            Array.Copy(VennouSig, 0, data, 0x02BA2A, VennouSig.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.growth_mod_extends.Vennou,
                PatchDetection.SearchGrowsMod(rom));
        }

        [Fact]
        public void SearchGrowsMod_FE8U_SkillSystemsGrep_ReturnsSkillSystems()
        {
            // Plant the SkillSystems signature AFTER compress_image_borderline_address
            // (the grep start). With NO Vennou table present, stage 2 finds it.
            var rom0 = MakeRom("BE8E01", new byte[0x1000000]);
            uint border = rom0.RomInfo.compress_image_borderline_address;
            uint plantAt = U.Padding4(border + 0x100); // 4-aligned, inside the scan window
            var data = new byte[0x1000000];
            Array.Copy(SkillSystemsSig, 0, data, plantAt, SkillSystemsSig.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.growth_mod_extends.SkillSystems,
                PatchDetection.SearchGrowsMod(rom));
        }

        [Fact]
        public void SearchGrowsMod_FE8U_VennouWins_WhenBothPresent()
        {
            // Stage 1 (Vennou table) is checked first; even if the SkillSystems
            // signature is also present, Vennou is returned (WF order).
            var rom0 = MakeRom("BE8E01", new byte[0x1000000]);
            uint border = rom0.RomInfo.compress_image_borderline_address;
            uint plantAt = U.Padding4(border + 0x100);
            var data = new byte[0x1000000];
            Array.Copy(VennouSig, 0, data, 0x02BA2A, VennouSig.Length);
            Array.Copy(SkillSystemsSig, 0, data, plantAt, SkillSystemsSig.Length);
            var rom = MakeRom("BE8E01", data);
            Assert.Equal(PatchDetection.growth_mod_extends.Vennou,
                PatchDetection.SearchGrowsMod(rom));
        }

        [Fact]
        public void SearchGrowsMod_FE8J_SignaturesPresent_StillNO()
        {
            // Both stages are FE8U-gated -> FE8J never matches.
            var rom0 = MakeRom("BE8J01", new byte[0x1100000]);
            uint border = rom0.RomInfo.compress_image_borderline_address;
            uint plantAt = U.Padding4(border + 0x100);
            var data = new byte[0x1100000];
            Array.Copy(VennouSig, 0, data, 0x02BA2A, VennouSig.Length);
            Array.Copy(SkillSystemsSig, 0, data, plantAt, SkillSystemsSig.Length);
            var rom = MakeRom("BE8J01", data);
            Assert.Equal(PatchDetection.growth_mod_extends.NO,
                PatchDetection.SearchGrowsMod(rom));
        }
    }
}
