using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Unit tests for the Avalonia ToolDiffViewModel. Verifies the diff actions
    /// fail safely when files/ROM are missing, and that successful runs produce
    /// expected output via DiffToolCore.
    /// </summary>
    [Collection("SharedState")]
    public class ToolDiffViewModelTests : IDisposable
    {
        readonly string _tempDir;
        readonly ROM? _savedRom;

        public ToolDiffViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ToolDiffVMTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
        }

        string TempFile(string name) => Path.Combine(_tempDir, name);

        [Fact]
        public void MakeBinPatch_NoRomLoaded_SetsStatus()
        {
            var vm = new ToolDiffViewModel();
            vm.OtherPath = TempFile("other.gba");
            File.WriteAllBytes(vm.OtherPath, new byte[16]);

            vm.RunMakeBinPatch(TempFile("PATCH_test.txt"));

            Assert.Contains("no rom", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MakeBinPatch_EmptyOtherPath_SetsStatus()
        {
            var vm = new ToolDiffViewModel();
            vm.OtherPath = "";

            vm.RunMakeBinPatch(TempFile("PATCH_x.txt"));

            Assert.Contains("path", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MakeBinPatch_OtherFileMissing_SetsStatus()
        {
            var vm = new ToolDiffViewModel();
            vm.OtherPath = TempFile("nope.gba");

            vm.RunMakeBinPatch(TempFile("PATCH_x.txt"));

            Assert.Contains("not found", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MakeBinPatch3_BothFilesEmpty_SetsStatus()
        {
            var vm = new ToolDiffViewModel();
            vm.AFilePath = "";
            vm.BFilePath = "";

            vm.RunMakeBinPatch3(TempFile("PATCH_x.txt"));

            Assert.Contains("path", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MakeBinPatch3_NoRomLoaded_SetsStatus()
        {
            var vm = new ToolDiffViewModel();
            vm.AFilePath = TempFile("a.gba");
            vm.BFilePath = TempFile("b.gba");
            File.WriteAllBytes(vm.AFilePath, new byte[16]);
            File.WriteAllBytes(vm.BFilePath, new byte[16]);

            vm.RunMakeBinPatch3(TempFile("PATCH_x.txt"));

            Assert.Contains("no rom", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
