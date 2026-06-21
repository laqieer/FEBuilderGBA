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

        // Plant the shortest ROMTCS terminator pattern (CalcRomTcsLength needArray[4] = {00 00 FF FF 10 00},
        // plusOffset 4) at `offset + termAt`; the ROMTCS length is then (termAt + 4). Mirrors the
        // RebuildProducerCoreTests EmitRomTcsPointer plant.
        static uint WriteRomTcs(ROM rom, uint offset, uint termAt)
        {
            byte[] term = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x10, 0x00 };
            for (int i = 0; i < term.Length; i++) rom.write_u8(offset + termAt + (uint)i, term[i]);
            return termAt + 4u; // (match + plusOffset) - offset
        }

        // Plant a minimal VALID PROCS stream at `offset`: one 0x0B instruction (parg-is-null contract)
        // then a 0x00 EXIT. CalcProcsLengthAndCheck consumes 16 bytes. Mirrors PlantProcsStream in
        // RebuildProducerCoreTests.
        static uint WriteProcs(ROM rom, uint offset)
        {
            rom.write_u16(offset + 0, 0x000B);
            rom.write_u16(offset + 2, 0x0000);
            rom.write_u32(offset + 4, 0x00000000);
            rom.write_u16(offset + 8, 0x0000);
            rom.write_u16(offset + 10, 0x0000);
            rom.write_u32(offset + 12, 0x00000000);
            return 16u;
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
        // 4. The FORM-BOUND arms -> ALL PRECISE as of s2pf-10 (NO interim remains)
        // ====================================================================

        // Every STRUCT form-bound type now emits its precise WF sub-walked TARGET region — the
        // INTERIM default-MIX group is EMPTY. The historic interim placeholder test
        // (EmitPatchStruct_FormBoundArm_IsInterimDefaultMix) was REMOVED in s2pf-10 because its sole
        // remaining InlineData, BATTLEANIMEPOINTER, is now precise (see
        // EmitPatchStruct_BattleAnimeField_* below). The migration order was: EVENT (s2pf-6, real
        // EventScriptForm.ScanScript walk — EmitPatchStruct_EventArm_*), PatchImage_HEADERTSA (s2pf-7,
        // EmitHeaderTsaPointer — EmitPatchStruct_HeaderTsaField_*), AP/ROMTCS/PROCS (s2pf-8,
        // EmitApPointer/EmitRomTcsPointer/EmitProcsPointer — EmitPatchStruct_ApField_* / _RomTcsField_* /
        // _ProcsField_*), the SIX deterministic STRUCT forms (s2pf-9, VENNOUWEAPONLOCK/AOERANGEPOINTER/
        // SMEPROMOLIST/CLASSLIST/TERRAINBATTLELISTPOINTER/BATTLEBGLISTPOINTER — the precise length-walks
        // EmitPatchStruct_VennouWeaponLockField_* etc.), and finally BATTLEANIMEPOINTER (s2pf-10, the LAST
        // — block-4 u32!=0 SETTING IFR). The partial WF-parity comparison now covers ALL form-bound types
        // (no exclusions); only WF's genuine `default` (unknown pointer -> length-0 MIX, which keeps the
        // TARGET tracked) remains a length-0 MIX, which IS faithful to WF.

        // ====================================================================
        // 4a'. The AP / ROMTCS / PROCS arms (s2pf-8) — the REAL pointer-emitters
        //      (WF PatchForm.cs:6644-6675 -> AddressWinForms.AddAPPointer /
        //      AddROMTCSPointer / AddProcsPointer), reproduced via the Core
        //      EmitApPointer / EmitRomTcsPointer / EmitProcsPointer. Each derefs
        //      `a = p32(p)`, pre-gates isSafetyOffset(a), then emits the target
        //      sized by ImageUtilAPCore.CalcAPLength / CalcRomTcsLength /
        //      CalcProcsLengthAndCheck. PROCS SKIPS on NOT_FOUND (no emission).
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_ApField_EmitsApWithCalcApLength()
        {
            // WF 6644-6653: deref p, then AddAPPointer -> EmitApPointer; length =
            // ImageUtilAPCore.CalcAPLength over the dereferenced target, typed AP, named
            // "... AP <n>". CalcAPLength NEVER throws and returns 0 on an unparseable stream;
            // EmitApPointer always emits (unlike PROCS, which skips on NOT_FOUND), so the AP
            // Address is present whatever the planted bytes parse to. Cross-check its length
            // against CalcAPLength directly.
            var rom = MakeRom();
            const uint table = 0x2000;
            const uint target = 0x8000;
            PlantPointer(rom, table + 0, target);
            uint expect = ImageUtilAPCore.CalcAPLength(rom.Data, target);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("AP",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:AP", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.AP);
            Assert.Equal(target, a.Addr);
            Assert.Equal(expect, a.Length);
            Assert.Equal(table + 0, a.Pointer);
            Assert.EndsWith("AP 0", a.Info);
            // NOT the interim default-MIX placeholder.
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_RomTcsField_EmitsRomTcsWithCalcLength()
        {
            // WF 6655-6664: deref p, then AddROMTCSPointer -> EmitRomTcsPointer; length =
            // CalcRomTcsLength (terminator scan) over the dereferenced target, typed ROMTCS,
            // named "... ROMTCS <n>".
            var rom = MakeRom();
            const uint table = 0x2000;
            const uint target = 0x8000;
            PlantPointer(rom, table + 0, target);
            uint expect = WriteRomTcs(rom, target, 0x10); // terminator @ +0x10 -> length 0x14

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("RT",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:ROMTCS", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.ROMTCS);
            Assert.Equal(target, a.Addr);
            Assert.Equal(expect, a.Length);
            Assert.Equal(table + 0, a.Pointer);
            Assert.EndsWith("ROMTCS 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_ProcsField_EmitsProcsWithCalcLength()
        {
            // WF 6666-6675: deref p, then AddProcsPointer -> EmitProcsPointer; length =
            // CalcProcsLengthAndCheck over the dereferenced target, typed PROCS, named
            // "... PROCS <n>".
            var rom = MakeRom();
            const uint table = 0x2000;
            const uint target = 0x8000;
            PlantPointer(rom, table + 0, target);
            uint expect = WriteProcs(rom, target); // valid PROCS -> length 16

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("PR",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PROCS", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.PROCS);
            Assert.Equal(target, a.Addr);
            Assert.Equal(expect, a.Length);
            Assert.Equal(table + 0, a.Pointer);
            Assert.EndsWith("PROCS 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_ProcsField_NotFoundLength_SkipsEntirely()
        {
            // WF AddProcsAddress: `length = CalcLengthAndCheck(addr); if (length == U.NOT_FOUND) return;`
            // A target that is NOT a valid PROCS (unknown opcode 0x07FF) -> CalcProcsLengthAndCheck
            // returns NOT_FOUND -> the PROCS arm emits NOTHING (NEVER a zero/guessed length, and NOT
            // the interim default-MIX placeholder either).
            var rom = MakeRom();
            const uint table = 0x2000;
            const uint target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u16(target + 0, 0x07FF);   // unknown opcode -> contract violation -> NOT_FOUND
            rom.write_u16(target + 2, 0x0000);
            rom.write_u32(target + 4, 0x00000000);

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("PR",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:PROCS", "0")), isPointerOnly: false);

            // The PROCS skip: no PROCS Address AND no MIX placeholder for this field.
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.PROCS);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Theory]
        [InlineData("AP")]
        [InlineData("ROMTCS")]
        [InlineData("PROCS")]
        public void EmitPatchStruct_ApRomTcsProcsField_UnsafeTarget_EmitsNothing(string fieldType)
        {
            // WF pre-gate `if (isSafetyOffset(a))` (a = p32(p)) fails when the slot derefs below the
            // 0x200 floor -> the arm is skipped, nothing emitted (NO AP/ROMTCS/PROCS, NO MIX).
            var rom = MakeRom();
            const uint table = 0x2000;
            U.write_u32(rom.Data, table + 0, U.toPointer(0x100)); // target below safety floor
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("US",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:" + fieldType, "0")), isPointerOnly: false);

            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.AP);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.ROMTCS);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.PROCS);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Theory]
        [InlineData("AP")]
        [InlineData("ROMTCS")]
        [InlineData("PROCS")]
        public void EmitPatchStruct_ApRomTcsProcsField_SlotNearEof_NoThrow(string fieldType)
        {
            // A pointer FIELD sitting in the last 3 bytes -> the slot+3 EOF guard makes a clean skip
            // (no throw) rather than reading p32 past EOF. Use a STRUCT whose entry-0 +0 slot lands
            // at Data.Length-1 via a large ADDRESS.
            var rom = MakeRom();
            uint near = (uint)rom.Data.Length - 1;
            var list = new List<Address>();
            Exception ex = Record.Exception(() =>
                RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("EOF",
                    ("TYPE", "STRUCT"), ("ADDRESS", "0x" + near.ToString("X")),
                    ("DATASIZE", "4"), ("DATACOUNT", "1"),
                    ("P0:" + fieldType, "0")), isPointerOnly: false));
            Assert.Null(ex);
        }

        // ====================================================================
        // 4a2. The SIX deterministic STRUCT form-arms (s2pf-9) — the REAL
        //      length-walks. WF PatchForm.cs arms 6693/6722/6698/6703/6708/6713 ->
        //      VennouWeaponLockForm / AOERANGEForm / SMEPromoListForm /
        //      SomeClassListForm / MapTerrainFloorLookupTableForm /
        //      MapTerrainBGLookupTableForm .MakeDataLength, reproduced via the Core
        //      EmitVennouWeaponLockPointer / EmitAoeRangePointer / EmitSmePromoListPointer /
        //      EmitSomeClassListPointer / EmitMapTerrainLookupPointer. Each derefs the
        //      slot and emits the precise target length (0x00-terminator BIN / 4+w*h BIN /
        //      block-2 u16!=0 IFR / block-1 u8!=0 IFR / fixed map_terrain_type_count IFR).
        // ====================================================================

        [Fact]
        public void EmitPatchStruct_VennouWeaponLockField_EmitsBinTerminatorLength()
        {
            // WF VennouWeaponLockForm.MakeDataLength -> CalcLength: deref u32(p), scan from start+1
            // to the first 0x00, length = end - start, type BIN. Plant a 5-byte list [01 02 03 04 05]
            // followed by 0x00 at +5 -> the scan (start at +1) finds 0x00 at +5 -> length = 5.
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u8(target + 0, 0x01);
            rom.write_u8(target + 1, 0x02);
            rom.write_u8(target + 2, 0x03);
            rom.write_u8(target + 3, 0x04);
            rom.write_u8(target + 4, 0x05);
            rom.write_u8(target + 5, 0x00); // terminator
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("VWL",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:VENNOUWEAPONLOCK", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(target, a.Addr);
            Assert.Equal(5u, a.Length); // end(0x8005) - start(0x8000)
            Assert.Equal(table + 0, a.Pointer);
            Assert.EndsWith("DATA 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_VennouWeaponLockField_TerminatorAtStart_IsConsumedAsData_LengthAtLeastOne()
        {
            // The WF off-by-one: CalcLength does start=addr; addr++; THEN scans. A 0x00 AT start is
            // NOT the terminator (the byte at start is consumed as data) -> length >= 1. Plant 0x00 at
            // +0 and the next 0x00 at +1 -> scan starts at +1, finds 0x00 there -> length = 1.
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u8(target + 0, 0x00); // terminator AT start — consumed as data
            rom.write_u8(target + 1, 0x00); // real terminator found by the scan
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("VWL0",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:VENNOUWEAPONLOCK", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(target, a.Addr);
            Assert.Equal(1u, a.Length); // NOT 0 — the start byte is data, the +1 byte is the terminator
        }

        [Fact]
        public void EmitPatchStruct_AoeRangeField_EmitsBinLengthFourPlusWtimesH()
        {
            // WF AOERANGEForm.MakeDataLength: deref p32(p), w=u8(+0), h=u8(+1), length = 4 + w*h, BIN.
            // w=3, h=5 -> length = 4 + 15 = 19. EXACTLY 4+w*h (not w*h, not (w+1)*(h+1)=24).
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u8(target + 0, 3); // w
            rom.write_u8(target + 1, 5); // h
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("AOE",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:AOERANGEPOINTER", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(target, a.Addr);
            Assert.Equal(4u + 3u * 5u, a.Length); // 19, EXACTLY 4 + w*h
            Assert.NotEqual((3u + 1u) * (5u + 1u), a.Length); // NOT (w+1)*(h+1)
            Assert.NotEqual(3u * 5u, a.Length);               // NOT w*h
            Assert.Equal(table + 0, a.Pointer);
            Assert.EndsWith("DATA 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_AoeRangeField_ZeroBox_LengthIsFour()
        {
            // w=0,h=0 -> length = 4 + 0 = 4 (the 4-byte header only).
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u8(target + 0, 0);
            rom.write_u8(target + 1, 0);
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("AOE0",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:AOERANGEPOINTER", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(4u, a.Length);
        }

        [Fact]
        public void EmitPatchStruct_SmePromoListField_EmitsBlock2U16Ifr()
        {
            // WF SMEPromoListForm.MakeDataLength: IFR block 2, count = getBlockDataCount(base, 2, u16!=0),
            // length = 2*(count+1). Plant 3 non-zero u16 entries then a 0x0000 terminator -> count = 3,
            // length = 2*(3+1) = 8.
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u16(target + 0, 0x0102);
            rom.write_u16(target + 2, 0x0304);
            rom.write_u16(target + 4, 0x0506);
            rom.write_u16(target + 6, 0x0000); // terminator (u16 == 0)
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("SME",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:SMEPROMOLIST", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.Equal(target, a.Addr);
            Assert.Equal(2u * (3u + 1u), a.Length); // 8
            Assert.Equal(2u, a.BlockSize);
            Assert.EndsWith("DATA 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_ClassListField_EmitsBlock1U8Ifr()
        {
            // WF SomeClassListForm.MakeDataLength (CLASSLIST): IFR block 1, count =
            // getBlockDataCount(base, 1, u8!=0), length = 1*(count+1). Plant 4 non-zero u8 then 0x00 ->
            // count = 4, length = 1*(4+1) = 5.
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u8(target + 0, 0x11);
            rom.write_u8(target + 1, 0x22);
            rom.write_u8(target + 2, 0x33);
            rom.write_u8(target + 3, 0x44);
            rom.write_u8(target + 4, 0x00); // terminator (u8 == 0)
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("CLS",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:CLASSLIST", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.Equal(target, a.Addr);
            Assert.Equal(1u * (4u + 1u), a.Length); // 5
            Assert.Equal(1u, a.BlockSize);
            Assert.EndsWith("DATA 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        // ---- s2pf-10: BATTLEANIMEPOINTER — the LAST interim form-bound arm, now precise.
        //      WF ImageBattleAnimeForm.MakeBattleAnimeSettingDataLength (PatchForm.cs:6558): the per-field
        //      SETTING walk = Init(null) (block 4, IsDataExists u32(addr+0)!=0) -> ReInitPointer(p) ->
        //      AddAddress(IFR, name, new uint[]{}) (EMPTY pointerIndexes). length = 4*(count+1). This is
        //      NOT the slice-2s full-path per-class MakeAllDataLength (EmitImageBattleAnime).

        [Fact]
        public void EmitPatchStruct_BattleAnimeField_EmitsBlock4U32Ifr_EmptyPI()
        {
            // Plant 3 non-zero u32 setting entries then a 0x00000000 terminator -> count = 3,
            // length = 4*(3+1) = 16, block 4, type InputFormRef, EMPTY pointerIndexes.
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u32(target + 0, 0x11223344);
            rom.write_u32(target + 4, 0x55667788);
            rom.write_u32(target + 8, 0x99AABBCC);
            rom.write_u32(target + 12, 0x00000000); // terminator (u32 == 0)
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("BA",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:BATTLEANIMEPOINTER", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.Equal(target, a.Addr);
            Assert.Equal(4u * (3u + 1u), a.Length); // 16
            Assert.Equal(4u, a.BlockSize);
            Assert.EndsWith("DATA 0", a.Info);
            // EMPTY pointerIndexes — the setting block is flat (no embedded sub-pointers to walk).
            Assert.Empty(a.PointerIndexes);
            // NOT the interim default-MIX placeholder.
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Fact]
        public void EmitPatchStruct_BattleAnimeField_ImmediateTerminator_LengthIsFour()
        {
            // u32 == 0 at the very base -> count = 0, length = 4*(0+1) = 4 (the lone terminator slot).
            var rom = MakeRom();
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            rom.write_u32(target + 0, 0x00000000); // terminator at base
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("BA0",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:BATTLEANIMEPOINTER", "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.Equal(target, a.Addr);
            Assert.Equal(4u, a.Length); // 4*(0+1)
            Assert.Equal(4u, a.BlockSize);
        }

        [Theory]
        [InlineData("TERRAINBATTLELISTPOINTER")]
        [InlineData("BATTLEBGLISTPOINTER")]
        public void EmitPatchStruct_TerrainLookupField_FixedCountIfr_NotTerminatorScan(string fieldType)
        {
            // WF MapTerrain{Floor,BG}LookupTableForm.MakeDataLength: IFR block 1, FIXED count =
            // rom.RomInfo.map_terrain_type_count (NOT a 0x00-terminator scan). Plant the target with
            // EARLY 0x00 bytes — a terminator scan would stop at count 0, but the FIXED-count walk
            // ignores the bytes and uses map_terrain_type_count. length = 1*(count+1).
            var rom = MakeRom();
            uint count = rom.RomInfo.map_terrain_type_count;
            Assert.True(count > 0);
            const uint table = 0x2000, target = 0x8000;
            PlantPointer(rom, table + 0, target);
            // Deliberately plant 0x00 at the very start: a terminator scan WOULD yield count 0.
            rom.write_u8(target + 0, 0x00);
            rom.write_u8(target + 1, 0x00);
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("TL",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:" + fieldType, "0")), isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.Equal(target, a.Addr);
            Assert.Equal(1u * (count + 1u), a.Length); // FIXED-count length, NOT 1 (terminator-scan count 0)
            Assert.NotEqual(1u, a.Length);
            Assert.Equal(1u, a.BlockSize);
            Assert.EndsWith("DATA 0", a.Info);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Theory]
        [InlineData("VENNOUWEAPONLOCK")]
        [InlineData("AOERANGEPOINTER")]
        [InlineData("SMEPROMOLIST")]
        [InlineData("CLASSLIST")]
        [InlineData("TERRAINBATTLELISTPOINTER")]
        [InlineData("BATTLEBGLISTPOINTER")]
        [InlineData("BATTLEANIMEPOINTER")] // s2pf-10
        public void EmitPatchStruct_FormBoundField_UnsafeTarget_EmitsNothing(string fieldType)
        {
            // The slot derefs below the 0x200 safety floor -> every precise form-bound arm skips
            // (no emission, and NOT a default-MIX placeholder either).
            var rom = MakeRom();
            const uint table = 0x2000;
            U.write_u32(rom.Data, table + 0, U.toPointer(0x100)); // target below safety floor
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("USB",
                ("TYPE", "STRUCT"), ("ADDRESS", "0x2000"),
                ("DATASIZE", "4"), ("DATACOUNT", "1"),
                ("P0:" + fieldType, "0")), isPointerOnly: false);

            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.BIN);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.InputFormRef
                && x.Pointer == table + 0);
            Assert.DoesNotContain(list, x => x.DataType == Address.DataTypeEnum.MIX);
        }

        [Theory]
        [InlineData("VENNOUWEAPONLOCK")]
        [InlineData("AOERANGEPOINTER")]
        [InlineData("SMEPROMOLIST")]
        [InlineData("CLASSLIST")]
        [InlineData("TERRAINBATTLELISTPOINTER")]
        [InlineData("BATTLEBGLISTPOINTER")]
        [InlineData("BATTLEANIMEPOINTER")] // s2pf-10
        public void EmitPatchStruct_FormBoundField_SlotNearEof_NoThrow(string fieldType)
        {
            // A pointer FIELD sitting in the last 3 bytes -> the slot+3 EOF guard makes a clean skip
            // (no throw) rather than reading p32/u32 past EOF.
            var rom = MakeRom();
            uint near = (uint)rom.Data.Length - 1;
            var list = new List<Address>();
            Exception ex = Record.Exception(() =>
                RebuildProducerCore.EmitPatchStruct(rom, list, MakePatch("EOF9",
                    ("TYPE", "STRUCT"), ("ADDRESS", "0x" + near.ToString("X")),
                    ("DATASIZE", "4"), ("DATACOUNT", "1"),
                    ("P0:" + fieldType, "0")), isPointerOnly: false));
            Assert.Null(ex);
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
