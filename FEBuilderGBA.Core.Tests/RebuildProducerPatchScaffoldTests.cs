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
            // BaseDirectory has no config/patch2/<version> tree -> ScanPatchs returns
            // empty -> the orchestrator does nothing and the list stays empty.
            CoreState.BaseDirectory = _tempDir; // empty temp dir, no patch tree
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
            // A real patch tree with two STRUCT patches that PASS the gate. Every TYPE arm
            // is a NO-OP STUB this slice, so the list MUST stay empty even though the gate
            // admits the patches. Progress is reported once per admitted patch.
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

            Assert.Empty(list); // STUBS emit nothing this slice
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
