// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-15 (#1261) — the TYPE=BIN producer ARM
// (option-B PatchForm epic, sub-slice 15 of 17):
//   RebuildProducerCore.TraceBINPatchedMappingForProducer = WF PatchForm.TraceBINPatchedMapping        @:4951
//   RebuildProducerCore.EmitPatchBIN                       = WF PatchForm.MakePatchStructDataListForBIN @:6317
//   EventAssemblerUninstallCore.ReadMod(string[],string,out,ROM) = WF PatchForm.ReadMod(file)          @:4309
//
// The BIN trace is a FROM-SCRATCH port (no Core BIN trace existed). It RE-LOCATES where
// the already-installed BIN bytes landed in the SAVED ROM (deterministic — never
// allocates). Three WF arms in WF order: JUMP, BIN-family ($FREEAREA GREP-locate /
// fixed-addr), SLIDE, CLEAR (UNUSEDBIN).
//
// VERIFICATION of an "installed-BIN" scenario without a real installed patch (vanilla
// FE8U installs 0 BIN patches): a synthetic FE8 ROM is loaded, the patch BYTES are PLANTED
// at known addresses, and hand-authored .bin files + a synthetic PatchSt drive the
// trace/emit. The asserts pin the reconstructed BinMappings + the emitted Address entries
// (addr/length/pointer/type). Coverage per arm: every JUMP length case ($NONE/$NONE+1/
// $NONE-above-borderline/$B/$BL/generated-aligned/generated-misaligned), $FREEAREA
// GREP-locate + lastMatchAddr advance + the JUMP-match->ASM cross-arm rule, fixed-addr BIN
// (BIN/BINP/BINAP/BINF), SLIDE, CLEAR, the EmitPatchBIN per-type dispatch, isPointerOnly,
// and ASMMAP=false early-return.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchBINTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly List<string> _tempDirs = new List<string>();

        public RebuildProducerPatchBINTests()
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
        // trace's compress_image_borderline_address seed + $NONE borderline split work. Also
        // sets CoreState.ROM: although the trace threads `rom` explicitly, EmitPatchBIN's
        // Address.Add* sinks use the single-arg U.isSafetyOffset/isSafetyPointer overloads
        // that read CoreState.ROM (same coupling as RebuildProducerPatchEATests).
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
            string d = Path.Combine(Path.GetTempPath(), "bin-s2pf15-" + Path.GetRandomFileName());
            Directory.CreateDirectory(d);
            _tempDirs.Add(d);
            return d;
        }

        // Build a TYPE=BIN PatchSt whose PatchFileName lives in `dir` (so $FREEAREA/fixed BIN
        // files resolve against that dir) with the given params (full colon-keys, like a real
        // PATCH file: "BIN:$FREEAREA", "JUMP:0x...:$NONE", "CLEAR:0x...:0x...").
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
            foreach (var (key, value) in kv)
            {
                p.Param[key] = value;
            }
            return p;
        }

        static void Write(ROM rom, uint addr, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) rom.write_u8(addr + (uint)i, bytes[i]);
        }

        static EventAssemblerUninstallCore.BinMapping ByKey(
            List<EventAssemblerUninstallCore.BinMapping> map, string key)
        {
            return map.FirstOrDefault(m => m.key == key);
        }

        // ====================================================================
        // JUMP arm — every length/type case (WF :4960-5037)
        // ====================================================================

        [Fact]
        public void TraceJump_None_AboveBorderline_IsPointerLen4()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // borderline for FE8 is well below 0x800000, so this addr is "above" => POINTER.
            uint addr = 0x800000;
            Write(rom, addr, 0x11, 0x22, 0x33, 0x44, 0x55);
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$NONE", "lab"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + addr.ToString("X") + ":$NONE");
            Assert.NotNull(b);
            Assert.Equal(addr, b.addr);
            Assert.Equal(4u, b.length);
            Assert.Equal(Address.DataTypeEnum.POINTER, b.type);
            Assert.Equal("$JUMP:lab", b.filename);
            Assert.Equal(4, b.mask.Length);
            Assert.All(b.mask, m => Assert.False(m));     // WF :5030 — all false.
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, b.bin);
        }

        [Fact]
        public void TraceJump_NonePlus1_IsPointerAsmLen4()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$NONE:+1", "lab"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + addr.ToString("X") + ":$NONE:+1");
            Assert.NotNull(b);
            Assert.Equal(4u, b.length);
            Assert.Equal(Address.DataTypeEnum.POINTER_ASM, b.type);   // WF :4985 — +1 => code.
        }

        [Fact]
        public void TraceJump_None_AtOrBelowBorderline_IsPointerAsmLen4()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // An addr at/below the compress borderline => code => POINTER_ASM (WF :4989).
            uint border = rom.RomInfo.compress_image_borderline_address;
            // Pick a safe offset that is <= border (border itself is safe and > 0x200).
            uint addr = border;
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$NONE", "lab"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + addr.ToString("X") + ":$NONE");
            Assert.NotNull(b);
            Assert.Equal(4u, b.length);
            Assert.Equal(Address.DataTypeEnum.POINTER_ASM, b.type);
        }

        [Fact]
        public void TraceJump_B_And_BL_AreBinLen2()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint a1 = 0x800000;
            uint a2 = 0x800010;
            Write(rom, a1, 0xAA, 0xBB, 0xCC);
            Write(rom, a2, 0xDD, 0xEE, 0xFF);
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + a1.ToString("X") + ":$B", "lb"),
                ("JUMP:0x" + a2.ToString("X") + ":$BL", "lbl"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + a1.ToString("X") + ":$B");
            Assert.NotNull(b);
            Assert.Equal(2u, b.length);
            Assert.Equal(Address.DataTypeEnum.BIN, b.type);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, b.bin);

            var bl = ByKey(map, "JUMP:0x" + a2.ToString("X") + ":$BL");
            Assert.NotNull(bl);
            Assert.Equal(2u, bl.length);
            Assert.Equal(Address.DataTypeEnum.BIN, bl.type);
        }

        [Fact]
        public void TraceJump_Generated_Aligned_IsJumpToHackLen8()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;            // % 4 == 0 => length 8 (WF :5016).
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$r3", "tgt"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + addr.ToString("X") + ":$r3");
            Assert.NotNull(b);
            Assert.Equal(8u, b.length);
            Assert.Equal(Address.DataTypeEnum.JUMPTOHACK, b.type);
        }

        [Fact]
        public void TraceJump_Generated_Misaligned_IsJumpToHackLen10()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800002;            // % 4 != 0 => length 10 (NOP align, WF :5012).
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$r3", "tgt"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "JUMP:0x" + addr.ToString("X") + ":$r3");
            Assert.NotNull(b);
            Assert.Equal(10u, b.length);
            Assert.Equal(Address.DataTypeEnum.JUMPTOHACK, b.type);
        }

        [Fact]
        public void TraceJump_UnsafeAddress_IsSkipped()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // 0x100 < 0x200 => isSafetyOffset false => WF :4973 continue.
            var patch = MakeBinPatch(dir, "j", ("JUMP:0x100:$NONE", "lab"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Empty(map);
        }

        // ====================================================================
        // BIN-family arm — fixed-addr (WF :5097-5110) + $FREEAREA GREP (WF :5072-5096)
        // ====================================================================

        [Fact]
        public void TraceBin_FixedAddr_KeyTypes_AreMapped()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] payload = { 0x01, 0x02, 0x03, 0x04 };
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), payload);

            uint aMix = 0x800000;   // BIN  -> MIX
            uint aPtr = 0x800100;   // BINP -> POINTER
            uint aAsm = 0x800200;   // BINAP-> POINTER_ASM
            uint aBin = 0x800300;   // BINF -> BIN
            var patch = MakeBinPatch(dir, "b",
                ("BIN:0x" + aMix.ToString("X"), "p.bin"),
                ("BINP:0x" + aPtr.ToString("X"), "p.bin"),
                ("BINAP:0x" + aAsm.ToString("X"), "p.bin"),
                ("BINF:0x" + aBin.ToString("X"), "p.bin"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var mix = ByKey(map, "BIN:0x" + aMix.ToString("X"));
            Assert.NotNull(mix);
            Assert.Equal(aMix, mix.addr);
            Assert.Equal(4u, mix.length);
            Assert.Equal(Address.DataTypeEnum.MIX, mix.type);
            // Fixed-addr: bin == the FILE bytes (WF :5108), not the (zeroed) ROM.
            Assert.Equal(payload, mix.bin);

            Assert.Equal(Address.DataTypeEnum.POINTER, ByKey(map, "BINP:0x" + aPtr.ToString("X")).type);
            Assert.Equal(Address.DataTypeEnum.POINTER_ASM, ByKey(map, "BINAP:0x" + aAsm.ToString("X")).type);
            Assert.Equal(Address.DataTypeEnum.BIN, ByKey(map, "BINF:0x" + aBin.ToString("X")).type);
        }

        [Fact]
        public void TraceBin_FreeArea_GrepLocates_AndAdvancesBaseline()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A unique pattern PLANTED above the borderline (the GREP search floor).
            byte[] pat = { 0xCA, 0xFE, 0xBA, 0xBE, 0x12, 0x34, 0x56, 0x78 };
            uint border = rom.RomInfo.compress_image_borderline_address;
            uint planted = ((border + 0x10000) + 3) & ~3u;   // 4-aligned, above border.
            Write(rom, planted, pat);
            File.WriteAllBytes(Path.Combine(dir, "fa.dmp"), pat);

            var patch = MakeBinPatch(dir, "fa", ("BIN:$FREEAREA", "fa.dmp"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "BIN:$FREEAREA");
            Assert.NotNull(b);
            Assert.Equal(planted, b.addr);                  // GREP found the planted copy.
            Assert.Equal((uint)pat.Length, b.length);
            Assert.Equal(Address.DataTypeEnum.MIX, b.type);  // not JUMP-matched => stays MIX.
            Assert.Equal(pat, b.bin);                        // read from ROM at the match.
        }

        [Fact]
        public void TraceBin_FreeArea_AlsoJumpMatched_BecomesAsm()
        {
            // The ContinueBattleBGM real-patch shape: a JUMP and a $FREEAREA BIN that name
            // the SAME file => the $FREEAREA BIN is typed ASM (WF :5090-5093).
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] pat = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0x00, 0x11 };
            uint border = rom.RomInfo.compress_image_borderline_address;
            uint planted = ((border + 0x20000) + 3) & ~3u;
            Write(rom, planted, pat);
            File.WriteAllBytes(Path.Combine(dir, "code.dmp"), pat);

            uint jumpAddr = 0x800000;
            Write(rom, jumpAddr, 0x99, 0x88, 0x77, 0x66);
            var patch = MakeBinPatch(dir, "cbgm",
                ("BIN:$FREEAREA", "code.dmp"),
                ("JUMP:0x" + jumpAddr.ToString("X") + ":$r3", "code.dmp"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var fa = ByKey(map, "BIN:$FREEAREA");
            Assert.NotNull(fa);
            Assert.Equal(planted, fa.addr);
            Assert.Equal(Address.DataTypeEnum.ASM, fa.type);   // JUMP-matched => ASM.
        }

        [Fact]
        public void TraceBin_FreeArea_TwoBlocks_AdvanceBaseline_MatchSecondCopy()
        {
            // Rigorous lastMatchAddr-advance coverage (Copilot #1331): TWO $FREEAREA blocks
            // share the SAME byte pattern, PLANTED at two distinct ROM addresses (the install
            // order). If the trace did NOT advance lastMatchAddr to addr+length after the first
            // hit, the SECOND GREP would re-match the FIRST (earliest) copy — corruption. With
            // the advance, the second entry matches the SECOND copy.
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] pat = { 0xC0, 0xDE, 0xF0, 0x0D, 0xBA, 0xAD, 0xF0, 0x0D };
            uint border = rom.RomInfo.compress_image_borderline_address;
            uint first = ((border + 0x40000) + 3) & ~3u;
            uint second = first + 0x10000;      // a SECOND, later copy of the same pattern.
            Write(rom, first, pat);
            Write(rom, second, pat);
            File.WriteAllBytes(Path.Combine(dir, "a.dmp"), pat);
            File.WriteAllBytes(Path.Combine(dir, "b.dmp"), pat);

            // Two distinct dict keys, both with sp[1]=="$FREEAREA" (sp[2] chaddr 0/1 yields no
            // LDR mask for this pointer-free pattern, so the masks are identical all-false).
            var patch = MakeBinPatch(dir, "two",
                ("BIN:$FREEAREA:0", "a.dmp"),
                ("BIN:$FREEAREA:1", "b.dmp"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var a = ByKey(map, "BIN:$FREEAREA:0");
            var b = ByKey(map, "BIN:$FREEAREA:1");
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(first, a.addr);        // first hit at the first planted copy.
            Assert.Equal(second, b.addr);       // advanced => second hit at the SECOND copy.
            Assert.NotEqual(a.addr, b.addr);    // would be EQUAL if the baseline did not advance.
        }

        [Fact]
        public void TraceBin_FreeArea_NoMatch_IsSkipped()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A pattern that is NOT planted anywhere => GREP miss => WF :5082 continue.
            byte[] pat = { 0x7F, 0x6E, 0x5D, 0x4C, 0x3B, 0x2A, 0x19, 0x08 };
            File.WriteAllBytes(Path.Combine(dir, "miss.dmp"), pat);
            var patch = MakeBinPatch(dir, "m", ("BIN:$FREEAREA", "miss.dmp"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Empty(map);
        }

        // ====================================================================
        // SLIDE arm (WF :5129-5170) + CLEAR arm (WF :5171-5210)
        // ====================================================================

        [Fact]
        public void TraceSlide_LiteralRange_IsMixWithRomBytes()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            uint dest = 0x800008;
            Write(rom, addr, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80);
            var patch = MakeBinPatch(dir, "s",
                ("SLIDE:0x" + addr.ToString("X") + ":x", "0x" + dest.ToString("X")));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "SLIDE:0x" + addr.ToString("X") + ":x");
            Assert.NotNull(b);
            Assert.Equal(addr, b.addr);
            Assert.Equal(dest - addr, b.length);            // WF :5156
            Assert.Equal(Address.DataTypeEnum.MIX, b.type);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 }, b.bin);
        }

        [Fact]
        public void TraceSlide_TooFewFields_IsSkipped()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // sp.Length < 3 => WF :5141 continue. "SLIDE:0x800000" has only 2 fields.
            var patch = MakeBinPatch(dir, "s", ("SLIDE:0x800000", "0x800008"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Empty(map);
        }

        [Fact]
        public void TraceSlide_DestBelowOrEqualAddr_IsSkipped_NoUnderflow()
        {
            // Producer fault-safety (Copilot #1331): a malformed SLIDE with dest <= addr would
            // underflow `dest_addr - addr` to a huge uint => OOM on new bool[length]. We skip it.
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800010;
            uint dest = 0x800000;   // dest < addr => skip (no allocation).
            var patch = MakeBinPatch(dir, "s",
                ("SLIDE:0x" + addr.ToString("X") + ":x", "0x" + dest.ToString("X")));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Empty(map);
        }

        [Fact]
        public void TraceClear_MissingLengthField_IsSkipped_NoCrash()
        {
            // Producer fault-safety (Copilot #1331): WF guards sp.Length<2 but reads sp[2]
            // (length). A malformed `CLEAR:0x800000` (no length) would throw. We skip it.
            var rom = MakeRom();
            string dir = NewTempDir();
            var patch = MakeBinPatch(dir, "c", ("CLEAR:0x800000", "x"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Empty(map);
        }

        [Fact]
        public void TraceClear_LiteralAddrLength_IsUnusedBin()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            uint len = 0x10;
            Write(rom, addr, 0xAB, 0xCD);
            var patch = MakeBinPatch(dir, "c",
                ("CLEAR:0x" + addr.ToString("X") + ":0x" + len.ToString("X"), "x"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            var b = ByKey(map, "CLEAR:0x" + addr.ToString("X") + ":0x" + len.ToString("X"));
            Assert.NotNull(b);
            Assert.Equal(addr, b.addr);
            Assert.Equal(len, b.length);                    // WF :5196 (sp[2]).
            Assert.Equal(Address.DataTypeEnum.UNUSEDBIN, b.type);
            Assert.Equal(0xAB, b.bin[0]);
        }

        // ====================================================================
        // WF trace ORDER (WF :4960 JUMP, then :5039 BIN, then :5129 SLIDE, then :5171 CLEAR)
        // ====================================================================

        [Fact]
        public void Trace_EmitsInWfArmOrder_JumpBinSlideClear()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] pat = { 0x01, 0x02, 0x03, 0x04 };
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), pat);

            uint jumpAddr = 0x801000;
            uint binAddr = 0x802000;
            uint slideAddr = 0x803000;
            uint slideDest = 0x803010;
            uint clearAddr = 0x804000;
            var patch = MakeBinPatch(dir, "ord",
                ("CLEAR:0x" + clearAddr.ToString("X") + ":0x8", "x"),
                ("SLIDE:0x" + slideAddr.ToString("X") + ":y", "0x" + slideDest.ToString("X")),
                ("BIN:0x" + binAddr.ToString("X"), "p.bin"),
                ("JUMP:0x" + jumpAddr.ToString("X") + ":$NONE", "lab"));

            var map = RebuildProducerCore.TraceBINPatchedMappingForProducer(rom, patch);

            Assert.Equal(4, map.Count);
            Assert.StartsWith("JUMP:", map[0].key);
            Assert.StartsWith("BIN:", map[1].key);
            Assert.StartsWith("SLIDE:", map[2].key);
            Assert.StartsWith("CLEAR:", map[3].key);
        }

        // ====================================================================
        // EmitPatchBIN per-type dispatch (WF :6317-6422)
        // ====================================================================

        [Fact]
        public void EmitPatchBIN_AsmMapFalse_EmitsNothing()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), new byte[] { 1, 2, 3, 4 });
            var patch = MakeBinPatch(dir, "n",
                ("ASMMAP", "false"),
                ("BIN:0x" + addr.ToString("X"), "p.bin"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchBIN_PointerType_EmitsPointerPlusExtra()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A BINP at a slot whose 4 bytes point to a safe ROM target so AddPointer keeps it.
            uint slot = 0x800000;
            uint target = 0x08800100; // GBA pointer to offset 0x800100 (safe).
            Write(rom, slot, (byte)(target & 0xFF), (byte)((target >> 8) & 0xFF),
                (byte)((target >> 16) & 0xFF), (byte)((target >> 24) & 0xFF));
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), new byte[] { 0, 0, 0, 0 });
            var patch = MakeBinPatch(dir, "p", ("BINP:0x" + slot.ToString("X"), "p.bin"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            // WF :6338-6352 — AddPointer (POINTER) + AddAddress (POINTER) at the slot.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.POINTER && a.Pointer == slot);
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.POINTER && a.Addr == slot && a.Pointer == U.NOT_FOUND);
        }

        [Fact]
        public void EmitPatchBIN_BinType_EmitsBinWithLength()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            byte[] payload = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), payload);
            var patch = MakeBinPatch(dir, "f", ("BINF:0x" + addr.ToString("X"), "p.bin"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            // WF :6368-6377 — AddAddress length m.length typed BIN.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.BIN
                && a.Addr == addr && a.Length == (uint)payload.Length);
        }

        [Fact]
        public void EmitPatchBIN_JumpToHackType_EmitsBinLengthMinus4()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;   // aligned => length 8 => BIN length 8-4 = 4 (WF :6392).
            var patch = MakeBinPatch(dir, "j",
                ("JUMP:0x" + addr.ToString("X") + ":$r3", "tgt"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.BIN
                && a.Addr == addr && a.Length == 4u);
        }

        [Fact]
        public void EmitPatchBIN_UnusedBinType_EmitsUnusedBin()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            uint len = 0x20;
            var patch = MakeBinPatch(dir, "c",
                ("CLEAR:0x" + addr.ToString("X") + ":0x" + len.ToString("X"), "x"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            // WF :6378-6387 — AddAddress length m.length typed UNUSEDBIN.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.UNUSEDBIN
                && a.Addr == addr && a.Length == len);
        }

        [Fact]
        public void EmitPatchBIN_PointerOnly_EmitsSingleLengthZero()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            uint addr = 0x800000;
            byte[] payload = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), payload);
            var patch = MakeBinPatch(dir, "f", ("BINF:0x" + addr.ToString("X"), "p.bin"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: true);

            // WF :6408-6416 — pointer-only collapses to ONE length-0 AddAddress typed m.type.
            var emitted = list.Where(a => a.Addr == addr).ToList();
            Assert.Single(emitted);
            Assert.Equal(0u, emitted[0].Length);
            Assert.Equal(Address.DataTypeEnum.BIN, emitted[0].DataType);
        }

        [Fact]
        public void EmitPatchBIN_DefaultMixType_EmitsLengthZero()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            // A fixed-addr BIN: => MIX => default arm => AddAddress length 0 typed MIX (WF :6398-6406).
            uint addr = 0x800000;
            File.WriteAllBytes(Path.Combine(dir, "p.bin"), new byte[] { 1, 2, 3, 4 });
            var patch = MakeBinPatch(dir, "m", ("BIN:0x" + addr.ToString("X"), "p.bin"));

            var list = new List<Address>();
            RebuildProducerCore.EmitPatchBIN(rom, list, patch, isPointerOnly: false);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX
                && a.Addr == addr && a.Length == 0u);
        }

        // ====================================================================
        // ReadMod(file) overload — byte-faithful to WF :4309
        // ====================================================================

        [Fact]
        public void ReadMod_File_MissingFile_ReturnsEmpty()
        {
            var rom = MakeRom();
            string[] sp = { "BIN", "$FREEAREA" };
            bool[] mask;
            byte[] b = EventAssemblerUninstallCore.ReadMod(sp, "no-such-file.bin", out mask, rom);
            Assert.Empty(b);
            Assert.Empty(mask);
        }

        [Fact]
        public void ReadMod_File_ReadsBytes_AndBuildsMask()
        {
            var rom = MakeRom();
            string dir = NewTempDir();
            byte[] payload = { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
            string f = Path.Combine(dir, "m.bin");
            File.WriteAllBytes(f, payload);

            string[] sp = { "BIN", "$FREEAREA" }; // sp[2] absent => chaddr 0 => no LDR mask.
            bool[] mask;
            byte[] b = EventAssemblerUninstallCore.ReadMod(sp, f, out mask, rom);

            Assert.Equal(payload, b);
            Assert.Equal(payload.Length, mask.Length);
            Assert.All(mask, m => Assert.False(m));   // no LDR pointers in this payload at base 0.
        }

        // ====================================================================
        // s2pf-17 (CAPSTONE) — END-TO-END through the ORCHESTRATOR
        // (MakePatchStructDataListCore), proving the EA/BIN wiring routes a real
        // installed TYPE=BIN patch from the patch tree to EmitPatchBIN.
        // ====================================================================

        [Fact]
        public void MakePatchStructDataListCore_InstalledBinPatch_RoutesToEmitPatchBIN_EmitsUnusedBin()
        {
            // s2pf-17: the orchestrator now dispatches TYPE=BIN -> EmitPatchBIN (WF PatchForm.cs:7153-7156).
            // Stage a real installed TYPE=BIN patch (a CLEAR -> deterministic UNUSEDBIN entry, no file
            // dependency) under config/patch2/FE8U, set BaseDirectory, and run the FULL orchestrator with
            // the live rebuild flags (isInstallOnly=true). The wired BIN arm must emit the UNUSEDBIN entry.
            var rom = MakeRom();                 // sets CoreState.ROM to a BE8E01 16 MiB ROM
            string baseDir = NewTempDir();       // the directory that CONTAINS config/
            string patchDir = Path.Combine(baseDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            uint addr = 0x800000;
            uint len = 0x20;
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN.txt"), new[]
            {
                "NAME=PatchBIN",
                "TYPE=BIN",
                // PATCHED_IF whose bytes MATCH the all-zero ROM at 0x1000 -> CheckIF=="I" (installed), so the
                // isInstallOnly=true gate admits this non-STRUCT/IMAGE patch and it reaches the BIN arm.
                "PATCHED_IF:0x001000=0x00 0x00",
                // A CLEAR key -> EmitPatchBIN emits a deterministic UNUSEDBIN entry of the literal length.
                "CLEAR:0x" + addr.ToString("X") + ":0x" + len.ToString("X") + "=x",
            });

            // BaseDirectory is the parent of config/ so ResolvePatchDirectory("FE8U") finds the tree.
            CoreState.BaseDirectory = baseDir;

            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                rom, list, isPointerOnly: false, isInstallOnly: true, isStructOnly: false);

            // The wired BIN arm emitted the CLEAR -> UNUSEDBIN entry (WF :6378-6387).
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.UNUSEDBIN
                && a.Addr == addr && a.Length == len);
        }
    }
}
