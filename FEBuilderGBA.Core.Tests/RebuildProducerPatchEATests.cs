// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-14 (#1261) — the TYPE=EA producer ARM
// (option-B PatchForm epic, sub-slice 14 of 17):
//   RebuildProducerCore.TraceEAPatchedMappingForProducer = WF PatchForm.TraceEAPatchedMapping        @:5247
//   RebuildProducerCore.EmitPatchEA                       = WF PatchForm.MakePatchStructDataListForEA @:6259
//
// The trace REUSES the s2pf-13 shared walker EventAssemblerUninstallCore.EmitEaDataList,
// so the ORG/ASM/MIX/LYN/LYNHOOK/POINTER_ARRAY/BIN/mask/GREP/EraseORG logic stays
// byte-identical to the verified #1242 uninstall trace. The ONE producer divergence is
// PROCS: the uninstall path SKIPS PROCS as residue, but the PRODUCER EMITS it via the
// EmitEaDataList procsHandler hook backed by the verbatim CalcProcsLengthAndCheck
// (= ProcsScriptForm.CalcLengthAndCheck), skipping ONLY on NOT_FOUND.
//
// VERIFICATION of an "installed-EA" scenario without a real installed patch: a synthetic
// FE8U ROM is loaded, the patch BYTES are PLANTED at known addresses (a valid PROCS table,
// a POINTER_ARRAY, a unique BIN pattern), and a hand-authored .event + synthetic PatchSt
// drive the trace/emit. The asserts pin the reconstructed BinMappings + the emitted Address
// entries (addr/length/pointer/type). Coverage: ORG, BIN(GREP), PROCS emit, PROCS
// skip-on-NOT_FOUND, GREP-miss -> untraceable, POINTER_ARRAY dispatch, isPointerOnly,
// ASMMAP=false early-return, SYMBOL= side-channel, and the uninstall NON-regression
// (null procsHandler still skips PROCS).

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchEATests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly List<string> _tempDirs = new List<string>();

        public RebuildProducerPatchEATests()
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

        // 16 MiB zero-filled FE8U ROM (LoadLow minimum for BE8E01) — RomInfo-bearing so the
        // walker's compress_image_borderline_address GREP seed + CalcProcsLengthAndCheck work.
        // Also sets CoreState.ROM: although the emitters thread `rom` explicitly, the
        // Address.Add* sinks they call use the single-arg U.isSafetyOffset/isSafetyPointer
        // overloads that read CoreState.ROM (same coupling as RebuildProducerPatchAddrSwitchTests).
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            CoreState.ROM = rom;
            return rom;
        }

        // A fresh temp directory tracked for cleanup in Dispose.
        string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "ea-s2pf14-" + Path.GetRandomFileName());
            Directory.CreateDirectory(d);
            _tempDirs.Add(d);
            return d;
        }

        // Build a TYPE=EA PatchSt whose PatchFileName lives in `dir` (so the *.event scan
        // walks that dir) with the given EA= main-file name + extra params.
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
            foreach (var (key, value) in extra)
            {
                p.Param[key] = value;
            }
            return p;
        }

        // Plant a minimal VALID PROCS table at `offset`: a single all-zero 8-byte
        // instruction (code=0,sarg=0,parg=0) is the EXIT opcode → CalcProcsLengthAndCheck
        // returns 8. (The ROM is zero-filled, so this is already true at `offset`; we write
        // a non-zero guard instruction AFTER it so the length is deterministically > 0 and
        // not dependent on surrounding zeros being interpreted as more PROCS.) Actually we
        // keep it simple: an explicit EXIT word. Returns the expected length.
        static uint PlantProcsExit(ROM rom, uint offset)
        {
            // code=0x00 sarg=0x0000 parg=0x00000000 → valid EXIT, length 8.
            for (int i = 0; i < 8; i++) rom.write_u8(offset + (uint)i, 0x00);
            return 8;
        }

        // ====================================================================
        // EmitPatchEA: ASMMAP=false early-return (WF :6261-6265)
        // ====================================================================

        [Fact]
        public void EmitPatchEA_AsmMapFalse_EmitsNothing()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A .event that WOULD trace an ORG, but ASMMAP=false opts out entirely.
            File.WriteAllText(Path.Combine(dir, "main.event"), "ORG 0x800000\r\nBYTE 0xAA\r\n");
            var patch = MakeEaPatch(dir, "Opt", "main.event", ("ASMMAP", "false"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            Assert.Empty(list);
        }

        // ====================================================================
        // TraceEAPatchedMappingForProducer: ORG + BIN trace (shared walker)
        // ====================================================================

        [Fact]
        public void Trace_OrgAndBin_ReconstructsMappings()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            byte[] pattern = { 0xCA, 0xFE, 0xBA, 0xBE, 0xDE, 0xAD, 0xBE, 0xEF };
            const uint orgAddr = 0x800000;
            const uint binAddr = 0x801000;
            for (int i = 0; i < pattern.Length; i++) rom.write_u8(binAddr + (uint)i, pattern[i]);

            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "#incbin \"blk.bin\" // HINT=BIN\r\n");

            var patch = MakeEaPatch(dir, "OrgBin", "main.event");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            // The BIN GREP-matched the planted pattern; the ORG is provisional and gets
            // EraseORG'd only if a concrete block lands on its address (it does not here),
            // so both the ORG and the BIN appear.
            Assert.Contains(map, m => m.addr == binAddr && m.key == "BIN" && m.type == Address.DataTypeEnum.BIN);
            Assert.Contains(map, m => m.addr == orgAddr && m.key == "ORG");
            Assert.Empty(untraceable);
        }

        [Fact]
        public void Trace_PicksUpEaMainFileWhenHiddenInTxtPatch()
        {
            // The EA= main file is a .txt (not a *.event), so the directory *.event scan
            // would miss it; WF AddIfNotExist adds it. We give it a .txt extension and assert
            // it is still traced. (The parser keys off content, not extension.)
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] pattern = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            const uint binAddr = 0x802000;
            for (int i = 0; i < pattern.Length; i++) rom.write_u8(binAddr + (uint)i, pattern[i]);

            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            // Main file is "main.txt" (NOT .event) → only reachable via the EA= AddIfNotExist.
            File.WriteAllText(Path.Combine(dir, "main.txt"),
                "ORG 0x800000\r\n#incbin \"blk.bin\" // HINT=BIN\r\n");

            var patch = MakeEaPatch(dir, "TxtMain", "main.txt");
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            Assert.Contains(map, m => m.addr == binAddr && m.key == "BIN");
        }

        // The GREP baseline (lastMatchAddr) is seeded ONCE and CARRIED ACROSS .event files
        // (WF PatchForm.TraceEAPatchedMapping seeds it before its foreach, NOT per file). With
        // TWO files that both #incbin the SAME pattern, the FIRST match claims a copy and
        // advances the baseline; the SECOND file MUST match the LATER copy, not re-match the
        // earlier (decoy) one. If the per-file walker reset the baseline to the borderline,
        // the second file would re-select the FIRST copy → a duplicate/wrong free-list entry.
        // (Copilot PR #1329 finding.)
        [Fact]
        public void Trace_MultipleEventFiles_CarriesBaselineAcrossFiles()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            byte[] pattern = { 0x5A, 0xA5, 0x3C, 0xC3, 0x0F, 0xF0, 0x69, 0x96 };
            // Two distinct copies of the SAME pattern, both after the borderline. The first
            // file should match the FIRST copy, the second file the SECOND copy.
            const uint firstAddr = 0x900000;
            const uint secondAddr = 0x901000;
            for (int i = 0; i < pattern.Length; i++)
            {
                rom.write_u8(firstAddr + (uint)i, pattern[i]);
                rom.write_u8(secondAddr + (uint)i, pattern[i]);
            }

            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            // Two event files. The dir *.event scan returns them sorted; both #incbin blk.bin.
            // Name them so the ordering is deterministic (a_, b_). Each is a separate file →
            // each gets its own EmitEaDataList call; the baseline must persist between them.
            File.WriteAllText(Path.Combine(dir, "a_first.event"), "#incbin \"blk.bin\" // HINT=BIN\r\n");
            File.WriteAllText(Path.Combine(dir, "b_second.event"), "#incbin \"blk.bin\" // HINT=BIN\r\n");

            // EA= names one of them with a .EVENT ext (so AddIfNotExist is skipped; the dir
            // scan supplies both). The patch just needs a valid EA= entry.
            var patch = MakeEaPatch(dir, "Multi", "a_first.event");
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            // Both copies are claimed — the second file matched the LATER address, proving the
            // baseline carried across files (NOT reset to the borderline, which would have
            // re-matched firstAddr and dropped secondAddr).
            Assert.Contains(map, m => m.addr == firstAddr && m.key == "BIN");
            Assert.Contains(map, m => m.addr == secondAddr && m.key == "BIN");
        }

        // ====================================================================
        // PROCS EMIT — the producer's STRICTER divergence vs the uninstall path.
        // ====================================================================

        [Fact]
        public void Trace_Procs_EmitsBinMapping_WithRealLength()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // ORG anchor → lastMatchAddr = orgAddr. PROCS ADD advances to advanced; we plant
            // a valid PROCS EXIT there so CalcProcsLengthAndCheck returns a real length.
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd; // Padding4(0x801000) == 0x801000
            uint expectedLen = PlantProcsExit(rom, advanced);

            // PROCS with EMPTY BINData (no quoted-string hint) → handler uses addr = advanced.
            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

            var patch = MakeEaPatch(dir, "Procs", "main.event");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            var procs = map.Find(m => m.type == Address.DataTypeEnum.PROCS);
            Assert.NotNull(procs); // the PRODUCER emits PROCS (the uninstall path would skip it)
            Assert.Equal(advanced, procs.addr);
            Assert.Equal(expectedLen, procs.length);
            Assert.Equal("PROCS", procs.key);
            // A successfully-emitted PROCS is NOT residue.
            Assert.DoesNotContain(untraceable, s => s.Contains("PROCS"));
        }

        [Fact]
        public void EmitPatchEA_Procs_EmitsProcsTypedAddress()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;
            uint expectedLen = PlantProcsExit(rom, advanced);

            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

            var patch = MakeEaPatch(dir, "ProcsEmit", "main.event");
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.PROCS);
            Assert.Equal(advanced, a.Addr);
            Assert.Equal(expectedLen, a.Length);
            Assert.Equal(U.NOT_FOUND, a.Pointer);
            Assert.Contains("@PROCS", a.Info);
        }

        [Fact]
        public void EmitPatchEA_Procs_PointerOnly_LengthZero()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;
            PlantProcsExit(rom, advanced);

            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

            var patch = MakeEaPatch(dir, "ProcsPO", "main.event");
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: true);

            var a = Assert.Single(list, x => x.DataType == Address.DataTypeEnum.PROCS);
            Assert.Equal(0u, a.Length); // pointer-only → length 0 (WF :6295)
        }

        // PROCS whose length cannot be determined (invalid opcode at the resolved address)
        // must be SKIPPED (verbatim WF `continue` at :5453) and recorded as residue —
        // NEVER emitted with a guessed length.
        [Fact]
        public void Trace_Procs_NotFound_SkipsAndRecordsUntraceable()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;

            // Plant an INVALID PROCS opcode (code=0x00FF is unhandled → CalcProcsLengthAndCheck
            // returns NOT_FOUND). Word at +0: FF 00 (u16) = 0x00FF; the rest non-terminating.
            rom.write_u8(advanced + 0, 0xFF);
            rom.write_u8(advanced + 1, 0x00);

            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

            var patch = MakeEaPatch(dir, "ProcsBad", "main.event");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            Assert.DoesNotContain(map, m => m.type == Address.DataTypeEnum.PROCS); // skipped
            Assert.Contains(untraceable, s => s.Contains("PROCS"));                 // recorded (honest)
        }

        // A PROCS skip must STILL advance the GREP baseline (Append+Padding4), so a block
        // AFTER it matches at the advanced address — not an earlier decoy. (Same correctness
        // guard as the uninstall test, here on the producer arm whose handler returns null.)
        [Fact]
        public void Trace_ProcsSkip_AdvancesBaseline_NextBlockMatchesAfter()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            byte[] pattern = { 0xDE, 0xAD, 0xC0, 0xDE, 0xFE, 0xED, 0xFA, 0xCE };
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;     // 0x801000
            const uint decoyAddr = orgAddr + 0x100;        // 0x800100 — BEFORE advanced
            const uint realAddr = advanced + 0x1000;       // 0x802000 — AFTER advanced

            // Invalid PROCS at advanced → skip (but baseline still advances).
            rom.write_u8(advanced + 0, 0xFF);
            rom.write_u8(advanced + 1, 0x00);

            for (int i = 0; i < pattern.Length; i++)
            {
                rom.write_u8(decoyAddr + (uint)i, pattern[i]);
                rom.write_u8(realAddr + (uint)i, pattern[i]);
            }

            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n" +
                "#incbin \"blk.bin\" // HINT=BIN\r\n");

            var patch = MakeEaPatch(dir, "ProcsBaseline", "main.event");
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            Assert.Contains(map, m => m.addr == realAddr && m.key == "BIN");
            Assert.DoesNotContain(map, m => m.addr == decoyAddr);
        }

        // ====================================================================
        // GREP-miss -> untraceable (honest omission, NEVER silent)
        // ====================================================================

        [Fact]
        public void Trace_BinGrepMiss_RecordsUntraceable()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // A BIN pattern that is NOT present anywhere in the (zero-filled) ROM after the
            // borderline → GREP miss → recorded as residue, never silently dropped.
            byte[] pattern = { 0x13, 0x57, 0x9B, 0xDF, 0x24, 0x68, 0xAC, 0xE0 };
            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            File.WriteAllText(Path.Combine(dir, "main.event"),
                "#incbin \"blk.bin\" // HINT=BIN\r\n");

            var patch = MakeEaPatch(dir, "Miss", "main.event");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            Assert.DoesNotContain(map, m => m.key == "BIN");
            Assert.NotEmpty(untraceable);
            Assert.Contains(untraceable, s => s.Contains("blk.bin") || s.Contains("BIN"));
        }

        // ====================================================================
        // POINTER_ARRAY dispatch in EmitPatchEA (WF :6278-6284)
        // ====================================================================

        [Fact]
        public void EmitPatchEA_PointerArray_DispatchesToAddPointerArray()
        {
            var rom = MakeRom();
            string dir = NewTempDir();

            // ORG anchor → lastMatchAddr = orgAddr. POINTER_ARRAY scans from the advanced
            // baseline for consecutive valid pointers. Plant exactly two valid pointers, then
            // a non-pointer word to terminate the run.
            const uint orgAddr = 0x800000;
            const uint paAdd = 0x1000;
            const uint paStart = orgAddr + paAdd;          // 0x801000
            // Two safe pointers (0x08000200 <= p < 0x08000000+romlen).
            rom.write_u32(paStart + 0, 0x08800000);
            rom.write_u32(paStart + 4, 0x08810000);
            // Terminator: a clearly non-pointer value so the run stops at length 8.
            rom.write_u32(paStart + 8, 0x00000001);

            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "pa_label: // HINT=POINTER_ARRAY ADD=" + paAdd.ToString() + "\r\n");

            var patch = MakeEaPatch(dir, "PArr", "main.event");

            // First confirm the trace produced a POINTER_ARRAY mapping of length 8.
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);
            var pa = map.Find(m => m.type == Address.DataTypeEnum.POINTER_ARRAY);
            Assert.NotNull(pa);
            Assert.Equal(paStart, pa.addr);
            Assert.Equal(8u, pa.length);

            // EmitPatchEA dispatches POINTER_ARRAY → AddPointerArray, which adds one inner
            // (MIX) Address per 4-byte slot pointing at the deref'd target.
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            // 2 slots → 2 inner MIX entries whose pointer is the slot, target the deref.
            var inner = list.FindAll(a => a.DataType == Address.DataTypeEnum.MIX
                && a.Info.Contains("Pointer_Array"));
            Assert.Equal(2, inner.Count);
            Assert.Contains(inner, a => a.Pointer == paStart + 0 && a.Addr == U.toOffset(0x08800000));
            Assert.Contains(inner, a => a.Pointer == paStart + 4 && a.Addr == U.toOffset(0x08810000));
        }

        // ====================================================================
        // SYMBOL= side-channel (WF :6266 -> ProcessSymbolByList(list, patch))
        // ====================================================================

        [Fact]
        public void EmitPatchEA_SymbolParam_AddsCommentEntries()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "main.event"), "ORG 0x800000\r\n");
            // EA-format symbol: NAME=$<programAddr>. 0x08800001 (thumb) -> offset 0x800000.
            File.WriteAllText(Path.Combine(dir, "syms.txt"), "MyFunc=$8800001\r\n");

            var patch = MakeEaPatch(dir, "Sym", "main.event", ("SYMBOL", "syms.txt"));
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            // The SYMBOL side-channel adds a comment Address for MyFunc at the symbol's offset.
            Assert.Contains(list, a => a.Info.Contains("MyFunc"));
        }

        [Fact]
        public void EmitPatchEA_NoSymbolParam_NoSymbolEntries()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "main.event"), "ORG 0x800000\r\n");

            var patch = MakeEaPatch(dir, "NoSym", "main.event");
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchEA(rom, list, patch, isPointerOnly: false);

            // No SYMBOL= → no comment entries from the side-channel (ORG-only trace may still
            // emit its provisional ORG MIX entry; just assert no symbol leaked in).
            Assert.DoesNotContain(list, a => a.Info.Contains("MyFunc"));
        }

        // ====================================================================
        // Guards
        // ====================================================================

        [Fact]
        public void Trace_NullRom_Throws()
        {
            var patch = MakeEaPatch(NewTempDir(), "N", "main.event");
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.TraceEAPatchedMappingForProducer(null, patch));
        }

        [Fact]
        public void Trace_MissingEaMainTxt_RecordsUntraceable_NoThrow()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // EA= names a NON-.event file that does not exist. WF AddIfNotExist adds it to the
            // file list (because its ext != ".EVENT"); since it is missing, the producer
            // records the gap instead of throwing (WF's EAUtil ctor would throw on it).
            var patch = MakeEaPatch(dir, "Gone", "does-not-exist.txt");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            Assert.Empty(map);
            Assert.NotEmpty(untraceable); // the missing main file is a recorded gap, not a crash
        }

        [Fact]
        public void Trace_MissingEventFileInDir_NoThrow_EmptyResult()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A .event EA= name that does not exist relies on the dir scan finding it (no
            // AddIfNotExist for .event); the dir is empty, so nothing is traced — and nothing
            // crashes. WF behaves identically (the file is simply never in the list).
            var patch = MakeEaPatch(dir, "GoneEvent", "does-not-exist.event");
            var untraceable = new List<string>();
            var map = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch, untraceable);

            Assert.Empty(map);
        }

        // ====================================================================
        // NON-REGRESSION: the uninstall walker (null procsHandler) still SKIPS PROCS.
        // This is the contract that keeps EmitEaDataList byte-identical for #1242.
        // ====================================================================

        [Fact]
        public void EmitEaDataList_NullProcsHandler_SkipsProcs_AsBeforeS2pf14()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;
            PlantProcsExit(rom, advanced); // a VALID PROCS the producer WOULD emit

            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n");

            var ea = new EAUtilCore(Path.Combine(dir, "main.event"));
            var binMappings = new List<EventAssemblerUninstallCore.BinMapping>();
            var untraceable = new List<string>();

            // Null procsHandler (the uninstall path's default) → PROCS recorded as residue,
            // NOT emitted — byte-identical to pre-s2pf-14 behaviour even though the PROCS is
            // perfectly valid and the producer handler WOULD emit it.
            EventAssemblerUninstallCore.EmitEaDataList(rom, ea, binMappings, untraceable, null);

            Assert.DoesNotContain(binMappings, m => m.type == Address.DataTypeEnum.PROCS);
            Assert.Contains(untraceable, s => s.Contains("PROCS"));
        }

        // The PRODUCER handler EMITS the SAME valid PROCS the uninstall path skips — the two
        // callers of the ONE walker diverge ONLY on PROCS, as designed.
        [Fact]
        public void EmitEaDataList_ProducerVsUninstall_DivergeOnlyOnProcs()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            const uint orgAddr = 0x800000;
            const uint procsAdd = 0x1000;
            const uint advanced = orgAddr + procsAdd;
            uint expectedLen = PlantProcsExit(rom, advanced);

            // Also a BIN block after the PROCS so we can assert the NON-PROCS mappings match.
            byte[] pattern = { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
            const uint binAddr = advanced + 0x2000;
            for (int i = 0; i < pattern.Length; i++) rom.write_u8(binAddr + (uint)i, pattern[i]);

            File.WriteAllBytes(Path.Combine(dir, "blk.bin"), pattern);
            File.WriteAllText(Path.Combine(dir, "main.event"),
                "ORG 0x" + orgAddr.ToString("X") + "\r\n" +
                "procs_label: // HINT=PROCS ADD=" + procsAdd.ToString() + "\r\n" +
                "#incbin \"blk.bin\" // HINT=BIN\r\n");

            // Producer path (via the public producer trace).
            var patch = MakeEaPatch(dir, "Diverge", "main.event");
            var prod = RebuildProducerCore.TraceEAPatchedMappingForProducer(rom, patch);

            // Uninstall path (the shared walker with null handler).
            var ea = new EAUtilCore(Path.Combine(dir, "main.event"));
            var uninst = new List<EventAssemblerUninstallCore.BinMapping>();
            EventAssemblerUninstallCore.EmitEaDataList(rom, ea, uninst, new List<string>(), null);

            // Producer has the PROCS; uninstall does not.
            Assert.Contains(prod, m => m.type == Address.DataTypeEnum.PROCS && m.addr == advanced && m.length == expectedLen);
            Assert.DoesNotContain(uninst, m => m.type == Address.DataTypeEnum.PROCS);

            // Every NON-PROCS mapping is identical between the two (same ORG, same BIN).
            var prodNonProcs = prod.FindAll(m => m.type != Address.DataTypeEnum.PROCS);
            Assert.Equal(uninst.Count, prodNonProcs.Count);
            for (int i = 0; i < uninst.Count; i++)
            {
                Assert.Equal(uninst[i].addr, prodNonProcs[i].addr);
                Assert.Equal(uninst[i].length, prodNonProcs[i].length);
                Assert.Equal(uninst[i].type, prodNonProcs[i].type);
                Assert.Equal(uninst[i].key, prodNonProcs[i].key);
            }
            Assert.Contains(prodNonProcs, m => m.addr == binAddr && m.key == "BIN");
        }
    }
}
