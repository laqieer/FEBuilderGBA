// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-3 (#1261) — the two TRIVIAL terminal
// PatchForm producer TYPE arms (option-B epic, sub-slice 3 of 11):
//   RebuildProducerCore.EmitPatchAddr   = WF PatchForm.MakePatchStructDataListForADDR   @:6213
//   RebuildProducerCore.EmitPatchSwitch = WF PatchForm.MakePatchStructDataListForSWITCH @:6425
//
// Coverage (synthetic in-memory ROM + synthetic PatchSt — no real GBA ROM file,
// mirroring RebuildProducerPatchResolverTests):
//   1. EmitPatchAddr:
//        - literal multi-address ADDRESS -> one Address per token (addr/length/pointer/type/name)
//        - the BIN(<4) vs MIX(>=4) length classification boundary (length 3,4,5)
//        - $0x macro single-address path (resolves via ResolvePatchAddress, startOffset=0x100)
//        - COMBO-driven length (CalcAddrLength)
//        - per-token isSafetyOffset SKIP (one unsafe addr skipped, the rest emit)
//        - isPointerOnly -> length 0
//        - empty ADDRESS -> nothing
//        - INHERITED s2pf-2 contract: an unsafe $0x deref macro SKIPS (returns, emits nothing)
//          rather than throwing — pinning the intentional NOT_FOUND-vs-WF-throw divergence.
//   2. EmitPatchSwitch:
//        - ONN:/OFF: keys -> one Address per pair (VERBATIM WF prefixes; NOT "ON:")
//        - length = param VALUE space-token count; BIN/MIX boundary
//        - non-ONN:/OFF: key ignored; op.Length<2 key skipped; "ON:" (3-char) NOT matched
//        - $-macro and literal address resolution (both via ResolvePatchAddress unconditionally)
//        - per-pair isSafetyOffset SKIP; isPointerOnly -> length 0
//   3. Integration through the public orchestrator MakePatchStructDataListCore on a
//      staged config/patch2 tree (proves the ADDR/SWITCH switch arms are wired).

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchAddrSwitchTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        string _tempDir;

        public RebuildProducerPatchAddrSwitchTests()
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
        // RebuildProducerPatchResolverTests.MakeRom. Also sets CoreState.ROM: although the
        // emitters thread `rom` explicitly, the Address.AddAddress sink they call uses the
        // single-arg U.isSafetyOffset overload, which reads CoreState.ROM (same coupling the
        // RebuildProducerAsmTests CreateTestRom helper handles). Restored in Dispose.
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            CoreState.ROM = rom;
            return rom;
        }

        // Build a PatchSt from raw key/value lines (insertion-ordered Param, exactly like
        // PatchHardCodeScanner.LoadPatch). Name drives the emitted "@ADDRESS"/"@SWITCH" info.
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

        // Find the single Address whose Info matches (helper for single-entry asserts).
        static Address Single(List<Address> list, string info)
        {
            return Assert.Single(list, a => a.Info == info);
        }

        // ====================================================================
        // 1. EmitPatchAddr (WF :6213)
        // ====================================================================

        [Fact]
        public void EmitPatchAddr_EmptyAddress_EmitsNothing()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // No ADDRESS key -> U.at returns "" -> return.
            RebuildProducerCore.EmitPatchAddr(rom, list, MakePatch("p", ("TYPE", "ADDR")), isPointerOnly: false);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchAddr_SingleLiteral_NoCombo_LengthOne_IsBIN()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // No COMBO -> CalcAddrLength = 1 -> length 1 (< 4) -> BIN. ADDRESS 0x1000 is safe.
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("PatchA", ("TYPE", "ADDR"), ("ADDRESS", "0x1000")), isPointerOnly: false);

            var a = Single(list, "PatchA@ADDRESS");
            Assert.Equal(0x1000u, a.Addr);
            Assert.Equal(1u, a.Length);
            Assert.Equal(U.NOT_FOUND, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.BIN, a.DataType);
        }

        [Fact]
        public void EmitPatchAddr_MultipleLiterals_EmitsOnePerToken()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // Space-split literal -> three addresses, each its own Address entry.
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Trio", ("TYPE", "ADDR"), ("ADDRESS", "0x1000 0x2000 0x3000")), isPointerOnly: false);

            Assert.Equal(3, list.Count);
            Assert.Contains(list, a => a.Addr == 0x1000u && a.Info == "Trio@ADDRESS");
            Assert.Contains(list, a => a.Addr == 0x2000u && a.Info == "Trio@ADDRESS");
            Assert.Contains(list, a => a.Addr == 0x3000u && a.Info == "Trio@ADDRESS");
            Assert.All(list, a => Assert.Equal(Address.DataTypeEnum.BIN, a.DataType)); // length 1 each
        }

        // ---- BIN(<4) vs MIX(>=4) classification boundary (load-bearing) -----
        // A COMBO whose 2nd '|'-section has N space tokens => length N. The
        // length<4 ? BIN : MIX boundary must put length 3 = BIN, 4 = MIX, 5 = MIX
        // exactly as WF (a >=4 entry mis-typed BIN would drop its pointer sub-scan
        // during rebuild = corruption).

        [Theory]
        [InlineData("c|AA BB CC", 3u, Address.DataTypeEnum.BIN)]        // 3 < 4 -> BIN
        [InlineData("c|AA BB CC DD", 4u, Address.DataTypeEnum.MIX)]     // 4 >= 4 -> MIX
        [InlineData("c|AA BB CC DD EE", 5u, Address.DataTypeEnum.MIX)]  // 5 >= 4 -> MIX
        public void EmitPatchAddr_ComboLength_ClassifiesBinMixAtFour(string combo, uint expectLen, Address.DataTypeEnum expectType)
        {
            var rom = MakeRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Boundary", ("TYPE", "ADDR"), ("ADDRESS", "0x4000"), ("COMBO", combo)), isPointerOnly: false);

            var a = Single(list, "Boundary@ADDRESS");
            Assert.Equal(0x4000u, a.Addr);
            Assert.Equal(expectLen, a.Length);
            Assert.Equal(expectType, a.DataType);
        }

        [Fact]
        public void EmitPatchAddr_IsPointerOnly_LengthZero_IsBIN()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // isPointerOnly skips CalcAddrLength -> length stays 0 (< 4) -> BIN, even though
            // COMBO would otherwise size it to 5.
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("PtrOnly", ("TYPE", "ADDR"), ("ADDRESS", "0x5000"), ("COMBO", "c|AA BB CC DD EE")), isPointerOnly: true);

            var a = Single(list, "PtrOnly@ADDRESS");
            Assert.Equal(0u, a.Length);
            Assert.Equal(Address.DataTypeEnum.BIN, a.DataType);
        }

        // ---- $0x macro single-address path ----------------------------------

        [Fact]
        public void EmitPatchAddr_DollarHexMacro_ResolvesAndEmitsSingle()
        {
            var rom = MakeRom();
            const uint slot = 0x1000;
            const uint target = 0x2000;
            U.write_u32(rom.Data, slot, U.toPointer(target));

            var list = new List<Address>();
            // ADDRESS "$0x1000" -> ResolvePatchAddress derefs to target 0x2000 (safe) ->
            // a single Address at 0x2000. COMBO sizes it to length 2 (< 4) -> BIN.
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Macro", ("TYPE", "ADDR"), ("ADDRESS", "$0x1000"), ("COMBO", "c|AA BB")), isPointerOnly: false);

            var a = Single(list, "Macro@ADDRESS");
            Assert.Equal(target, a.Addr);
            Assert.Equal(2u, a.Length);
            Assert.Equal(Address.DataTypeEnum.BIN, a.DataType);
        }

        // ---- INHERITED s2pf-2 contract: unsafe $0x deref SKIPS (no throw) ----
        // WF convertBinAddressString would THROW PatchException on an unsafe $0x deref
        // and (no try/catch up the call chain) abort the whole rebuild. The merged
        // s2pf-2 resolver maps it to U.NOT_FOUND, which fails the isSafetyOffset guard
        // -> EmitPatchAddr returns, emitting nothing. This pins that intentional,
        // inherited divergence so a future change can't silently flip it.

        [Fact]
        public void EmitPatchAddr_UnsafeDollarHexMacro_Skips_NoThrow()
        {
            var rom = MakeRom();
            const uint slot = 0x1000;
            // Point the slot at an UNSAFE offset (0x100 < the 0x200 floor) so the resolved
            // macro address fails isSafetyOffset.
            U.write_u32(rom.Data, slot, U.toPointer(0x100));

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Unsafe", ("TYPE", "ADDR"), ("ADDRESS", "$0x1000")), isPointerOnly: false));

            Assert.Null(ex);    // INHERITED contract: skip, never throw
            Assert.Empty(list); // nothing emitted
        }

        [Fact]
        public void EmitPatchAddr_UnknownMacro_Skips_NoThrow()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // An unknown $MACRO resolves to NOT_FOUND (resolver fall-through) -> skip, no throw.
            var ex = Record.Exception(() => RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Unk", ("TYPE", "ADDR"), ("ADDRESS", "$NOSUCHMACRO 0x1")), isPointerOnly: false));

            Assert.Null(ex);
            Assert.Empty(list);
        }

        // ---- per-token isSafetyOffset SKIP (literal list) -------------------

        [Fact]
        public void EmitPatchAddr_PerTokenUnsafe_SkipsOnlyThatEntry()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // 0x100 is below the 0x200 safe-offset floor -> unsafe -> that one token is
            // skipped (continue), but 0x1000 and 0x2000 still emit. Proves the skip is
            // per-address, not whole-patch.
            RebuildProducerCore.EmitPatchAddr(rom, list,
                MakePatch("Mixed", ("TYPE", "ADDR"), ("ADDRESS", "0x1000 0x100 0x2000")), isPointerOnly: false);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, a => a.Addr == 0x1000u);
            Assert.Contains(list, a => a.Addr == 0x2000u);
            Assert.DoesNotContain(list, a => a.Addr == 0x100u);
        }

        // ====================================================================
        // 2. EmitPatchSwitch (WF :6425)
        // ====================================================================

        [Fact]
        public void EmitPatchSwitch_OnnAndOffKeys_EmitOnePerPair()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // ONN: and OFF: keys both act. Value space-token count = length (here 1 each -> BIN).
            // A non-ONN:/OFF: key (TYPE) is ignored.
            RebuildProducerCore.EmitPatchSwitch(rom, list, MakePatch("Sw",
                ("TYPE", "SWITCH"),
                ("ONN:0x1000", "AA"),
                ("OFF:0x2000", "BB")), isPointerOnly: false);

            Assert.Equal(2, list.Count);
            // both entries share the same Info ("Sw@SWITCH"); assert by addr.
            Assert.Contains(list, a => a.Addr == 0x1000u && a.Length == 1u && a.DataType == Address.DataTypeEnum.BIN);
            Assert.Contains(list, a => a.Addr == 0x2000u && a.Length == 1u && a.DataType == Address.DataTypeEnum.BIN);
            Assert.All(list, a => { Assert.Equal("Sw@SWITCH", a.Info); Assert.Equal(U.NOT_FOUND, a.Pointer); });
        }

        // ---- BIN/MIX boundary on the VALUE space-token count ----------------

        [Theory]
        [InlineData("AA BB CC", 3u, Address.DataTypeEnum.BIN)]        // 3 < 4 -> BIN
        [InlineData("AA BB CC DD", 4u, Address.DataTypeEnum.MIX)]     // 4 >= 4 -> MIX
        [InlineData("AA BB CC DD EE", 5u, Address.DataTypeEnum.MIX)]  // 5 >= 4 -> MIX
        public void EmitPatchSwitch_ValueTokenCount_ClassifiesBinMixAtFour(string value, uint expectLen, Address.DataTypeEnum expectType)
        {
            var rom = MakeRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchSwitch(rom, list,
                MakePatch("B", ("ONN:0x3000", value)), isPointerOnly: false);

            var a = Single(list, "B@SWITCH");
            Assert.Equal(0x3000u, a.Addr);
            Assert.Equal(expectLen, a.Length);
            Assert.Equal(expectType, a.DataType);
        }

        [Fact]
        public void EmitPatchSwitch_IsPointerOnly_LengthZero()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitPatchSwitch(rom, list,
                MakePatch("P", ("ONN:0x3000", "AA BB CC DD EE")), isPointerOnly: true);

            var a = Single(list, "P@SWITCH");
            Assert.Equal(0u, a.Length);
            Assert.Equal(Address.DataTypeEnum.BIN, a.DataType);
        }

        [Fact]
        public void EmitPatchSwitch_NonOnnOffKeys_Ignored()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // VERBATIM WF: only "ONN:" / "OFF:" prefixes act. "ON:" (3-char), "TYPE",
            // "NAME" and an arbitrary key must all be ignored.
            RebuildProducerCore.EmitPatchSwitch(rom, list, MakePatch("X",
                ("TYPE", "SWITCH"),
                ("NAME", "X"),
                ("ON:0x1000", "AA"),     // "ON:" is NOT "ONN:" -> ignored
                ("RANDOM:0x2000", "BB")  // unrelated prefix -> ignored
            ), isPointerOnly: false);

            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchSwitch_KeyWithoutColonSuffix_Skipped()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // "ONN:" with NO address after the colon: Split(':') = {"ONN", ""} -> op.Length==2,
            // op[1] == "" -> ResolvePatchAddress("") -> NOT_FOUND -> isSafetyOffset false -> skip.
            // A bare "ONN" key (no colon) would be IndexOf("ONN:")!=0 -> not matched anyway.
            RebuildProducerCore.EmitPatchSwitch(rom, list,
                MakePatch("Y", ("ONN:", "AA"), ("ONN", "BB")), isPointerOnly: false);

            Assert.Empty(list);
        }

        [Fact]
        public void EmitPatchSwitch_DollarMacroAddress_Resolves()
        {
            var rom = MakeRom();
            const uint slot = 0x1100u;
            const uint target = 0x2200u;
            U.write_u32(rom.Data, slot, U.toPointer(target));

            var list = new List<Address>();
            // SWITCH resolves op[1] via ResolvePatchAddress unconditionally; a $P32 macro
            // (SWITCH callsite appnedSize=8, startOffset=0) derefs the pointer at 0x1100.
            RebuildProducerCore.EmitPatchSwitch(rom, list,
                MakePatch("M", ("ONN:$P32 0x1100", "AA BB")), isPointerOnly: false);

            var a = Single(list, "M@SWITCH");
            Assert.Equal(target, a.Addr);
            Assert.Equal(2u, a.Length);
        }

        [Fact]
        public void EmitPatchSwitch_PerPairUnsafe_SkipsOnlyThatPair()
        {
            var rom = MakeRom();
            var list = new List<Address>();
            // ONN:0x100 is below the 0x200 floor -> unsafe -> skipped; OFF:0x4000 still emits.
            RebuildProducerCore.EmitPatchSwitch(rom, list, MakePatch("Z",
                ("ONN:0x100", "AA"),
                ("OFF:0x4000", "BB")), isPointerOnly: false);

            var a = Single(list, "Z@SWITCH");
            Assert.Equal(0x4000u, a.Addr);
            Assert.DoesNotContain(list, x => x.Addr == 0x100u);
        }

        // ====================================================================
        // 3. Integration through the public orchestrator (ADDR/SWITCH arms wired)
        // ====================================================================

        [Fact]
        public void Orchestrator_AddrAndSwitchPatches_EmitViaWiredArms()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RebuildProducerPatchAddrSwitch_" + Guid.NewGuid().ToString("N"));
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_ADDR.txt"), new[]
            {
                "NAME=AddrPatch",
                "TYPE=ADDR",
                "ADDRESS=0x1000",
                "COMBO=c|AA BB CC DD",   // length 4 -> MIX
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_SWITCH.txt"), new[]
            {
                "NAME=SwitchPatch",
                "TYPE=SWITCH",
                "ONN:0x2000=AA BB",      // length 2 -> BIN
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeRom();
            CoreState.ROM = fe8;

            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false);

            // The leaner Core scanner (PatchHardCodeScanner.LoadPatch) sets only PatchFileName
            // + Param, NOT Name, so the emitted Info is "@ADDRESS"/"@SWITCH" (null Name prefix).
            Assert.Contains(list, a => a.Addr == 0x1000u && a.Info.EndsWith("@ADDRESS") && a.Length == 4u && a.DataType == Address.DataTypeEnum.MIX);
            Assert.Contains(list, a => a.Addr == 0x2000u && a.Info.EndsWith("@SWITCH") && a.Length == 2u && a.DataType == Address.DataTypeEnum.BIN);
        }
    }
}
