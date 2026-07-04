// SPDX-License-Identifier: GPL-3.0-or-later
// #1799: on a fresh install (no config/config.xml yet) CoreState.Config was left
// null by a File.Exists guard in App.axaml.cs / RomLoader.cs, so OptionsViewModel
// .Save() silently discarded every setting (tool paths, theme, submodule URLs).
// With Config.LoadOrCreate wired into startup, a first-run Options save must now
// persist and a fresh reopen must read the values back. This test reproduces the
// exact user scenario (@Chrixcalibur) end-to-end through the real ViewModel.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class OptionsConfigPersistenceTests : IDisposable
{
    readonly Config? _savedConfig;
    readonly string? _savedBaseDir;
    readonly string? _savedLanguage;
    readonly string _savedGitPath;
    readonly string _baseDir;

    public OptionsConfigPersistenceTests()
    {
        _savedConfig = CoreState.Config;
        _savedBaseDir = CoreState.BaseDirectory;
        _savedLanguage = CoreState.Language;
        _savedGitPath = CoreState.GitPath;

        // Isolated temp base dir OUTSIDE the repo (and not a git repo) so Save()'s
        // ApplySubmoduleRemotes() cannot touch the real config/patch2 submodule.
        _baseDir = Path.Combine(Path.GetTempPath(), $"opt_persist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_baseDir, "config"));
        CoreState.BaseDirectory = _baseDir;
    }

    public void Dispose()
    {
        CoreState.Config = _savedConfig;
        CoreState.BaseDirectory = _savedBaseDir;
        CoreState.Language = _savedLanguage;
        CoreState.GitPath = _savedGitPath;
        // OptionsViewModel.Save() reloads the *global* MyTranslateResource for the temp
        // BaseDirectory (clearing it here). Restore it for the saved language/base dir so
        // it doesn't leak into other [Collection("SharedState")] tests (matches the L10n
        // restore convention). Best-effort; never fail Dispose over it.
        try { OptionsViewModel.ReloadTranslations(); } catch { /* best effort */ }
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void FreshInstall_OptionsSave_PersistsToolPaths_AcrossReopen()
    {
        string configPath = Path.Combine(_baseDir, "config", "config.xml");
        Assert.False(File.Exists(configPath)); // fresh install: no prefs file yet

        // Startup wiring (mirrors the fixed App.axaml.cs / RomLoader.cs).
        CoreState.Config = Config.LoadOrCreate(configPath);
        Assert.NotNull(CoreState.Config);

        // User browses for tools and presses OK.
        var vm = new OptionsViewModel();
        vm.Load();
        vm.Emulator = @"C:\emu\vba.exe";
        vm.Sappy = @"C:\tools\sappy.exe";
        vm.EventAssembler = @"C:\ea\ColorzCore.dll";
        vm.Save();

        Assert.True(File.Exists(configPath)); // config.xml created on the first save

        // Reopen Options: a fresh VM over a freshly-loaded config must read them back.
        CoreState.Config = Config.LoadOrCreate(configPath);
        var vm2 = new OptionsViewModel();
        vm2.Load();
        Assert.Equal(@"C:\emu\vba.exe", vm2.Emulator);
        Assert.Equal(@"C:\tools\sappy.exe", vm2.Sappy);
        Assert.Equal(@"C:\ea\ColorzCore.dll", vm2.EventAssembler);
    }
}
