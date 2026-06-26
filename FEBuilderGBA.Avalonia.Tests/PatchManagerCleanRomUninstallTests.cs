// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for #1462: Avalonia Patch Manager clean-ROM-diff uninstall — a BIN patch
// installed in a PRIOR session (no per-patch backup file) is uninstalled by diffing
// against a user-supplied patch-free ("clean") ROM via
// PatchManagerViewModel.UninstallPatchWithCleanRom(cleanRomPath), mirroring the
// WinForms PatchFormUninstallDialogForm -> UninstallPatchInner flow.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PatchManagerCleanRomUninstallTests
    {
        // A 0x1000000-byte FE8U ROM. `seed` mutates the byte[] BEFORE LoadLow so the
        // header/crc32 reflect the seeded content. Returns (rom, the exact bytes loaded).
        static (ROM rom, byte[] bytes) MakeFe8uRom(Action<byte[]>? seed)
        {
            var data = new byte[0x1000000];
            seed?.Invoke(data);
            var rom = new ROM();
            rom.LoadLow("pm-clean.gba", data, "BE8E01"); // FE8U
            return (rom, (byte[])data.Clone());
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        // The patch overwrites 0x200..0x204 (clears the WF 0x200 safety floor edge).
        const uint PatchAddr = 0x200;
        static readonly byte[] OriginalBytes = { 0x11, 0x22, 0x33, 0x44 };
        static readonly byte[] PatchBytes = { 0xAA, 0xBB, 0xCC, 0xDD };

        static string WriteBinPatch(string dir, string binName = "test.bin")
        {
            File.WriteAllBytes(Path.Combine(dir, binName), PatchBytes);
            string patchFile = Path.Combine(dir, "PATCH_Test.txt");
            File.WriteAllLines(patchFile, new[]
            {
                "TYPE=BIN",
                "BIN:0x200=" + binName,
                "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
            });
            return patchFile;
        }

        // Diff-based uninstall round-trip: NO backup file present. The VM loads the
        // user-supplied clean ROM, validates it, and diff-restores the patched region
        // under an undo scope. ROM returns to the clean bytes; an undo snapshot is
        // pushed; rolling it back restores the patched state.
        [Fact]
        public void UninstallWithCleanRom_NoBackup_RestoresCleanBytes_AndIsUndoable()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_clean_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                // Build the CLEAN ROM bytes (original at 0x200) and save them to disk.
                var (_, cleanBytes) = MakeFe8uRom(d =>
                {
                    Array.Copy(OriginalBytes, 0, d, PatchAddr, OriginalBytes.Length);
                });
                string cleanRomPath = Path.Combine(tempDir, "clean.gba");
                File.WriteAllBytes(cleanRomPath, cleanBytes);

                // Build the loaded (PATCHED) ROM = same bytes but patched at 0x200.
                var (rom, _) = MakeFe8uRom(d =>
                {
                    Array.Copy(PatchBytes, 0, d, PatchAddr, PatchBytes.Length);
                });
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                string patchFile = WriteBinPatch(tempDir);
                // Deliberately NO backup file (HasBackup == false).
                Assert.False(PatchMetadataCore.HasBackup(patchFile));

                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    DirectoryPath = tempDir,
                    Status = PatchMetadataCore.PatchStatus.Installed,
                };

                // The VM signals the View to open the clean-ROM dialog (no backup).
                Assert.True(vm.SelectedPatchNeedsCleanRom);
                // The Uninstall button must STILL be enabled (installed BIN, no backup).
                Assert.True(vm.CanUninstall);

                Assert.Equal(0xAAu, rom.u8(PatchAddr)); // patched

                int snapshotsBefore = CoreState.Undo.UndoBuffer.Count;
                byte[] patchedSnapshot = (byte[])rom.Data.Clone();

                string msg = vm.UninstallPatchWithCleanRom(cleanRomPath);
                Assert.Contains("restored", msg, StringComparison.OrdinalIgnoreCase);

                // ROM restored to the clean ORIGINAL bytes.
                Assert.Equal(0x11u, rom.u8(PatchAddr));
                Assert.Equal(0x44u, rom.u8(PatchAddr + 3));

                // Exactly one new undo snapshot pushed; rollback restores the patched state.
                Assert.Equal(snapshotsBefore + 1, CoreState.Undo.UndoBuffer.Count);
                CoreState.Undo.RunUndo();
                Assert.Equal(patchedSnapshot, rom.Data);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
                CoreState.Language = savedLang;
                TryDelete(tempDir);
            }
        }

        // Validation: a wrong-version clean ROM (different GBA header) must fail closed
        // BEFORE any mutation, and no undo snapshot is pushed.
        [Fact]
        public void UninstallWithCleanRom_WrongVersion_FailsClosed_NoMutation()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_clean2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                // A "clean" ROM whose game title (0xA0) is corrupted -> incompatible header.
                var (_, wrongBytes) = MakeFe8uRom(d =>
                {
                    Array.Copy(OriginalBytes, 0, d, PatchAddr, OriginalBytes.Length);
                    d[0xA0] ^= 0xFF;
                });
                string wrongRomPath = Path.Combine(tempDir, "wrong.gba");
                File.WriteAllBytes(wrongRomPath, wrongBytes);

                var (rom, _) = MakeFe8uRom(d =>
                {
                    Array.Copy(PatchBytes, 0, d, PatchAddr, PatchBytes.Length);
                });
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                string patchFile = WriteBinPatch(tempDir);
                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    DirectoryPath = tempDir,
                    Status = PatchMetadataCore.PatchStatus.Installed,
                };

                byte[] before = (byte[])rom.Data.Clone();
                int snapshotsBefore = CoreState.Undo.UndoBuffer.Count;

                string msg = vm.UninstallPatchWithCleanRom(wrongRomPath);
                Assert.Contains("failed", msg, StringComparison.OrdinalIgnoreCase);

                Assert.Equal(before, rom.Data);  // ROM untouched
                Assert.Equal(snapshotsBefore, CoreState.Undo.UndoBuffer.Count); // no snapshot
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
                CoreState.Language = savedLang;
                TryDelete(tempDir);
            }
        }

        // Validation: a candidate ROM that STILL contains the patch (e.g. the user picked
        // the already-patched ROM) is rejected before mutation.
        [Fact]
        public void UninstallWithCleanRom_CandidateStillPatched_Rejected()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_clean3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                var (rom, patchedBytes) = MakeFe8uRom(d =>
                {
                    Array.Copy(PatchBytes, 0, d, PatchAddr, PatchBytes.Length);
                });
                string patchedRomPath = Path.Combine(tempDir, "patched.gba");
                File.WriteAllBytes(patchedRomPath, patchedBytes);

                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                string patchFile = WriteBinPatch(tempDir);
                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    DirectoryPath = tempDir,
                    Status = PatchMetadataCore.PatchStatus.Installed,
                };

                byte[] before = (byte[])rom.Data.Clone();
                string msg = vm.UninstallPatchWithCleanRom(patchedRomPath);

                Assert.Contains("failed", msg, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
                CoreState.Language = savedLang;
                TryDelete(tempDir);
            }
        }
    }
}
