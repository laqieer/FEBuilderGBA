// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class RecentFileProjectModeTests
{
    sealed class MainWindowHarness : IDisposable
    {
        readonly Window? _previousMainWindow = WindowManager.Instance.MainWindow;

        public MainWindow Window { get; } = new();

        public void Dispose()
        {
            try { Window.Close(); } catch { }
            WindowManager.Instance.MainWindow = _previousMainWindow;
        }
    }

    static string NewTestDir()
    {
        string dir = Path.Combine(
            AppContext.BaseDirectory,
            "recent_project_mode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string CreateDecompRom(string root)
    {
        string outputDir = Path.Combine(root, "build", "release");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(
            Path.Combine(root, DecompProject.ManifestFileName),
            "{ \"schemaVersion\": 1, \"builtRom\": \"build/release/game.gba\" }");
        string romPath = Path.Combine(outputDir, "game.gba");
        File.WriteAllBytes(romPath, new byte[] { 0, 1, 2, 3 });
        return romPath;
    }

    [AvaloniaFact]
    public void RecentLoad_DecompToPlain_ClearsProjectBeforeLoading()
    {
        string dir = NewTestDir();
        DecompProject? previousProject = CoreState.DecompProject;
        try
        {
            string plainRom = Path.Combine(dir, "plain.gba");
            File.WriteAllBytes(plainRom, new byte[] { 0, 1, 2, 3 });
            CoreState.DecompProject = new DecompProject
            {
                ProjectRoot = dir,
                BuiltRomPath = Path.Combine(dir, "old.gba"),
            };
            using var harness = new MainWindowHarness();

            bool loaded = harness.Window.LoadRecentRomFile(plainRom, path =>
            {
                Assert.Equal(plainRom, path);
                Assert.Null(CoreState.DecompProject);
                return true;
            });

            Assert.True(loaded);
            Assert.Null(CoreState.DecompProject);
        }
        finally
        {
            CoreState.DecompProject = previousProject;
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public void RecentLoad_PlainToDecomp_SetsMatchingProjectBeforeLoading()
    {
        string dir = NewTestDir();
        DecompProject? previousProject = CoreState.DecompProject;
        try
        {
            string decompRom = CreateDecompRom(dir);
            CoreState.DecompProject = null;
            using var harness = new MainWindowHarness();

            bool loaded = harness.Window.LoadRecentRomFile(decompRom, path =>
            {
                Assert.Equal(decompRom, path);
                Assert.NotNull(CoreState.DecompProject);
                Assert.Equal(Path.GetFullPath(decompRom), CoreState.DecompProject!.BuiltRomPath);
                return true;
            });

            Assert.True(loaded);
            Assert.NotNull(CoreState.DecompProject);
            Assert.Equal(Path.GetFullPath(dir), CoreState.DecompProject!.ProjectRoot);
        }
        finally
        {
            CoreState.DecompProject = previousProject;
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public void RecentLoad_FailureClearsDetectedProject()
    {
        string dir = NewTestDir();
        DecompProject? previousProject = CoreState.DecompProject;
        try
        {
            string decompRom = CreateDecompRom(dir);
            CoreState.DecompProject = null;
            using var harness = new MainWindowHarness();
            DecompProject? projectDuringLoad = null;

            bool loaded = harness.Window.LoadRecentRomFile(decompRom, _ =>
            {
                projectDuringLoad = CoreState.DecompProject;
                return false;
            });

            Assert.False(loaded);
            Assert.NotNull(projectDuringLoad);
            Assert.Null(CoreState.DecompProject);
        }
        finally
        {
            CoreState.DecompProject = previousProject;
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public void RecentLoad_ExceptionClearsDetectedProjectAndPropagates()
    {
        string dir = NewTestDir();
        DecompProject? previousProject = CoreState.DecompProject;
        try
        {
            string decompRom = CreateDecompRom(dir);
            CoreState.DecompProject = null;
            using var harness = new MainWindowHarness();

            Assert.Throws<InvalidOperationException>(() =>
                harness.Window.LoadRecentRomFile(
                    decompRom,
                    _ => throw new InvalidOperationException("load failed")));

            Assert.Null(CoreState.DecompProject);
        }
        finally
        {
            CoreState.DecompProject = previousProject;
            Directory.Delete(dir, true);
        }
    }
}
