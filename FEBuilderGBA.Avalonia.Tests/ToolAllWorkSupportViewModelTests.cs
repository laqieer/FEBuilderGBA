using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// VM-level tests for the All-Work-Support tool (#1196): scanning behaviour,
    /// read-only (clean) loading, and the injectable update-check pass.
    /// </summary>
    [Collection("SharedState")]
    public class ToolAllWorkSupportViewModelTests : IDisposable
    {
        readonly string _root;
        readonly string _savedBaseDir;
        readonly ROM _savedRom;

        public ToolAllWorkSupportViewModelTests()
        {
            _savedBaseDir = CoreState.BaseDirectory;
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
            _root = Path.Combine(Path.GetTempPath(), "fe_aws_vm_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            CoreState.BaseDirectory = _savedBaseDir;
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* best effort */ }
        }

        string SeedProject(string name)
        {
            string projDir = Path.Combine(_root, "projects");
            string etcDir = Path.Combine(_root, "config", "etc", name);
            Directory.CreateDirectory(projDir);
            Directory.CreateDirectory(etcDir);

            string rom = Path.Combine(projDir, name + ".gba");
            File.WriteAllBytes(rom, new byte[16]);
            File.WriteAllText(Path.Combine(etcDir, "worksupport_.txt"), "0\t" + rom + "\n");
            File.WriteAllText(Path.ChangeExtension(rom, ".updateinfo.txt"),
                "NAME=" + name + "\nCHECK_URL=http://example.com/v\nCHECK_REGEX=ver=(\\d{8})\n");
            return rom;
        }

        [Fact]
        public void LoadList_NoBaseDir_ReturnsEmpty()
        {
            CoreState.BaseDirectory = null;
            var vm = new ToolAllWorkSupportViewModel();
            var list = vm.LoadList();
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void LoadList_SeededProjects_ReturnsThem_AndStaysClean()
        {
            CoreState.BaseDirectory = _root;
            SeedProject("Alpha");
            SeedProject("Beta");

            var vm = new ToolAllWorkSupportViewModel();
            var list = vm.LoadList();

            Assert.Equal(2, list.Count);
            Assert.Contains(list, p => p.Name == "Alpha");
            Assert.Contains(list, p => p.Name == "Beta");
            Assert.True(vm.IsLoaded);
            Assert.False(vm.IsDirty); // read-only load must not dirty the VM
            Assert.Equal(2, vm.GetListCount());
        }

        [Fact]
        public void UpdateCheckAll_MarksUpdateableProjects()
        {
            CoreState.BaseDirectory = _root;
            SeedProject("Gamma");

            var vm = new ToolAllWorkSupportViewModel();
            vm.LoadList();

            // Inject an offline "remote newer than rom" outcome.
            int updateable = vm.UpdateCheckAll(
                httpGet: _ => "ver=20300101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2010, 1, 1));

            Assert.Equal(1, updateable);
            Assert.True(vm.Projects[0].IsUpdateMark);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void UpdateCheckAll_NoUpdate_LeavesMarksFalse()
        {
            CoreState.BaseDirectory = _root;
            SeedProject("Delta");

            var vm = new ToolAllWorkSupportViewModel();
            vm.LoadList();

            int updateable = vm.UpdateCheckAll(
                httpGet: _ => "ver=20100101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2030, 1, 1));

            Assert.Equal(0, updateable);
            Assert.False(vm.Projects[0].IsUpdateMark);
        }
    }
}
