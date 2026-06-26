using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="ProjectRenameCore"/> — the project (ROM) rename
    /// engine ported from WinForms <c>ToolChangeProjectnameForm</c> (#1461).
    /// Uses an in-memory filesystem so no real disk is touched.
    /// The full-flow Rename tests touch CoreState.BaseDirectory (for the etc
    /// directory path), so the class joins the SharedState collection.
    /// </summary>
    [Collection("SharedState")]
    public class ProjectRenameCoreTests
    {
        // ---- in-memory filesystem fake --------------------------------------

        sealed class FakeFs : ProjectRenameCore.IProjectRenameFileSystem
        {
            public readonly HashSet<string> Files =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Dirs =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> Log = new List<string>();

            public string[] GetFilesTopDirectory(string dir)
            {
                // Return files whose directory equals dir (top-level only).
                return Files
                    .Where(f => string.Equals(Path.GetDirectoryName(f), dir, StringComparison.Ordinal))
                    .ToArray();
            }
            public bool FileExists(string path) => Files.Contains(path);
            public void FileMove(string oldPath, string newPath)
            {
                if (!Files.Contains(oldPath))
                    throw new FileNotFoundException(oldPath);
                if (Files.Contains(newPath))
                    throw new IOException("dest exists: " + newPath);
                Files.Remove(oldPath);
                Files.Add(newPath);
                Log.Add($"FileMove {oldPath} -> {newPath}");
            }
            public void FileDelete(string path)
            {
                Files.Remove(path);
                Log.Add($"FileDelete {path}");
            }
            public bool DirectoryExists(string path) => Dirs.Contains(path);
            public void DirectoryMove(string oldPath, string newPath)
            {
                if (!Dirs.Contains(oldPath))
                    throw new DirectoryNotFoundException(oldPath);
                Dirs.Remove(oldPath);
                Dirs.Add(newPath);
                Log.Add($"DirMove {oldPath} -> {newPath}");
            }
            public void DirectoryDelete(string path)
            {
                Dirs.Remove(path);
                Log.Add($"DirDelete {path}");
            }
        }

        /// <summary>
        /// Models a case-INSENSITIVE filesystem (Windows / most macOS volumes):
        /// FileExists/DirectoryExists compare ignoring case, and a case-only move
        /// keeps a single entry. Used to prove the delete-then-move guard cannot
        /// destroy the source on a case-only rename.
        /// </summary>
        sealed class CaseInsensitiveFakeFs : ProjectRenameCore.IProjectRenameFileSystem
        {
            public readonly HashSet<string> Files =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Dirs =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool Deleted;
            public bool DirDeleted;

            public string[] GetFilesTopDirectory(string dir) => Files
                .Where(f => string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            public bool FileExists(string path) => Files.Contains(path);
            public void FileMove(string oldPath, string newPath)
            {
                Files.Remove(oldPath);
                Files.Add(newPath);
            }
            public void FileDelete(string path) { Deleted = true; Files.Remove(path); }
            public bool DirectoryExists(string path) => Dirs.Contains(path);
            public void DirectoryMove(string oldPath, string newPath)
            {
                Dirs.Remove(oldPath);
                Dirs.Add(newPath);
            }
            public void DirectoryDelete(string path) { DirDeleted = true; Dirs.Remove(path); }
        }

        static ROM MakeRom(string filename = "rom.gba")
        {
            var rom = new ROM();
            rom.LoadLow(filename, new byte[0x200000], "NAZO");
            return rom;
        }

        // ---- Validate -------------------------------------------------------

        [Fact]
        public void Validate_NullRom_NoRomFilename()
        {
            Assert.Equal(ProjectRenameCore.ValidateResult.NoRomFilename,
                ProjectRenameCore.Validate(null, "a", "b"));
        }

        [Fact]
        public void Validate_ModifiedRom_Rejected()
        {
            var rom = MakeRom();
            rom.write_u8(0x100, 0x55); // make it Modified
            Assert.True(rom.Modified);
            Assert.Equal(ProjectRenameCore.ValidateResult.ModifiedRom,
                ProjectRenameCore.Validate(rom, "rom", "newrom"));
        }

        [Fact]
        public void Validate_VirtualRom_Rejected()
        {
            var rom = MakeRom();
            rom.SetVirtualROMFlag("rom.gba");
            Assert.True(rom.IsVirtualROM);
            Assert.Equal(ProjectRenameCore.ValidateResult.VirtualRom,
                ProjectRenameCore.Validate(rom, "rom", "newrom"));
        }

        [Fact]
        public void Validate_BadFilename_Rejected()
        {
            var rom = MakeRom();
            // Use NUL (\0): it is in Path.GetInvalidFileNameChars() on EVERY OS,
            // so this assertion is platform-independent. (Chars like '*' are
            // illegal only on Windows, where IsBadFilename → escape_filename uses
            // the OS-dependent Path.GetInvalidFileNameChars().)
            Assert.Equal(ProjectRenameCore.ValidateResult.BadFilename,
                ProjectRenameCore.Validate(rom, "rom", "bad\0name"));
        }

        [Fact]
        public void Validate_EmptyName_Rejected()
        {
            var rom = MakeRom();
            Assert.Equal(ProjectRenameCore.ValidateResult.EmptyName,
                ProjectRenameCore.Validate(rom, "rom", "   "));
        }

        [Fact]
        public void Validate_SameName_Rejected()
        {
            var rom = MakeRom();
            Assert.Equal(ProjectRenameCore.ValidateResult.SameName,
                ProjectRenameCore.Validate(rom, "rom", "rom"));
        }

        [Fact]
        public void Validate_GoodName_Ok()
        {
            var rom = MakeRom();
            Assert.Equal(ProjectRenameCore.ValidateResult.Ok,
                ProjectRenameCore.Validate(rom, "rom", "newrom"));
        }

        // ---- BuildPlan (PURE) ----------------------------------------------

        [Fact]
        public void BuildPlan_RomAndBackups_PrefixMatchOnly()
        {
            string dir = Path.Combine("C:", "proj");
            string romPath = Path.Combine(dir, "rom.gba");
            var files = new[]
            {
                Path.Combine(dir, "rom.gba"),         // the ROM
                Path.Combine(dir, "rom.bak001.gba"),  // backup, prefix match
                Path.Combine(dir, "rom.bak002.gba"),  // backup, prefix match
                Path.Combine(dir, "other.gba"),       // NOT a prefix match
                Path.Combine(dir, "readme.txt"),      // NOT a prefix match
            };

            var plan = ProjectRenameCore.BuildPlan(
                romPath, "rom", "newrom", files, "", "");

            // 3 prefix matches -> 3 moves; 'other'/'readme' skipped.
            Assert.Equal(3, plan.FileMoves.Count);
            var dests = plan.FileMoves.Select(m => Path.GetFileName(m.NewPath)).ToHashSet();
            Assert.Contains("newrom.gba", dests);
            Assert.Contains("newrom.bak001.gba", dests);
            Assert.Contains("newrom.bak002.gba", dests);
            Assert.Equal(Path.Combine(dir, "newrom.gba"), plan.NewRomPath);
        }

        [Fact]
        public void BuildPlan_PreservesSuffixAndExtension()
        {
            string dir = Path.Combine("C:", "proj");
            string romPath = Path.Combine(dir, "MyHack.gba");
            var files = new[]
            {
                Path.Combine(dir, "MyHack.gba"),
                Path.Combine(dir, "MyHack.emulator.sav"),
            };

            var plan = ProjectRenameCore.BuildPlan(
                romPath, "MyHack", "Renamed", files, "", "");

            var byOld = plan.FileMoves.ToDictionary(m => Path.GetFileName(m.OldPath));
            Assert.Equal(Path.Combine(dir, "Renamed.gba"),
                byOld["MyHack.gba"].NewPath);
            Assert.Equal(Path.Combine(dir, "Renamed.emulator.sav"),
                byOld["MyHack.emulator.sav"].NewPath);
        }

        [Fact]
        public void BuildPlan_NonPrefixSubstring_NotMatched()
        {
            string dir = Path.Combine("C:", "proj");
            string romPath = Path.Combine(dir, "rom.gba");
            // "myrom" CONTAINS "rom" but does not START with it -> skip.
            var files = new[] { Path.Combine(dir, "myrom.gba") };

            var plan = ProjectRenameCore.BuildPlan(
                romPath, "rom", "newrom", files, "", "");

            Assert.Empty(plan.FileMoves);
        }

        // ---- ExecutePlan ----------------------------------------------------

        [Fact]
        public void ExecutePlan_MovesAllFiles()
        {
            string dir = Path.Combine("C:", "proj");
            var fs = new FakeFs();
            fs.Files.Add(Path.Combine(dir, "rom.gba"));
            fs.Files.Add(Path.Combine(dir, "rom.bak001.gba"));

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba"), Path.Combine(dir, "rom.bak001.gba") },
                "", "");

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.DoesNotContain(Path.Combine(dir, "rom.gba"), fs.Files);
            Assert.Contains(Path.Combine(dir, "newrom.gba"), fs.Files);
            Assert.Contains(Path.Combine(dir, "newrom.bak001.gba"), fs.Files);
        }

        [Fact]
        public void ExecutePlan_ExistingDestination_DeletedFirst()
        {
            string dir = Path.Combine("C:", "proj");
            var fs = new FakeFs();
            fs.Files.Add(Path.Combine(dir, "rom.gba"));
            fs.Files.Add(Path.Combine(dir, "newrom.gba")); // stale destination

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba") }, "", "");

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.Contains(Path.Combine(dir, "newrom.gba"), fs.Files);
            Assert.Contains(fs.Log, l => l.StartsWith("FileDelete"));
        }

        [Fact]
        public void ExecutePlan_MovesEtcDirectory()
        {
            string dir = Path.Combine("C:", "proj");
            string oldEtc = Path.Combine("C:", "etc", "rom");
            string newEtc = Path.Combine("C:", "etc", "newrom");
            var fs = new FakeFs();
            fs.Files.Add(Path.Combine(dir, "rom.gba"));
            fs.Dirs.Add(oldEtc);

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba") }, oldEtc, newEtc);

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.DoesNotContain(oldEtc, fs.Dirs);
            Assert.Contains(newEtc, fs.Dirs);
        }

        [Fact]
        public void ExecutePlan_EtcDirMissing_NoMove()
        {
            string dir = Path.Combine("C:", "proj");
            string oldEtc = Path.Combine("C:", "etc", "rom");
            string newEtc = Path.Combine("C:", "etc", "newrom");
            var fs = new FakeFs();
            fs.Files.Add(Path.Combine(dir, "rom.gba"));
            // oldEtc deliberately absent

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba") }, oldEtc, newEtc);

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.DoesNotContain(fs.Log, l => l.StartsWith("DirMove"));
        }

        [Fact]
        public void ExecutePlan_ExistingEtcDestination_DeletedFirst()
        {
            string dir = Path.Combine("C:", "proj");
            string oldEtc = Path.Combine("C:", "etc", "rom");
            string newEtc = Path.Combine("C:", "etc", "newrom");
            var fs = new FakeFs();
            fs.Files.Add(Path.Combine(dir, "rom.gba"));
            fs.Dirs.Add(oldEtc);
            fs.Dirs.Add(newEtc); // stale destination

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba") }, oldEtc, newEtc);

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.Contains(fs.Log, l => l.StartsWith("DirDelete"));
            Assert.Contains(newEtc, fs.Dirs);
        }

        // ---- Rename (full, against a live ROM) ------------------------------

        [Fact]
        public void Rename_FullFlow_ReturnsNewPath_AndMovesFiles()
        {
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = Path.GetTempPath();
                string dir = Path.Combine(Path.GetTempPath(), "pr1461test");
                string romPath = Path.Combine(dir, "rom.gba");
                var rom = MakeRom(romPath);

                var fs = new FakeFs();
                fs.Files.Add(romPath);
                fs.Files.Add(Path.Combine(dir, "rom.bak001.gba"));

                string newPath = ProjectRenameCore.Rename(
                    rom, "rom", "newrom", fs,
                    out ProjectRenameCore.ValidateResult result);

                Assert.Equal(ProjectRenameCore.ValidateResult.Ok, result);
                Assert.Equal(Path.Combine(dir, "newrom.gba"), newPath);
                Assert.Contains(Path.Combine(dir, "newrom.gba"), fs.Files);
                Assert.Contains(Path.Combine(dir, "newrom.bak001.gba"), fs.Files);
                Assert.DoesNotContain(romPath, fs.Files);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void Rename_FullFlow_MovesEtcDirectory()
        {
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = Path.GetTempPath();
                string dir = Path.Combine(Path.GetTempPath(), "pr1461test3");
                string romPath = Path.Combine(dir, "rom.gba");
                var rom = MakeRom(romPath);

                // The etc dir is config/etc/<firstPeriodTitle>/ under BaseDirectory.
                string oldEtc = Path.GetDirectoryName(
                    U.ConfigEtcFilename("flag", rom));
                string newEtc = Path.GetDirectoryName(
                    U.ConfigEtcFilename("flag",
                        Path.Combine(dir, "newrom.gba")));

                var fs = new FakeFs();
                fs.Files.Add(romPath);
                fs.Dirs.Add(oldEtc);

                ProjectRenameCore.Rename(rom, "rom", "newrom", fs,
                    out ProjectRenameCore.ValidateResult result);

                Assert.Equal(ProjectRenameCore.ValidateResult.Ok, result);
                Assert.DoesNotContain(oldEtc, fs.Dirs);
                Assert.Contains(newEtc, fs.Dirs);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void Rename_ValidationFailure_ReturnsNull_NoFileMove()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pr1461test2");
            string romPath = Path.Combine(dir, "rom.gba");
            var rom = MakeRom(romPath);

            var fs = new FakeFs();
            fs.Files.Add(romPath);

            // Same name -> rejected, no moves (validation runs before etc-dir
            // resolution, so BaseDirectory is irrelevant here).
            string newPath = ProjectRenameCore.Rename(
                rom, "rom", "rom", fs,
                out ProjectRenameCore.ValidateResult result);

            Assert.Null(newPath);
            Assert.Equal(ProjectRenameCore.ValidateResult.SameName, result);
            Assert.Empty(fs.Log);
        }

        [Fact]
        public void ExecutePlan_CaseOnlyRename_DoesNotDeleteSource()
        {
            // rom.gba -> Rom.gba: on a case-insensitive FS these are the same
            // file. The fake FS treats FileExists ordinally, so to model the
            // hazard we add ONLY the source under its old casing; the guard must
            // skip the delete (which would otherwise be a no-op here) AND, more
            // importantly, must not delete a same-file destination. We assert the
            // move happens and NO FileDelete was logged.
            string dir = Path.Combine("C:", "proj");
            var fs = new FakeFs();
            string src = Path.Combine(dir, "rom.gba");
            fs.Files.Add(src);

            var plan = ProjectRenameCore.BuildPlan(
                src, "rom", "Rom",
                new[] { src }, "", "");

            ProjectRenameCore.ExecutePlan(plan, fs);

            Assert.Contains(Path.Combine(dir, "Rom.gba"), fs.Files);
            Assert.DoesNotContain(src, fs.Files);
            Assert.DoesNotContain(fs.Log, l => l.StartsWith("FileDelete"));
        }

        [Fact]
        public void ExecutePlan_CaseOnlyRename_SameFileDestination_NotDeleted()
        {
            // Model a case-insensitive FS explicitly: a FileExists check on the
            // new-cased path returns true because the source file exists. A naive
            // delete-then-move would delete the source. The guard must prevent it.
            string dir = Path.Combine("C:", "proj");
            var ciFs = new CaseInsensitiveFakeFs();
            string src = Path.Combine(dir, "rom.gba");
            ciFs.Files.Add(src);

            var plan = ProjectRenameCore.BuildPlan(
                src, "rom", "Rom",
                new[] { src }, "", "");

            ProjectRenameCore.ExecutePlan(plan, ciFs);

            // Source preserved under the new casing; nothing deleted.
            Assert.False(ciFs.Deleted);
            Assert.Single(ciFs.Files);
            Assert.Contains(Path.Combine(dir, "Rom.gba"), ciFs.Files);
        }

        [Fact]
        public void ExecutePlan_CaseOnlyEtcDirRename_DoesNotDeleteSource()
        {
            string dir = Path.Combine("C:", "proj");
            string oldEtc = Path.Combine("C:", "etc", "rom");
            string newEtc = Path.Combine("C:", "etc", "Rom"); // case-only diff
            var ciFs = new CaseInsensitiveFakeFs();
            ciFs.Files.Add(Path.Combine(dir, "rom.gba"));
            ciFs.Dirs.Add(oldEtc);

            var plan = ProjectRenameCore.BuildPlan(
                Path.Combine(dir, "rom.gba"), "rom", "newrom",
                new[] { Path.Combine(dir, "rom.gba") }, oldEtc, newEtc);

            ProjectRenameCore.ExecutePlan(plan, ciFs);

            // The delete-then-move guard must NOT have deleted the (same-on-disk)
            // source directory; the move renames it to the new casing.
            Assert.False(ciFs.DirDeleted);
            Assert.Single(ciFs.Dirs);
            Assert.Contains(newEtc, ciFs.Dirs);
        }

        [Fact]
        public void ExecutePlan_NullArgs_Throws()
        {
            var fs = new FakeFs();
            Assert.Throws<ArgumentNullException>(
                () => ProjectRenameCore.ExecutePlan(null, fs));
            var plan = new ProjectRenameCore.RenamePlan();
            Assert.Throws<ArgumentNullException>(
                () => ProjectRenameCore.ExecutePlan(plan, null));
        }
    }
}
