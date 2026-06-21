// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-4 (#1261) — the TYPE=IMAGE terminal
// PatchForm producer arm (option-B epic, sub-slice 4 of 11):
//   RebuildProducerCore.EmitPatchImage          = WF PatchForm.MakePatchStructDataListForIMAGE @:6738
//   RebuildProducerCore.PatchImageVariantLength = the reusable per-variant length math
//                                                 (factored so s2pf-5 STRUCT PatchImage_* reuses it).
//
// ALL 8 variants ported (HEADERTSA_POINTER wired in s2pf-7 via EmitHeaderTsaPointer /
// CalcHeaderTsaLength = WF CalcByteLengthForHeaderTSAData). Coverage (synthetic in-memory
// FE8U ROM + synthetic PatchSt — no real GBA ROM file, mirroring RebuildProducerPatchAddrSwitchTests):
//   1. EmitPatchImage:
//        - IMAGE_POINTER    -> w*h/2,  IMG     (raw 4bpp)
//        - ZIMAGE_POINTER   -> LZ77,   LZ77IMG (hand-authored stream, getCompressedSize)
//        - Z256IMAGE_POINTER-> LZ77,   LZ77IMG
//        - TSA_POINTER      -> w*h/32, TSA
//        - ZTSA_POINTER     -> LZ77,   LZ77TSA
//        - ZHEADERTSA_POINTER -> LZ77, LZ77TSA
//        - HEADERTSA_POINTER -> 2+(x+1)*(y+1)*2, HEADERTSA (s2pf-7)
//        - PALETTE_POINTER  -> count*0x20, PAL  (count default 1; explicit count)
//        - PALETTE_ADDRESS  -> count*0x20, PAL  (direct address, pointer slot NOT_FOUND;
//                              only when PALETTE_POINTER absent/unsafe)
//        - WIDTH/HEIGHT default "8"; PALETTE default "1"
//        - emitted Address addr/length/pointer/type/name byte-faithful to WF
//        - per-variant safety gates (p==0 for IMAGE/ZIMAGE/Z256IMAGE; unsafe slot; unsafe target)
//        - all-variants-absent -> nothing (no-op), null-arg guards.
//   2. PatchImageVariantLength: the raw math (IMAGE=w*h/2, TSA=w*h/32, PALETTE=count*0x20,
//      LZ77 variants = getCompressedSize, HEADERTSA=2+(x+1)*(y+1)*2), and the unknown-key throw.
//   3. Integration through the public orchestrator MakePatchStructDataListCore on a staged
//      config/patch2 tree (proves the IMAGE switch arm is wired).
//   4. The no-patch-dir invariant is re-asserted in RebuildProducerPatchScaffoldTests; this
//      file additionally proves the IMAGE arm is a clean no-op when its params are absent.

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchImageTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        string? _tempDir;

        public RebuildProducerPatchImageTests()
        {
            CoreState.Language = "en";
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
            CoreState.BaseDirectory = _savedBaseDir;
            try { if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // 16 MiB zero-filled FE8U ROM (LoadLow minimum for BE8E01) — same idiom as
        // RebuildProducerPatchAddrSwitchTests.MakeRom. Also sets CoreState.ROM (the
        // Address.AddAddress sink uses the single-arg U.isSafetyOffset overload bound to it).
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            CoreState.ROM = rom;
            return rom;
        }

        static PatchInstallCore.PatchSt MakePatch(string name, params (string key, string value)[] kv)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = name,
                PatchFileName = name + ".txt",
                Param = new Dictionary<string, string>()
            };
            foreach (var (key, value) in kv)
            {
                p.Param[key] = value;
            }
            return p;
        }

        static Address Single(List<Address> list, string info)
        {
            return Assert.Single(list, a => a.Info == info);
        }

        // Hand-author a VALID all-literal LZ77 stream of `uncompSize` uncompressed bytes at `offset`
        // (slice-2e idiom from RebuildProducerCoreTests.WriteLz77AllLiteral). Returns the byte length the
        // stream occupies (== LZ77.getCompressedSize on it): header(4) + ceil(N/8) flags + N literals.
        static uint WriteLz77AllLiteral(ROM rom, uint offset, int uncompSize)
        {
            var bytes = new List<byte>();
            bytes.Add(0x10);
            bytes.Add((byte)(uncompSize & 0xFF));
            bytes.Add((byte)((uncompSize >> 8) & 0xFF));
            bytes.Add((byte)((uncompSize >> 16) & 0xFF));
            int written = 0;
            while (written < uncompSize)
            {
                bytes.Add(0x00); // flag: next 8 bytes all literal
                for (int b = 0; b < 8 && written < uncompSize; b++, written++)
                {
                    bytes.Add((byte)(0x40 + (written & 0x3F)));
                }
            }
            for (int i = 0; i < bytes.Count; i++)
            {
                rom.write_u8(offset + (uint)i, bytes[i]);
            }
            uint clen = LZ77.getCompressedSize(rom.Data, offset);
            Assert.True(clen > 0, "hand-authored LZ77 stream must be valid (getCompressedSize > 0)");
            Assert.Equal((uint)bytes.Count, clen);
            return clen;
        }

        // Plant a header-TSA master header {x, y} (each stored value = dimension minus one) at `offset`
        // and return the WF byte length: 2 + (x+1)*(y+1)*2 (the body cells are left zero — the length
        // depends only on the 2-byte header, matching CalcHeaderTsaLength = WF CalcByteLengthForHeaderTSAData).
        static uint WriteHeaderTsa(ROM rom, uint offset, byte x, byte y)
        {
            rom.write_u8(offset + 0, x);
            rom.write_u8(offset + 1, y);
            return 2u + ((uint)x + 1) * ((uint)y + 1) * 2;
        }

        // ====================================================================
        // 1. PatchImageVariantLength — the reusable math (WF :6738 lengths)
        // ====================================================================

        [Fact]
        public void PatchImageVariantLength_RawImage_IsWidthTimesHeightOver2()
        {
            var rom = MakeRom();
            // 8x8 4bpp = 32 bytes.
            Assert.Equal(32u, RebuildProducerCore.PatchImageVariantLength(rom, "IMAGE", 0x1000, 8, 8, 1));
            // 240x160 4bpp = 19200 bytes.
            Assert.Equal(19200u, RebuildProducerCore.PatchImageVariantLength(rom, "IMAGE", 0x1000, 240, 160, 1));
        }

        [Fact]
        public void PatchImageVariantLength_Tsa_IsWidthTimesHeightOver32()
        {
            var rom = MakeRom();
            // 8x8 TSA = 2 bytes (NOT 32 — the /2 vs /32 must not be swapped).
            Assert.Equal(2u, RebuildProducerCore.PatchImageVariantLength(rom, "TSA", 0x1000, 8, 8, 1));
            Assert.Equal(1200u, RebuildProducerCore.PatchImageVariantLength(rom, "TSA", 0x1000, 240, 160, 1));
        }

        [Fact]
        public void PatchImageVariantLength_Palette_IsCountTimes0x20()
        {
            var rom = MakeRom();
            Assert.Equal(0x20u, RebuildProducerCore.PatchImageVariantLength(rom, "PALETTE", 0x1000, 8, 8, 1));
            Assert.Equal(0x60u, RebuildProducerCore.PatchImageVariantLength(rom, "PALETTE", 0x1000, 8, 8, 3));
        }

        [Theory]
        [InlineData("ZIMAGE")]
        [InlineData("Z256IMAGE")]
        [InlineData("ZTSA")]
        [InlineData("ZHEADERTSA")]
        public void PatchImageVariantLength_Lz77Variants_UseGetCompressedSize(string key)
        {
            var rom = MakeRom();
            uint at = 0x3000;
            uint expect = WriteLz77AllLiteral(rom, at, 16);
            Assert.Equal(expect, RebuildProducerCore.PatchImageVariantLength(rom, key, at, 8, 8, 1));
        }

        [Fact]
        public void PatchImageVariantLength_HeaderTsa_MatchesCalcByteLengthForHeaderTSAData()
        {
            var rom = MakeRom();
            // WF ImageUtil.CalcByteLengthForHeaderTSAData: 2 + (x+1)*(y+1)*2 from the 2-byte master
            // header. Plant {0x07, 0x03} -> (7+1)*(3+1)=32 cells -> 2 + 32*2 = 66 bytes. The +1 on BOTH
            // dimensions and the /2-vs-/32 axes must not be swapped (an off-by-one mis-sizes the region).
            uint at = 0x3000;
            uint expect = WriteHeaderTsa(rom, at, 0x07, 0x03);
            Assert.Equal(66u, expect);
            Assert.Equal(expect, RebuildProducerCore.PatchImageVariantLength(rom, "HEADERTSA", at, 8, 8, 1));
        }

        [Fact]
        public void PatchImageVariantLength_HeaderTsa_NearEof_IsZero_NoThrow()
        {
            var rom = MakeRom();
            // pos + 2 >= Length -> WF returns 0 (the 2-byte header itself does not fit). Out-of-bounds
            // header -> 0 length (the caller's safety gate then skips emission — NOT a 0-length region
            // that would drop following tiles).
            uint len = 0;
            uint nearEof = (uint)rom.Data.Length - 1;
            var ex = Record.Exception(() => len = RebuildProducerCore.PatchImageVariantLength(rom, "HEADERTSA", nearEof, 8, 8, 1));
            Assert.Null(ex);
            Assert.Equal(0u, len);
        }

        [Fact]
        public void PatchImageVariantLength_Lz77_MalformedStream_IsZero_NoThrow()
        {
            var rom = MakeRom();
            // 0x10 header claiming a huge decompressed size with no body -> getCompressedSize 0 (EOF-safe).
            rom.write_u8(0x4000, 0x10);
            rom.write_u8(0x4001, 0xFF);
            rom.write_u8(0x4002, 0xFF);
            rom.write_u8(0x4003, 0xFF);
            uint len = 0;
            var ex = Record.Exception(() => len = RebuildProducerCore.PatchImageVariantLength(rom, "ZIMAGE", 0x4000, 8, 8, 1));
            Assert.Null(ex);
            Assert.Equal(0u, len);
        }

        [Fact]
        public void PatchImageVariantLength_UnknownKey_Throws()
        {
            var rom = MakeRom();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RebuildProducerCore.PatchImageVariantLength(rom, "NOTAVARIANT", 0x1000, 8, 8, 1));
        }

        // ====================================================================
        // 2. EmitPatchImage — the 6 ported variants
        // ====================================================================

        // Plant a GBA pointer (toPointer) at `slot` -> `target`. Helper for the deref variants.
        static void PlantPointer(ROM rom, uint slot, uint target)
        {
            U.write_u32(rom.Data, slot, U.toPointer(target));
        }

        [Fact]
        public void EmitPatchImage_ImagePointer_DefaultSize_EmitsImgWidthHeightOver2()
        {
            var rom = MakeRom();
            const uint slot = 0x1000, target = 0x2000;
            PlantPointer(rom, slot, target);

            var list = new List<Address>();
            // No WIDTH/HEIGHT -> default 8x8 -> 8*8/2 = 32 bytes, IMG.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Img", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0x1000")), isPointerOnly: false);

            var a = Single(list, "Img@IMAGE_POINTER");
            Assert.Equal(target, a.Addr);
            Assert.Equal(32u, a.Length);
            Assert.Equal(slot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.IMG, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_ImagePointer_ExplicitSize_UsesWidthHeight()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Big", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0x1000"),
                          ("WIDTH", "16"), ("HEIGHT", "32")), isPointerOnly: false);

            var a = Single(list, "Big@IMAGE_POINTER");
            Assert.Equal(16u * 32u / 2u, a.Length); // 256
            Assert.Equal(Address.DataTypeEnum.IMG, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_TsaPointer_EmitsTsaWidthHeightOver32()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("T", ("TYPE", "IMAGE"), ("TSA_POINTER", "0x1000"),
                          ("WIDTH", "240"), ("HEIGHT", "160")), isPointerOnly: false);

            var a = Single(list, "T@TSA_POINTER");
            Assert.Equal(0x2000u, a.Addr);
            Assert.Equal(240u * 160u / 32u, a.Length); // 1200
            Assert.Equal(0x1000u, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.TSA, a.DataType);
        }

        [Theory]
        [InlineData("ZIMAGE_POINTER", Address.DataTypeEnum.LZ77IMG)]
        [InlineData("Z256IMAGE_POINTER", Address.DataTypeEnum.LZ77IMG)]
        [InlineData("ZTSA_POINTER", Address.DataTypeEnum.LZ77TSA)]
        [InlineData("ZHEADERTSA_POINTER", Address.DataTypeEnum.LZ77TSA)]
        public void EmitPatchImage_Lz77Variants_EmitGetCompressedSizeLength(string paramKey, Address.DataTypeEnum expectType)
        {
            var rom = MakeRom();
            const uint slot = 0x1000, target = 0x3000;
            PlantPointer(rom, slot, target);
            uint expect = WriteLz77AllLiteral(rom, target, 24);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Z", ("TYPE", "IMAGE"), (paramKey, "0x1000")), isPointerOnly: false);

            var a = Single(list, "Z@" + paramKey);
            Assert.Equal(target, a.Addr);
            Assert.Equal(expect, a.Length);
            Assert.Equal(slot, a.Pointer);
            Assert.Equal(expectType, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_PalettePointer_DefaultCount_Is0x20()
        {
            var rom = MakeRom();
            const uint slot = 0x1000, target = 0x2000;
            PlantPointer(rom, slot, target);

            var list = new List<Address>();
            // No PALETTE -> default count 1 -> 0x20 bytes.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Pal", ("TYPE", "IMAGE"), ("PALETTE_POINTER", "0x1000")), isPointerOnly: false);

            var a = Single(list, "Pal@PALETTE_POINTER");
            Assert.Equal(target, a.Addr);
            Assert.Equal(0x20u, a.Length);
            Assert.Equal(slot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.PAL, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_PalettePointer_ExplicitCount_IsCountTimes0x20()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("P4", ("TYPE", "IMAGE"), ("PALETTE_POINTER", "0x1000"), ("PALETTE", "4")), isPointerOnly: false);

            var a = Single(list, "P4@PALETTE_POINTER");
            Assert.Equal(0x80u, a.Length); // 4 * 0x20
            Assert.Equal(Address.DataTypeEnum.PAL, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_PaletteAddress_DirectAddress_PointerIsNotFound()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // No PALETTE_POINTER -> the else-branch reads PALETTE_ADDRESS as a DIRECT address
            // (no deref). Pointer slot is NOT_FOUND (WF passes U.NOT_FOUND). Count 2 -> 0x40.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("PA", ("TYPE", "IMAGE"), ("PALETTE_ADDRESS", "0x5000"), ("PALETTE", "2")), isPointerOnly: false);

            var a = Single(list, "PA@PALETTE_ADDRESS");
            Assert.Equal(0x5000u, a.Addr);
            Assert.Equal(0x40u, a.Length);
            Assert.Equal(U.NOT_FOUND, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.PAL, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_PalettePointerPresent_DoesNotUsePaletteAddress()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000);
            var list = new List<Address>();
            // Both PALETTE_POINTER (safe) and PALETTE_ADDRESS present -> WF takes the POINTER branch ONLY.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Both", ("TYPE", "IMAGE"),
                          ("PALETTE_POINTER", "0x1000"), ("PALETTE_ADDRESS", "0x5000")), isPointerOnly: false);

            Assert.Single(list, a => a.Info == "Both@PALETTE_POINTER" && a.Addr == 0x2000u);
            Assert.DoesNotContain(list, a => a.Info == "Both@PALETTE_ADDRESS");
        }

        // ---- the HEADERTSA_POINTER (NON-Z) variant (s2pf-7) -----------------

        [Fact]
        public void EmitPatchImage_HeaderTsaPointer_EmitsHeaderTsa_WithCalcLength()
        {
            var rom = MakeRom();
            const uint slot = 0x1000, target = 0x2000;
            PlantPointer(rom, slot, target);
            // WF 6798: AddHeaderTSAPointer(list, p, Name+"@HEADERTSA_POINTER", false) -> length =
            // CalcByteLengthForHeaderTSAData(target) = 2 + (x+1)*(y+1)*2, typed HEADERTSA.
            uint expect = WriteHeaderTsa(rom, target, 0x0F, 0x01); // (15+1)*(1+1)=32 -> 2 + 32*2 = 66
            Assert.Equal(66u, expect);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("H", ("TYPE", "IMAGE"), ("HEADERTSA_POINTER", "0x1000")), isPointerOnly: false);

            var a = Single(list, "H@HEADERTSA_POINTER");
            Assert.Equal(target, a.Addr);
            Assert.Equal(expect, a.Length);
            Assert.Equal(slot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.HEADERTSA, a.DataType);
        }

        [Fact]
        public void EmitPatchImage_HeaderTsaPointer_AlongsideOtherVariants()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000); // IMAGE
            PlantPointer(rom, 0x1100, 0x2100); // HEADERTSA
            uint expect = WriteHeaderTsa(rom, 0x2100, 0x07, 0x03); // 2 + 32*2 = 66
            var list = new List<Address>();
            // Both the IMAGE_POINTER and the (now-ported) HEADERTSA_POINTER emit, in order.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("M", ("TYPE", "IMAGE"),
                          ("IMAGE_POINTER", "0x1000"), ("HEADERTSA_POINTER", "0x1100")), isPointerOnly: false);

            Assert.Single(list, a => a.Info == "M@IMAGE_POINTER");
            var h = Single(list, "M@HEADERTSA_POINTER");
            Assert.Equal(0x2100u, h.Addr);
            Assert.Equal(expect, h.Length);
            Assert.Equal(Address.DataTypeEnum.HEADERTSA, h.DataType);
        }

        [Fact]
        public void EmitPatchImage_HeaderTsaPointer_UnsafeTarget_Skips()
        {
            var rom = MakeRom();
            // Pointer slot is safe but the dereferenced target is NOT a safe pointer (points below floor).
            U.write_u32(rom.Data, 0x1000, U.toPointer(0x100));
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("U", ("TYPE", "IMAGE"), ("HEADERTSA_POINTER", "0x1000")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_HeaderTsaPointer_SlotNearEof_Skips_NoThrow()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // Slot within the last 3 bytes: EmitHeaderTsaPointer's full-extent EOF guard skips cleanly.
            uint nearEof = (uint)rom.Data.Length - 1;
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("E", ("TYPE", "IMAGE"), ("HEADERTSA_POINTER", "0x" + nearEof.ToString("X"))),
                isPointerOnly: false));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        // ---- safety gates --------------------------------------------------

        [Fact]
        public void EmitPatchImage_ImagePointerZero_Skips()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // IMAGE_POINTER resolves to 0 (WF `p > 0` guard) -> skip. "0" is a literal numeric.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Z0", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_UnsafePointerSlot_Skips()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // 0x100 is below the 0x200 safe-offset floor -> the pointer slot itself is unsafe -> skip.
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("U", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0x100")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_UnsafeTarget_Skips()
        {
            var rom = MakeRom();
            // Pointer slot is safe but the dereferenced target is NOT a safe pointer (points below floor).
            U.write_u32(rom.Data, 0x1000, U.toPointer(0x100));
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Ut", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0x1000")), isPointerOnly: false);
            Assert.Empty(list);
        }

        // ---- near-EOF pointer-slot guard (#1314 Copilot review) -------------
        // A pointer slot resolving to within the last 3 bytes of the ROM passes the single-arg
        // isSafetyOffset(p) but rom.u32(p) reads p..p+3 and would throw. The slot+3 guard skips it
        // gracefully (WF's verbatim Program.ROM.u32(p) after isSafetyOffset(p) would throw there).

        [Fact]
        public void EmitPatchImage_PointerSlotNearEof_Skips_NoThrow()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // Slot at Data.Length - 1: isSafetyOffset(p) true, but p+3 is past EOF -> guarded skip.
            uint nearEof = (uint)rom.Data.Length - 1;
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("E", ("TYPE", "IMAGE"), ("IMAGE_POINTER", "0x" + nearEof.ToString("X"))),
                isPointerOnly: false));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_PalettePointerSlotNearEof_Skips_NoThrow()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // PALETTE_POINTER slot near EOF: isSafetyOffset(pPalette) true, pPalette+3 past EOF.
            // The guard is INSIDE the WF if-branch (skips the deref) so it does NOT fall to PALETTE_ADDRESS.
            uint nearEof = (uint)rom.Data.Length - 2;
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("PE", ("TYPE", "IMAGE"),
                          ("PALETTE_POINTER", "0x" + nearEof.ToString("X")),
                          ("PALETTE_ADDRESS", "0x5000")), isPointerOnly: false));
            Assert.Null(ex);
            // Near-EOF PALETTE_POINTER is in the WF `if` branch -> the deref is skipped, and (per WF's
            // branch selection) the else PALETTE_ADDRESS is NOT taken. Nothing emitted.
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_NoImageParams_EmitsNothing_NoThrow()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // The IMAGE arm must be a clean no-op when no image params are present (the
            // no-patch-dir invariant's IMAGE-arm complement).
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchImage(rom, list,
                MakePatch("Empty", ("TYPE", "IMAGE")), isPointerOnly: false));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchImage_AllSixVariantsTogether_EmitOneEach()
        {
            var rom = MakeRom();
            // IMAGE @0x2000, ZIMAGE @0x3000, Z256IMAGE @0x3100, TSA @0x2100, ZTSA @0x3200, ZHEADERTSA @0x3300
            PlantPointer(rom, 0x1000, 0x2000);
            PlantPointer(rom, 0x1010, 0x3000);
            PlantPointer(rom, 0x1020, 0x3100);
            PlantPointer(rom, 0x1030, 0x2100);
            PlantPointer(rom, 0x1040, 0x3200);
            PlantPointer(rom, 0x1050, 0x3300);
            PlantPointer(rom, 0x1060, 0x2200); // PALETTE_POINTER
            uint zimg = WriteLz77AllLiteral(rom, 0x3000, 8);
            uint z256 = WriteLz77AllLiteral(rom, 0x3100, 8);
            uint ztsa = WriteLz77AllLiteral(rom, 0x3200, 8);
            uint zhtsa = WriteLz77AllLiteral(rom, 0x3300, 8);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchImage(rom, list, MakePatch("All",
                ("TYPE", "IMAGE"),
                ("IMAGE_POINTER", "0x1000"),
                ("ZIMAGE_POINTER", "0x1010"),
                ("Z256IMAGE_POINTER", "0x1020"),
                ("TSA_POINTER", "0x1030"),
                ("ZTSA_POINTER", "0x1040"),
                ("ZHEADERTSA_POINTER", "0x1050"),
                ("PALETTE_POINTER", "0x1060")), isPointerOnly: false);

            Assert.Equal(7, list.Count);
            Assert.Single(list, a => a.Info == "All@IMAGE_POINTER" && a.Length == 32u && a.DataType == Address.DataTypeEnum.IMG);
            Assert.Single(list, a => a.Info == "All@ZIMAGE_POINTER" && a.Length == zimg && a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Single(list, a => a.Info == "All@Z256IMAGE_POINTER" && a.Length == z256 && a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Single(list, a => a.Info == "All@TSA_POINTER" && a.Length == 2u && a.DataType == Address.DataTypeEnum.TSA);
            Assert.Single(list, a => a.Info == "All@ZTSA_POINTER" && a.Length == ztsa && a.DataType == Address.DataTypeEnum.LZ77TSA);
            Assert.Single(list, a => a.Info == "All@ZHEADERTSA_POINTER" && a.Length == zhtsa && a.DataType == Address.DataTypeEnum.LZ77TSA);
            Assert.Single(list, a => a.Info == "All@PALETTE_POINTER" && a.Length == 0x20u && a.DataType == Address.DataTypeEnum.PAL);
        }

        [Fact]
        public void EmitPatchImage_NullArgs_Throw()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            var patch = MakePatch("p", ("IMAGE_POINTER", "0x1000"));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchImage(null, list, patch, false));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchImage(rom, null, patch, false));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchImage(rom, list, null, false));
            var noParam = new PatchInstallCore.PatchSt { Name = "p", PatchFileName = "p.txt", Param = null };
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchImage(rom, list, noParam, false));
        }

        [Fact]
        public void EmitPatchImage_NullPatchFileName_NormalizesBasedir_NoThrow()
        {
            var rom = MakeRom();
            PlantPointer(rom, 0x1000, 0x2000);
            var list = new List<Address>();
            var patch = new PatchInstallCore.PatchSt
            {
                Name = "NoFile",
                PatchFileName = null,
                Param = new Dictionary<string, string> { ["IMAGE_POINTER"] = "0x1000" }
            };
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchImage(rom, list, patch, false));
            Assert.Null(ex);
            Assert.Single(list, a => a.Addr == 0x2000u && a.Info == "NoFile@IMAGE_POINTER");
        }

        // ====================================================================
        // 3. Integration through the public orchestrator (IMAGE arm wired)
        // ====================================================================

        [Fact]
        public void Orchestrator_ImagePatch_EmitsViaWiredArm()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RebuildProducerPatchImage_" + Guid.NewGuid().ToString("N"));
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_IMAGE.txt"), new[]
            {
                "NAME=ImagePatch",
                "TYPE=IMAGE",
                "IMAGE_POINTER=0x1000",
                "WIDTH=8",
                "HEIGHT=8",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeRom();
            CoreState.ROM = fe8;
            PlantPointer(fe8, 0x1000, 0x2000);

            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false);

            // The leaner Core scanner sets only PatchFileName + Param (NOT Name), so Info is "@IMAGE_POINTER".
            Assert.Contains(list, a => a.Addr == 0x2000u && a.Info.EndsWith("@IMAGE_POINTER")
                && a.Length == 32u && a.DataType == Address.DataTypeEnum.IMG);
        }
    }
}
