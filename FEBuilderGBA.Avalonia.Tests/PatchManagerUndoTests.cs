// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1429: Avalonia Patch Manager uninstall ROM writes must be
// recorded into undo. PatchManagerViewModel.UninstallPatch() now mirrors
// InstallPatch — it opens an Undo.UndoData scope, routes restore writes through the
// recording PatchMetadataCore.UninstallPatch(rom, path, undoData) overload, and
// Pushes a snapshot on success so the uninstall is undoable (byte-identical restore
// of the patched state).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PatchManagerUndoTests
    {
        static ROM MakeFe8uRom(Action<byte[]> seed)
        {
            var data = new byte[0x1000000];
            seed?.Invoke(data);
            var rom = new ROM();
            rom.LoadLow("pm-undo.gba", data, "BE8E01"); // FE8U
            return rom;
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        // Test D: install a BIN patch through the VM (creates a backup + pushes a
        // snapshot), capture the patched ROM + the Undo snapshot count, uninstall via
        // the VM, then assert:
        //   - uninstall succeeded,
        //   - exactly ONE new undo snapshot was pushed (uninstall is recorded),
        //   - rolling that snapshot back restores the ROM byte-for-byte to the
        //     pre-uninstall (patched) state.
        [Fact]
        public void UninstallPatch_RecordsUndoSnapshot_AndRollbackRestoresPatchedRom()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_undo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                // ROM with known ORIGINAL bytes at 0x200.
                var rom = MakeFe8uRom(d =>
                {
                    d[0x200] = 0x11; d[0x201] = 0x22; d[0x202] = 0x33; d[0x203] = 0x44;
                });
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                // Patch overwrites 0x200 with AA BB CC DD; fixed address => backup is saved.
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    Status = PatchMetadataCore.PatchStatus.NotInstalled,
                };

                // Install (force-ignore deps) -> patches the ROM + saves a backup +
                // pushes an install snapshot.
                string installMsg = vm.InstallPatch(forceIgnoreDependencies: true);
                Assert.Contains("installed", installMsg, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                // Capture the PATCHED ROM + the current snapshot count.
                byte[] patchedSnapshot = (byte[])rom.Data.Clone();
                int snapshotsBefore = CoreState.Undo.UndoBuffer.Count;

                // Uninstall via the VM.
                string uninstallMsg = vm.UninstallPatch();
                Assert.Contains("restored", uninstallMsg, StringComparison.OrdinalIgnoreCase);

                // The uninstall pushed exactly one new snapshot.
                Assert.Equal(snapshotsBefore + 1, CoreState.Undo.UndoBuffer.Count);

                // ROM is now restored to ORIGINAL.
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.Equal(0x44u, rom.u8(0x203));

                // Undo the uninstall -> ROM byte-identical to the captured patched state.
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

        // Regression for the Copilot review of #1498: the backup file must be PRESERVED
        // across uninstall. Previously the uninstall deleted the .backup_* file, so after
        // the user UNDID the uninstall (which reapplies the patched ROM bytes => the patch
        // is installed again) the patch became a dead-end in the Patch Manager: CanInstall
        // was false (Status==Installed) AND CanUninstall was false (no backup). With the
        // backup preserved, undoing an uninstall leaves the patch uninstallable again.
        [Fact]
        public void UninstallThenUndo_LeavesPatchUninstallableAgain()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_undo2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                // ROM with known ORIGINAL bytes at 0x200.
                var rom = MakeFe8uRom(d =>
                {
                    d[0x200] = 0x11; d[0x201] = 0x22; d[0x202] = 0x33; d[0x203] = 0x44;
                });
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                // Patch overwrites 0x200 with AA BB CC DD; fixed address => backup is saved.
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    DirectoryPath = tempDir,
                    Status = PatchMetadataCore.PatchStatus.NotInstalled,
                };

                // Install (force-ignore deps) -> patches the ROM + saves a backup.
                string installMsg = vm.InstallPatch(forceIgnoreDependencies: true);
                Assert.Contains("installed", installMsg, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                // Uninstall via the VM -> ROM restored to ORIGINAL, backup PRESERVED.
                string uninstallMsg = vm.UninstallPatch();
                Assert.Contains("restored", uninstallMsg, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.True(PatchMetadataCore.HasBackup(patchFile)); // backup preserved across uninstall

                // Undo the uninstall -> patched bytes reapplied (patch installed again).
                CoreState.Undo.RunUndo();
                Assert.Equal(0xAAu, rom.u8(0x200));

                // Re-parse the patch status exactly the way the VM's RefreshSelectedPatchStatus
                // does (real re-parse, not a hand-forced status). Because PATCHED_IF (AA BB CC DD)
                // matches again after undo, refreshed.Status is Installed.
                var refreshed = PatchMetadataCore.ParsePatchFile(
                    patchFile,
                    Path.GetFileName(Path.GetDirectoryName(patchFile)) ?? "",
                    rom,
                    "en");
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, refreshed.Status);
                vm.SelectedPatch.Status = refreshed.Status;

                // THE CORE REGRESSION: with the backup preserved, the patch is uninstallable
                // again (installed-by-undo + a backup present) => no Patch Manager dead-end.
                Assert.True(vm.CanUninstall);
                Assert.True(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
                CoreState.Language = savedLang;
                TryDelete(tempDir);
            }
        }

        // Guard for the Copilot INLINE review: when an uninstall fails BEFORE any ROM
        // write (here the FIRST backup record's address+length exceeds the ROM size),
        // undoData.list is empty. The VM must NOT push a no-op Undo History entry on the
        // failure (Rollback == Push + RunUndo). Assert UndoBuffer.Count is unchanged.
        [Fact]
        public void UninstallFailsBeforeAnyWrite_DoesNotPushNoOpUndoSnapshot()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "fe_pm_undo3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            var savedLang = CoreState.Language;
            try
            {
                var rom = MakeFe8uRom(null); // 0x1000000-byte ROM
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                CoreState.Language = "en";

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                // Hand-write a backup whose FIRST record's address+length (0xFFFFFF + 0x20)
                // exceeds the 0x1000000-byte ROM => UninstallPatch bails out BEFORE any
                // write_range, so undoData.list stays empty.
                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                File.WriteAllLines(backupPath, new[]
                {
                    "0xFFFFFF:32:00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                });
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                var vm = new PatchManagerViewModel();
                vm.SelectedPatch = new PatchEntry
                {
                    Name = "Test Patch",
                    Type = "BIN",
                    PatchFilePath = patchFile,
                    DirectoryPath = tempDir,
                    Status = PatchMetadataCore.PatchStatus.Installed,
                };

                int snapshotsBefore = CoreState.Undo.UndoBuffer.Count;

                string uninstallMsg = vm.UninstallPatch();
                Assert.Contains("failed", uninstallMsg, StringComparison.OrdinalIgnoreCase);

                // No no-op Undo History entry was created on the pre-write failure.
                Assert.Equal(snapshotsBefore, CoreState.Undo.UndoBuffer.Count);
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
