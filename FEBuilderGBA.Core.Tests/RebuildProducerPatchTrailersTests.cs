// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-16 (#1261) — the THREE SHARED EA/BIN trace
// TRAILERS (option-B PatchForm epic, sub-slice 16 of 17). These run at the tail of BOTH
// TraceEAPatchedMapping (WF PatchForm.cs:5508-5512) and TraceBINPatchedMapping (WF :5214-5218):
//
//   1. AppendNewTargetSelectionStruct (WF :5534) — ROM-DETERMINISTIC -> PORTED. Each
//      NEW_TARGET_SELECTION_STRUCT param: convertBinAddressString GREP loop -> rom.u32
//      deref -> isSafetyPointer -> a 32-byte NEW_TARGET_SELECTION_STRUCT BinMapping.
//   2. TraceEditPatch (WF :4600) — param+ROM-DETERMINISTIC -> PORTED. Each EDIT_PATCH param:
//      LoadPatch the referenced patch, trace its nested STRUCT / IMAGE / BIN|EA ranges.
//   3. AppendMenuPatch (WF :5583) — WinForms-only (live MenuDefinitionForm/MenuCommandForm
//      state) -> LOUD REJECT onto `untraceable` (RejectMenuPatch); never a silent omission.
//
// VERIFICATION without a real installed patch: a synthetic FE8U ROM is loaded, the patch
// BYTES are PLANTED at known addresses, and hand-authored patch files + a synthetic PatchSt
// drive the trace. The asserts pin the reconstructed BinMappings and the EmitPatch{EA,BIN}
// dispatch + the recorded untraceable notes for MENU. Both the EA and BIN arms are exercised
// (the trailers are TYPE-independent — they run identically in both).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchTrailersTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly List<string> _tempDirs = new List<string>();

        public RebuildProducerPatchTrailersTests()
        {
            CoreState.Language = "en";
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
            CoreState.BaseDirectory = _savedBaseDir;
            foreach (string d in _tempDirs)
            {
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
        }

        // 16 MiB zero-filled FE8U ROM (LoadLow minimum for BE8E01) — RomInfo-bearing. Also sets
        // CoreState.ROM: the trace threads `rom` explicitly, but Address.AddNewTargetSelectionStruct
        // (the 8-pointer walk in the EA emit) + the Address.Add* sinks use the single-arg
        // U.isSafetyOffset/isSafetyPointer overloads that read CoreState.ROM.
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            CoreState.ROM = rom;
            return rom;
        }

        string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "trailers-s2pf16-" + Path.GetRandomFileName());
            Directory.CreateDirectory(d);
            _tempDirs.Add(d);
            return d;
        }

        static void Write(ROM rom, uint addr, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) rom.write_u8(addr + (uint)i, bytes[i]);
        }

        // Write a 4-byte GBA pointer (little-endian 0x08000000|offset) at a ROM slot.
        static void WritePointer(ROM rom, uint slot, uint targetOffset)
        {
            uint gba = 0x08000000u + targetOffset;
            Write(rom, slot,
                (byte)(gba & 0xFF), (byte)((gba >> 8) & 0xFF),
                (byte)((gba >> 16) & 0xFF), (byte)((gba >> 24) & 0xFF));
        }

        static PatchInstallCore.PatchSt MakeEaPatch(string dir, string name, string eaFileName,
            params (string key, string value)[] extra)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "EA";
            p.Param["EA"] = eaFileName;
            foreach (var (key, value) in extra) p.Param[key] = value;
            return p;
        }

        static PatchInstallCore.PatchSt MakeBinPatch(string dir, string name,
            params (string key, string value)[] kv)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = name,
                PatchFileName = Path.Combine(dir, "PATCH_" + name + ".txt"),
                Param = new Dictionary<string, string>(),
            };
            p.Param["TYPE"] = "BIN";
            foreach (var (key, value) in kv) p.Param[key] = value;
            return p;
        }

        static EventAssemblerUninstallCore.BinMapping ByKey(
            List<EventAssemblerUninstallCore.BinMapping> map, string key)
        {
            return map.FirstOrDefault(m => m.key == key);
        }

        // $GREP4 macro that finds the EXACT 4 bytes planted at a 4-aligned slot. GREP returns
        // the slot offset; the trailer then derefs rom.u32(slot) for the target.
        static string Grep4(params byte[] bytes)
        {
            return "$GREP4 " + string.Join(" ", bytes.Select(b => "0x" + b.ToString("X2")));
        }

        // ====================================================================
        // 1. NEW_TARGET_SELECTION_STRUCT trailer (WF :5534) — PORTED
        // ====================================================================

        [Fact]
        public void NewTargetSelectionStruct_EmitsThe32ByteStruct_OnBinTrace()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // Plant a pointer SLOT (4-aligned) holding a GBA pointer to a struct target.
            uint slot = 0x900000;
            uint target = 0x901000;
            WritePointer(rom, slot, target);
            // Plant some recognizable struct bytes so the 32-byte read is non-zero.
            for (uint i = 0; i < 32; i++) Write(rom, target + i, (byte)(0xC0 + i));

            // The slot's own 4 bytes (the GBA pointer LE) are the GREP needle: GREP finds the
            // slot, the trailer derefs it. After the first hit, lastAddr advances past the slot,
            // so the (unique) pattern is not re-found => the WF while(true) loop terminates.
            byte[] needle = rom.getBinaryData(slot, 4u);
            var patch = MakeBinPatch(dir, "nt",
                ("NEW_TARGET_SELECTION_STRUCT", Grep4(needle)));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "NEW_TARGET_SELECTION_STRUCT");
            Assert.NotNull(b);
            Assert.Equal(target, b.addr);                       // WF :5560/:5565 — deref'd target.
            Assert.Equal(32u, b.length);                        // WF :5567 — 8*4.
            Assert.Equal(Address.DataTypeEnum.NEW_TARGET_SELECTION_STRUCT, b.type);
            Assert.Equal("NEW_TARGET_SELECTION_STRUCT", b.filename);
            Assert.Equal(32, b.bin.Length);
            // Byte-parity vs WF :5574 (getBinaryData(addr, 32)): the full 32-byte struct read
            // back must equal the planted bytes (0xC0..0xDF), exactly what WF would extract.
            byte[] expected = new byte[32];
            for (int i = 0; i < 32; i++) expected[i] = (byte)(0xC0 + i);
            Assert.Equal(expected, b.bin);
            Assert.All(b.mask, m => Assert.True(m));            // WF :5575 — MakeFullMask (all true).
        }

        [Fact]
        public void NewTargetSelectionStruct_RunsOnEaTrace_Too()
        {
            // The trailer is TYPE-independent — it must ALSO fire for a TYPE=EA patch (WF :5512).
            var rom = MakeRom();
            string dir = NewTempDir();
            // A trivial empty .event so the EA walker has a file but emits no EA blocks.
            File.WriteAllText(Path.Combine(dir, "main.event"), "// empty\n");

            uint slot = 0x910000;
            uint target = 0x911000;
            WritePointer(rom, slot, target);
            byte[] needle = rom.getBinaryData(slot, 4u);

            var patch = MakeEaPatch(dir, "nt", "main.event",
                ("NEW_TARGET_SELECTION_STRUCT", Grep4(needle)));

            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "NEW_TARGET_SELECTION_STRUCT");
            Assert.NotNull(b);
            Assert.Equal(target, b.addr);
            Assert.Equal(32u, b.length);
            Assert.Equal(Address.DataTypeEnum.NEW_TARGET_SELECTION_STRUCT, b.type);
        }

        [Fact]
        public void NewTargetSelectionStruct_UnsafeTargetPointer_IsSkipped()
        {
            // A slot whose deref'd value is NOT a safe pointer (e.g. 0) => WF :5561 continue.
            var rom = MakeRom();
            string dir = NewTempDir();
            uint slot = 0x920000;
            // Plant a NON-pointer value (a small literal, not in 0x08000200..) at the slot.
            Write(rom, slot, 0x10, 0x00, 0x00, 0x00);     // u32 = 0x10 => not a safe pointer.
            byte[] needle = rom.getBinaryData(slot, 4u);
            var patch = MakeBinPatch(dir, "nt",
                ("NEW_TARGET_SELECTION_STRUCT", Grep4(needle)));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Null(ByKey(map, "NEW_TARGET_SELECTION_STRUCT"));
        }

        [Fact]
        public void NewTargetSelectionStruct_EmitPatchEA_RoutesToAddNewTargetSelectionStruct()
        {
            // End-to-end through EmitPatchEA: a NEW_TARGET mapping must drive
            // Address.AddNewTargetSelectionStruct (WF :6287) — a length-32 NEW_TARGET Address.
            var rom = MakeRom();
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "main.event"), "// empty\n");

            uint slot = 0x930000;
            uint target = 0x931000;
            WritePointer(rom, slot, target);
            byte[] needle = rom.getBinaryData(slot, 4u);
            var patch = MakeEaPatch(dir, "nt", "main.event",
                ("NEW_TARGET_SELECTION_STRUCT", Grep4(needle)));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.NEW_TARGET_SELECTION_STRUCT
                && a.Addr == target && a.Length == 32u);
        }

        [Fact]
        public void NewTargetSelectionStruct_EmitPatchBIN_EmitsLengthZeroDefault_VerbatimWf()
        {
            // End-to-end through EmitPatchBIN: a NEW_TARGET mapping in the BIN arm falls through
            // the WF default arm (WF :6398-6406) and emits a LENGTH-0 NEW_TARGET_SELECTION_STRUCT
            // Address — VERBATIM WF (the BIN arm, unlike the EA arm at WF :6287, does NOT call
            // Address.AddNewTargetSelectionStruct). This pins the documented BIN/EA asymmetry and
            // proves the traced range DOES survive into EmitPatchBIN's output (Copilot #1333).
            var rom = MakeRom();
            string dir = NewTempDir();
            uint slot = 0x980000;
            uint target = 0x981000;
            WritePointer(rom, slot, target);
            byte[] needle = rom.getBinaryData(slot, 4u);
            var patch = MakeBinPatch(dir, "nt",
                ("NEW_TARGET_SELECTION_STRUCT", Grep4(needle)));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            // The mapping flows through (NOT dropped) — emitted as a length-0 NEW_TARGET Address.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.NEW_TARGET_SELECTION_STRUCT
                && a.Addr == target && a.Length == 0u);
        }

        [Fact]
        public void EditPatch_EmitPatchBIN_StructDataAndPointerSurvive_VerbatimWf()
        {
            // End-to-end through EmitPatchBIN: the EDIT_PATCH STRUCT POINTER (default MIX) + DATA
            // (default MIX) mappings flow through the WF default arm as LENGTH-0 MIX Addresses
            // (verbatim WF :6398-6406). Proves the traced trailer ranges reach EmitPatchBIN output.
            var rom = MakeRom();
            string dir = NewTempDir();
            uint slot = 0x990000;
            uint structAddr = 0x991000;
            WritePointer(rom, slot, structAddr);
            byte[] needle = rom.getBinaryData(slot, 4u);

            string editFile = Path.Combine(dir, "edit_struct.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=STRUCT",
                "POINTER=" + Grep4(needle),
                "DATASIZE=0x4",
                "DATACOUNT=0x2",
            }));
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_struct.txt"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            // Both the POINTER slot and the struct DATA flow through as MIX length-0 Addresses.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX && a.Addr == slot);
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX && a.Addr == structAddr);
        }

        [Fact]
        public void EditPatch_EmitPatchBIN_NestedBinUnusedBinSurvivesWithLength_VerbatimWf()
        {
            // A nested-BIN EDIT_PATCH CLEAR range is UNUSEDBIN-typed, which DOES hit a non-default
            // arm (WF :6378-6387) and keeps its real length through EmitPatchBIN — proving the
            // recursive trailer mappings reach the BIN producer output with the correct length.
            var rom = MakeRom();
            string dir = NewTempDir();
            uint clearAddr = 0x9A0000;
            uint clearLen = 0x20;
            string editFile = Path.Combine(dir, "edit_bin.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=BIN",
                "CLEAR:0x" + clearAddr.ToString("X") + ":0x" + clearLen.ToString("X") + "=x",
            }));
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_bin.txt"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.UNUSEDBIN
                && a.Addr == clearAddr && a.Length == clearLen);
        }

        [Fact]
        public void EditPatch_EmitPatchEA_NestedStructDataSurvives()
        {
            // End-to-end through EmitPatchEA: the EA arm's default branch (WF :6301-6309) also
            // emits a length-0 Address for the EDIT_PATCH STRUCT DATA (default MIX) — proving the
            // EDIT_PATCH trailer reaches the EA producer output too.
            var rom = MakeRom();
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "main.event"), "// empty\n");
            uint slot = 0x9B0000;
            uint structAddr = 0x9B1000;
            WritePointer(rom, slot, structAddr);
            byte[] needle = rom.getBinaryData(slot, 4u);

            string editFile = Path.Combine(dir, "edit_struct.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=STRUCT",
                "POINTER=" + Grep4(needle),
                "DATASIZE=0x4",
                "DATACOUNT=0x2",
            }));
            var patch = MakeEaPatch(dir, "ep", "main.event", ("EDIT_PATCH", "edit_struct.txt"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX && a.Addr == structAddr);
        }

        // ====================================================================
        // 2. EDIT_PATCH trailer (WF :4600) — PORTED
        // ====================================================================

        [Fact]
        public void EditPatch_StructPointer_TracesPointerSlotAndDataBlock()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // A POINTER-form STRUCT nested patch: POINTER resolves a slot, derefs to a struct,
            // DATASIZE*DATACOUNT sizes the data block.
            uint slot = 0x940000;
            uint structAddr = 0x941000;
            WritePointer(rom, slot, structAddr);
            byte[] needle = rom.getBinaryData(slot, 4u);

            // The nested STRUCT patch file (EDIT_PATCH target). POINTER uses a GREP to locate the
            // slot; DATASIZE=4, DATACOUNT=2 => an 8-byte data block at structAddr.
            string editFile = Path.Combine(dir, "edit_struct.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=STRUCT",
                "POINTER=" + Grep4(needle),
                "DATASIZE=0x4",
                "DATACOUNT=0x2",
            }));

            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_struct.txt"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            // The POINTER slot mapping (WF :4710-4717) — 4-byte, all-true mask.
            var ptr = map.FirstOrDefault(m => m.key == "POINTER" && m.addr == slot);
            Assert.NotNull(ptr);
            Assert.Equal(4u, ptr.length);
            Assert.Equal(new bool[] { true, true, true, true }, ptr.mask);

            // The DATA block mapping (WF :4761-4770) — datacount*datasize = 8 bytes at structAddr.
            var data = map.FirstOrDefault(m => m.key == "DATA" && m.addr == structAddr);
            Assert.NotNull(data);
            Assert.Equal(8u, data.length);
        }

        [Fact]
        public void EditPatch_StructPointer_LiteralDataCount_MultiInstallStops()
        {
            // The multi-install loop (WF :4673) advances +16 each pass; a POINTER GREP that only
            // matches once yields exactly ONE struct (the second pass finds nothing => stop).
            var rom = MakeRom();
            string dir = NewTempDir();
            uint slot = 0x950000;
            uint structAddr = 0x951000;
            WritePointer(rom, slot, structAddr);
            byte[] needle = rom.getBinaryData(slot, 4u);

            string editFile = Path.Combine(dir, "edit_one.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=STRUCT",
                "POINTER=" + Grep4(needle),
                "DATASIZE=0x10",
                "DATACOUNT=0x1",
            }));
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_one.txt"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Single(map.Where(m => m.key == "DATA"));
            var data = map.First(m => m.key == "DATA");
            Assert.Equal(structAddr, data.addr);
            Assert.Equal(0x10u, data.length);          // 1 * 0x10.
        }

        [Fact]
        public void EditPatch_Image_TracesImageDataBlock()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // A nested IMAGE patch: IMAGE_POINTER slot derefs to an image target; default 8x8
            // => length 8*8/2 = 32 bytes typed IMG.
            uint slot = 0x960000;
            uint imgTarget = 0x961000;
            WritePointer(rom, slot, imgTarget);

            // The IMAGE patch resolves IMAGE_POINTER via convertBinAddressString from start 0x100;
            // a literal hex slot offset parses directly (no GREP needed for IMAGE_POINTER).
            string editFile = Path.Combine(dir, "edit_image.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=IMAGE",
                "IMAGE_POINTER=0x" + slot.ToString("X"),
            }));
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_image.txt"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            // The image data wraps as a "DATA" BinMapping at the deref'd target (WF :4658-4666).
            var data = map.FirstOrDefault(m => m.key == "DATA" && m.addr == imgTarget);
            Assert.NotNull(data);
            Assert.Equal(32u, data.length);                  // 8*8/2.
            Assert.Equal(Address.DataTypeEnum.IMG, data.type);
        }

        [Fact]
        public void EditPatch_NestedBin_RecursivelyTracesInnerBinRanges()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // A nested BIN patch with a literal CLEAR range => UNUSEDBIN mapping, traced
            // recursively via TracePatchedMapping (WF :4646-4649).
            uint clearAddr = 0x970000;
            uint clearLen = 0x20;
            string editFile = Path.Combine(dir, "edit_bin.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=BIN",
                "CLEAR:0x" + clearAddr.ToString("X") + ":0x" + clearLen.ToString("X") + "=x",
            }));
            var patch = MakeEaPatch(dir, "ep", "main.event", ("EDIT_PATCH", "edit_bin.txt"));
            File.WriteAllText(Path.Combine(dir, "main.event"), "// empty\n");

            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            // The inner CLEAR range surfaces as an UNUSEDBIN mapping at the nested addr.
            var cleared = map.FirstOrDefault(m => m.type == Address.DataTypeEnum.UNUSEDBIN
                && m.addr == clearAddr);
            Assert.NotNull(cleared);
            Assert.Equal(clearLen, cleared.length);
        }

        [Fact]
        public void EditPatch_StructZeroDataSize_RecordsUntraceable_NeverThrows()
        {
            // A malformed STRUCT EDIT_PATCH with DATASIZE=0 (or absent) must NOT throw / abort the
            // producer; it records an honest omission and emits no struct (Copilot #1333). WF's
            // double-division would also yield "no struct" via +Infinity->0; this guard makes that
            // explicit + platform-independent.
            var rom = MakeRom();
            string dir = NewTempDir();
            uint slot = 0x9C0000;
            uint structAddr = 0x9C1000;
            WritePointer(rom, slot, structAddr);
            byte[] needle = rom.getBinaryData(slot, 4u);

            string editFile = Path.Combine(dir, "edit_bad.txt");
            File.WriteAllText(editFile, string.Join("\n", new[]
            {
                "TYPE=STRUCT",
                "POINTER=" + Grep4(needle),
                "DATASIZE=0x0",                       // zero datasize.
                "DATACOUNT=$GREP4 0x99 0x88 0x77 0x66", // macro branch -> would divide by datasize.
            }));
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "edit_bad.txt"));

            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch, untraceable);

            // No DATA struct emitted; the gap is recorded; no exception.
            Assert.DoesNotContain(map, m => m.key == "DATA");
            Assert.Contains(untraceable, s => s.Contains("DATASIZE"));
        }

        [Fact]
        public void EditPatch_MissingFile_RecordsUntraceable_NeverThrows()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            var patch = MakeBinPatch(dir, "ep", ("EDIT_PATCH", "no_such_edit.txt"));

            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch, untraceable);

            // No crash; the gap is recorded (honest omission), no mapping added.
            Assert.DoesNotContain(map, m => m.key == "DATA");
            Assert.Contains(untraceable, s => s.Contains("EDIT_PATCH"));
        }

        // ====================================================================
        // 3. MENU trailer (WF :5583) — LOUD REJECT (WinForms-only)
        // ====================================================================

        [Theory]
        [InlineData("EA_EXTENDS_UNITMENU")]
        [InlineData("EA_EXTENDS_GAMEMENU")]
        [InlineData("EA_EXTENDS_ITEMMENU")]
        public void MenuPatch_RecordsLoudReject_OnBinTrace(string menuKey)
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            var patch = MakeBinPatch(dir, "menu", (menuKey, "something"));

            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch, untraceable);

            // LOUD REJECT: recorded on untraceable, NEVER a silent MENU mapping.
            Assert.Contains(untraceable, s => s.Contains("MENU") && s.Contains(menuKey));
            Assert.DoesNotContain(map, m => m.key == "MENU");
        }

        [Fact]
        public void MenuPatch_RecordsLoudReject_OnEaTrace_Too()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "main.event"), "// empty\n");
            var patch = MakeEaPatch(dir, "menu", "main.event",
                ("EA_EXTENDS_UNITMENU", "x"));

            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            Assert.Contains(untraceable, s => s.Contains("MENU"));
            Assert.DoesNotContain(map, m => m.key == "MENU");
        }

        [Fact]
        public void MenuPatch_NonMenuPatch_RecordsNothing()
        {
            // A bare patch with NO menu-extend param records no MENU reject (regression guard:
            // the trailers must be NO-OP for ordinary patches).
            var rom = MakeRom();
            string dir = NewTempDir();
            var patch = MakeBinPatch(dir, "plain",
                ("CLEAR:0x800000:0x10", "x"));

            var untraceable = new List<string>();
            RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch, untraceable);

            Assert.DoesNotContain(untraceable, s => s.Contains("MENU"));
        }

        // ====================================================================
        // No-trailer-param regression: a bare patch is unaffected by all three trailers.
        // ====================================================================

        [Fact]
        public void BarePatch_NoTrailerParams_TrailersAreNoOp()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] payload = { 0x01, 0x02, 0x03, 0x04 };
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), payload);
            uint addr = 0x800000;
            var patch = MakeBinPatch(dir, "plain", ("BIN:0x" + addr.ToString("X"), "p.bin"));

            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch, untraceable);

            // Exactly the one BIN mapping — no NEW_TARGET, no DATA(EDIT_PATCH), no MENU.
            Assert.Single(map);
            Assert.Equal("BIN:0x" + addr.ToString("X"), map[0].key);
            Assert.DoesNotContain(untraceable, s => s.Contains("MENU"));
        }
    }
}
