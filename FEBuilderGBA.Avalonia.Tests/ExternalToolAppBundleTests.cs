// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class ExternalToolAppBundleTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "avalonia_external_tool_" + Guid.NewGuid().ToString("N"));
    readonly Config? _savedConfig = CoreState.Config;
    readonly string? _savedBaseDirectory = CoreState.BaseDirectory;

    public ExternalToolAppBundleTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        CoreState.BaseDirectory = _root;
        CoreState.Config = Config.LoadOrCreate(
            Path.Combine(_root, "config", "config.xml"));
    }

    public void Dispose()
    {
        CoreState.Config = _savedConfig;
        CoreState.BaseDirectory = _savedBaseDirectory;
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AllLaunchBoundariesResolveTheSameBundleExecutable()
    {
        string bundle = CreateBundle();
        string expected = Path.Combine(bundle, "Contents", "MacOS", "mGBA");

        Assert.True(MainWindow.TryResolveExternalToolForLaunch(
            bundle, isMacOS: true, _ => true, out string mainPath));
        Assert.True(ToolDiffDebugSelectView.TryResolveEmulatorForLaunch(
            bundle, isMacOS: true, _ => true, out string diffPath));
        Assert.True(ToolInitWizardViewModel.IsConfiguredExternalTool(
            bundle, isMacOS: true, _ => true));

        Assert.Equal(expected, mainPath);
        Assert.Equal(expected, diffPath);
    }

    [Fact]
    public void OptionsSave_PreservesOriginalBundlePath()
    {
        string bundle = CreateBundle();
        var vm = new OptionsViewModel();
        vm.Load();
        vm.Emulator = bundle;
        vm.Save();

        Assert.Equal(bundle, CoreState.Config?.at("emulator"));

        var reopened = new OptionsViewModel();
        reopened.Load();
        Assert.Equal(bundle, reopened.Emulator);
        Assert.Equal(Path.GetDirectoryName(bundle), Path.GetDirectoryName(reopened.Emulator));
    }

    [AvaloniaFact]
    public void OptionsView_LoadsOriginalBundlePathIntoExternalToolsTextBox()
    {
        string bundle = CreateBundle();
        CoreState.Config!["emulator"] = bundle;
        CoreState.Config.Save();
        var view = new OptionsView();
        var host = new Window { Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var emulator = Assert.IsType<TextBox>(
                view.FindControl<TextBox>("EmulatorTextBox"));
            Assert.Equal(bundle, emulator.Text);
        }
        finally
        {
            host.Close();
        }
    }

    string CreateBundle()
    {
        string bundle = Path.Combine(_root, "mGBA.app");
        string macOS = Path.Combine(bundle, "Contents", "MacOS");
        Directory.CreateDirectory(macOS);
        File.WriteAllText(Path.Combine(macOS, "mGBA"), "not executed");
        return bundle;
    }
}
