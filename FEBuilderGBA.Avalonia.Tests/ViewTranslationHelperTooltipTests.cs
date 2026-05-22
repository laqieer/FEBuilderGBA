using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for ViewTranslationHelper's `ToolTip.Tip` translation path
    /// (added for issue #356).
    ///
    /// These tests use a controlled translation map (loaded via
    /// <see cref="MyTranslateResource.LoadResource"/>) with sentinel values
    /// (e.g., "[TRANSLATED-CLICK]") that cannot be confused with the English
    /// source string. This ensures assertions PROVE the helper's tooltip path
    /// actually ran — a weaker test using <c>R._("Click to open")</c> as the
    /// expected value could pass even when no translation occurred (it would
    /// just return the source string unchanged).
    ///
    /// The Copilot CLI plan review explicitly flagged this concern (
    /// "tests must prove translation actually happened"); the sentinel design
    /// is the response.
    /// </summary>
    [Collection("SharedState")]
    public class ViewTranslationHelperTooltipTests : IDisposable
    {
        readonly string _tmpDir;
        readonly string _primaryMapFile;

        public ViewTranslationHelperTooltipTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tmpDir);
            _primaryMapFile = Path.Combine(_tmpDir, "primary.txt");
            File.WriteAllText(
                _primaryMapFile,
                ":Click to open\n[TRANSLATED-CLICK]\n\n" +
                ":Help text panel\n[TRANSLATED-HELP]\n\n" +
                ":Open settings\n[TRANSLATED-OPEN-SETTINGS]\n\n");
            MyTranslateResource.LoadResource(_primaryMapFile);
        }

        public void Dispose()
        {
            // Restore the shared MyTranslateResource state so other tests in
            // the SharedState collection don't see our sentinel map.
            MyTranslateResource.Clear();
            try { Directory.Delete(_tmpDir, recursive: true); } catch { }
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesToolTipTip()
        {
            // Arrange: a Button with a string-valued ToolTip.Tip set as the
            // Avalonia attached property (this mirrors `ToolTip.Tip="..."` in
            // AXAML).
            var btn = new Button();
            ToolTip.SetTip(btn, "Click to open");

            // Act
            var helper = new ViewTranslationHelper(btn);
            helper.TranslateAll();

            // Assert: the sentinel translation replaced the source string.
            // If the helper's new ToolTip path were not invoked, the tooltip
            // would still read "Click to open" and the assertion would fail.
            Assert.Equal("[TRANSLATED-CLICK]", ToolTip.GetTip(btn));
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesNestedToolTipTip()
        {
            // Verify the helper recurses into logical children when walking
            // tooltips (the same recursion that handles Text / Header etc.).
            var outer = new StackPanel();
            var inner = new StackPanel();
            var btn = new Button();
            ToolTip.SetTip(btn, "Click to open");
            inner.Children.Add(btn);
            outer.Children.Add(inner);

            var helper = new ViewTranslationHelper(outer);
            helper.TranslateAll();

            Assert.Equal("[TRANSLATED-CLICK]", ToolTip.GetTip(btn));
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsNonStringToolTipTip()
        {
            // A control-typed tooltip (e.g. a rich popup) must NOT be unwrapped
            // or replaced by the translator — that would silently corrupt UI.
            var btn = new Button();
            var richTooltip = new TextBlock { Text = "Rich content" };
            ToolTip.SetTip(btn, richTooltip);

            var helper = new ViewTranslationHelper(btn);
            helper.TranslateAll();

            // ReferenceEquals — the original control instance is still in place.
            Assert.Same(richTooltip, ToolTip.GetTip(btn));
        }

        [AvaloniaFact]
        public void TranslateAll_LeavesUntranslatedTooltipsAsSourceString()
        {
            // When R._() finds no translation entry, it returns the source
            // string. The helper still records the entry so a future language
            // change can re-translate. Verifies the lookup doesn't crash or
            // wipe the tooltip when no match exists.
            var btn = new Button();
            ToolTip.SetTip(btn, "No translation entry exists for this string");

            var helper = new ViewTranslationHelper(btn);
            helper.TranslateAll();

            Assert.Equal("No translation entry exists for this string",
                ToolTip.GetTip(btn));
        }

        [AvaloniaFact]
        public void OnLanguageChanged_ReappliesTooltipAfterMapSwap()
        {
            // Simulate the runtime LanguageChanged signal: load a new
            // translation map AND call OnLanguageChanged. The helper should
            // re-apply translations against the new map (because the original
            // English source string was retained in the entries list).
            var btn = new Button();
            ToolTip.SetTip(btn, "Open settings");

            var helper = new ViewTranslationHelper(btn);
            helper.TranslateAll();
            Assert.Equal("[TRANSLATED-OPEN-SETTINGS]", ToolTip.GetTip(btn));

            // Swap to a different sentinel map
            string altFile = Path.Combine(_tmpDir, "alt.txt");
            File.WriteAllText(altFile, ":Open settings\n[ALT-OPEN]\n\n");
            MyTranslateResource.LoadResource(altFile);
            helper.OnLanguageChanged();

            // OnLanguageChanged coalesces via Dispatcher.UIThread.Post — pump
            // the dispatcher so the queued re-apply runs synchronously here.
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("[ALT-OPEN]", ToolTip.GetTip(btn));
            File.Delete(altFile);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesTextBlockText_WhenConverted()
        {
            // The MapSettingDifficultyDialog help text was changed from
            // <TextBox IsReadOnly=True> to <SelectableTextBlock> precisely so
            // the existing TextBlock.Text translation path catches it. This
            // test verifies the same path works with the sentinel map
            // (regression guard: don't accidentally remove TextBlock support
            // from the helper).
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "Help text panel" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("[TRANSLATED-HELP]", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesSelectableTextBlockText()
        {
            // SelectableTextBlock inherits from TextBlock in Avalonia 11+, so
            // the existing TextBlock translation path catches it. This test
            // verifies that inheritance — addresses Copilot inline review on
            // PR #458: the help-panel switch from TextBox -> SelectableTextBlock
            // (instead of plain TextBlock) preserves user copy/paste while
            // still being translatable.
            var panel = new StackPanel();
            var stb = new SelectableTextBlock { Text = "Help text panel" };
            panel.Children.Add(stb);

            // Verify inheritance assumption (compile-time check would be ideal
            // but runtime check is sufficient since the helper uses
            // `is TextBlock tb` dispatch).
            Assert.True(stb is TextBlock,
                "SelectableTextBlock must inherit from TextBlock for the helper's translation dispatch to work.");

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("[TRANSLATED-HELP]", stb.Text);
        }
    }
}
