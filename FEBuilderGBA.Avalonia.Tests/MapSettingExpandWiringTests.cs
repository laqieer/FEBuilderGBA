// SPDX-License-Identifier: GPL-3.0-or-later
// #1603 — wiring tests for the chapter-list "Expand List" (リストの拡張) button
// added to the Avalonia FE7 / FE7U / FE8 Map Settings editors.
//
// The FE6 view already had this affordance (#1085, wired onto the
// version-agnostic Core helper MapSettingCore.ExpandMapSettingTable). The FE7,
// FE7U, and FE8 editors lacked it. This PR copies the FE6 wiring onto all three.
//
// Three layers of proof:
//   1. AXAML source scan — each view's .axaml carries the per-view
//      *_ExpandList_Button AutomationId AND the literal リストの拡張.
//   2. Code-behind source scan — each .axaml.cs wires the Click handler and
//      routes through MapSettingCore.ExpandMapSettingTable under one
//      UndoService scope (Begin + Rollback present). FE7/FE7U use _vm.LoadList()
//      + EntryList; FE8 uses the distinct _vm.LoadMapSettingList() + MapList.
//   3. Headless Avalonia (AvaloniaFact) — construct each view and assert the
//      ExpandListButton exists, is visible, and is enabled.
using System;
using System.IO;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapSettingExpandWiringTests
    {
        // ---- path helpers (mirror MapSettingFE6JumpWiringTests) ----

        private static string FindProjectRoot()
        {
            foreach (var start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                string dir = start;
                for (int i = 0; i < 12; i++)
                {
                    if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                        return dir;
                    string? parent = Path.GetDirectoryName(dir);
                    if (parent == null || parent == dir) break;
                    dir = parent;
                }
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        private static string ViewsDir()
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views");

        private static string ReadAxaml(string file)
            => File.ReadAllText(Path.Combine(ViewsDir(), file));

        private static string ReadCode(string file)
            => File.ReadAllText(Path.Combine(ViewsDir(), file));

        // ---- Layer 1: AXAML carries the button id + Japanese literal ----

        [Theory]
        [InlineData("MapSettingFE7View.axaml", "MapSettingFE7_ExpandList_Button")]
        [InlineData("MapSettingFE7UView.axaml", "MapSettingFE7U_ExpandList_Button")]
        [InlineData("MapSettingView.axaml", "MapSetting_ExpandList_Button")]
        public void Axaml_HasExpandButton_AndJapaneseLiteral(string axamlFile, string buttonId)
        {
            string xaml = ReadAxaml(axamlFile);
            Assert.Contains(buttonId, xaml);
            Assert.Contains("リストの拡張", xaml);
            // The button must be named so the code-behind can wire it.
            Assert.Contains("Name=\"ExpandListButton\"", xaml);
        }

        // ---- Layer 2: code-behind wires the handler through the Core helper ----

        [Theory]
        [InlineData("MapSettingFE7View.axaml.cs")]
        [InlineData("MapSettingFE7UView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        public void CodeBehind_WiresExpand_ThroughCoreHelper_UnderUndoScope(string codeFile)
        {
            string code = ReadCode(codeFile);
            Assert.Contains("ExpandListButton.Click += OnExpandListClick", code);
            Assert.Contains("void OnExpandListClick(", code);
            Assert.Contains("MapSettingCore.ExpandMapSettingTable", code);
            Assert.Contains("_undoService.Begin", code);
            Assert.Contains("_undoService.Rollback", code);
            // Dialogs namespace needed for NumberInputDialog.
            Assert.Contains("using FEBuilderGBA.Avalonia.Dialogs;", code);
        }

        // The list-expand UI caps the new count at 255 (WF convention). When the
        // table is already at/over that cap, the handler must bail BEFORE opening
        // NumberInputDialog -- otherwise it would build an invalid (min > max)
        // numeric range (min=current, max=255). (Copilot PR #1615 review.) The
        // early guard must sit before the NumberInputDialog.Show call.
        [Theory]
        [InlineData("MapSettingFE7View.axaml.cs")]
        [InlineData("MapSettingFE7UView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        public void CodeBehind_GuardsAgainstMaxCount_BeforeDialog(string codeFile)
        {
            string code = ReadCode(codeFile);
            Assert.Contains("if (current >= 255)", code);
            int guardIdx = code.IndexOf("if (current >= 255)", StringComparison.Ordinal);
            int dialogIdx = code.IndexOf("NumberInputDialog.Show", StringComparison.Ordinal);
            Assert.True(guardIdx >= 0 && dialogIdx >= 0,
                "Both the max-count guard and the NumberInputDialog.Show call must be present.");
            Assert.True(guardIdx < dialogIdx,
                "The current >= 255 guard must come BEFORE NumberInputDialog.Show so an " +
                "invalid (min > max) range is never constructed.");
        }

        [Theory]
        [InlineData("MapSettingFE7View.axaml.cs")]
        [InlineData("MapSettingFE7UView.axaml.cs")]
        public void CodeBehind_FE7Variants_UseEntryList_AndLoadList(string codeFile)
        {
            string code = ReadCode(codeFile);
            // FE7/FE7U share the EntryList control + the _vm.LoadList() count path.
            Assert.Contains("EntryList", code);
            Assert.Contains("_vm.LoadList()", code);
        }

        [Fact]
        public void CodeBehind_FE8_UsesMapList_AndLoadMapSettingList()
        {
            // FE8 (MapSettingView) is the distinct path — it has NO LoadList()
            // alias on the VM and uses the MapList control, not EntryList.
            // (Copilot plan-review finding #2.)
            string code = ReadCode("MapSettingView.axaml.cs");
            Assert.Contains("MapList", code);
            Assert.Contains("_vm.LoadMapSettingList()", code);
        }

        // ---- Layer 3: headless construction + button enabled-state ----

        [AvaloniaFact]
        public void MapSettingFE7View_ExpandButton_Exists_Visible_Enabled()
        {
            var view = new MapSettingFE7View();
            var btn = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(btn);
            Assert.True(btn!.IsVisible, "ExpandListButton must be visible.");
            Assert.True(btn.IsEnabled, "ExpandListButton must be enabled (wired).");
        }

        [AvaloniaFact]
        public void MapSettingFE7UView_ExpandButton_Exists_Visible_Enabled()
        {
            var view = new MapSettingFE7UView();
            var btn = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(btn);
            Assert.True(btn!.IsVisible, "ExpandListButton must be visible.");
            Assert.True(btn.IsEnabled, "ExpandListButton must be enabled (wired).");
        }

        [AvaloniaFact]
        public void MapSettingView_ExpandButton_Exists_Visible_Enabled()
        {
            var view = new MapSettingView();
            var btn = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(btn);
            Assert.True(btn!.IsVisible, "ExpandListButton must be visible.");
            Assert.True(btn.IsEnabled, "ExpandListButton must be enabled (wired).");
        }
    }
}
