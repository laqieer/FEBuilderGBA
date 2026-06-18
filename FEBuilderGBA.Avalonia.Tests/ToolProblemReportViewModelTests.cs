using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// VM-level tests for the Problem-Report tool (#1193). Exercises the guards
    /// (no ROM / empty problem / empty path) and the success path that delegates to
    /// <see cref="ProblemReportCore.CreateReport"/>. READ-ONLY w.r.t. the ROM.
    /// </summary>
    [Collection("SharedState")]
    public class ToolProblemReportViewModelTests : IDisposable
    {
        readonly ROM _savedRom;
        readonly string _root;

        public ToolProblemReportViewModelTests()
        {
            _savedRom = CoreState.ROM;
            _root = Path.Combine(Path.GetTempPath(), "fe_pr_vm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* best effort */ }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            rom.LoadLow("problemreport-vm-fe8u.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        [Fact]
        public void CreateReport_NoRom_ReturnsError_SetsStatus()
        {
            CoreState.ROM = null;
            var vm = new ToolProblemReportViewModel { ProblemText = "x" };

            string err = vm.CreateReport(Path.Combine(_root, "a.report.7z"));

            Assert.False(string.IsNullOrEmpty(err));
            Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void CreateReport_EmptyProblem_ReturnsError()
        {
            CoreState.ROM = MakeRom();
            var vm = new ToolProblemReportViewModel { ProblemText = "   " };

            string err = vm.CreateReport(Path.Combine(_root, "b.report.7z"));

            Assert.False(string.IsNullOrEmpty(err));
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void CreateReport_EmptyPath_ReturnsError()
        {
            CoreState.ROM = MakeRom();
            var vm = new ToolProblemReportViewModel { ProblemText = "real problem" };

            string err = vm.CreateReport("");

            Assert.False(string.IsNullOrEmpty(err));
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void CreateReport_Success_WritesArchive_SetsLoadedAndStatus()
        {
            CoreState.ROM = MakeRom();
            var vm = new ToolProblemReportViewModel
            {
                ProblemText = "Freeze on chapter 1 when the lord attacks. UNIQUE_VM_MARKER"
            };

            string outPath = Path.Combine(_root, "ok.report.7z");
            string err = vm.CreateReport(outPath);

            Assert.Equal("", err);
            Assert.True(vm.IsLoaded);
            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 0);
            Assert.Contains(outPath, vm.StatusMessage);

            // The well-known public URLs are exposed for the about-link.
            Assert.Contains("feuniverse.us", ToolProblemReportViewModel.Report7zUrl);
        }

        [Fact]
        public void Report7zUrl_MatchesWinFormsDestination()
        {
            Assert.Equal(
                "https://feuniverse.us/t/fe-builder-gba-if-you-have-any-questions-attach-report7z/2845/4937",
                ToolProblemReportViewModel.Report7zUrl);
        }
    }
}
