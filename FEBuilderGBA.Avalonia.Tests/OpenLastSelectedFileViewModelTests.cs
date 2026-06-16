// SPDX-License-Identifier: GPL-3.0-or-later
// #1195 — Last-Used-File tool (Avalonia port of WinForms OpenLastSelectedFileForm).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class OpenLastSelectedFileViewModelTests
    {
        [Fact]
        public void GetLastFile_ReturnsLoadedRomFilename_AndHasFileWhenOnDisk()
        {
            var prevRom = CoreState.ROM;
            string tempRom = Path.Combine(Path.GetTempPath(), "feb_lastfile_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                File.WriteAllBytes(tempRom, new byte[0x100]);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                rom.Filename = tempRom;
                CoreState.ROM = rom;

                var vm = new OpenLastSelectedFileViewModel();
                vm.Load();
                Assert.Equal(tempRom, vm.LastFile);   // last file = the loaded ROM
                Assert.True(vm.HasFile);               // exists on disk -> actions enabled
                Assert.True(vm.IsLoaded);
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(tempRom); } catch { }
            }
        }

        [Fact]
        public void HasFile_FalseWhenNoRomOrMissing()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new OpenLastSelectedFileViewModel();
                vm.Load();
                Assert.Equal("", vm.LastFile);
                Assert.False(vm.HasFile);

                // A ROM whose Filename does not exist on disk -> HasFile false (no action).
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x10]);
                rom.Filename = Path.Combine(Path.GetTempPath(), "feb_missing_" + Guid.NewGuid().ToString("N") + ".gba");
                CoreState.ROM = rom;
                vm.Load();
                Assert.False(vm.HasFile);
            }
            finally { CoreState.ROM = prevRom; }
        }
    }
}
