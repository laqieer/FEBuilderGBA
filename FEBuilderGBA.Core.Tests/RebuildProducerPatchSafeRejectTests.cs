// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-12 (#1261) — the PatchForm EA/BIN
// per-ROM SAFE-REJECT BACKSTOP (option-B epic, sub-slice 12 of 17, the FIRST of
// the EA/BIN phase).
//
// This slice adds:
//   - PatchHardCodeScanner.EaBinInstallStatus(ROM, PatchSt): a SOUND tri-state
//     install check (Installed / NotInstalled / Unknown) — a conservative
//     over-approximation of WF PatchForm.IsInstalled. Refuses on Installed OR
//     Unknown so it never reports "safe" while a patch could be present.
//   - RebuildProducerCore.PatchFormHasUnportableInstalledPatch(ROM) -> bool:
//     true iff the ROM carries an EA/BIN patch the producer can't emit that is
//     Installed or Unknown.
//   - MakeWithProducer wiring: REFUSE the rebuild (InvalidOperationException naming
//     the offending patch) when the predicate is true — IN ADDITION to the existing
//     IsComplete gate (the token still keeps that gate closed today, so this is
//     belt-and-suspenders now / load-bearing at the gate-open in s2pf-17).
//
// SOUNDNESS: the dangerous direction is a false-negative ("safe" when actually
// unsafe). The naive CheckIF=="I" predicate is UNSOUND (Copilot plan review, issue
// #1325): it misses PATCHED_IFNOT-installed (e.g. FE8U/MNC2Fix) and $FGREP-signature
// patches. EaBinInstallStatus fixes both (PATCHED_IFNOT match => Installed;
// unresolvable signature => Unknown => refuse). These tests pin every branch:
// PATCHED_IF-installed, PATCHED_IFNOT-installed, resolvable-not-installed,
// $FGREP-unknown, TYPE/CANONICAL_SKIP/no-dir/version-0 boundaries.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchSafeRejectTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly string _savedLang = CoreState.Language;
        readonly string _savedBaseDir = CoreState.BaseDirectory;
        readonly string _tempDir;

        public RebuildProducerPatchSafeRejectTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RebuildProducerPatchSafeReject_" + Guid.NewGuid().ToString("N"));
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

        // 16 MiB = FE8 LoadLow minimum (Rom.cs requires >= 0x0100_0000). Keeping it at the
        // minimum cuts per-test allocation vs a 32 MiB buffer (same idiom as the scaffold tests).
        static ROM MakeVersionedRom(string versionString, int size = 0x0100_0000)
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("fake.gba", new byte[size], versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        // Build a PatchSt directly from key/value lines (mirrors a real LoadPatch result) for the
        // unit-level EaBinInstallStatus tests that don't need a staged directory.
        static PatchInstallCore.PatchSt MakePatch(string fileName, params (string key, string value)[] kv)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = fileName,
                PatchFileName = fileName,
                Param = new Dictionary<string, string>(),
            };
            foreach (var (key, value) in kv) p.Param[key] = value;
            return p;
        }

        string StageFe8uPatchDir()
        {
            string patchDir = Path.Combine(_tempDir, "config", "patch2", "FE8U");
            Directory.CreateDirectory(patchDir);
            return patchDir;
        }

        // ====================================================================
        // 1. PatchHardCodeScanner.EaBinInstallStatus — the SOUND tri-state.
        //    The all-zero synthetic ROM reads 0x00 at every offset, so:
        //      PATCHED_IF:...=0x00 0x00   matches   -> Installed
        //      PATCHED_IF:...=0xFF 0xFF   mismatch  -> NotInstalled
        //      PATCHED_IFNOT:...=0xFF 0xFF mismatch -> Installed (the un-patched sig is absent)
        //      PATCHED_IFNOT:...=0x00 0x00 match    -> NotInstalled (un-patched sig present)
        //      PATCHED_IF:$FGREP <file>=...         -> Unknown (file-inclusion unresolvable)
        // ====================================================================

        [Fact]
        public void EaBinInstallStatus_PatchedIfMatch_Installed()
        {
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"), ("PATCHED_IF:0x001000", "0x00 0x00"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.Installed,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_PatchedIfNotMismatch_Installed()
        {
            // The KEY false-negative the naive CheckIF=="I" predicate missed (Copilot #1325): a patch
            // installed via PATCHED_IFNOT only (e.g. FE8U/MNC2Fix). WF IsInstalled deems it installed;
            // CheckIF returns "E"/"" there. EaBinInstallStatus correctly returns Installed.
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"), ("PATCHED_IFNOT:0x001000", "0xFF 0xFF"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.Installed,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
            // Sanity: the OLD predicate basis (CheckIF) does NOT report "I" here — proving the gap is real.
            Assert.NotEqual("I", PatchHardCodeScanner.CheckIF(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_PatchedIfMismatch_NotInstalled()
        {
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"), ("PATCHED_IF:0x001000", "0xFF 0xFF"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.NotInstalled,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_PatchedIfNotMatch_NotInstalled()
        {
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"), ("PATCHED_IFNOT:0x001000", "0x00 0x00"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.NotInstalled,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_UnresolvableFgrepSignature_Unknown()
        {
            // The OTHER false-negative (Copilot #1325): an install marker using $FGREP <file> file-inclusion
            // (162 such PATCHED_IF rows in the FE8U tree). Core's resolver does not port FGREP file-inclusion
            // -> NOT_FOUND -> never "I". EaBinInstallStatus returns Unknown so the gate refuses conservatively.
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "BIN"), ("PATCHED_IF:$FGREP4 nope_missing.bin", "0x00 0xB5"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.Unknown,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_NoInstallMarker_NotInstalled()
        {
            // An EA/BIN patch with NO PATCHED_IF/PATCHED_IFNOT marker -> NotInstalled (inherits WF's own
            // blind spot: WF IsInstalled is likewise false for a marker-less patch). Plain IF is a
            // PREREQUISITE, not an install marker, so it does not count toward install status.
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"), ("IF:0x001000", "0x00 0x00"), ("ADDRESS", "0x800100"));
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.NotInstalled,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_NullParam_Unknown()
        {
            // A null Param cannot be inspected at all -> the only SOUND answer is Unknown (NOT
            // NotInstalled): uninspectable proves nothing, so the safe-reject gate must not treat it as
            // safe (Copilot PR #1326 review). LoadPatch never produces a null Param; this guards a direct
            // caller's contract.
            var rom = MakeVersionedRom("BE8E01");
            var patch = new PatchInstallCore.PatchSt { Name = "p.txt", PatchFileName = "p.txt", Param = null };
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.Unknown,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        [Fact]
        public void EaBinInstallStatus_InstalledMarkerWins_OverUnknownMarker()
        {
            // A patch with BOTH an unresolvable marker AND a resolvable matching marker is Installed
            // (the precise answer), not Unknown — the resolvable match proves presence.
            var rom = MakeVersionedRom("BE8E01");
            var patch = MakePatch("p.txt", ("TYPE", "EA"),
                ("PATCHED_IF:$FGREP4 missing.bin", "0x00 0xB5"),   // unresolvable -> would be Unknown alone
                ("PATCHED_IF:0x001000", "0x00 0x00"));             // resolvable + matches -> Installed
            Assert.Equal(PatchHardCodeScanner.InstallStatusEnum.Installed,
                PatchHardCodeScanner.EaBinInstallStatus(rom, patch));
        }

        // ====================================================================
        // 2. PatchFormHasUnportableInstalledPatch — the per-ROM predicate.
        // ====================================================================

        [Fact]
        public void Predicate_InstalledEaAndBin_ReturnsTrue()
        {
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA.txt"), new[]
            {
                "NAME=PatchEA", "TYPE=EA", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN.txt"), new[]
            {
                "NAME=PatchBIN", "TYPE=BIN", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.True(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_PatchedIfNotInstalledEa_ReturnsTrue()
        {
            // Regression for Copilot #1325 false-negative #1: a PATCHED_IFNOT-installed EA patch must be
            // caught (the naive CheckIF=="I" predicate would MISS it).
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_IFNOT.txt"), new[]
            {
                "NAME=PatchIfNot", "TYPE=EA", "PATCHED_IFNOT:0x001000=0xFF 0xFF", "ADDRESS=0x800100",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.True(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_UnknownFgrepEa_ReturnsTrue()
        {
            // Regression for Copilot #1325 false-negative #2: an EA patch whose install marker uses an
            // unresolvable $FGREP file-inclusion signature is Unknown -> the gate refuses conservatively.
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_FGREP.txt"), new[]
            {
                "NAME=PatchFgrep", "TYPE=EA", "PATCHED_IF:$FGREP4 nope_missing.bin=0x00 0xB5", "ADDRESS=0x800100",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.True(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_ResolvablyNotInstalledEaBin_ReturnsFalse()
        {
            // EA/BIN patches with a RESOLVABLE PATCHED_IF marker that does NOT match -> NotInstalled ->
            // the predicate allows (the patch's bytes are provably absent, so a rebuild drops nothing).
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA.txt"), new[]
            {
                "NAME=PatchEA", "TYPE=EA", "PATCHED_IF:0x001000=0xFF 0xFF", "ADDRESS=0x800100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN.txt"), new[]
            {
                "NAME=PatchBIN", "TYPE=BIN", "PATCHED_IF:0x001000=0xFF 0xFF", "ADDRESS=0x800200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_InstalledStruct_ReturnsFalse()
        {
            // A TYPE=STRUCT installed patch must NOT trip the predicate — STRUCT is faithfully emitted
            // (s2pf-5..10), so it is never a reason to refuse. Only EA/BIN are un-ported.
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_STRUCT.txt"), new[]
            {
                "NAME=PatchStruct", "TYPE=STRUCT", "PATCHED_IF:0x001000=0x00 0x00", "P0:POINTER=0x100",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_CanonicalSkipInstalledEa_ReturnsFalse()
        {
            // CANONICAL_SKIP=1 short-circuits the patch (isCanonicalSkip) BEFORE the TYPE/install gate —
            // exactly as the orchestrator skips it — so even an "installed" EA patch with CANONICAL_SKIP
            // set is not a reason to refuse.
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA_SKIP.txt"), new[]
            {
                "NAME=PatchEaSkip", "TYPE=EA", "CANONICAL_SKIP=1", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800100",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_MixedTree_TrueWhenAnyInstalledEaBin()
        {
            // A realistic mix: an installed STRUCT (emittable, ignored), a resolvably-not-installed EA
            // (ignored), and ONE installed BIN (the offender) -> predicate true.
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_STRUCT.txt"), new[]
            {
                "NAME=PatchStruct", "TYPE=STRUCT", "PATCHED_IF:0x001000=0x00 0x00", "P0:POINTER=0x100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA_NOTINSTALLED.txt"), new[]
            {
                "NAME=PatchEaNot", "TYPE=EA", "PATCHED_IF:0x001000=0xFF 0xFF", "ADDRESS=0x800100",
            });
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN_INSTALLED.txt"), new[]
            {
                "NAME=PatchBinInst", "TYPE=BIN", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.True(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_NoPatchDir_ReturnsFalse()
        {
            // An EMPTY config/patch2/FE8U tree -> ScanPatchs returns empty -> nothing installed -> false.
            // (The dir EXISTS but is empty so ResolvePatchDirectory pins it instead of falling through to
            // the build-copied real FE8U tree in bin/, same as the scaffold test.)
            StageFe8uPatchDir(); // exists, no PATCH_*.txt
            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));
        }

        [Fact]
        public void Predicate_Version0Rom_ReturnsFalse()
        {
            // An unrecognized ROM -> version 0 -> SafePatchVersionFolder "" -> no patch dir -> false.
            // Even with a populated FE8U tree present, the version-0 guard short-circuits the scan.
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_EA.txt"), new[]
            {
                "NAME=PatchEA", "TYPE=EA", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800100",
            });

            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x8000]);
            CoreState.ROM = rom;
            CoreState.BaseDirectory = _tempDir;

            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(rom));
        }

        [Fact]
        public void Predicate_NullRom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RebuildProducerCore.PatchFormHasUnportableInstalledPatch(null));
        }

        // ====================================================================
        // 3. MakeWithProducer — the per-ROM backstop refuses (naming the patch).
        // ====================================================================

        [Fact]
        public void MakeWithProducer_InstalledEaBin_Throws_NamingThePatch()
        {
            // The public MakeWithProducer(rom, vanilla, ...) overload must REFUSE before running the
            // producers when the rom carries an installed EA/BIN patch — and the message must NAME the
            // offending patch file. This is the per-ROM backstop path (distinct from the IsComplete-gate).
            string patchDir = StageFe8uPatchDir();
            File.WriteAllLines(Path.Combine(patchDir, "PATCH_BIN.txt"), new[]
            {
                "NAME=PatchBIN", "TYPE=BIN", "PATCHED_IF:0x001000=0x00 0x00", "ADDRESS=0x800200",
            });

            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            var vanilla = MakeVersionedRom("BE8E01");
            string manifest = Path.Combine(_tempDir, "out.rebuild");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RebuildProducerCore.MakeWithProducer(
                    fe8, vanilla, 0x800000u, manifest,
                    isUseOtherGraphics: false, isUseOAMSP: false));

            Assert.Contains("EA/BIN", ex.Message);
            Assert.Contains("PATCH_BIN.txt", ex.Message);     // names the offending patch file
            Assert.False(File.Exists(manifest));              // refused before Make -> no manifest
        }

        [Fact]
        public void MakeWithProducer_NoEaBin_StillRefuses_OnIsCompleteGate_NamingPatchForm()
        {
            // With NO installed/unknown EA/BIN patch, the per-ROM backstop passes (false) — but the rebuild
            // is STILL refused by the existing IsComplete gate, because the "PatchForm(MakePatchStructDataList)"
            // token stays in AsmNotYetPortedRaw (gate-open is s2pf-17). Proves the backstop did NOT open the
            // gate and the token-based refusal is intact.
            StageFe8uPatchDir(); // empty -> no installed/unknown EA/BIN -> backstop passes
            CoreState.BaseDirectory = _tempDir;
            var fe8 = MakeVersionedRom("BE8E01");
            CoreState.ROM = fe8;

            // Sanity: the per-ROM backstop is NOT the reason for the refusal here.
            Assert.False(RebuildProducerCore.PatchFormHasUnportableInstalledPatch(fe8));

            var vanilla = MakeVersionedRom("BE8E01");
            string manifest = Path.Combine(_tempDir, "out.rebuild");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RebuildProducerCore.MakeWithProducer(
                    fe8, vanilla, 0x800000u, manifest,
                    isUseOtherGraphics: false, isUseOAMSP: false));

            // The IsComplete-gate refusal (not the backstop) — it names the still-deferred PatchForm form.
            Assert.Contains("PatchForm(MakePatchStructDataList)", ex.Message);
            Assert.Contains("not yet ported", ex.Message);
            Assert.False(File.Exists(manifest));
        }

        [Fact]
        public void Token_StaysInAsmNotYetPorted()
        {
            // Belt-and-suspenders invariant: this slice does NOT open the gate. The
            // "PatchForm(MakePatchStructDataList)" token MUST still be present so the IsComplete gate stays
            // closed for the common case (gate-open is s2pf-17).
            string[] liveAsmNotYet = RebuildProducerCore.GetAsmNotYetPortedForms();
            Assert.Contains("PatchForm(MakePatchStructDataList)", liveAsmNotYet);
        }
    }
}
