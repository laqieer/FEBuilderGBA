// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class SongTableExpansionOptionsTests : IDisposable
{
    const string ConfigKey = "func_show_song_table_extends";
    readonly Config? _savedConfig = CoreState.Config;
    readonly ROM? _savedRom = CoreState.ROM;
    readonly string? _savedBaseDirectory = CoreState.BaseDirectory;
    readonly string? _savedLanguage = CoreState.Language;
    readonly string _savedGitPath = CoreState.GitPath;
    readonly string _baseDirectory;
    readonly string _configPath;

    public SongTableExpansionOptionsTests()
    {
        _baseDirectory = Path.Combine(
            Path.GetTempPath(), $"song_table_option_{Guid.NewGuid():N}");
        _configPath = Path.Combine(_baseDirectory, "config", "config.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CoreState.BaseDirectory = _baseDirectory;
        CoreState.Config = Config.LoadOrCreate(_configPath);
        CoreState.Language = "en";
    }

    public void Dispose()
    {
        CoreStateTestState.RestoreConfig(_savedConfig);
        CoreState.ROM = _savedRom;
        CoreStateTestState.RestoreBaseDirectory(_savedBaseDirectory);
        CoreStateTestState.RestoreLanguage(_savedLanguage);
        CoreState.GitPath = _savedGitPath;
        try
        {
            if (Directory.Exists(_baseDirectory))
                Directory.Delete(_baseDirectory, true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    [Fact]
    public void Load_DefaultsFalse_AndReadsSharedWinFormsKey()
    {
        var vm = new OptionsViewModel();

        vm.Load();
        Assert.False(vm.ShowSongTableExpansion);

        CoreState.Config![ConfigKey] = "1";
        vm.Load();
        Assert.True(vm.ShowSongTableExpansion);
    }

    [Fact]
    public void Save_PersistsSharedKey_WithoutChangingUnrelatedSetting()
    {
        CoreState.Config!["unrelated_setting"] = "keep";
        var vm = new OptionsViewModel();
        vm.Load();
        vm.ShowSongTableExpansion = true;

        vm.Save();

        CoreState.Config = Config.LoadOrCreate(_configPath);
        Assert.Equal("1", CoreState.Config.at(ConfigKey));
        Assert.Equal("keep", CoreState.Config.at("unrelated_setting"));
    }

    [Fact]
    public void SavedOption_MakesVanillaSongTableExpansionVisible()
    {
        CoreState.ROM = MakeRomWithSongTable();
        var options = new OptionsViewModel();
        options.Load();
        options.ShowSongTableExpansion = true;
        options.Save();

        Assert.True(new SongTableViewModel().ShouldShowExpansion());
    }

    [AvaloniaFact]
    public void OptionsView_LoadsAndSavesExpansionCheckbox()
    {
        CoreState.Config![ConfigKey] = "1";
        var view = new OptionsView();
        InvokePrivate(view, "LoadOptions");
        CheckBox checkBox = Assert.IsType<CheckBox>(
            view.FindControl<CheckBox>("ShowSongTableExpansionCheckBox"));
        Assert.True(checkBox.IsChecked);

        checkBox.IsChecked = false;
        InvokePrivate(
            view,
            "OkButton_Click",
            null,
            new RoutedEventArgs());

        CoreState.Config = Config.LoadOrCreate(_configPath);
        Assert.Equal("0", CoreState.Config.at(ConfigKey));
    }

    static void InvokePrivate(object target, string methodName, params object?[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, args);
    }

    static ROM MakeRomWithSongTable()
    {
        const uint tableBase = 0x3000;
        var rom = new ROM();
        rom.LoadLow("song-table-option.gba", new byte[0x1000000], "BE8E01");
        WriteU32(
            rom.Data,
            (int)rom.RomInfo.sound_table_pointer,
            U.toPointer(tableBase));
        WriteU32(rom.Data, (int)tableBase, U.toPointer(0x4000));
        WriteU32(rom.Data, (int)(tableBase + 8), 0xFFFFFFFF);
        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
