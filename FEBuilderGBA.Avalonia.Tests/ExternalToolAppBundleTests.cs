// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class ExternalToolAppBundleTests : IDisposable
{
    readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "test-output",
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
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
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
    public void ExternalToolPicker_UsesFileSelectionForExecutablesAndAppBundles()
    {
        var options = FileDialogHelper.CreateExternalToolOpenOptions("Select File");

        Assert.Equal("Select File", options.Title);
        Assert.False(options.AllowMultiple);
        Assert.NotNull(options.FileTypeFilter);
        Assert.Collection(options.FileTypeFilter!,
            executables =>
            {
                Assert.Equal("Executables", executables.Name);
                Assert.Equal(new[] { "*.exe", "*.app", "*" }, executables.Patterns);
            },
            allFiles =>
            {
                Assert.Equal("All Files", allFiles.Name);
                Assert.Equal(new[] { "*" }, allFiles.Patterns);
            });
    }

    [AvaloniaFact]
    public void OptionsView_BrowseButtons_RouteTagsThroughAxamlWiring()
    {
        var actualRoutes = LoadOptionsViewBrowseRoutes();
        var expectedRoutes = ExpectedOptionsViewBrowseRoutes()
            .OrderBy(route => route.Tag, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedRoutes, actualRoutes);

        var view = new OptionsView();
        var host = new Window { Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            foreach ((string targetName, _) in actualRoutes)
                Assert.NotNull(view.FindControl<TextBox>(targetName));
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void RawMacExecutablePath_RemainsUnchangedAtLaunchBoundary()
    {
        string executable = Path.Combine(_root, "mGBA");
        File.WriteAllText(executable, "not executed");

        Assert.True(MainWindow.TryResolveExternalToolForLaunch(
            executable, isMacOS: true, File.Exists, out string resolved));
        Assert.Equal(executable, resolved);
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

    static (string Tag, string Click)[] LoadOptionsViewBrowseRoutes()
    {
        string axaml = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "OptionsView.axaml"));
        XDocument document = XDocument.Parse(axaml);
        XNamespace ns = document.Root!.Name.Namespace;
        var routes = new List<(string Tag, string Click)>();

        foreach (XElement button in document.Descendants(ns + "Button"))
        {
            if (!string.Equals((string?)button.Attribute("Content"), "Browse...", StringComparison.Ordinal))
                continue;

            string automationId = (string?)button.Attribute("AutomationProperties.AutomationId") ?? "<unnamed>";
            string? tag = (string?)button.Attribute("Tag");
            Assert.False(string.IsNullOrWhiteSpace(tag), $"{automationId} is missing Tag.");
            string? click = (string?)button.Attribute("Click");
            Assert.False(string.IsNullOrWhiteSpace(click), $"{automationId} is missing Click.");
            routes.Add((tag!, click!));
        }

        return routes
            .OrderBy(route => route.Tag, StringComparer.Ordinal)
            .ToArray();
    }

    static IEnumerable<(string Tag, string Click)> ExpectedOptionsViewBrowseRoutes()
    {
        yield return ("BinaryEditorTextBox", "BrowseFile_Click");
        yield return ("DevkitproEabiTextBox", "BrowseFile_Click");
        yield return ("Emulator2TextBox", "BrowseFile_Click");
        yield return ("EmulatorTextBox", "BrowseFile_Click");
        yield return ("EventAssemblerTextBox", "BrowseFile_Click");
        yield return ("FEMapCreatorAssetsRootTextBox", "BrowseFolder_Click");
        yield return ("FEMapCreatorPathTextBox", "BrowseFile_Click");
        yield return ("FeclibTextBox", "BrowseFile_Click");
        yield return ("GitPathTextBox", "BrowseFile_Click");
        yield return ("GbaMusRiperTextBox", "BrowseFile_Click");
        yield return ("GoldroadAsmTextBox", "BrowseFile_Click");
        yield return ("Mid2agbTextBox", "BrowseFile_Click");
        yield return ("Midfix4agbTextBox", "BrowseFile_Click");
        yield return ("Program1TextBox", "BrowseFile_Click");
        yield return ("Program2TextBox", "BrowseFile_Click");
        yield return ("Program3TextBox", "BrowseFile_Click");
        yield return ("Python3TextBox", "BrowseFile_Click");
        yield return ("RetdecTextBox", "BrowseFile_Click");
        yield return ("SappyTextBox", "BrowseFile_Click");
        yield return ("SoxTextBox", "BrowseFile_Click");
        yield return ("SrccodeDirectoryTextBox", "BrowseFolder_Click");
        yield return ("SrccodeTexteditorTextBox", "BrowseFile_Click");
    }

    static string ReadRepoFile(string relativePath)
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found at {path}");
        return File.ReadAllText(path);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (FEBuilderGBA.sln).");
    }
}
