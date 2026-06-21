// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-1 (#1261) — the PatchForm producer
// SCAFFOLD (option-B epic, sub-slice 1 of 11).
//
// This slice ports ONLY the foundation of WinForms
// PatchForm.MakePatchStructDataList (FEBuilderGBA/PatchForm.cs:7126):
//   - the public scanner surface (PatchHardCodeScanner.{ScanPatchs, LoadPatch,
//     isCanonicalSkip, CheckIF, ResolvePatchDirectory}) + PatchInstallCore.PatchSt,
//   - IsMakePatchStructDataListTarget (WF :6841) — pure gate routing,
//   - MakePointerIndexes (WF :7174) — pure Param-dict parse + order,
//   - MakePatchStructDataListCore (WF :7126) — orchestrator SKELETON whose six
//     TYPE arms are NO-OP STUBS (nothing emitted this slice).
//
// Coverage:
//   1. IsMakePatchStructDataListTarget — full truth table matching WF :6841-6887.
//   2. MakePointerIndexes — parsed offsets/types + iftType (ASM-only / mix / none)
//      and the preserved Param iteration ORDER.
//   3. MakePatchStructDataListCore — empty/no-dir patch set emits nothing & does
//      not throw; a pre-cancelled token returns the partial list without throwing;
//      the public scanner surface is reachable from the producer assembly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchScaffoldTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly string _tempDir;

        public RebuildProducerPatchScaffoldTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RebuildProducerPatchScaffold_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            CoreState.Language = "en";
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
            CoreState.BaseDirectory = _savedBaseDir;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // 16 MiB is the FE8 LoadLow minimum (Rom.cs requires >= 0x0100_0000); keeping it
        // at the minimum cuts per-test allocation/GC pressure vs a 32 MiB buffer.
        static ROM MakeVersionedRom(string versionString, int size = 0x0100_0000)
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("fake.gba", new byte[size], versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        // Build a PatchInstallCore.PatchSt from raw key/value lines (mirrors a real
        // LoadPatch result). Param iteration order = insertion order, exactly as
        // PatchHardCodeScanner.LoadPatch produces it.
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

        // ====================================================================
        // 1. IsMakePatchStructDataListTarget — truth table (WF :6841-6887)
        // ====================================================================

        // STRUCT: included unless CheckIF == "E".
        [Theory]
        [InlineData("STRUCT", "", false, false, true)]
        [InlineData("STRUCT", "I", false, false, true)]
        [InlineData("STRUCT", "E", false, false, false)]   // error -> excluded
        [InlineData("STRUCT", "E", true, true, false)]     // error wins over flags
        [InlineData("STRUCT", "", true, true, true)]       // isStructOnly does NOT exclude STRUCT
        // IMAGE: included unless CheckIF == "E" (and only reached when !isStructOnly).
        [InlineData("IMAGE", "", false, false, true)]
        [InlineData("IMAGE", "I", false, false, true)]
        [InlineData("IMAGE", "E", false, false, false)]
        [InlineData("IMAGE", "", false, true, false)]      // isStructOnly excludes non-STRUCT BEFORE IMAGE check
        // non STRUCT/IMAGE, not install-only -> always included (when not struct-only).
        [InlineData("ADDR", "", false, false, true)]
        [InlineData("ADDR", "E", false, false, true)]      // checkIF ignored when !isInstallOnly
        [InlineData("BIN", "I", false, false, true)]
        [InlineData("EA", "", false, false, true)]
        [InlineData("SWITCH", "", false, false, true)]
        // isStructOnly excludes every non-STRUCT type.
        [InlineData("ADDR", "", false, true, false)]
        [InlineData("BIN", "I", false, true, false)]
        [InlineData("EA", "", true, true, false)]
        // isInstallOnly: excluded on "E"; included on "I"; excluded otherwise (non STRUCT/IMAGE).
        [InlineData("ADDR", "E", true, false, false)]
        [InlineData("ADDR", "I", true, false, true)]
        [InlineData("ADDR", "", true, false, false)]       // not installed -> excluded
        [InlineData("BIN", "", true, false, false)]
        public void IsMakePatchStructDataListTarget_Matches_WF(
            string type, string checkIF, bool isInstallOnly, bool isStructOnly, bool expected)
        {
            bool actual = RebuildProducerCore.IsMakePatchStructDataListTarget(type, checkIF, isInstallOnly, isStructOnly);
            Assert.Equal(expected, actual);
        }

        // ====================================================================
        // 2. MakePointerIndexes (WF :7174) — parse + iftType + ORDER
        // ====================================================================

        [Fact]
        public void MakePointerIndexes_NoAsm_DataOnly_InputFormRef()
        {
            // Two non-ASM pointer fields + a non-P key that must be ignored.
            var patch = MakePatch("p",
                ("TYPE", "STRUCT"),
                ("P0:POINTER", "0x100"),
                ("BLOCKSIZE", "4"),
                ("P1:POINTER", "0x200"));

            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Equal(new uint[] { 0, 1 }, idx);
            Assert.Equal(new[] { "POINTER", "POINTER" }, types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, ift);
        }

        [Fact]
        public void MakePointerIndexes_AsmOnly_InputFormRef_ASM()
        {
            var patch = MakePatch("p",
                ("TYPE", "STRUCT"),
                ("P0:ASM", "0x100"),
                ("P2:ASM", "0x200"));

            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Equal(new uint[] { 0, 2 }, idx);
            Assert.Equal(new[] { "ASM", "ASM" }, types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef_ASM, ift);
        }

        [Fact]
        public void MakePointerIndexes_AsmAndData_InputFormRef_MIX()
        {
            var patch = MakePatch("p",
                ("TYPE", "STRUCT"),
                ("P0:ASM", "0x100"),
                ("P1:POINTER", "0x200"));

            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Equal(new uint[] { 0, 1 }, idx);
            Assert.Equal(new[] { "ASM", "POINTER" }, types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef_MIX, ift);
        }

        [Fact]
        public void MakePointerIndexes_PreservesParamInsertionOrder()
        {
            // Insert pointer fields OUT of numeric order: the result MUST follow Param
            // insertion (Dictionary) order, NOT sorted-by-index. A later per-entry
            // sub-walk slice maps pointer slots to struct fields in exactly this order.
            var patch = MakePatch("p",
                ("TYPE", "STRUCT"),
                ("P5:POINTER", "0x500"),
                ("NAME", "ignored"),
                ("P2:ASM", "0x200"),
                ("P9:POINTER", "0x900"),
                ("P0:POINTER", "0x000"));

            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Equal(new uint[] { 5, 2, 9, 0 }, idx);             // insertion order, NOT {0,2,5,9}
            Assert.Equal(new[] { "POINTER", "ASM", "POINTER", "POINTER" }, types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef_MIX, ift); // P2:ASM + the data fields
        }

        [Fact]
        public void MakePointerIndexes_MalformedShortKey_SkippedWithoutThrow()
        {
            // A 1-char key (e.g. a malformed "P=..." line) must NOT crash on key[1];
            // it is skipped. A valid pointer field after it is still collected.
            var patch = MakePatch("p",
                ("TYPE", "STRUCT"),
                ("P", "0x100"),            // malformed: length 1 -> skipped (no IndexOutOfRange)
                ("P3:POINTER", "0x300"));

            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Equal(new uint[] { 3 }, idx);
            Assert.Equal(new[] { "POINTER" }, types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, ift);
        }

        [Fact]
        public void MakePointerIndexes_NullPatchOrParam_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.MakePointerIndexes(null, out _, out _));
            var noParam = new PatchInstallCore.PatchSt { Name = "p", PatchFileName = "p.txt", Param = null };
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.MakePointerIndexes(noParam, out _, out _));
        }

        [Fact]
        public void MakePointerIndexes_NoPointerFields_Empty_InputFormRef()
        {
            var patch = MakePatch("p", ("TYPE", "BIN"), ("ADDRESS", "0x100"));
            uint[] idx = RebuildProducerCore.MakePointerIndexes(patch, out string[] types, out Address.DataTypeEnum ift);

            Assert.Empty(idx);
            Assert.Empty(types);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, ift);
        }

        // ====================================================================
        // 3. MakePatchStructDataListCore (WF :7126) — orchestrator skeleton
        // ====================================================================

        [Fact]
        public void MakePatchStructDataListCore_NoPatchDir_EmitsNothing_NoThrow()
        {
            // An EMPTY config/patch2/<version> tree under BaseDirectory -> ScanPatchs returns
            // empty -> the orchestrator does nothing and the list stays empty.
            //
            // The version dir is created EMPTY (not just absent) so PatchHardCodeScanner.
            // ResolvePatchDirectory returns THIS path (the first existing root) instead of
            // falling through to AppContext.BaseDirectory — the test-output bin/, which in CI
            // carries the build-copied REAL config/patch2/FE8U tree. Before the s2pf-3 ADDR/
            // SWITCH arms emitted, that fallback was harmless (every TYPE arm was a no-op stub
            // regardless of what got scanned); now a fallback to the real FE8U tree would emit
            // genuine @ADDRESS/@SWITCH entries and the empty-list assertion would (correctly)
            // fail. Pinning an empty version dir keeps the test's "no patches -> nothing"
            // intent robust in CI (where the submodule is checked out) and locally (where it
            // is not).
            string emptyPatchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(emptyPatchDir); // exists but contains no PATCH_*.txt
            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.MakePatchStructDataListCore(
                    fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false));

            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void MakePatchStructDataListCore_Version0Rom_EmitsNothing()
        {
            // ROMFE0 ("NAZO") -> version 0 -> SafePatchVersionFolder "" -> WF ScanPatchs
            // version==0 guard equivalent -> empty list. Use a bare ROM (no recognized
            // version) which RomInfo defaults to version 0.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x8000]);
            CoreState.ROM = rom;
            CoreState.BaseDirectory = _tempDir;

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.MakePatchStructDataListCore(
                    rom, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false));

            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void MakePatchStructDataListCore_Version0Rom_WithPatchTreePresent_DoesNotScanRoot()
        {
            // Regression for the version-0 root-scan bug (Copilot PR #1311): a no-version
            // ROM must NOT fall through ResolvePatchDirectory("") -> the config/patch2 ROOT
            // and recurse EVERY version's PATCH_*.txt. With a fully populated FE8U tree
            // present under the base dir, the empty-version guard must short-circuit so
            // NOTHING is scanned (no progress reports, empty list).
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_A.txt"), new[]
            {
                "NAME=PatchA",
                "TYPE=STRUCT",
                "P0:POINTER=0x100",
            });

            var rom = new ROM(); // unrecognized -> version 0 -> SafePatchVersionFolder ""
            rom.SwapNewROMDataDirect(new byte[0x8000]);
            CoreState.ROM = rom;
            CoreState.BaseDirectory = _tempDir;

            var reports = new List<string>();
            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                rom, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false,
                progress: new TestProgress(reports.Add));

            Assert.Empty(list);     // guard short-circuits before any directory walk
            Assert.Empty(reports);  // NO patch was scanned -> no per-patch progress
        }

        [Fact]
        public void MakePatchStructDataListCore_WithPatches_StubsEmitNothing_ReportsProgress()
        {
            // A real patch tree with two STRUCT patches that PASS the gate. As of s2pf-5 the STRUCT
            // arm is WIRED (no longer a stub), but these patches have NO top-level POINTER/ADDRESS
            // param (only a P0:POINTER pointer-INDEX field), so EmitPatchStruct early-returns before
            // emitting a table base — the list still MUST stay empty even though the gate admits the
            // patches. Progress is reported once per admitted patch.
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_A.txt"), new[]
            {
                "NAME=PatchA",
                "TYPE=STRUCT",
                "P0:POINTER=0x100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_B.txt"), new[]
            {
                "NAME=PatchB",
                "TYPE=STRUCT",
                "P0:POINTER=0x200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            var reports = new List<string>();
            var progress = new TestProgress(reports.Add);

            var list = new List<Address>();
            RebuildProducerCore.MakePatchStructDataListCore(
                fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false, progress: progress);

            Assert.Empty(list); // no table base in these patches -> EmitPatchStruct early-returns
            // Both STRUCT patches pass the gate, so the per-patch progress fires twice.
            // (PatchHardCodeScanner.LoadPatch is the leaner Core scanner — it sets only
            // PatchFileName + Param, NOT Name, so the WF "Check Patch <name>" message has
            // an empty name here; the orchestrator null-guards it. The report COUNT is the
            // meaningful gate/loop signal for the scaffold.)
            Assert.Equal(2, reports.Count);
            Assert.All(reports, r => Assert.StartsWith("Check Patch ", r));
        }

        [Fact]
        public void MakePatchStructDataListCore_CancelToken_ReturnsPartial_NoThrow()
        {
            // A pre-cancelled token: WF early-returns the PARTIAL list on the DoEvents
            // stop-flag (does NOT throw). With one admitted patch, the Core port returns
            // after the first iteration's cancel check, list still empty (stubs), no throw.
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_A.txt"), new[]
            {
                "NAME=PatchA",
                "TYPE=STRUCT",
                "P0:POINTER=0x100",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.MakePatchStructDataListCore(
                    fe8, list, isPointerOnly: false, isInstallOnly: false, isStructOnly: false,
                    progress: null, ct: cts.Token));

            Assert.Null(ex);       // partial-return, never throws on cancel
            Assert.Empty(list);
        }

        [Fact]
        public void MakePatchStructDataListCore_NullArgs_Throw()
        {
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.MakePatchStructDataListCore(null, new List<Address>(), false, false, false));
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.MakePatchStructDataListCore(fe8, null, false, false, false));
        }

        // ====================================================================
        // s2pf-11 — EA/BIN are HONEST no-op skips (no emission, no throw).
        // ====================================================================

        [Fact]
        public void MakePatchStructDataListCore_EaAndBinPatches_AreSkipped_NoEmission_NoThrow()
        {
            // The orchestrator dispatches TYPE=EA -> MakePatchStructDataListForEA and TYPE=BIN ->
            // MakePatchStructDataListForBIN in WF (both drive TracePatchedMapping). That trace subsystem
            // is NOT yet in Core (the NEXT phase after s2pf-11), so the orchestrator SKIPS those patches:
            // no Address is emitted (a guessed entry would be corruption) and it does NOT throw (a throw
            // would abort the whole producer run on FE8U, which carries TYPE=EA/BIN patches). This is the
            // honest-omission convention covered by the "PatchForm(MakePatchStructDataList)" gate token.
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            // Two INSTALLED EA/BIN patches that reach the EA/BIN dispatch under the LIVE producer flag
            // (isInstallOnly=true). IsMakePatchStructDataListTarget admits a non-STRUCT/IMAGE only on
            // CheckIF=="I"; a PATCHED_IF line whose bytes MATCH the ROM returns "I" — the all-zero
            // synthetic ROM reads 0x00 0x00 at offset 0x1000, so each patch is "installed". We also give
            // each an ADDRESS the EA/BIN trace WOULD use if ported, proving the SKIP is by TYPE (the patch
            // is admitted and reaches the EA/BIN arm), not by the install gate or a missing param.
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA.txt"), new[]
            {
                "NAME=PatchEA",
                "TYPE=EA",
                "PATCHED_IF:0x001000=0x00 0x00",
                "ADDRESS=0x800100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN.txt"), new[]
            {
                "NAME=PatchBIN",
                "TYPE=BIN",
                "PATCHED_IF:0x001000=0x00 0x00",
                "ADDRESS=0x800200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            var reports = new List<string>();
            var list = new List<Address>();
            // The SAME flags as the live producer (ToolROMRebuildMake.cs:820): isInstallOnly=true. Both
            // patches are installed (CheckIF=="I"), so each is ADMITTED and reaches its EA/BIN dispatch
            // arm — where it is SKIPPED (no emission, never a throw). Each admitted patch reports progress
            // once, so the report count proves the patches really reached the dispatch (not gated out).
            var ex = Record.Exception(() =>
                RebuildProducerCore.MakePatchStructDataListCore(
                    fe8, list, isPointerOnly: false, isInstallOnly: true, isStructOnly: false,
                    progress: new TestProgress(reports.Add)));

            Assert.Null(ex);                 // EA/BIN never throw out of the producer
            Assert.Empty(list);              // EA/BIN emit NOTHING (honest skip — no guessed entry)
            Assert.Equal(2, reports.Count);  // both EA/BIN patches were ADMITTED and reached the dispatch
            Assert.All(reports, r => Assert.StartsWith("Check Patch ", r));
        }

        // ====================================================================
        // s2pf-11 — the orchestrator is WIRED into AppendAllAsmStructPointers as
        // the first unconditional call; the gate token STAYS (EA/BIN deferred).
        // ====================================================================

        [Fact]
        public void AppendAllAsmStructPointers_WiresPatchForm_EmitsAddrEntries_TokenStays()
        {
            // After s2pf-11, AppendAllAsmStructPointers calls MakePatchStructDataListCore FIRST
            // (WF U.cs:2619 order) with the rebuild flags isInstallOnly=true/isPointerOnly=false/
            // isStructOnly=false (ToolROMRebuildMake.cs:820). With an INSTALLED TYPE=ADDR patch in the
            // tree, the live producer must now emit the @ADDRESS entry — proving the wiring is real
            // (not a no-op). The token STAYS: IsComplete is false and PatchForm is still re-reported.
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_ADDR.txt"), new[]
            {
                "NAME=PatchAddr",
                "TYPE=ADDR",
                // The live producer uses isInstallOnly=true, which admits a non-STRUCT ADDR only on
                // CheckIF=="I". A PATCHED_IF line whose bytes MATCH the ROM returns "I" (installed): the
                // all-zero synthetic ROM reads 0x00 0x00 at offset 0x1000, so this patch is "installed".
                "PATCHED_IF:0x001000=0x00 0x00",
                // A safe ROM offset (>= 0x200; well inside a 0x0100_0000 image) for the ADDR emission.
                "ADDRESS=0x001000",
            });

            CoreState.BaseDirectory = _tempDir;
            // AppendAllAsmStructPointers requires rom == CoreState.ROM and a versioned ROM. The default
            // 16 MiB image is large enough for the ADDR offset; no ldrmap is needed for the PatchForm call.
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            var list = new List<Address>();
            // The wiring uses the WF rebuild flags internally; ldrmap=null skips the gated group (fine —
            // we only assert the PatchForm half ran).
            var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap: null);

            // The wiring is LIVE: the installed ADDR patch's entry was emitted by the first unconditional call.
            Assert.Contains(list, a => a.Info != null && a.Info.EndsWith("@ADDRESS", StringComparison.Ordinal));
            // The token STAYS: EA/BIN deferred -> never complete -> PatchForm still re-reported.
            Assert.False(res.IsComplete);
            Assert.Contains("PatchForm(MakePatchStructDataList)", res.NotYetPorted);
        }

        [Fact]
        public void AppendAllAsmStructPointers_PatchFormCancel_ReturnsCancelled_NoThrow()
        {
            // A pre-cancelled token must be observed by the wired PatchForm call (the orchestrator
            // early-returns the partial list; the wrapper then re-checks ct and reports cancelled).
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_ADDR.txt"), new[]
            {
                "NAME=PatchAddr",
                "TYPE=ADDR",
                "PATCHED_IF:0x001000=0x00 0x00",
                "ADDRESS=0x001000",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var list = new List<Address>();
            AsmProducerResultLike res = null;
            var ex = Record.Exception(() =>
            {
                var r = RebuildProducerCore.AppendAllAsmStructPointers(
                    fe8, list, ldrmap: null, ct: cts.Token);
                res = new AsmProducerResultLike(r.Cancelled, r.IsComplete);
            });

            Assert.Null(ex);                 // cancel never throws out of the producer
            Assert.NotNull(res);
            Assert.True(res.Cancelled);      // the wired PatchForm cancel propagated to a cancelled result
        }

        // Tiny value capture so the lambda above can surface the result fields without a closure-captured local.
        sealed class AsmProducerResultLike
        {
            public bool Cancelled { get; }
            public bool IsComplete { get; }
            public AsmProducerResultLike(bool cancelled, bool isComplete)
            {
                Cancelled = cancelled; IsComplete = isComplete;
            }
        }

        [Fact]
        public void MakeWithProducer_StillRefuses_ListsPatchForm_FromLiveAsmDeferredList()
        {
            // The completeness gate (MakeWithProducer) must REFUSE while PatchForm is incomplete. After
            // s2pf-11 the orchestrator is WIRED but EA/BIN are still deferred, so the LIVE
            // GetAsmNotYetPortedForms() still contains the token -> the ASM half's IsComplete is false ->
            // MakeWithProducer throws an InvalidOperationException naming PatchForm and never drives
            // RebuildMakeCore.Make. This is the load-bearing safety invariant: the still-incomplete
            // producer can never reach a real defragment. We use the LIVE deferred list (not a synthetic
            // token) so the test fails the day someone removes the token without porting EA/BIN.
            var modifiedRom = new ROM();
            byte[] modified = new byte[0x10000];
            for (uint i = 0; i < 0x100; i++) modified[i] = (byte)(0x11 + i);
            modifiedRom.SwapNewROMDataDirect((byte[])modified.Clone());
            var prevRom = CoreState.ROM;
            CoreState.ROM = modifiedRom;
            try
            {
                var vanilla = new ROM();
                vanilla.SwapNewROMDataDirect(new byte[0x1000]);

                // A COMPLETE data result + the LIVE-incomplete asm result (its NotYetPorted is the real
                // deferred list, which STILL contains the PatchForm token because EA/BIN are deferred).
                var data = new RebuildProducerCore.ProducerResult(
                    new List<Address>(), Array.Empty<string>(), cancelled: false);
                string[] liveAsmNotYet = RebuildProducerCore.GetAsmNotYetPortedForms();
                Assert.Contains("PatchForm(MakePatchStructDataList)", liveAsmNotYet);
                var asm = new RebuildProducerCore.AsmProducerResult(liveAsmNotYet, cancelled: false);
                Assert.False(asm.IsComplete);

                string manifest = Path.Combine(_tempDir, "out.rebuild");
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    RebuildProducerCore.MakeWithProducer(
                        data, asm, modified, vanilla, 0x1000u, manifest));

                Assert.Contains("PatchForm(MakePatchStructDataList)", ex.Message);
                Assert.Contains("not yet ported", ex.Message);
                Assert.False(File.Exists(manifest)); // gate refused before Make -> no manifest
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        // ====================================================================
        // Public scanner surface is reachable from the producer assembly.
        // ====================================================================

        [Fact]
        public void PublicScannerSurface_IsReachable()
        {
            // ResolvePatchDirectory + ScanPatchs + LoadPatch + isCanonicalSkip + CheckIF
            // are all public now (s2pf-1). Exercise them end-to-end on a real patch tree.
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_A.txt"), new[]
            {
                "NAME=PatchA",
                "TYPE=STRUCT",
                "CANONICAL_SKIP=0",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            string resolved = PatchHardCodeScanner.ResolvePatchDirectory("FE8U");
            Assert.Equal(patchDir, resolved);

            List<PatchInstallCore.PatchSt> patchs = PatchHardCodeScanner.ScanPatchs(fe8, resolved, "en");
            Assert.Single(patchs);

            PatchInstallCore.PatchSt p = patchs[0];
            Assert.False(PatchHardCodeScanner.isCanonicalSkip(p));
            // No IF lines -> CheckIF passes ("" — not "E").
            Assert.NotEqual("E", PatchHardCodeScanner.CheckIF(fe8, p));

            // LoadPatch is public too.
            PatchInstallCore.PatchSt loaded = PatchHardCodeScanner.LoadPatch(
                fe8, Path.Combine(patchDir, "PATCH_A.txt"), "en");
            Assert.NotNull(loaded);
            Assert.Equal("STRUCT", U.at(loaded.Param, "TYPE"));
        }

        // Minimal IProgress<string> capture.
        sealed class TestProgress : IProgress<string>
        {
            readonly Action<string> _onReport;
            public TestProgress(Action<string> onReport) { _onReport = onReport; }
            public void Report(string value) => _onReport(value);
        }
    }
}
