using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for the button-overlap layout fix (#1688).
    ///
    /// macOS renders the default UI font wider than Windows, so several editors
    /// had fixed-width label columns and fixed/Auto-column multi-button rows that
    /// overflowed: label text spilled over adjacent spinners and the rightmost
    /// button in a row spilled past the edit-panel edge. The fix converts the
    /// overflowing horizontal button bars to WrapPanel (per-child Margin since
    /// Avalonia WrapPanel has no Spacing) and sizes label-vs-control columns to
    /// Auto/* so labels and spinners no longer collide.
    ///
    /// These tests prove the AXAML still loads/parses after the layout changes
    /// (a malformed WrapPanel/Grid throws at InitializeComponent), and that the
    /// representative controls/handlers that moved survived (by Name). The
    /// per-view *_CanInstantiate tests assert on `v.Content` (populated by
    /// InitializeComponent) rather than `v` itself, since a constructor can only
    /// fail by throwing and never returns null — `v.Content` is a real signal
    /// that the visual tree was built.
    /// </summary>
    public class ButtonOverlapLayoutTests
    {
        // ===================================================================
        // Each changed view still instantiates (AXAML parses + loads)
        // ===================================================================

        [AvaloniaFact]
        public void UnitPaletteView_CanInstantiate()
        {
            var v = new UnitPaletteView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void MapTileAnimation2View_CanInstantiate()
        {
            var v = new MapTileAnimation2View();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void EventMapChangeView_CanInstantiate()
        {
            var v = new EventMapChangeView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void ImageBattleAnimePalletView_CanInstantiate()
        {
            var v = new ImageBattleAnimePalletView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void ImageBattleScreenView_CanInstantiate()
        {
            var v = new ImageBattleScreenView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillConfigSkillSystemView_CanInstantiate()
        {
            var v = new SkillConfigSkillSystemView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillConfigFE8UCSkillSys09xView_CanInstantiate()
        {
            var v = new SkillConfigFE8UCSkillSys09xView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillConfigFE8NSkillView_CanInstantiate()
        {
            var v = new SkillConfigFE8NSkillView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillConfigFE8NVer2SkillView_CanInstantiate()
        {
            var v = new SkillConfigFE8NVer2SkillView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillConfigFE8NVer3SkillView_CanInstantiate()
        {
            var v = new SkillConfigFE8NVer3SkillView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillAssignmentClassSkillSystemView_CanInstantiate()
        {
            var v = new SkillAssignmentClassSkillSystemView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void SkillAssignmentClassCSkillSysView_CanInstantiate()
        {
            var v = new SkillAssignmentClassCSkillSysView();
            Assert.NotNull(v.Content);
        }

        // ===================================================================
        // The GBA Color row's control survived the column-definition change
        // (40,* -> Auto,120). Proves the "GBA Color"-labelled NUD is intact.
        // ===================================================================

        [AvaloniaFact]
        public void MapTileAnimation2View_GbaColorBox_Survived()
        {
            var v = new MapTileAnimation2View();
            var box = v.FindControl<NumericUpDown>("NGbaBox");
            Assert.NotNull(box);
        }

        // ===================================================================
        // Action buttons that moved into a WrapPanel survived (by Name).
        // FERepoButton was a Grid.Column child re-parented into the icon-row
        // WrapPanel; assert it still resolves.
        // ===================================================================

        [AvaloniaFact]
        public void SkillConfigSkillSystemView_FERepoButton_Survived()
        {
            var v = new SkillConfigSkillSystemView();
            var btn = v.FindControl<Button>("FERepoButton");
            Assert.NotNull(btn);
        }

        // ===================================================================
        // EventMapChange Write button survived the StackPanel -> WrapPanel
        // conversion of the row-1 address bar.
        // ===================================================================

        [AvaloniaFact]
        public void EventMapChangeView_WriteButton_Survived()
        {
            var v = new EventMapChangeView();
            var btn = v.FindControl<Button>("WriteButton");
            Assert.NotNull(btn);
        }

        // ===================================================================
        // EventScriptView + ProcsScriptView: fixed-window + stable-combobox
        // fixes (#1714, #1716). These assert the actual AXAML contract so a
        // SizeToContent regression or a dropped fixed-width dropdown style is
        // caught — not just that the view instantiates.
        // ===================================================================

        [AvaloniaFact]
        public void EventScriptView_CanInstantiate()
        {
            var v = new EventScriptView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void EventScriptView_WindowIsFixedSize_NotSizeToContent()
        {
            var v = new EventScriptView();
            // #1714: SizeToContent must stay Manual so the window does not
            // auto-shrink (macOS black-strip repaint) after deleting a command.
            Assert.Equal(global::Avalonia.Controls.SizeToContent.Manual, v.SizeToContent);
            // A minimum floor keeps the fixed-size window usable (no drag-to-tiny).
            Assert.True(v.MinWidth >= 1180);
            Assert.True(v.MinHeight >= 780);
        }

        [AvaloniaFact]
        public void ProcsScriptView_WindowIsFixedSize_NotSizeToContent()
        {
            // ProcsScriptView shares the EventScriptView layout/engine — the #1714
            // fixed-size fix must hold on the sibling view too.
            var v = new ProcsScriptView();
            Assert.Equal(global::Avalonia.Controls.SizeToContent.Manual, v.SizeToContent);
            Assert.True(v.MinWidth >= 1180);
            Assert.True(v.MinHeight >= 780);
        }

        [AvaloniaFact]
        public void EventScriptView_CatalogCombo_HasFixedWidthDropdownStyle()
        {
            var v = new EventScriptView();
            var combo = v.FindControl<ComboBox>("CatalogCombo");
            Assert.NotNull(combo);
            AssertFixedWidthDropdown(combo!);
        }

        [AvaloniaFact]
        public void ProcsScriptView_CatalogCombo_HasFixedWidthDropdownStyle()
        {
            var v = new ProcsScriptView();
            var combo = v.FindControl<ComboBox>("CatalogCombo");
            Assert.NotNull(combo);
            // ProcsScriptView shares the same catalog engine — the #1716 fix
            // must be applied here too.
            AssertFixedWidthDropdown(combo!);
        }

        // #1716: the CatalogCombo dropdown must pin every item to a FIXED width so
        // the popup doesn't snap between sizes while scrolling. Verify a ComboBoxItem
        // style actually sets MinWidth == MaxWidth == 900, plus an ellipsis ItemTemplate.
        // (Asserts the setters' VALUES, not just style count — adding another local
        // style must not break this, and the width contract is checked.)
        static void AssertFixedWidthDropdown(ComboBox combo)
        {
            Assert.NotNull(combo.ItemTemplate);
            // Only the ComboBoxItem-targeting style governs the dropdown width — an
            // unrelated local style (e.g. on a nested TextBlock) must not affect this.
            var setters = combo.Styles
                .OfType<global::Avalonia.Styling.Style>()
                .Where(s => s.Selector?.ToString()?.Contains("ComboBoxItem") == true)
                .SelectMany(s => s.Setters)
                .OfType<global::Avalonia.Styling.Setter>()
                .ToList();
            var minSetters = setters.Where(s => s.Property == global::Avalonia.Layout.Layoutable.MinWidthProperty).ToList();
            var maxSetters = setters.Where(s => s.Property == global::Avalonia.Layout.Layoutable.MaxWidthProperty).ToList();
            // At least one of each, and EVERY Min/Max width setter pins 900 — so a
            // later style can't silently override the dropdown width to a different value.
            Assert.NotEmpty(minSetters);
            Assert.NotEmpty(maxSetters);
            Assert.All(minSetters, s => Assert.Equal(900d, s.Value));
            Assert.All(maxSetters, s => Assert.Equal(900d, s.Value));
        }
    }
}
