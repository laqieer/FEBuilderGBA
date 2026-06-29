// SPDX-License-Identifier: GPL-3.0-or-later
// #1607 — wiring tests for the Status Screen Option (Game Option) editor's new
// List Expand affordance (Data Expansion): a warning-gated table expand that grows
// the 44-byte status_game_option table and repoints all references
// (StatusGameOptionCore.ExpandGameOptionTable).
//
// Two layers of proof:
//   1. Headless Avalonia (AvaloniaFact): construct the view and assert the
//      Expand button exists, is ENABLED, and carries the expected label.
//   2. Source-text scan (Fact): the .Click += wiring + the Core orchestrator
//      route under one undo scope are present in the code-behind, the .axaml
//      carries the button + its AutomationId, and BOTH list enumerators were
//      widened past the old 64 cap to 0x100.
using System;
using System.IO;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class StatusOptionExpandTests
    {
        // ---- Layer 1: headless construction + button enabled-state ----

        [AvaloniaFact]
        public void StatusOptionView_ExpandListButton_Exists_Enabled_Labelled()
        {
            var view = new StatusOptionView();

            var expand = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(expand);
            Assert.True(expand!.IsEnabled, "ExpandListButton must be enabled (wired in #1607).");
            // #1691: AvaloniaFact constructs the view WITHOUT .Show(), so the Opened
            // handler (ViewTranslationHelper.TranslateAll) never fires; Content stays
            // the raw AXAML literal, which is now the English source "Data Expansion".
            Assert.Equal("Data Expansion", expand!.Content);
        }

        // ---- Layer 2: source-text wiring scan (CI-safe, no runtime) ----

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

        private static string Repo(params string[] parts)
            => Path.Combine(FindProjectRoot(), Path.Combine(parts));

        [Fact]
        public void CodeBehind_Wires_ExpandHandler_Through_Core_Orchestrator()
        {
            string src = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "Views", "StatusOptionView.axaml.cs"));
            Assert.Contains("ExpandListButton.Click += OnExpandListClick", src);
            Assert.Contains("void OnExpandListClick(", src);
            // The expand handler must route through the Core orchestrator under
            // one undo scope, with a warning-gate confirm first.
            Assert.Contains("StatusGameOptionCore.ExpandGameOptionTable", src);
            Assert.Contains("_undoService.Begin(\"Expand Game Options\")", src);
            Assert.Contains("CoreState.Services?.ShowYesNo", src);
        }

        [Fact]
        public void Axaml_Has_ExpandButton_With_AutomationId()
        {
            string xaml = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "Views", "StatusOptionView.axaml"));
            Assert.Contains("Name=\"ExpandListButton\"", xaml);
            Assert.Contains("StatusOption_ExpandList_Button", xaml);

            // The Expand button must not be disabled.
            var buttonTags = System.Text.RegularExpressions.Regex.Matches(xaml, @"<Button\b[^>]*?/?>");
            bool sawExpand = false;
            foreach (System.Text.RegularExpressions.Match m in buttonTags)
            {
                if (m.Value.Contains("Name=\"ExpandListButton\""))
                {
                    sawExpand = true;
                    Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
                }
            }
            Assert.True(sawExpand, "The ExpandListButton element must be found by the element scan.");
        }

        [Fact]
        public void List_Enumerators_Widened_Past_64()
        {
            // #1607 finding #2 — both Status Option list enumerators must enumerate
            // up to 0x100 (matching StructExportCore.status_options) so rows added
            // by the expand past 64 are visible / parity-checked, with no remaining
            // 64-row cap.
            string vm = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "ViewModels", "StatusOptionViewModel.cs"));
            string parity = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs"));

            Assert.Contains("for (int i = 0; i < 0x100; i++)", vm);
            Assert.DoesNotContain("for (int i = 0; i < 64; i++)", vm);

            // ListParityHelper has many builders; assert the StatusOption builder
            // body specifically carries the 0x100 cap (between its method header
            // and the next builder) and no longer the 64 cap there.
            int start = parity.IndexOf("static List<AddrResult> BuildStatusOptionList(", StringComparison.Ordinal);
            Assert.True(start >= 0, "BuildStatusOptionList must exist.");
            int next = parity.IndexOf("static List<AddrResult> ", start + 10, StringComparison.Ordinal);
            string body = next > start ? parity.Substring(start, next - start) : parity.Substring(start);
            Assert.Contains("for (int i = 0; i < 0x100; i++)", body);
            Assert.DoesNotContain("for (int i = 0; i < 64; i++)", body);
        }
    }
}
