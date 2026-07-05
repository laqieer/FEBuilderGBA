// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public sealed class UpdateCheckOptionsTests : IDisposable
    {
        readonly Config? _savedConfig;
        readonly string? _savedBaseDir;
        readonly string _baseDir;

        public UpdateCheckOptionsTests()
        {
            _savedConfig = CoreState.Config;
            _savedBaseDir = CoreState.BaseDirectory;
            _baseDir = Path.Combine(AppContext.BaseDirectory, "update-check-options-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_baseDir, "config"));
            CoreState.BaseDirectory = _baseDir;
        }

        public void Dispose()
        {
            CoreState.Config = _savedConfig;
            CoreState.BaseDirectory = _savedBaseDir;
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        [Fact]
        public void OptionsViewModel_LoadsAndSavesAutoUpdateGate()
        {
            string configPath = Path.Combine(_baseDir, "config", "config.xml");
            CoreState.Config = Config.LoadOrCreate(configPath);
            CoreState.Config["func_auto_update"] = "0";
            CoreState.Config.Save();

            var vm = new OptionsViewModel();
            vm.Load();
            Assert.False(vm.AutoUpdateEnabled);

            vm.AutoUpdateEnabled = true;
            vm.Save();

            CoreState.Config = Config.LoadOrCreate(configPath);
            Assert.Equal("3", CoreState.Config.at("func_auto_update", ""));
        }
    }
}
