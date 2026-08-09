// SPDX-License-Identifier: GPL-3.0-or-later
// #1195 — Last-Used-File tool (Avalonia port of WinForms OpenLastSelectedFileForm).
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
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
            string scratch = CreateScratchDirectory();
            string tempRom = Path.Combine(scratch, "last-file.gba");
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
                TryDeleteDirectory(scratch);
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
                rom.Filename = Path.Combine(AppContext.BaseDirectory, "open-last-selected-tests", "missing-" + Guid.NewGuid().ToString("N") + ".gba");
                CoreState.ROM = rom;
                vm.Load();
                Assert.False(vm.HasFile);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [AvaloniaFact]
        public async Task OpenUnsupportedLauncherWritesVisibleStatus()
        {
            var prevRom = CoreState.ROM;
            string scratch = CreateScratchDirectory();
            string romPath = Path.Combine(scratch, "last-file.gba");
            try
            {
                File.WriteAllBytes(romPath, new byte[0x100]);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                rom.Filename = romPath;
                CoreState.ROM = rom;

                using var launcherOverride = ExternalLauncher.OverrideCurrentForTests(
                    new UnsupportedLauncher("External process launching is not available on this device."));
                var view = new OpenLastSelectedFileView();
                view.LoadLastFileForTests();

                await view.OpenLastFileForTestsAsync();

                Assert.Contains("not available", view.StatusMessageForTests, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CoreState.ROM = prevRom;
                TryDeleteDirectory(scratch);
            }
        }

        [AvaloniaFact]
        public async Task RevealUnsupportedLauncherWritesVisibleStatus()
        {
            var prevRom = CoreState.ROM;
            string scratch = CreateScratchDirectory();
            string romPath = Path.Combine(scratch, "last-file.gba");
            try
            {
                File.WriteAllBytes(romPath, new byte[0x100]);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                rom.Filename = romPath;
                CoreState.ROM = rom;

                using var launcherOverride = ExternalLauncher.OverrideCurrentForTests(
                    new UnsupportedLauncher("External process launching is not available on this device."));
                var view = new OpenLastSelectedFileView();
                view.LoadLastFileForTests();

                await view.RevealLastFileForTestsAsync();

                Assert.Contains("not available", view.StatusMessageForTests, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CoreState.ROM = prevRom;
                TryDeleteDirectory(scratch);
            }
        }

        static string CreateScratchDirectory()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "open-last-selected-tests");
            string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        sealed class UnsupportedLauncher : IExternalLauncher
        {
            readonly string _message;

            public UnsupportedLauncher(string message) => _message = message;

            public Task<ExternalLaunchResult> OpenUriAsync(
                TopLevel? topLevel,
                Uri uri,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ExternalLaunchResult.Succeeded());

            public Task<ExternalLaunchResult> OpenPathAsync(
                string path,
                string? arguments = null,
                string? workingDirectory = null,
                bool useShellExecute = true,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ExternalLaunchResult.Unsupported(_message));

            public Task<ExternalLaunchResult> RevealPathAsync(
                string path,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ExternalLaunchResult.Unsupported(_message));

            public Task<ExternalProcessResult> RunCapturedProcessAsync(
                ExternalProcessRequest request,
                CancellationToken cancellationToken = default)
                => Task.FromResult(ExternalProcessResult.Unsupported(_message));
        }
    }
}
