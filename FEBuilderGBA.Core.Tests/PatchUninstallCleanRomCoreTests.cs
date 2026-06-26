using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Synthetic tests for the clean-ROM-diff uninstall engine (#1462):
    /// <c>PatchMetadataCore.CollectPatchRegions</c> / <c>RomContainsPatch</c> /
    /// <c>IsCompatibleRom</c> / <c>UninstallPatchWithCleanRom</c>.
    ///
    /// No real ROM / SkillSystems environment: a synthetic byte[] (with a GBA-style
    /// header so the compatibility gate passes) is patched, then uninstalled by
    /// diffing against a synthetic "clean" ROM. Mirrors PatchInstallCoreTests idioms.
    ///
    /// Touches CoreState.ROM/Undo, so [Collection("SharedState")] serializes it.
    /// </summary>
    [Collection("SharedState")]
    public class PatchUninstallCleanRomCoreTests : IDisposable
    {
        readonly string _tempDir;

        public PatchUninstallCleanRomCoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallCleanRom_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            CoreState.ROM = null;
            CoreState.Undo = null;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }

        // ---------- helpers ----------

        static ROM MakeRom(byte[] data)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect((byte[])data.Clone());
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "uninstall-clean-test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        // A 0x400-byte synthetic "clean" ROM filled with 0xFF, with a GBA header
        // game title (0xA0..0xAC) + game code (0xAC..0xB0) so IsCompatibleRom matches.
        static byte[] Clean(int size = 0x400)
        {
            var b = new byte[size];
            for (int i = 0; i < b.Length; i++) b[i] = 0xFF;
            byte[] header = System.Text.Encoding.ASCII.GetBytes("FETESTGAME01AE8U");
            Array.Copy(header, 0, b, 0xA0, header.Length); // 0xA0..0xB0
            return b;
        }

        // Apply a synthetic patch at 0x250 (clears the WF 0x200 safety floor):
        // overwrite 0x10 bytes with 0xAB and return the patched copy.
        static byte[] WithPatchAt(byte[] clean, uint addr, byte[] patchBytes)
        {
            var p = (byte[])clean.Clone();
            for (int i = 0; i < patchBytes.Length; i++) p[addr + i] = patchBytes[i];
            return p;
        }

        // Write a BIN patch file with one fixed-address BIN entry + .bin sidecar of `len` bytes.
        // The sidecar bytes ARE the patch's own bytes (WinForms BinMapping.bin) used by the
        // patch-absence check; pass `fill` to match what the patch wrote at install time.
        string MakeBinPatch(string name, uint addr, int len, byte fill = 0x00)
        {
            byte[] bin = new byte[len];
            for (int i = 0; i < len; i++) bin[i] = fill;
            return MakeBinPatchBytes(name, addr, bin);
        }

        // As MakeBinPatch but with explicit sidecar bytes.
        string MakeBinPatchBytes(string name, uint addr, byte[] bin)
        {
            string binName = name + "_" + addr.ToString("X") + ".bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), bin);
            string outFile = Path.Combine(_tempDir, "PATCH_" + name + ".txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=" + name,
                "TYPE=BIN",
                "BIN:0x" + addr.ToString("X") + "=" + binName,
            });
            return outFile;
        }

        // ---------- CollectPatchRegions ----------

        [Fact]
        public void CollectPatchRegions_FixedAddressBin_ReturnsRegion()
        {
            byte[] clean = Clean();
            var rom = MakeRom(WithPatchAt(clean, 0x250, new byte[0x10]));
            string patch = MakeBinPatch("Fixed", 0x250, 0x10);

            var regions = PatchMetadataCore.CollectPatchRegions(rom, patch, out int untraceable);

            Assert.Single(regions);
            Assert.Equal(0x250u, regions[0].address);
            Assert.Equal(0x10, regions[0].length);
            Assert.Equal(0, untraceable);
        }

        [Fact]
        public void CollectPatchRegions_FreeAreaEntry_CountedUntraceable()
        {
            byte[] clean = Clean();
            var rom = MakeRom(clean);
            // $FREEAREA payloads cannot be traced from text alone -> untraceable.
            string binName = "free.bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), new byte[0x10]);
            string outFile = Path.Combine(_tempDir, "PATCH_Free.txt");
            File.WriteAllLines(outFile, new[] { "NAME=Free", "TYPE=BIN", "BIN:$FREEAREA=" + binName });

            var regions = PatchMetadataCore.CollectPatchRegions(rom, outFile, out int untraceable);

            Assert.Empty(regions);
            Assert.Equal(1, untraceable);
        }

        [Fact]
        public void CollectPatchRegions_EmptyTypeLine_TreatedAsBin()
        {
            // Legacy BIN patches that OMIT the TYPE= line must still be uninstallable —
            // matching the Avalonia Patch Manager's string.IsNullOrEmpty(Type) convention.
            byte[] clean = Clean();
            var rom = MakeRom(WithPatchAt(clean, 0x250, new byte[0x10]));
            string binName = "NoType_250.bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), new byte[0x10]);
            string outFile = Path.Combine(_tempDir, "PATCH_NoType.txt");
            // No TYPE= line at all.
            File.WriteAllLines(outFile, new[] { "NAME=NoType", "BIN:0x250=" + binName });

            var regions = PatchMetadataCore.CollectPatchRegions(rom, outFile, out int untraceable);

            Assert.Single(regions);
            Assert.Equal(0x250u, regions[0].address);
            Assert.Equal(0, untraceable);
        }

        [Fact]
        public void Uninstall_TwoSeparateRuns_RestoresBothRuns_BatchedWrites()
        {
            // Two disjoint differing runs inside one over-length region. The batched
            // write_range path must restore BOTH runs and leave the identical gap untouched.
            byte[] clean = Clean();
            byte[] patched = (byte[])clean.Clone();
            patched[0x250] = 0xAB; patched[0x251] = 0xAB;          // run 1 (2 bytes)
            patched[0x25A] = 0xCD; patched[0x25B] = 0xCD; patched[0x25C] = 0xCD; // run 2 (3 bytes)
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("Runs", 0x250, 0x20); // covers both runs + the gap
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, clean, undo);

            Assert.True(result.Success, result.Message);
            Assert.Equal(5, result.BytesWritten); // 2 + 3 differing bytes
            Assert.Equal(clean, rom.Data);
        }

        [Fact]
        public void CollectPatchRegions_EaPatch_ReturnsEmptyAndUntraceable()
        {
            byte[] clean = Clean();
            var rom = MakeRom(clean);
            string outFile = Path.Combine(_tempDir, "PATCH_Ea.txt");
            File.WriteAllLines(outFile, new[] { "NAME=Ea", "TYPE=EA", "ORG=0x250" });

            var regions = PatchMetadataCore.CollectPatchRegions(rom, outFile, out int untraceable);

            Assert.Empty(regions);
            Assert.True(untraceable > 0);
        }

        // ---------- RomContainsPatch ----------

        [Fact]
        public void RomContainsPatch_CleanRomLacksPatch_ReturnsFalse()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, new byte[] { 0xAB, 0xAB, 0xAB, 0xAB });
            var rom = MakeRom(patched);
            var regions = new List<(uint, int)> { (0x250u, 4) };

            Assert.False(PatchMetadataCore.RomContainsPatch(rom, regions, clean));
        }

        [Fact]
        public void RomContainsPatch_CandidateStillPatched_ReturnsTrue()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, new byte[] { 0xAB, 0xAB, 0xAB, 0xAB });
            var rom = MakeRom(patched);
            var regions = new List<(uint, int)> { (0x250u, 4) };

            // Candidate == current patched ROM -> still contains the patch.
            Assert.True(PatchMetadataCore.RomContainsPatch(rom, regions, patched));
        }

        [Fact]
        public void RomContainsPatch_StillContainsPatchButEditedElsewhereInRegion_Rejected()
        {
            // FAITHFULNESS regression (WF SearchContainThisPatchBy compares against t.bin, the
            // patch's OWN bytes — NOT the current ROM). A candidate that STILL CONTAINS the patch
            // at its first bytes but differs from the loaded ROM elsewhere in the SAME region
            // (e.g. extra edits made after install) must be REJECTED as not-clean.
            byte[] clean = Clean();
            // Patch wrote 0xAB*0x10 at 0x250 (the .bin sidecar content == patch's own bytes).
            byte[] patchOwnBytes = FillBytes(0x10, 0xAB);
            // Loaded ROM = patch bytes PLUS a later post-install edit inside the same region.
            byte[] loaded = (byte[])clean.Clone();
            Array.Copy(patchOwnBytes, 0, loaded, 0x250, patchOwnBytes.Length);
            loaded[0x25F] = 0x99; // post-install edit at the tail of the region
            var rom = MakeRom(loaded);

            // Candidate STILL contains the patch's own bytes at 0x250..0x25F but its tail byte
            // (0x25F) holds the patch byte 0xAB, not the loaded ROM's 0x99.
            byte[] candidateStillPatched = (byte[])clean.Clone();
            Array.Copy(patchOwnBytes, 0, candidateStillPatched, 0x250, patchOwnBytes.Length);

            string patch = MakeBinPatchBytes("Edited", 0x250, patchOwnBytes);
            var regions = PatchMetadataCore.CollectPatchRegionsWithBytes(rom, patch, out _);

            // Old (buggy) impl compared candidate-vs-loaded-ROM: 0x25F differs (0xAB vs 0x99) ->
            // would wrongly classify the candidate as "clean". The faithful impl compares
            // candidate-vs-t.bin: 0x25F matches (both 0xAB) -> still contains the patch -> REJECT.
            Assert.True(PatchMetadataCore.RomContainsPatch(regions, candidateStillPatched));

            // And the full uninstall must REJECT it (no mutation).
            var rom2 = MakeRom(loaded);
            var undo = NewUndo(rom2);
            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom2, patch, candidateStillPatched, undo);
            Assert.False(result.Success);
            Assert.Equal(loaded, rom2.Data);
            Assert.Empty(undo.list);
        }

        // ---------- IsCompatibleRom ----------

        [Fact]
        public void IsCompatibleRom_SameHeader_True_DifferentHeader_False()
        {
            byte[] clean = Clean();
            var rom = MakeRom(clean);
            Assert.True(PatchMetadataCore.IsCompatibleRom(rom, (byte[])clean.Clone()));

            byte[] wrong = Clean();
            wrong[0xAC] ^= 0xFF; // corrupt the game code
            Assert.False(PatchMetadataCore.IsCompatibleRom(rom, wrong));
        }

        // ---------- UninstallPatchWithCleanRom round-trip ----------

        [Fact]
        public void Uninstall_RoundTrip_RestoresCleanBytes()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, FillBytes(0x10, 0xAB));
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("RT", 0x250, 0x10);
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, clean, undo);

            Assert.True(result.Success, result.Message);
            Assert.Equal(clean, rom.Data); // byte-identical to the clean ROM
            Assert.Equal(0x10, result.BytesWritten); // all 16 differing bytes restored
        }

        [Fact]
        public void Uninstall_UndoRollback_RestoresPatchedBytes()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, FillBytes(0x10, 0xAB));
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("Undo", 0x250, 0x10);
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, clean, undo);
            Assert.True(result.Success);
            Assert.Equal(clean, rom.Data);

            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();

            Assert.Equal(patched, rom.Data); // rollback restores the patched bytes
        }

        [Fact]
        public void Uninstall_OverEstimatedLength_OnlyRestoresDifferingBytes()
        {
            // The patch only changed 4 bytes at 0x250 but the .bin sidecar (region length)
            // is 0x20 — the engine must restore ONLY the 4 differing bytes (no clobber of
            // the identical surrounding bytes), proving correction-only restore.
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, new byte[] { 0xAB, 0xAB, 0xAB, 0xAB });
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("Over", 0x250, 0x20); // length 0x20 >> 4 actual
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, clean, undo);

            Assert.True(result.Success, result.Message);
            Assert.Equal(4, result.BytesWritten);   // only the 4 differing bytes written
            Assert.Equal(clean, rom.Data);
        }

        [Fact]
        public void Uninstall_WrongVersionRom_FailsClosedBeforeMutation()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, FillBytes(0x10, 0xAB));
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("Wrong", 0x250, 0x10);

            byte[] wrongVersion = Clean();
            wrongVersion[0xA0] ^= 0xFF; // different game title -> incompatible
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, wrongVersion, undo);

            Assert.False(result.Success);
            Assert.Equal(patched, rom.Data);     // ROM untouched
            Assert.Empty(undo.list);             // nothing recorded
        }

        [Fact]
        public void Uninstall_CandidateStillContainsPatch_Rejected()
        {
            byte[] clean = Clean();
            byte[] patched = WithPatchAt(clean, 0x250, FillBytes(0x10, 0xAB));
            var rom = MakeRom(patched);
            // .bin sidecar bytes (patch's own bytes) == 0xAB*0x10 to match what was installed.
            string patch = MakeBinPatch("Contains", 0x250, 0x10, fill: 0xAB);
            var undo = NewUndo(rom);

            // Hand the patched ROM itself as the "clean" candidate -> still contains the patch.
            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, patched, undo);

            Assert.False(result.Success);
            Assert.Equal(patched, rom.Data);
            Assert.Empty(undo.list);
        }

        [Fact]
        public void Uninstall_TruncatedCleanRom_FailsClosed_NotPartialOk()
        {
            // A clean ROM that is SMALLER than the highest traced region (e.g. predates a ROM
            // expansion) must FAIL closed BEFORE any write — never silently skip the region and
            // still return Ok(...). Region is at 0x250 (len 0x10); the clean ROM ends at 0x254.
            byte[] cleanFull = Clean(0x400);
            byte[] patched = WithPatchAt(cleanFull, 0x250, FillBytes(0x10, 0xAB));
            var rom = MakeRom(patched);
            string patch = MakeBinPatch("Trunc", 0x250, 0x10, fill: 0xAB);

            // Truncate the clean ROM so it does NOT cover the region end (0x260). Keep a valid
            // GBA header so the compatibility gate passes and we exercise the SIZE gate.
            byte[] truncatedClean = new byte[0x254];
            Array.Copy(cleanFull, truncatedClean, truncatedClean.Length);
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, truncatedClean, undo);

            Assert.False(result.Success);                       // FAIL, not partial Ok
            Assert.Contains("too small", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(patched, rom.Data);                    // ROM untouched
            Assert.Empty(undo.list);                            // nothing recorded
        }

        [Fact]
        public void Uninstall_EaPatch_ReportsNotSupported()
        {
            byte[] clean = Clean();
            var rom = MakeRom(clean);
            string outFile = Path.Combine(_tempDir, "PATCH_EaUn.txt");
            File.WriteAllLines(outFile, new[] { "NAME=EaUn", "TYPE=EA", "ORG=0x250" });
            var undo = NewUndo(rom);

            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, outFile, clean, undo);

            Assert.False(result.Success); // no fixed-address regions to uninstall
            Assert.Empty(undo.list);
        }

        static byte[] FillBytes(int len, byte v)
        {
            var b = new byte[len];
            for (int i = 0; i < len; i++) b[i] = v;
            return b;
        }
    }
}
