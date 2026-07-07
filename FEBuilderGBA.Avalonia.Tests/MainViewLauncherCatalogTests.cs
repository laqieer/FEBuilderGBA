// SPDX-License-Identifier: GPL-3.0-or-later
// #1891 — the single-view (WebAssembly / Android) launcher must show the FULL editor catalog,
// not the old 9-editor stub. This headless test loads a synthetic FE8U ROM, builds the MainView
// shell, and asserts the launcher root renders 100+ catalog editor buttons (Main_Launcher_*),
// including editors that were previously unreachable, and that ROM-version gating matches the
// desktop (FE8-only categories shown on FE8; FE6-specific editors hidden).
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public class MainViewLauncherCatalogTests
{
    [AvaloniaFact]
    public async Task Launcher_shows_full_catalog_with_version_gating_for_FE8U()
    {
        var originalService = WindowManager.Instance.Service;
        var prevBase = CoreState.BaseDirectory;
        var prevRom = CoreState.ROM;
        var prevAsm = CoreState.AsmMapFileAsmCache;
        var prevEnc = CoreState.SystemTextEncoder;
        var prevTid = CoreState.UseTextIDCache;
        var prevSkill = CoreState.SkillNameResolver;
        var prevExport = CoreState.ExportFunction;
        var prevUndo = CoreState.Undo;
        var prevEvent = CoreState.EventScript;
        try
        {
            var bytes = new byte[0x1000000];
            Encoding.ASCII.GetBytes("BE8E01").CopyTo(bytes, 0xAC); // FE8U header
            var rom = new ROM();
            using (var stream = new MemoryStream(bytes))
            {
                var (ok, _) = await rom.LoadFromStreamAsync(stream, "synthetic-fe8u.gba");
                Assert.True(ok);
            }

            CoreState.BaseDirectory = AppContext.BaseDirectory;
            RomFileService.InitializeLoadedRom(rom);
            WindowManager.Instance.SetService(new AndroidNavigationService());

            var view = new MainView { Width = 420, Height = 900 };
            Assert.NotNull(view);

            var host = (INavigationHost)WindowManager.Instance.Service;
            var launcher = Assert.IsAssignableFrom<Control>(host.CurrentContent);

            var launcherButtons = launcher.GetLogicalDescendants()
                .OfType<Button>()
                .Select(b => AutomationProperties.GetAutomationId(b))
                .Where(id => !string.IsNullOrEmpty(id) && id!.StartsWith("Main_Launcher_", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            // The old launcher had 9 editors; the catalog exposes the full desktop set (200+).
            Assert.True(launcherButtons.Count > 100,
                $"Launcher shows only {launcherButtons.Count} editors — expected the full catalog.");

            // Editors that were previously unreachable on the web app are now present.
            Assert.Contains("Main_Launcher_HexEditor_Button", launcherButtons);
            Assert.Contains("Main_Launcher_AIScript_Button", launcherButtons);
            Assert.Contains("Main_Launcher_EventScript_Button", launcherButtons);

            // FE8-only categories are shown on an FE8 ROM.
            Assert.Contains("Main_Launcher_MonsterProbabilityViewer_Button", launcherButtons);

            // Version gating: an FE6-specific editor is hidden on an FE8 ROM (mirrors desktop).
            Assert.DoesNotContain("Main_Launcher_ClassFE6_Button", launcherButtons);
        }
        finally
        {
            WindowManager.Instance.SetService(originalService);
            CoreState.BaseDirectory = prevBase;
            CoreState.ROM = prevRom;
            CoreState.AsmMapFileAsmCache = prevAsm;
            CoreState.SystemTextEncoder = prevEnc;
            CoreState.UseTextIDCache = prevTid;
            CoreState.SkillNameResolver = prevSkill;
            CoreState.ExportFunction = prevExport;
            CoreState.Undo = prevUndo;
            CoreState.EventScript = prevEvent;
            PatchDetectionService.Instance.Refresh();
        }
    }

    [AvaloniaFact]
    public void Launcher_without_rom_shows_hint_and_no_editor_buttons()
    {
        var originalService = WindowManager.Instance.Service;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            WindowManager.Instance.SetService(new AndroidNavigationService());

            var view = new MainView { Width = 420, Height = 900 };
            var host = (INavigationHost)WindowManager.Instance.Service;
            var launcher = Assert.IsAssignableFrom<Control>(host.CurrentContent);

            var launcherButtons = launcher.GetLogicalDescendants()
                .OfType<Button>()
                .Count(b => (AutomationProperties.GetAutomationId(b) ?? "").StartsWith("Main_Launcher_", StringComparison.Ordinal));
            Assert.Equal(0, launcherButtons);
        }
        finally
        {
            WindowManager.Instance.SetService(originalService);
            CoreState.ROM = prevRom;
        }
    }
}
