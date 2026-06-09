// SPDX-License-Identifier: GPL-3.0-or-later
// #1003 — wiring tests for the FE6 Map Settings editor's three inert
// placeholder buttons that are now functional: Refetch (再取得),
// Jump-to-MapEditor (マップエディタへJump), Jump-to-ExitPoint (離脱ポイントへJump).
//
// Two layers of proof:
//   1. Headless Avalonia (AvaloniaFact): construct the view and assert the
//      three wired buttons exist + are ENABLED, and the two deferred
//      placeholders (Expand list, Map style change) remain DISABLED.
//   2. Source-text scan (Fact): the three .Click += handler wirings are
//      present in the code-behind, and the three placeholder names lost their
//      IsEnabled="False" in the .axaml (while the two deferred ones keep it).
//      This guards CI even if the headless runtime regresses.
using System;
using System.IO;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapSettingFE6JumpWiringTests
    {
        // ---- Layer 1: headless construction + button enabled-state ----

        [AvaloniaFact]
        public void MapSettingFE6View_WiredButtons_Exist_And_Enabled()
        {
            var view = new MapSettingFE6View();

            var refetch = view.FindControl<Button>("RefetchButton");
            var jumpMap = view.FindControl<Button>("JumpMapEditorButton");
            var jumpExit = view.FindControl<Button>("JumpExitPointButton");

            Assert.NotNull(refetch);
            Assert.NotNull(jumpMap);
            Assert.NotNull(jumpExit);

            Assert.True(refetch!.IsEnabled, "RefetchButton must be enabled (wired).");
            Assert.True(jumpMap!.IsEnabled, "JumpMapEditorButton must be enabled (wired).");
            Assert.True(jumpExit!.IsEnabled, "JumpExitPointButton must be enabled (wired).");
        }

        [AvaloniaFact]
        public void MapSettingFE6View_DeferredPlaceholders_Remain_Disabled()
        {
            var view = new MapSettingFE6View();

            var expand = view.FindControl<Button>("ExpandListPlaceholder");
            var mapStyle = view.FindControl<Button>("MapStyleChangePlaceholder");

            Assert.NotNull(expand);
            Assert.NotNull(mapStyle);

            Assert.False(expand!.IsEnabled, "ExpandListPlaceholder must stay disabled (deferred).");
            Assert.False(mapStyle!.IsEnabled, "MapStyleChangePlaceholder must stay disabled (deferred).");
        }

        // ---- Layer 2: source-text wiring scan (CI-safe, no runtime) ----

        private static string FindProjectRoot()
        {
            // Try the build-output dir first, then the current working dir —
            // some CI runners/layouts launch the test host from a different CWD,
            // so a single starting point is brittle (matches sibling tests).
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

        [Fact]
        public void CodeBehind_Wires_Three_Click_Handlers()
        {
            string src = File.ReadAllText(Path.Combine(ViewsDir(), "MapSettingFE6View.axaml.cs"));
            Assert.Contains("RefetchButton.Click += OnRefetchClick", src);
            Assert.Contains("JumpMapEditorButton.Click += OnJumpMapEditorClick", src);
            Assert.Contains("JumpExitPointButton.Click += OnJumpExitPointClick", src);
            // Handlers themselves exist.
            Assert.Contains("void OnRefetchClick(", src);
            Assert.Contains("void OnJumpMapEditorClick(", src);
            Assert.Contains("void OnJumpExitPointClick(", src);
            // Refetch must RESELECT the kept address, not row 0.
            Assert.Contains("EntryList.SelectAddress(keep)", src);
        }

        [Fact]
        public void Axaml_Wired_Buttons_Lost_IsEnabledFalse_DeferredKeepIt()
        {
            string xaml = File.ReadAllText(Path.Combine(ViewsDir(), "MapSettingFE6View.axaml"));

            // The three wired buttons are renamed (no longer *Placeholder) and
            // do not carry IsEnabled="False".
            Assert.Contains("Name=\"RefetchButton\"", xaml);
            Assert.Contains("Name=\"JumpMapEditorButton\"", xaml);
            Assert.Contains("Name=\"JumpExitPointButton\"", xaml);
            Assert.DoesNotContain("Name=\"RefetchPlaceholder\"", xaml);
            Assert.DoesNotContain("Name=\"JumpMapEditorPlaceholder\"", xaml);
            Assert.DoesNotContain("Name=\"JumpExitPointPlaceholder\"", xaml);

            // Scope the disabled-state assertions to each <Button> ELEMENT (not
            // a single source line) so a harmless attribute-wrapping reformat
            // can't spuriously fail the test (Copilot bot review on #1086).
            // [^>]* matches across newlines, so this captures the full opening
            // tag even when attributes wrap.
            var buttonTags = System.Text.RegularExpressions.Regex.Matches(xaml, @"<Button\b[^>]*?/?>");
            bool sawExpand = false, sawMapStyle = false, sawRefetch = false, sawJumpMap = false, sawJumpExit = false;
            foreach (System.Text.RegularExpressions.Match m in buttonTags)
            {
                string el = m.Value;
                // Deferred placeholders MUST keep IsEnabled="False".
                if (el.Contains("Name=\"ExpandListPlaceholder\"")) { sawExpand = true; Assert.Contains("IsEnabled=\"False\"", el); }
                if (el.Contains("Name=\"MapStyleChangePlaceholder\"")) { sawMapStyle = true; Assert.Contains("IsEnabled=\"False\"", el); }
                // Wired buttons MUST NOT carry IsEnabled="False".
                if (el.Contains("Name=\"RefetchButton\"")) { sawRefetch = true; Assert.DoesNotContain("IsEnabled=\"False\"", el); }
                if (el.Contains("Name=\"JumpMapEditorButton\"")) { sawJumpMap = true; Assert.DoesNotContain("IsEnabled=\"False\"", el); }
                if (el.Contains("Name=\"JumpExitPointButton\"")) { sawJumpExit = true; Assert.DoesNotContain("IsEnabled=\"False\"", el); }
            }
            // Guard against the regex silently matching nothing.
            Assert.True(sawExpand && sawMapStyle && sawRefetch && sawJumpMap && sawJumpExit,
                "All five Button elements must be found by the element scan.");

            // AutomationIds preserved for all five buttons.
            Assert.Contains("MapSettingFE6_Refetch_Button", xaml);
            Assert.Contains("MapSettingFE6_JumpMapEditor_Button", xaml);
            Assert.Contains("MapSettingFE6_JumpExitPoint_Button", xaml);
            Assert.Contains("MapSettingFE6_ExpandList_Button", xaml);
            Assert.Contains("MapSettingFE6_MapStyleChange_Button", xaml);
        }
    }
}
