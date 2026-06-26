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
        string MakeBinPatch(string name, uint addr, int len)
        {
            string binName = name + "_" + addr.ToString("X") + ".bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), new byte[len]);
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
            string patch = MakeBinPatch("Contains", 0x250, 0x10);
            var undo = NewUndo(rom);

            // Hand the patched ROM itself as the "clean" candidate -> still contains the patch.
            var result = PatchMetadataCore.UninstallPatchWithCleanRom(rom, patch, patched, undo);

            Assert.False(result.Success);
            Assert.Equal(patched, rom.Data);
            Assert.Empty(undo.list);
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
