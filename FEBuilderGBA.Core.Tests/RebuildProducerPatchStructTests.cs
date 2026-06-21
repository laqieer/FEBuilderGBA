// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-5 (#1261) — the TYPE=STRUCT terminal
// PatchForm producer arm (option-B epic, sub-slice 5 of 11):
//   RebuildProducerCore.EmitPatchStruct = WF PatchForm.MakePatchStructDataListForSTRUCT @:6461
//
// Scope of s2pf-5 (verified here):
//   (A) the STRUCT SKELETON (WF 6461-6552) — VERBATIM:
//        - struct_address via POINTER deref OR ADDRESS direct (the WF selection)
//        - DATASIZE / DATACOUNT (literal + `$`-macro, start_offset = struct_address)
//        - the DATACOUNT early-return guards (NOT_FOUND, < struct_address, >= 0xffff, "")
//        - the MAIN InputFormRef Address (datasize*(datacount+1), iftType, blockSize, pointerIndexes)
//        - the per-entry loop p = struct_address + i*datasize + pointerIndexes[n], IN ORDER.
//   (B) the SAFE terminal arms (REAL): ASM/ASM_SWITCH/ASM_NOWARNING (AddFunction),
//        CSTRING (AddCString), PatchImage_IMAGE/_ZIMAGE/_TSA/_ZTSA/_ZHEADERTSA/_PALETTE
//        (AddAddress sized by PatchImageVariantLength), WF's `default` (AddPointer MIX).
//   (C) the FORM-BOUND arms -> DOCUMENTED INTERIM default-MIX (length-0 MIX placeholder),
//        upgraded in s2pf-6..10. Asserted to emit the interim placeholder (NOT a precise length).
//   + the 6624-6632 copy-paste defect behavior (a PatchImage_ZTSA field takes the LIVE
//     WF 6595 arm — LZ77IMG " ZTSA " — never the dead block's LZ77TSA " ZHEADERTSA ").
//   + the no-qualifying-param no-op (preserves the no-patch-dir invariant's STRUCT complement).
//
// Coverage idiom: synthetic in-memory FE8U ROM (BE8E01) + synthetic PatchSt — no real
// GBA ROM file (mirrors RebuildProducerPatchImageTests). CoreState.ROM is set in MakeRom
// (the ASM/CSTRING/MIX arms' AddFunction/AddCString/AddPointer read CoreState.ROM).

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchStructTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly ISystemTextEncoder _savedEncoder = CoreState.SystemTextEncoder;
        readonly EventScript _savedEventScript = CoreState.EventScript;
        readonly IEtcCache _savedCommentCache = CoreState.CommentCache;
        string? _tempDir;

        public RebuildProducerPatchStructTests()
        {
            CoreState.Language = "en";
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
            CoreState.BaseDirectory = _savedBaseDir;
            CoreState.SystemTextEncoder = _savedEncoder;
            CoreState.EventScript = _savedEventScript;
            CoreState.CommentCache = _savedCommentCache;
            try { if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // 16 MiB zero-filled FE8U ROM (LoadLow minimum for BE8E01). Sets CoreState.ROM (the
        // AddFunction/AddCString/AddPointer arms + the Address sink read it) and a headless
        // SystemTextEncoder (Address.AddCString -> rom.getString decodes via CoreState.SystemTextEncoder).
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
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

        // Plant a GBA pointer (toPointer) at `slot` -> `target`.
        static void PlantPointer(ROM rom, uint slot, uint target)
        {
            U.write_u32(rom.Data, slot, U.toPointer(target));
        }

        static Address Single(List<Address> list, string info)
        {
            return Assert.Single(list, a => a.Info == info);
        }

        static Address Main(List<Address> list)
        {
            return Assert.Single(list, a => a.Info != null && a.Info.EndsWith("@STRUCT"));
        }

        // Hand-author a VALID all-literal LZ77 stream of `uncompSize` uncompressed bytes at `offset`.
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
                bytes.Add(0x00);
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
            Assert.True(clen > 0);
            return clen;
        }

        // Plant a header-TSA master header {x, y} at `offset` and return the WF byte length
        // (2 + (x+1)*(y+1)*2 = CalcHeaderTsaLength = WF ImageUtil.CalcByteLengthForHeaderTSAData).
        static uint WriteHeaderTsa(ROM rom, uint offset, byte x, byte y)
        {
            rom.write_u8(offset + 0, x);
            rom.write_u8(offset + 1, y);
            return 2u + ((uint)x + 1) * ((uint)y + 1) * 2;
        }

        // ====================================================================
        // 1. The MAIN struct Address (WF 6536-6543) — POINTER and ADDRESS forms
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_PointerForm_DerefsTableBase_EmitsMainInputFormRef()
        {
            var rom = MakeRom();
            const uint slot = 0x1000, table = 0x2000, datasize = 8, datacount = 4;
            PlantPointer(rom, slot, table);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("S",
                ("TYPE", "STRUCT"),
                ("POINTER", "0x1000"),
                ("DATASIZE", "8"),
                ("DATACOUNT", "4"),
                ("P0:POINTER", "0")), isPointerOnly: false);

            var main = Main(list);
            Assert.Equal(table, main.Addr);
            // datasize*(datacount+1) = 8*5 = 40.
            Assert.Equal(datasize * (datacount + 1), main.Length);
            Assert.Equal(slot, main.Pointer);              // the table's own POINTER slot
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
            Assert.Equal(datasize, main.BlockSize);
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);
        }

        [Fact]
        public void EmitPatchStruct_AddressForm_DirectBase_MainPointerIsNotFound()
        {
            var rom = MakeRom();
            const uint table = 0x3000, datasize = 4, datacount = 2;

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("A",
                ("TYPE", "STRUCT"),
                ("ADDRESS", "0x3000"),
                ("DATASIZE", "4"),
                ("DATACOUNT", "2"),
                ("P0:POINTER", "0")), isPointerOnly: false);

            var main = Main(list);
            Assert.Equal(table, main.Addr);
            Assert.Equal(datasize * (datacount + 1), main.Length); // 4*3 = 12
            Assert.Equal(U.NOT_FOUND, main.Pointer);               // ADDRESS form -> no pointer slot
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
        }

        [Fact]
        public void EmitPatchStruct_AsmPointerField_MainIsInputFormRefAsm()
        {
            var rom = MakeRom();
            const uint table = 0x3000;
            var list = new List<Address>();
            // P0:ASM only -> iftType InputFormRef_ASM (MakePointerIndexes).
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Asm",
                ("TYPE", "STRUCT"),
                ("ADDRESS", "0x3000"),
                ("DATASIZE", "8"),
                ("DATACOUNT", "0"),         // explicit 0 -> MAIN emitted, no entry loop
                ("P0:ASM", "0")), isPointerOnly: false);

            var main = Main(list);
            Assert.Equal(table, main.Addr);
            Assert.Equal(8u * 1u, main.Length);   // datasize*(0+1)
            Assert.Equal(Address.DataTypeEnum.InputFormRef_ASM, main.DataType);
        }

        // ====================================================================
        // 2. DATACOUNT — `$`-macro grep + the early-return guards (WF 6501-6531)
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_DataCountMacro_GrepEndAddr_DerivesCount()
        {
            var rom = MakeRom();
            const uint table = 0x4000, datasize = 4;
            // $EndWeaponDebuffTable5: skip the leading 0x00*4 row, then walk u32 rows until byte[+3]
            // top nibble != 0. Build: row0 = 00 00 00 00 (skipped), then 3 data rows, then a row with
            // a high byte[+3]. start = table+4; the walk finds the terminator at table+4 + 3*4 = table+16,
            // so found = 0x4010, count = ceil((0x4010 - 0x4000)/4) = 4.
            for (uint i = 0; i < 4; i++) rom.write_u8(table + i, 0x00);          // leading skip row
            for (uint r = 0; r < 3; r++)
                for (uint b = 0; b < 4; b++)
                    rom.write_u8(table + 4 + r * 4 + b, (byte)(b == 3 ? 0x00 : 0x11)); // data rows: byte[+3] low nibble
            // terminator row at table+16: byte[+3] high nibble set.
            rom.write_u8(table + 16 + 3, 0x80);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("M",
                ("TYPE", "STRUCT"),
                ("ADDRESS", "0x4000"),
                ("DATASIZE", "4"),
                ("DATACOUNT", "$EndWeaponDebuffTable5 "),
                ("P0:POINTER", "0")), isPointerOnly: false);

            var main = Main(list);
            // found = 0x4010 -> count = 4 -> length = datasize*(4+1) = 20.
            Assert.Equal(datasize * (4u + 1u), main.Length);
        }

        [Fact]
        public void EmitPatchStruct_PointerSlotUnsafe_EmitsNothing()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // POINTER resolves to 0x100 (below the 0x200 safe floor) -> the slot itself is unsafe -> return.
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("U",
                ("TYPE", "STRUCT"), ("POINTER", "0x100"),
                ("DATASIZE", "8"), ("DATACOUNT", "4")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchStruct_PointerTargetUnsafe_EmitsNothing()
        {
            var rom = MakeRom();
            // The slot is safe but the dereferenced table base is below the floor -> return.
            U.write_u32(rom.Data, 0x1000, U.toPointer(0x100));
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Ut",
                ("TYPE", "STRUCT"), ("POINTER", "0x1000"),
                ("DATASIZE", "8"), ("DATACOUNT", "4")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchStruct_NoPointerNoAddress_EmitsNothing()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // Neither POINTER nor ADDRESS top-level param -> WF returns before emitting. This is the
            // STRUCT complement of the no-patch-dir invariant (a STRUCT patch lacking a table base is a no-op).
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("None",
                ("TYPE", "STRUCT"), ("DATASIZE", "8"), ("DATACOUNT", "4"),
                ("P0:POINTER", "0")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchStruct_DataSizeZero_EmitsNothing()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("DZ",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x3000"),
                ("DATASIZE", "0"), ("DATACOUNT", "4")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchStruct_DataCountAbsent_EmitsNothing()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // DATACOUNT absent -> datacount 0 AND datacount_str == "" -> WF returns (no MAIN emitted).
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("NoCount",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x3000"),
                ("DATASIZE", "8"), ("P0:POINTER", "0")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchStruct_DataCountExplicitZero_EmitsMainOnly_NoEntries()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // Explicit "0" -> datacount 0 but datacount_str != "" -> WF does NOT return; the MAIN
            // Address is emitted (length datasize*1) and the per-entry loop runs 0 times.
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Zero",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x3000"),
                ("DATASIZE", "8"), ("DATACOUNT", "0"),
                ("P0:POINTER", "0")), isPointerOnly: false);
            var main = Main(list);
            Assert.Equal(8u, main.Length);
            Assert.Single(list); // ONLY the main entry (no per-entry pointers)
        }

        // ====================================================================
        // 3. The SAFE terminal arms — per-entry dispatch
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_CStringField_EmitsCStringPerEntry()
        {
            var rom = MakeRom();
            const uint table = 0x2000, datasize = 4;
            // Two entries, each entry's +0 is a CSTRING pointer. Plant a string at 0x5000.
            uint strAddr = 0x5000;
            byte[] s = System.Text.Encoding.ASCII.GetBytes("Hi");
            for (uint i = 0; i < s.Length; i++) rom.write_u8(strAddr + i, s[i]);
            rom.write_u8(strAddr + (uint)s.Length, 0x00);
            // entry0 slot = table+0, entry1 slot = table+4 -> both point to the same string.
            PlantPointer(rom, table + 0, strAddr);
            PlantPointer(rom, table + 4, strAddr);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("C",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "2"),
                ("P0:CSTRING", "0")), isPointerOnly: false);

            // MAIN + 2 CSTRING entries (CSTRING Address.Info is the decoded string "Hi").
            int cstrings = 0;
            foreach (var a in list)
            {
                if (a.DataType == Address.DataTypeEnum.CSTRING)
                {
                    cstrings++;
                    Assert.Equal(strAddr, a.Addr);
                    Assert.Equal((uint)s.Length + 1, a.Length);
                }
            }
            Assert.Equal(2, cstrings);
            _ = datasize;
        }

        [Fact]
        public void EmitPatchStruct_AsmField_EmitsFunctionPerEntry()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            // entry0 P0:ASM -> the ASM function pointer at +0 (a THUMB pointer is odd; AddFunction
            // strips the low bit via ProgramAddrToPlain).
            U.write_u32(rom.Data, table + 0, U.toPointer(0x6000) | 1); // ASM pointer (odd)

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("F",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:ASM", "0")), isPointerOnly: false);

            // The ASM field uses AddFunction -> a length-0 entry named "...ASM 0" pointing at the
            // disassembled target (low bit stripped -> 0x6000).
            var fn = Assert.Single(list, a => a.Info != null && a.Info.EndsWith("ASM 0"));
            Assert.Equal(0x6000u, fn.Addr);
            Assert.Equal(table + 0, fn.Pointer);
        }

        [Fact]
        public void EmitPatchStruct_AsmFieldSlotNearEof_Skips_NoThrow()
        {
            // Copilot PR #1315 review: rom.p32(p) is called for the ASM arm; p32's only guard is
            // `p >= Data.Length`, so a p in the LAST 3 bytes (in-range but p+4 > Data.Length) would
            // throw inside u32 -> check_safety(p+4). The full-slot guard must skip it cleanly.
            var rom = MakeRom();
            // ADDRESS at the very last 4-aligned-ish offset so that struct_address + pointerIndexes[0]
            // (P0 -> +0) lands within the last 3 bytes. Use Data.Length - 1 as the table base.
            uint nearEof = (uint)rom.Data.Length - 1;
            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("E",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x" + nearEof.ToString("X")),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:ASM", "0")), isPointerOnly: false));
            Assert.Null(ex);
            // The near-EOF ASM field is a clean skip; no ASM entry emitted (the MAIN entry's own length
            // is clamped to 0 by the Address ctor's isSafetyLength guard, but it does not throw).
            Assert.DoesNotContain(list, a => a.Info != null && a.Info.EndsWith("ASM 0"));
        }

        [Fact]
        public void EmitPatchStruct_PatchImageFields_EmitSizedAddresses()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            // The P-NUMBER is the byte offset within the entry (MakePointerIndexes: datanum =
            // atoi(key.Substring(1)); the VALUE is ignored). So P0 -> +0, P4 -> +4, P8 -> +8.
            // entry0: P0 = PatchImage_IMAGE @+0, P4 = PatchImage_TSA @+4, P8 = PatchImage_PALETTE @+8.
            PlantPointer(rom, table + 0, 0x7000);
            PlantPointer(rom, table + 4, 0x7100);
            PlantPointer(rom, table + 8, 0x7200);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Img",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "12"), ("DATACOUNT", "1"),
                ("WIDTH", "8"), ("HEIGHT", "8"), ("PALETTE", "2"),
                ("P0:PatchImage_IMAGE", "0"),
                ("P4:PatchImage_TSA", "0"),
                ("P8:PatchImage_PALETTE", "0")), isPointerOnly: false);

            // IMAGE: 8*8/2 = 32, IMG, named "... IMAGE 0" (n is the field ORDINAL, here 0).
            Assert.Single(list, a => a.Addr == 0x7000u && a.Length == 32u
                && a.DataType == Address.DataTypeEnum.IMG && a.Info.EndsWith("IMAGE 0"));
            // TSA: 8*8/32 = 2, TSA, named "... TSA 1".
            Assert.Single(list, a => a.Addr == 0x7100u && a.Length == 2u
                && a.DataType == Address.DataTypeEnum.TSA && a.Info.EndsWith("TSA 1"));
            // PALETTE: count 2 * 0x20 = 0x40, PAL, named "... PALETTE 2".
            Assert.Single(list, a => a.Addr == 0x7200u && a.Length == 0x40u
                && a.DataType == Address.DataTypeEnum.PAL && a.Info.EndsWith("PALETTE 2"));
        }

        [Fact]
        public void EmitPatchStruct_PatchImageImage_DefaultWidthHeightIsZero_NotEight()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            PlantPointer(rom, table + 0, 0x7000);
            var list = new List<Address>();
            // NO WIDTH/HEIGHT param -> WF STRUCT arm's atOffset default is "0" (NOT "8"): length 0*0/2 = 0.
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Z",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_IMAGE", "0")), isPointerOnly: false);

            var img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.IMG);
            Assert.Equal(0u, img.Length); // default WIDTH/HEIGHT = 0 (NOT 8 -> would be 32)
        }

        [Fact]
        public void EmitPatchStruct_ZtsaField_ReproducesWf6595_NotTheDeadDefect()
        {
            // The 6624-6632 copy-paste defect: a PatchImage_ZTSA field must take the LIVE WF 6595 arm
            // (LZ77IMG named " ZTSA "), NEVER the dead WF 6624 block (LZ77TSA named " ZHEADERTSA ").
            var rom = MakeRom();
            const uint table = 0x2000;
            PlantPointer(rom, table + 0, 0x8000);
            uint zlen = WriteLz77AllLiteral(rom, 0x8000, 16);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("ZT",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_ZTSA", "0")), isPointerOnly: false);

            // LIVE arm: LZ77IMG, " ZTSA 0". The dead block would have been LZ77TSA, " ZHEADERTSA 0".
            var a = Assert.Single(list, x => x.Addr == 0x8000u);
            Assert.Equal(zlen, a.Length);
            Assert.Equal(Address.DataTypeEnum.LZ77IMG, a.DataType);   // NOT LZ77TSA (the dead defect path)
            Assert.EndsWith("ZTSA 0", a.Info);                       // NOT "ZHEADERTSA 0"
        }

        [Fact]
        public void EmitPatchStruct_ZHeaderTsaField_EmitsLz77ImgNoIndexSuffix()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            PlantPointer(rom, table + 0, 0x8000);
            uint zlen = WriteLz77AllLiteral(rom, 0x8000, 16);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("ZH",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_ZHEADERTSA", "0")), isPointerOnly: false);

            // WF 6614: LZ77IMG, named " ZHEADERTSA " (NO index suffix -> ends with "ZHEADERTSA ").
            var a = Assert.Single(list, x => x.Addr == 0x8000u);
            Assert.Equal(zlen, a.Length);
            Assert.Equal(Address.DataTypeEnum.LZ77IMG, a.DataType);
            Assert.EndsWith("ZHEADERTSA ", a.Info);
        }

        [Fact]
        public void EmitPatchStruct_HeaderTsaField_EmitsHeaderTsaWithCalcLength()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            const uint target = 0x8000;
            PlantPointer(rom, table + 0, target);
            // WF 6605-6613: non-Z header-TSA -> AddHeaderTSAPointer -> length =
            // CalcByteLengthForHeaderTSAData(target) = 2 + (x+1)*(y+1)*2, typed HEADERTSA,
            // named "... HEADERTSA <n>" (n is the field ORDINAL, here 0).
            uint expect = WriteHeaderTsa(rom, target, 0x07, 0x03); // (7+1)*(3+1)=32 -> 2 + 32*2 = 66
            Assert.Equal(66u, expect);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("HT",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_HEADERTSA", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.Addr == target);
            Assert.Equal(expect, a.Length);
            Assert.Equal(table + 0, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.HEADERTSA, a.DataType);
            Assert.EndsWith("HEADERTSA 0", a.Info);
            // NOT the interim default-MIX placeholder (no length-0 MIX entry).
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_HeaderTsaField_UnsafeTarget_EmitsNoHeaderTsa()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            // Slot derefs to an unsafe target (below the 0x200 floor) -> WF's `if (isSafetyOffset(a))`
            // pre-gate fails -> nothing emitted (NO HEADERTSA, NO MIX placeholder).
            U.write_u32(rom.Data, table + 0, U.toPointer(0x100));
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("HU",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_HEADERTSA", "0")), isPointerOnly: false);

            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.HEADERTSA);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_UnknownType_DefaultMixPlaceholder()
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            // entry0 +0 -> a pointer to a safe target. An unknown field type hits WF's `default` arm
            // -> AddPointer(p, 0, .., MIX) -> a length-0 MIX entry.
            PlantPointer(rom, table + 0, 0x9000);
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Unk",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:SOMETHINGUNKNOWN", "0")), isPointerOnly: false);

            var mix = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.MIX);
            Assert.Equal(0x9000u, mix.Addr);
            Assert.Equal(0u, mix.Length);              // WF default length 0
            Assert.EndsWith("DATA 0", mix.Info);
        }

        // ====================================================================
        // 4. The FORM-BOUND arms -> INTERIM default-MIX (this slice ONLY)
        // ====================================================================

        // Each interim form-bound type is routed through the same default-MIX placeholder as an
        // unknown type. THIS IS INTENTIONAL FOR s2pf-5: the precise sub-walked TARGET length is
        // ported in s2pf-8..10. The test asserts the INTERIM placeholder (length-0 MIX), NOT a
        // precise WF length — the partial WF-parity test EXCLUDES these types accordingly.
        // NOTE: EVENT was REMOVED from this interim group in s2pf-6 (it runs the real
        // EventScriptForm.ScanScript walk — see EmitPatchStruct_EventArm_*), and
        // PatchImage_HEADERTSA in s2pf-7 (it runs EmitHeaderTsaPointer — see
        // EmitPatchStruct_HeaderTsaField_* below).
        [Theory]
        [InlineData("AP")]                        // -> s2pf-8
        [InlineData("ROMTCS")]                    // -> s2pf-8
        [InlineData("PROCS")]                     // -> s2pf-8
        [InlineData("VENNOUWEAPONLOCK")]          // -> s2pf-9
        [InlineData("AOERANGEPOINTER")]           // -> s2pf-9
        [InlineData("SMEPROMOLIST")]              // -> s2pf-9
        [InlineData("CLASSLIST")]                 // -> s2pf-9
        [InlineData("TERRAINBATTLELISTPOINTER")]  // -> s2pf-9
        [InlineData("BATTLEBGLISTPOINTER")]       // -> s2pf-9
        [InlineData("BATTLEANIMEPOINTER")]        // -> s2pf-10
        public void EmitPatchStruct_FormBoundArm_IsInterimDefaultMix(string fieldType)
        {
            var rom = MakeRom();
            const uint table = 0x2000;
            PlantPointer(rom, table + 0, 0x9000);
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("FB",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:" + fieldType, "0")), isPointerOnly: false);

            // INTERIM: emitted as the length-0 MIX placeholder (NOT the precise sub-walked region).
            var mix = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.MIX);
            Assert.Equal(0x9000u, mix.Addr);
            Assert.Equal(0u, mix.Length);
            Assert.EndsWith("DATA 0", mix.Info);
        }

        // ====================================================================
        // 4b. The EVENT arm (s2pf-6) — the REAL EventScriptForm.ScanScript walk
        //     (WF PatchForm.cs:6553-6557), reproduced via EmitScanScript and gated
        //     on IsEventScriptDisasmReady (the EventCondForm slice-2u convention).
        // ====================================================================

        // Wire CoreState so IsEventScriptDisasmReady(rom) is TRUE: ROM == rom, an EventScript with
        // the planted ENDA opcode is set, and a CommentCache is present. Returns the rom (already
        // CoreState.ROM from MakeRom). The Dispose() above restores EventScript/CommentCache.
        static ROM MakeRomWithDisasm()
        {
            var rom = MakeRom();
            var es = new EventScript();
            // The only opcode the EVENT walk needs is the terminator ENDA (0x0A) — IsExitCode true.
            typeof(EventScript).GetProperty("Scripts")!
                .SetValue(es, new[] { EventScript.ParseScriptLine("0A000000\tENDA [TERM]") });
            CoreState.EventScript = es;
            CoreState.CommentCache = new HeadlessEtcCache();
            return rom;
        }

        // Plant a one-command (ENDA) event script at `eventAddr` and a GBA pointer to it at `slot`.
        static void PlantEvent(ROM rom, uint slot, uint eventAddr)
        {
            PlantPointer(rom, slot, eventAddr);     // slot -> toPointer(eventAddr)
            rom.write_u32(eventAddr, 0x0000000A);   // ENDA [TERM]
        }

        [Fact]
        public void EmitPatchStruct_EventArm_DisasmReady_EmitsEventScriptBlock()
        {
            // WF 6553-6557: EventScriptForm.ScanScript(list, p, true, false, patchname+" DATA "+n, tracelist).
            // The address passed is `p` (the FIELD slot), NOT rom.p32(p); ScanScript derefs it internally.
            var rom = MakeRomWithDisasm();
            const uint table = 0x2000;
            uint slot = table + 0;          // p = struct_address + 0*datasize + 0
            uint eventAddr = 0x9000;
            PlantEvent(rom, slot, eventAddr);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Ev",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:EVENT", "0")), isPointerOnly: false);

            // The EVENT walk emits one EVENTSCRIPT block: target = eventAddr, slot/pointer = p (the
            // FIELD slot), length = one 4-byte ENDA command. NOT a length-0 MIX placeholder.
            var evt = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            Assert.Equal(eventAddr, evt.Addr);
            Assert.Equal(slot, evt.Pointer);        // ScanScript was given `p` (the FIELD), not p32(p)
            Assert.Equal(4u, evt.Length);           // precise: one ENDA, NOT 0
            Assert.EndsWith("DATA 0", evt.Info);    // WF info: patchname + " DATA " + n
            // No MIX placeholder is emitted for the EVENT field anymore.
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_EventArm_DisasmNotWired_SkipsWalk_NoThrow_NoMix()
        {
            // Disasm-unavailable path (slice-2u convention): EmitScanScript would THROW, so the EVENT
            // dispatch GATES on IsEventScriptDisasmReady and SKIPS the walk when it is unwired. The skip
            // is NOT silent — PatchForm stays in AsmNotYetPortedRaw unconditionally (the standing gate
            // token until s2pf-11). The dispatch must NOT throw and must NOT fall back to a MIX placeholder
            // (a length-0 MIX would mis-size the embedded pointer's target on a live rebuild).
            var rom = MakeRom();
            CoreState.EventScript = null;       // disasm NOT wired
            CoreState.CommentCache = null;
            const uint table = 0x2000;
            PlantEvent(rom, table + 0, 0x9000);

            var list = new List<Address>();
            Exception ex = Record.Exception(() => RebuildProducerCore.EmitPatchStruct(rom, list,
                MakePatch("Ev",
                    ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                    ("DATASIZE", "4"), ("DATACOUNT", "1"),
                    ("P0:EVENT", "0")), isPointerOnly: false));

            Assert.Null(ex); // the orchestrator never throws just because disasm is unwired
            // The MAIN @STRUCT entry is still emitted; only the EVENT block is skipped.
            Assert.Contains(list, a => a.Info != null && a.Info.EndsWith("@STRUCT"));
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            // CRITICAL: no MIX placeholder for the skipped EVENT field.
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_EventArm_SharedTracelist_DedupsAcrossEntries()
        {
            // WF 6545 allocates the tracelist ONCE per STRUCT; two entries whose EVENT fields point to
            // the SAME event script emit the block ONCE — the second reference becomes a zero-length
            // alias EVENTSCRIPT pointer for ITS slot.
            var rom = MakeRomWithDisasm();
            const uint table = 0x2000, datasize = 4, datacount = 2;
            uint eventAddr = 0x9000;
            // both entry slots point to the SAME event.
            PlantEvent(rom, table + 0 * datasize, eventAddr);
            PlantPointer(rom, table + 1 * datasize, eventAddr);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Ev2",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "2"),
                ("P0:EVENT", "0")), isPointerOnly: false);

            var blocks = list.FindAll(a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            // Exactly one real block (length>0) at eventAddr; one zero-length alias for the 2nd slot.
            Assert.Single(blocks, b => b.Addr == eventAddr && b.Length == 4u);
            Assert.Single(blocks, b => b.Length == 0u && b.Pointer == table + 1 * datasize);
        }

        // ====================================================================
        // 5. Multi-pointer entries + the per-entry ORDER and addressing
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_MultiEntryMultiPointer_VisitsEveryFieldAtCorrectAddress()
        {
            var rom = MakeRom();
            const uint table = 0x2000, datasize = 16, datacount = 3;
            // Each entry has P0:PatchImage_IMAGE @+0 and P8:CSTRING @+8 (the P-NUMBER is the byte
            // offset). Plant distinct targets so the per-entry addressing
            // (struct_address + i*datasize + pointerIndexes[n]) is verifiable.
            uint strAddr = 0xA000;
            rom.write_u8(strAddr, (byte)'X'); rom.write_u8(strAddr + 1, 0x00);
            for (uint i = 0; i < datacount; i++)
            {
                uint imgSlot = table + i * datasize + 0;
                uint strSlot = table + i * datasize + 8;
                PlantPointer(rom, imgSlot, 0xB000 + i * 0x100); // distinct image target per entry
                PlantPointer(rom, strSlot, strAddr);
            }

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Multi",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "16"), ("DATACOUNT", "3"),
                ("WIDTH", "8"), ("HEIGHT", "8"),
                ("P0:PatchImage_IMAGE", "0"),
                ("P8:CSTRING", "0")), isPointerOnly: false);

            // MAIN + 3 IMG + 3 CSTRING = 7.
            Assert.Equal(7, list.Count);
            for (uint i = 0; i < datacount; i++)
            {
                uint imgTarget = 0xB000 + i * 0x100;
                Assert.Single(list, a => a.Addr == imgTarget && a.DataType == Address.DataTypeEnum.IMG);
            }
            Assert.Equal(3, list.FindAll(a => a.DataType == Address.DataTypeEnum.CSTRING).Count);
        }

        [Fact]
        public void EmitPatchStruct_EntryLoopBound_DoesNotVisitSentinelPlusOneRow()
        {
            // Copilot plan-review finding #1: the per-entry loop is `i < datacount` (WF 6547) even
            // though the MAIN block length is datasize*(datacount+1). The sentinel/+1 row must NOT
            // be visited. Plant a DISTINCT pointer at the sentinel row (i == datacount); it must NOT
            // be emitted.
            var rom = MakeRom();
            const uint table = 0x2000, datasize = 4, datacount = 2;
            // entries 0,1 -> images; the sentinel row (i==2) at table+2*4 -> a DIFFERENT target.
            PlantPointer(rom, table + 0 * datasize, 0xB000);
            PlantPointer(rom, table + 1 * datasize, 0xB100);
            PlantPointer(rom, table + 2 * datasize, 0xBEEF00 & 0xFFFFFF); // sentinel row target

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Bound",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "2"),
                ("WIDTH", "8"), ("HEIGHT", "8"),
                ("P0:PatchImage_IMAGE", "0")), isPointerOnly: false);

            // Only entries 0 and 1 emit images; the sentinel row is NOT visited.
            Assert.Single(list, a => a.Addr == 0xB000u && a.DataType == Address.DataTypeEnum.IMG);
            Assert.Single(list, a => a.Addr == 0xB100u && a.DataType == Address.DataTypeEnum.IMG);
            Assert.DoesNotContain(list, a => a.Addr == (0xBEEF00u & 0xFFFFFFu));
            // MAIN + exactly 2 entry images = 3.
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void EmitPatchStruct_Z256ImageField_IsNotAWfStructArm_FallsToDefaultMix()
        {
            // Copilot plan-review finding #3: PatchImage_Z256IMAGE is NOT a WF STRUCT arm (it exists
            // only in the TYPE=IMAGE handler). WF STRUCT falls through to the `default` -> MIX. The Core
            // port adds NO real Z256IMAGE STRUCT arm; a Z256IMAGE field must land in default-MIX
            // (a length-0 MIX placeholder), NOT a sized LZ77 entry.
            var rom = MakeRom();
            const uint table = 0x2000;
            PlantPointer(rom, table + 0, 0x8000);
            uint unused = WriteLz77AllLiteral(rom, 0x8000, 16); // a real LZ77 stream at the target
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("Z256",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PatchImage_Z256IMAGE", "0")), isPointerOnly: false);

            var mix = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.MIX);
            Assert.Equal(0x8000u, mix.Addr);
            Assert.Equal(0u, mix.Length);   // default-MIX placeholder, NOT the LZ77 length `unused`
            Assert.NotEqual(unused, mix.Length);
            Assert.EndsWith("DATA 0", mix.Info);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG
                || a.DataType == Address.DataTypeEnum.LZ77TSA);
        }

        // ====================================================================
        // 6. Null-arg guards + integration through the orchestrator
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_NullArgs_Throw()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            var patch = MakePatch("p", ("TYPE", "STRUCT"), ("ADDRESS", "0x3000"), ("DATASIZE", "4"), ("DATACOUNT", "1"));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchStruct(null, list, patch, false));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchStruct(rom, null, patch, false));
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchStruct(rom, list, null, false));
            var noParam = new PatchInstallCore.PatchSt { Name = "p", PatchFileName = "p.txt", Param = null };
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.EmitPatchStruct(rom, list, noParam, false));
        }

        [Fact]
        public void Orchestrator_StructPatch_EmitsViaWiredArm()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RebuildProducerPatchStruct_" + Guid.NewGuid().ToString("N"));
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_STRUCT.txt"), new[]
            {
                "NAME=StructPatch",
                "TYPE=STRUCT",
                "ADDRESS=0x3000",
                "DATASIZE=8",
                "DATACOUNT=2",
                "P0:POINTER=0",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeRom();
            CoreState.ROM = fe8;

            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false);

            // The wired STRUCT arm emits the MAIN InputFormRef table entry (length 8*(2+1) = 24).
            Assert.Contains(list, a => a.Addr == 0x3000u && a.Info != null && a.Info.EndsWith("@STRUCT")
                && a.Length == 24u && a.DataType == Address.DataTypeEnum.InputFormRef);
        }
    }
}
