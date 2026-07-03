// SPDX-License-Identifier: GPL-3.0-or-later
// #1793: generalize the #1727 fix into a single app-wide rule. Avalonia's Fluent
// Button defaults VerticalContentAlignment to Stretch, so a button taller than its
// intrinsic height renders its label above vertical centre (the #1727 "Apply"
// symptom). Instead of patching each tall button, App.axaml declares a global
//   <Style Selector="Button"><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
// which fixes every plain <Button> in one place. The exact-match `Button` selector
// leaves subclasses (CheckBox/ToggleButton/RadioButton/RepeatButton) untouched, and
// a local VerticalContentAlignment on a specific button still wins.
//
// TestApp.axaml mirrors App.axaml's global styles (#315), so these headless
// [AvaloniaFact] tests exercise the real production style. The source-scan [Fact]
// keeps the two declarations in sync.
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Presenters;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using global::Avalonia.VisualTree;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class GlobalButtonVerticalCenteringTests
    {
        // Host a bare Button in a top-level Window and realize its template so the
        // rendered content element (AccessText/TextBlock) has real Bounds.
        static Window ShowHosted(Button b)
        {
            var w = new Window { Width = 220, Height = 180, Content = b };
            w.Show();
            w.UpdateLayout();
            return w;
        }

        [AvaloniaFact]
        public void PlainButton_GetsCenterVerticalContentAlignment_FromGlobalStyle()
        {
            var b = new Button { Content = "Tall", Height = 66, Width = 150 };
            var w = ShowHosted(b);
            try
            {
                // The global `Selector="Button"` style (mirrored into TestApp.axaml)
                // overrides the Fluent default (Stretch) to Center.
                Assert.Equal(VerticalAlignment.Center, b.VerticalContentAlignment);
            }
            finally { w.Close(); }
        }

        [AvaloniaFact]
        public void TallButton_RendersContentVerticallyCentered_NotStretchedOrTop()
        {
            var b = new Button { Content = "Tall", Height = 66, Width = 150 };
            var w = ShowHosted(b);
            try
            {
                var presenter = b.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
                Assert.NotNull(presenter);
                var child = presenter!.Child;
                Assert.NotNull(child);

                double contentH = presenter.Bounds.Height;      // full content area (~66)
                Rect cb = child!.Bounds;                          // relative to the presenter
                double childMid = cb.Y + cb.Height / 2;

                // 1) Natural line height — NOT stretched to fill the 66px button.
                //    (The Fluent Stretch default gives a child ~= the full height.)
                Assert.True(cb.Height < contentH * 0.5,
                    $"content should keep its natural height (was {cb.Height:F1} of {contentH:F1}) — " +
                    "button content is stretched, i.e. NOT centered (#1793 regression).");
                // 2) Not hugging the top edge (the "glyph above centre" symptom).
                Assert.True(cb.Y > contentH * 0.25,
                    $"content top {cb.Y:F1} hugs the top of {contentH:F1} — label is above centre (#1727/#1793).");
                // 3) Actually vertically centered.
                Assert.True(System.Math.Abs(childMid - contentH / 2) < contentH * 0.2,
                    $"content mid {childMid:F1} is not near button centre {contentH / 2:F1} (#1793).");
            }
            finally { w.Close(); }
        }

        [AvaloniaFact]
        public void LocalVerticalContentAlignment_WinsOverGlobalStyle()
        {
            var b = new Button
            {
                Content = "Top",
                Height = 66,
                Width = 150,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            var w = ShowHosted(b);
            try
            {
                // A directly-set (local) value out-prioritizes the global style setter,
                // preserving any intentional per-button exception.
                Assert.Equal(VerticalAlignment.Top, b.VerticalContentAlignment);
            }
            finally { w.Close(); }
        }

        [Fact]
        public void GlobalButtonStyle_IsDeclaredIn_App_And_TestApp_InSync()
        {
            var root = FindRepoRoot();
            if (root == null) return; // packaged CI without repo checkout — nothing to scan

            var appAxaml = Path.Combine(root, "FEBuilderGBA.Avalonia", "App.axaml");
            var testAppAxaml = Path.Combine(root, "FEBuilderGBA.Avalonia.Tests", "TestApp.axaml");
            Assert.True(File.Exists(appAxaml), $"missing {appAxaml}");
            Assert.True(File.Exists(testAppAxaml), $"missing {testAppAxaml}");

            // Extract the <Style Selector="Button"> ... </Style> block from each file.
            var blockRx = new Regex("(?s)<Style\\s+Selector=\"Button\"\\s*>.*?</Style>");
            var appBlock = blockRx.Match(File.ReadAllText(appAxaml));
            var testBlock = blockRx.Match(File.ReadAllText(testAppAxaml));
            Assert.True(appBlock.Success,
                "App.axaml must declare a global <Style Selector=\"Button\"> (#1793).");
            Assert.True(testBlock.Success,
                "TestApp.axaml must mirror the global <Style Selector=\"Button\"> (#315) so " +
                "headless tests exercise the production behaviour.");

            // Each block must set exactly VerticalContentAlignment="Center".
            var setterRx = new Regex(
                "<Setter\\s+Property=\"VerticalContentAlignment\"\\s+Value=\"Center\"\\s*/>");
            Assert.True(setterRx.IsMatch(appBlock.Value),
                "App.axaml Button style must set VerticalContentAlignment=\"Center\" (#1793).");
            Assert.True(setterRx.IsMatch(testBlock.Value),
                "TestApp.axaml Button style must set VerticalContentAlignment=\"Center\" (#315).");

            // The two declarations must be IDENTICAL (whitespace-normalized) so they can't
            // silently diverge: an extra setter, a different value, or reordering in only
            // one file would break the App.axaml <-> TestApp.axaml mirror and fail here.
            static string Norm(string s) => Regex.Replace(s, "\\s+", " ").Trim();
            Assert.Equal(Norm(appBlock.Value), Norm(testBlock.Value));
        }

        private static string? FindRepoRoot()
        {
            // Walk parents to the drive root (no fixed depth cap) so a deeper/shallower
            // test output path can't silently turn this sync guard into a no-op (#1794
            // review). This parent-walk-to-FEBuilderGBA.sln pattern is the repo-root
            // convention used across the test suite. The only null case is a genuinely
            // source-less run (packaged CI), in which there is nothing to scan.
            for (DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
