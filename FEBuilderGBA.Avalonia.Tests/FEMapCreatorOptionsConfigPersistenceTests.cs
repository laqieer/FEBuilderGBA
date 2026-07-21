// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 2: durable FEMapCreator executable/assets-root setup lives in the same
// Config/Options pattern as every other external tool path. These tests follow the exact
// isolation convention established by OptionsConfigPersistenceTests (#1799): an isolated temp
// base dir OUTSIDE the repo so ApplySubmoduleRemotes() never touches the real config/patch2
// submodule, and full CoreState save/restore around each test.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class FEMapCreatorOptionsConfigPersistenceTests : IDisposable
{
    readonly Config? _savedConfig;
    readonly string? _savedBaseDir;
    readonly string? _savedLanguage;
    readonly string _savedGitPath;
    readonly string _baseDir;

    public FEMapCreatorOptionsConfigPersistenceTests()
    {
        _savedConfig = CoreState.Config;
        _savedBaseDir = CoreState.BaseDirectory;
        _savedLanguage = CoreState.Language;
        _savedGitPath = CoreState.GitPath;

        _baseDir = Path.Combine(Path.GetTempPath(), $"femc_opt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_baseDir, "config"));
        CoreState.BaseDirectory = _baseDir;
        CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
    }

    public void Dispose()
    {
        CoreState.Config = _savedConfig;
        CoreState.BaseDirectory = _savedBaseDir;
        CoreState.Language = _savedLanguage;
        CoreState.GitPath = _savedGitPath;
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { /* best effort */ }
    }

    string MakeFile(string name)
    {
        string path = Path.Combine(_baseDir, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    string MakeDir(string name)
    {
        string path = Path.Combine(_baseDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Save_ThenReload_RoundTripsPathAndAssetsRoot()
    {
        string exe = MakeFile("FEMapCreator.exe");
        string assets = MakeDir("assets");

        var vm = new OptionsViewModel();
        vm.Load();
        vm.FEMapCreatorPath = exe;
        vm.FEMapCreatorAssetsRoot = assets;
        vm.Save();

        string configPath = Path.Combine(_baseDir, "config", "config.xml");
        Assert.True(File.Exists(configPath));

        CoreState.Config = Config.LoadOrCreate(configPath);
        var vm2 = new OptionsViewModel();
        vm2.Load();
        Assert.Equal(exe, vm2.FEMapCreatorPath);
        Assert.Equal(assets, vm2.FEMapCreatorAssetsRoot);
    }

    [Fact]
    public void EmptyAssetsRoot_IsValidConfiguredState_NotInvalid()
    {
        string exe = MakeFile("FEMapCreator.exe");

        var vm = new OptionsViewModel();
        vm.Load();
        vm.FEMapCreatorPath = exe;
        vm.FEMapCreatorAssetsRoot = "";

        FEMapCreatorSetupSnapshot snapshot = vm.GetFEMapCreatorStatusSnapshot();
        Assert.Equal(FEMapCreatorSetupStatus.Configured, snapshot.Status);
    }

    [Fact]
    public void BlankExecutablePath_IsNotConfigured_NotInvalid()
    {
        var vm = new OptionsViewModel();
        vm.Load();
        vm.FEMapCreatorPath = "";
        vm.FEMapCreatorAssetsRoot = "";

        FEMapCreatorSetupSnapshot snapshot = vm.GetFEMapCreatorStatusSnapshot();
        Assert.Equal(FEMapCreatorSetupStatus.NotConfigured, snapshot.Status);
    }

    [Fact]
    public void NonExistentExecutable_IsInvalid_WithReason()
    {
        var vm = new OptionsViewModel();
        vm.Load();
        vm.FEMapCreatorPath = Path.Combine(_baseDir, "does-not-exist.exe");
        vm.FEMapCreatorAssetsRoot = "";

        FEMapCreatorSetupSnapshot snapshot = vm.GetFEMapCreatorStatusSnapshot();
        Assert.Equal(FEMapCreatorSetupStatus.Invalid, snapshot.Status);
        Assert.False(string.IsNullOrEmpty(snapshot.ErrorMessage));
    }

    [Fact]
    public void Load_NeverValidatesOrThrows_ForUnconfiguredOrBogusStoredPath()
    {
        // Simulate a prior/foreign save of a bogus path directly into config.xml, bypassing the
        // ViewModel entirely, then confirm merely constructing/Loading a fresh ViewModel over it
        // does not throw and does not itself compute or surface a validation status — Validate()
        // must be an explicit, separate call the view makes only when it wants to display status.
        CoreState.Config!["femapcreator_path"] = @"C:\definitely\not\a\real\path.exe";
        CoreState.Config!.Save();

        CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
        var vm = new OptionsViewModel();
        vm.Load(); // must not throw, must not launch a process, must not touch the network

        Assert.Equal(@"C:\definitely\not\a\real\path.exe", vm.FEMapCreatorPath);
    }

    [Fact]
    public void ClearAssetsRoot_ThenSave_PersistsEmptyValue()
    {
        string exe = MakeFile("FEMapCreator.exe");
        string assets = MakeDir("assets");

        var vm = new OptionsViewModel();
        vm.Load();
        vm.FEMapCreatorPath = exe;
        vm.FEMapCreatorAssetsRoot = assets;
        vm.Save();

        // Simulate the Options view's "Clear" button, then re-save.
        vm.FEMapCreatorAssetsRoot = "";
        vm.Save();

        CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
        var vm2 = new OptionsViewModel();
        vm2.Load();
        Assert.Equal(exe, vm2.FEMapCreatorPath);
        Assert.Equal("", vm2.FEMapCreatorAssetsRoot);
        Assert.Equal(FEMapCreatorSetupStatus.Configured, vm2.GetFEMapCreatorStatusSnapshot().Status);
    }

    [Fact]
    public void ConfigKeys_AreDistinctFromOtherToolPaths_NoCrossLeakage()
    {
        string exe = MakeFile("FEMapCreator.exe");

        var vm = new OptionsViewModel();
        vm.Load();
        vm.Emulator = MakeFile("emu.exe");
        vm.FEMapCreatorPath = exe;
        vm.Save();

        CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
        var vm2 = new OptionsViewModel();
        vm2.Load();
        Assert.NotEqual(vm2.Emulator, vm2.FEMapCreatorPath);
        Assert.Equal(exe, vm2.FEMapCreatorPath);
    }
}
