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
        // EventScriptView: fixed-window + stable-combobox fixes (#1714, #1716)
        // ===================================================================

        [AvaloniaFact]
        public void EventScriptView_CanInstantiate()
        {
            var v = new EventScriptView();
            Assert.NotNull(v.Content);
        }

        [AvaloniaFact]
        public void EventScriptView_CatalogCombo_Resolved()
        {
            var v = new EventScriptView();
            var combo = v.FindControl<ComboBox>("CatalogCombo");
            Assert.NotNull(combo);
        }
    }
}
