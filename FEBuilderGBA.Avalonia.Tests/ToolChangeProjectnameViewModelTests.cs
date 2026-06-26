// SPDX-License-Identifier: GPL-3.0-or-later
// Change Project Name dialog wiring tests (#1461).
//
// Proves the Avalonia "Change Project Name" tool now performs the real
// project-file rename ported from WinForms ToolChangeProjectnameForm:
//   1. TryRename against a non-virtual, unmodified ROM with an injected
//      in-memory filesystem produces the expected renamed file set
//      (ROM + prefix-matched backups), returns the new ROM path, and leaves
//      StatusMessage empty;
//   2. validation failures (modified ROM, virtual ROM, bad filename, same
//      name) return null, perform NO file moves, and set a user-facing
//      StatusMessage;
//   3. DescribeValidate maps every ValidateResult to a non-empty message.
//
// Marked [Collection("SharedState")] because it mutates CoreState.ROM /
// CoreState.BaseDirectory. No UndoService is committed (file I/O only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolChangeProjectnameViewModelTests
    {
        sealed class FakeFs : ProjectRenameCore.IProjectRenameFileSystem
        {
            public readonly HashSet<string> Files =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Dirs =
                new HashSet<string>(StringComparer.Ordinal);
            public int Moves;

            public string[] GetFilesTopDirectory(string dir) => Files
                .Where(f => string.Equals(Path.GetDirectoryName(f), dir, StringComparison.Ordinal))
                .ToArray();
            public bool FileExists(string path) => Files.Contains(path);
            public void FileMove(string oldPath, string newPath)
            {
                Files.Remove(oldPath);
                Files.Add(newPath);
                Moves++;
            }
            public void FileDelete(string path) => Files.Remove(path);
            public bool DirectoryExists(string path) => Dirs.Contains(path);
            public void DirectoryMove(string oldPath, string newPath)
            {
                Dirs.Remove(oldPath);
                Dirs.Add(newPath);
            }
            public void DirectoryDelete(string path) => Dirs.Remove(path);
        }

        static ROM MakeRom(string filename)
        {
            var rom = new ROM();
            rom.LoadLow(filename, new byte[0x200000], "NAZO");
            return rom;
        }

        [Fact]
        public void TryRename_HappyPath_RenamesRomAndBackups()
        {
            ROM saved = CoreState.ROM;
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = Path.GetTempPath();
                string dir = Path.Combine(Path.GetTempPath(), "pr1461vm");
                string romPath = Path.Combine(dir, "MyHack.gba");
                CoreState.ROM = MakeRom(romPath);

                var fs = new FakeFs();
                fs.Files.Add(romPath);
                fs.Files.Add(Path.Combine(dir, "MyHack.bak001.gba"));
                fs.Files.Add(Path.Combine(dir, "Other.gba")); // not a prefix match

                var vm = new ToolChangeProjectnameViewViewModel();
                vm.CurrentName = "MyHack";
                vm.NewName = "Renamed";

                string newPath = vm.TryRename(fs);

                Assert.Equal(Path.Combine(dir, "Renamed.gba"), newPath);
                Assert.Equal(string.Empty, vm.StatusMessage);
                // Exactly the two prefix-matched files moved.
                Assert.Equal(2, fs.Moves);
                Assert.True(fs.Files.Contains(Path.Combine(dir, "Renamed.gba")));
                Assert.True(fs.Files.Contains(Path.Combine(dir, "Renamed.bak001.gba")));
                Assert.True(fs.Files.Contains(Path.Combine(dir, "Other.gba")));
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void TryRename_SameName_NoMove_SetsStatus()
        {
            ROM saved = CoreState.ROM;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "pr1461vm2");
                CoreState.ROM = MakeRom(Path.Combine(dir, "rom.gba"));

                var fs = new FakeFs();
                fs.Files.Add(Path.Combine(dir, "rom.gba"));

                var vm = new ToolChangeProjectnameViewViewModel();
                vm.CurrentName = "rom";
                vm.NewName = "rom";

                string newPath = vm.TryRename(fs);

                Assert.Null(newPath);
                Assert.Equal(0, fs.Moves);
                Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void TryRename_ModifiedRom_NoMove_SetsStatus()
        {
            ROM saved = CoreState.ROM;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "pr1461vm3");
                var rom = MakeRom(Path.Combine(dir, "rom.gba"));
                rom.write_u8(0x100, 0x55); // Modified = true
                CoreState.ROM = rom;

                var fs = new FakeFs();
                fs.Files.Add(Path.Combine(dir, "rom.gba"));

                var vm = new ToolChangeProjectnameViewViewModel();
                vm.CurrentName = "rom";
                vm.NewName = "newrom";

                string newPath = vm.TryRename(fs);

                Assert.Null(newPath);
                Assert.Equal(0, fs.Moves);
                Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void TryRename_BadFilename_NoMove_SetsStatus()
        {
            ROM saved = CoreState.ROM;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "pr1461vm4");
                CoreState.ROM = MakeRom(Path.Combine(dir, "rom.gba"));

                var fs = new FakeFs();
                fs.Files.Add(Path.Combine(dir, "rom.gba"));

                var vm = new ToolChangeProjectnameViewViewModel();
                vm.CurrentName = "rom";
                vm.NewName = "bad*name";

                string newPath = vm.TryRename(fs);

                Assert.Null(newPath);
                Assert.Equal(0, fs.Moves);
                Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Theory]
        [InlineData(ProjectRenameCore.ValidateResult.ModifiedRom)]
        [InlineData(ProjectRenameCore.ValidateResult.VirtualRom)]
        [InlineData(ProjectRenameCore.ValidateResult.BadFilename)]
        [InlineData(ProjectRenameCore.ValidateResult.EmptyName)]
        [InlineData(ProjectRenameCore.ValidateResult.SameName)]
        [InlineData(ProjectRenameCore.ValidateResult.NoRomFilename)]
        public void DescribeValidate_EveryFailure_HasMessage(
            ProjectRenameCore.ValidateResult r)
        {
            Assert.False(string.IsNullOrEmpty(
                ToolChangeProjectnameViewViewModel.DescribeValidate(r)));
        }

        [Fact]
        public void DescribeValidate_Ok_Empty()
        {
            Assert.Equal(string.Empty,
                ToolChangeProjectnameViewViewModel.DescribeValidate(
                    ProjectRenameCore.ValidateResult.Ok));
        }
    }
}
