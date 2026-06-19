using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Synthetic round-trip tests for PatchInstallCore (issue #1248, slice 1).
    /// No real ROM / SkillSystems environment: a vanilla byte[] is mutated, fed
    /// through DiffToolCore.MakeDiff to produce a CustomBuild patch + .bin sidecars,
    /// then PatchInstallCore loads + applies it and we assert the ROM equals the
    /// mutated bytes. Mirrors the #1242 synthetic-round-trip idiom.
    ///
    /// Touches CoreState.ROM/Undo, so [Collection("SharedState")] serializes it.
    /// </summary>
    [Collection("SharedState")]
    public class PatchInstallCoreTests : IDisposable
    {
        readonly string _tempDir;
        readonly string _prevLanguage;

        public PatchInstallCoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PatchInstallCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _prevLanguage = CoreState.Language;
            CoreState.Language = "en";
        }

        public void Dispose()
        {
            CoreState.Language = _prevLanguage;
            CoreState.ROM = null;
            CoreState.Undo = null;
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
        }

        // ---------- helpers ----------

        // Build a ROM around the given bytes and register it as CoreState.ROM so the
        // Undo machinery (which snapshots from / rolls back into CoreState.ROM) works.
        static ROM MakeRom(byte[] data)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect((byte[])data.Clone());
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom)
        {
            return new Undo.UndoData
            {
                time = DateTime.Now,
                name = "patch-install-test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
        }

        // 0x200 of 0xFF, the spec's vanilla baseline.
        static byte[] Vanilla(int size = 0x200)
        {
            var b = new byte[size];
            for (int i = 0; i < b.Length; i++) b[i] = 0xFF;
            return b;
        }

        // Produce a CustomBuild patch (PATCH_*.txt + *.bin sidecars) in _tempDir from
        // a vanilla→modified diff, and load it back into a PatchSt.
        PatchInstallCore.PatchSt MakeAndLoad(byte[] vanilla, byte[] modified, string name = "Synthetic")
        {
            string outFile = Path.Combine(_tempDir, "PATCH_" + name + ".txt");
            DiffToolCore.MakeDiff(outFile, vanilla, modified, patchedIfMinSize: 10,
                collectFreeSpace: false, version: 8, isMultibyte: false);
            return PatchInstallCore.LoadPatch(outFile);
        }

        // ---------- core round-trip ----------

        [Fact]
        public void RoundTrip_MakeDiff_LoadPatch_ApplyPatch_RomEqualsModified()
        {
            byte[] vanilla = Vanilla();
            byte[] modified = (byte[])vanilla.Clone();
            // Mutate a few disjoint ranges (offsets > 0x100 so they clear the guard).
            for (int i = 0x110; i < 0x118; i++) modified[i] = 0xAB;
            for (int i = 0x180; i < 0x190; i++) modified[i] = (byte)(i & 0xFF);
            modified[0x1F0] = 0x12;

            var patch = MakeAndLoad(vanilla, modified);
            Assert.NotNull(patch);
            // CustomBuild emits at least one BINF line.
            Assert.Contains(patch.Param.Keys, k => k.StartsWith("BINF:", StringComparison.Ordinal));

            var rom = MakeRom(vanilla);
            var undo = NewUndo(rom);
            PatchInstallCore.ApplyPatch(patch, rom, undo);

            Assert.Equal(modified, rom.Data);
        }

        [Fact]
        public void RoundTrip_UndoRollback_RestoresVanillaBytes()
        {
            byte[] vanilla = Vanilla();
            byte[] modified = (byte[])vanilla.Clone();
            for (int i = 0x120; i < 0x140; i++) modified[i] = 0xCC;

            var patch = MakeAndLoad(vanilla, modified, "UndoCase");
            var rom = MakeRom(vanilla);
            var undo = NewUndo(rom);

            PatchInstallCore.ApplyPatch(patch, rom, undo);
            Assert.Equal(modified, rom.Data);

            // Commit the recorded undo then roll it back — ROM returns to vanilla.
            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();

            Assert.Equal(vanilla, rom.Data);
        }

        [Fact]
        public void RoundTrip_WriteBeyondEnd_ResizesRom()
        {
            // Modified is LONGER than vanilla: the diff range lands past the original
            // 0x200 end, forcing write_resize_data inside ApplyPatch.
            byte[] vanilla = Vanilla(0x200);
            byte[] modified = new byte[0x240];
            Array.Copy(vanilla, modified, vanilla.Length);
            for (int i = 0x200; i < 0x240; i++) modified[i] = 0x77;

            var patch = MakeAndLoad(vanilla, modified, "ResizeCase");
            var rom = MakeRom(vanilla);
            Assert.Equal(0x200, rom.Data.Length);

            var undo = NewUndo(rom);
            PatchInstallCore.ApplyPatch(patch, rom, undo);

            Assert.True(rom.Data.Length >= 0x240, "ROM should have grown to fit the write");
            // Every modified byte must now be present.
            for (int i = 0x200; i < 0x240; i++)
                Assert.Equal(0x77, rom.Data[i]);
        }

        // ---------- guard / reject cases ----------

        [Fact]
        public void ApplyPatch_ZeroAddressWrite_Rejected()
        {
            // Hand-craft a BINF that targets a low/protected address (<= 0x100).
            string binName = "00000000.bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string outFile = Path.Combine(_tempDir, "PATCH_ZeroAddr.txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=ZeroAddr",
                "TYPE=BIN",
                "BINF:0x0=" + binName,
            });

            var patch = PatchInstallCore.LoadPatch(outFile);
            var rom = MakeRom(Vanilla());
            var undo = NewUndo(rom);

            Assert.Throws<PatchInstallException>(() => PatchInstallCore.ApplyPatch(patch, rom, undo));
        }

        [Fact]
        public void ApplyPatch_UnsupportedKeyword_ThrowsLoudly()
        {
            // A COPY line is valid in the full PatchForm engine but outside the
            // CustomBuild subset — the installer must reject it rather than ignore it.
            string outFile = Path.Combine(_tempDir, "PATCH_Copy.txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=Copy",
                "TYPE=BIN",
                "COPY:0x800:0x10=dummy.bin",
            });

            var patch = PatchInstallCore.LoadPatch(outFile);
            var rom = MakeRom(Vanilla());
            var undo = NewUndo(rom);

            Assert.Throws<PatchInstallException>(() => PatchInstallCore.ApplyPatch(patch, rom, undo));
        }

        [Fact]
        public void ApplyPatch_MacroAddress_ThrowsLoudly()
        {
            // A '$' macro/pointer address is outside the literal-only CustomBuild subset.
            string binName = "deadbeef.bin";
            File.WriteAllBytes(Path.Combine(_tempDir, binName), new byte[] { 0xAA });
            string outFile = Path.Combine(_tempDir, "PATCH_Macro.txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=Macro",
                "TYPE=BIN",
                "BINF:$FREEAREA=" + binName,
            });

            var patch = PatchInstallCore.LoadPatch(outFile);
            var rom = MakeRom(Vanilla());
            var undo = NewUndo(rom);

            Assert.Throws<PatchInstallException>(() => PatchInstallCore.ApplyPatch(patch, rom, undo));
        }

        // ---------- LoadPatch parse ----------

        [Fact]
        public void LoadPatch_NoTypeLine_ReturnsNull()
        {
            string outFile = Path.Combine(_tempDir, "PATCH_NoType.txt");
            File.WriteAllLines(outFile, new[] { "NAME=NoType", "INFO=nothing" });
            Assert.Null(PatchInstallCore.LoadPatch(outFile));
        }

        [Fact]
        public void LoadPatch_LanguageSuffixedKey_ResolvedToActiveLanguage()
        {
            CoreState.Language = "ja";
            string[] lines =
            {
                "TYPE=BIN",
                "NAME.ja=日本語",   // Japanese name
                "NAME.en=English",
            };
            var patch = PatchInstallCore.LoadPatch(lines, Path.Combine(_tempDir, "PATCH_Lang.txt"));
            Assert.NotNull(patch);
            Assert.Equal("日本語", patch.Param["NAME"]);
        }

        // ---------- UNINSTALL extension ----------

        [Fact]
        public void ApplyPatch_Uninstall_CopiesVanillaBytesBack()
        {
            // Address must clear the 0x200 safety floor (WF isSafetyOffset), so use a
            // larger ROM and target 0x250.
            byte[] vanilla = Vanilla(0x400);     // all 0xFF
            byte[] installed = (byte[])vanilla.Clone();
            for (int i = 0x250; i < 0x260; i++) installed[i] = 0x00; // pretend a patch wrote zeros

            // Patch: UNINSTALL the 0x10 bytes at 0x250 from the vanilla source.
            string outFile = Path.Combine(_tempDir, "PATCH_Uninstall.txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=Uninstall",
                "TYPE=BIN",
                "UNINSTALL:0x250=0x10",
            });

            var patch = PatchInstallCore.LoadPatch(outFile);
            var rom = MakeRom(installed);
            var undo = NewUndo(rom);

            PatchInstallCore.ApplyPatch(patch, rom, undo, vanillaRom: vanilla);

            // The 0x250..0x260 window is restored to vanilla (0xFF).
            Assert.Equal(vanilla, rom.Data);
        }

        [Fact]
        public void ApplyPatch_Uninstall_NoVanillaRom_ThrowsLoudly()
        {
            string outFile = Path.Combine(_tempDir, "PATCH_UninstallNoRom.txt");
            File.WriteAllLines(outFile, new[]
            {
                "NAME=UninstallNoRom",
                "TYPE=BIN",
                "UNINSTALL:0x150=0x10",
            });

            var patch = PatchInstallCore.LoadPatch(outFile);
            var rom = MakeRom(Vanilla());
            var undo = NewUndo(rom);

            Assert.Throws<PatchInstallException>(() => PatchInstallCore.ApplyPatch(patch, rom, undo));
        }
    }
}
