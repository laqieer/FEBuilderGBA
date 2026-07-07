using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #988 source-scan tests: verify the Avalonia Battle Screen Layout editor's
    /// bulk Image Import/Export buttons are enabled + wired (the greyed KnownGap
    /// tooltips removed) and the bulk Redo button keeps its documented deferral.
    /// The Core seams (FEBuilderGBA.Core.Tests/ImageBattleScreenBulkImportTests)
    /// carry the behavioral coverage; these tests just guard the wire-up.
    /// </summary>
    public class ImageBattleScreenBulkWiringTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string Axaml => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "ImageBattleScreenView.axaml"));

        private string CodeBehind => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "ImageBattleScreenView.axaml.cs"));

        // -----------------------------------------------------------------
        // Bulk Import button: enabled (no IsEnabled=False), KnownGap tooltip
        // removed, Click handler wired.
        // -----------------------------------------------------------------

        [Fact]
        public void BulkImportButton_HasClickHandler()
        {
            Assert.Matches(new Regex(
                @"AutomationId=""ImageBattleScreen_BulkImport_Button""[\s\S]{0,300}Click=""BulkImport_Click"""),
                Axaml);
        }

        [Fact]
        public void BulkImportButton_OldKnownGapStubStateRemoved()
        {
            // Guards that the OLD KnownGap stub (a hard IsEnabled="False" PAIRED
            // with the WinForms-coupled ImageUtil.ImageToByteKeepTSA tooltip) is
            // gone from the BulkImport button. NOTE: the button still declares
            // IsEnabled="False" as its INITIAL state — runtime enablement is the
            // render-success gate asserted by CodeBehind_BulkButtons_GatedOnRenderSuccess
            // (BulkImportButton.IsEnabled = _vm.CanExportBattle). This test only
            // proves the WinForms-coupled KnownGap stub no longer pins the button
            // disabled with no code path to enable it.
            Assert.DoesNotMatch(new Regex(
                @"AutomationId=""ImageBattleScreen_BulkImport_Button""[\s\S]{0,200}IsEnabled=""False""[\s\S]{0,200}ImageUtil\.ImageToByteKeepTSA"),
                Axaml);
        }

        [Fact]
        public void BulkImportButton_KnownGapTooltipRemoved()
        {
            Assert.DoesNotContain(
                "KnownGap: bulk image Import requires ImageFormRef.ImportFilenameDialog", Axaml);
        }

        // -----------------------------------------------------------------
        // Bulk Export button: enabled (no IsEnabled=False), KnownGap tooltip
        // removed, Click handler wired.
        // -----------------------------------------------------------------

        [Fact]
        public void BulkExportButton_HasClickHandler()
        {
            Assert.Matches(new Regex(
                @"AutomationId=""ImageBattleScreen_BulkExport_Button""[\s\S]{0,300}Click=""BulkExport_Click"""),
                Axaml);
        }

        [Fact]
        public void BulkExportButton_KnownGapTooltipRemoved()
        {
            Assert.DoesNotContain(
                "KnownGap: bulk image Export requires ImageFormRef.ExportImage", Axaml);
        }

        // -----------------------------------------------------------------
        // Bulk Redo button: keeps its documented deferral (still disabled, tooltip
        // present). CORRECTION: this stays a documented gap.
        // -----------------------------------------------------------------

        [Fact]
        public void BulkRedoButton_KeepsDocumentedDeferral()
        {
            // The Redo button element must still carry IsEnabled="False" and a
            // tooltip explaining the deferral (main ROM-level Undo covers it).
            Assert.Matches(new Regex(
                @"AutomationId=""ImageBattleScreen_BulkRedo_Button""[\s\S]{0,300}IsEnabled=""False"""),
                Axaml);
            Assert.Contains("local TSA redo buffer is WinForms-coupled", Axaml);
        }

        // -----------------------------------------------------------------
        // Code-behind: handlers present, import wrapped in undo scope, gating in
        // RefreshBattlePreview mirrors Export PNG (CORRECTION 4).
        // -----------------------------------------------------------------

        [Fact]
        public void CodeBehind_BulkExport_UsesBattlePreviewExportPng()
        {
            var src = CodeBehind;
            Assert.Matches(new Regex(
                @"void\s+BulkExport_Click[\s\S]{0,300}BattlePreview\.ExportPng\(TopLevel\.GetTopLevel\(this\)\s+as\s+Window,\s*""battle_screen""\)",
                RegexOptions.Singleline), src);
        }

        [Fact]
        public void CodeBehind_BulkImport_CallsCoreSeamUnderUndoScope()
        {
            var src = CodeBehind;
            // BulkImport_Click must Begin an undo scope, call the Core seam, and
            // Commit/Rollback around it.
            Assert.Matches(new Regex(
                @"_undoService\.Begin\([^)]*\)[\s\S]*?ImportBattleScreenBulk\([\s\S]*?_undoService\.(Commit|Rollback)\(\)",
                RegexOptions.Singleline), src);
        }

        [Fact]
        public void CodeBehind_BulkImport_RollsBackOnError()
        {
            var src = CodeBehind;
            // The error path (non-empty returned string) must Rollback.
            Assert.Matches(new Regex(
                @"if\s*\(!string\.IsNullOrEmpty\(error\)\)[\s\S]{0,200}_undoService\.Rollback\(\)",
                RegexOptions.Singleline), src);
        }

        [Fact]
        public void CodeBehind_BulkButtons_GatedOnRenderSuccess()
        {
            var src = CodeBehind;
            // CORRECTION 4: both bulk buttons gated on CanExportBattle (same as
            // BattleExportPngButton), set inside RefreshBattlePreview.
            Assert.Contains("BulkExportButton.IsEnabled = _vm.CanExportBattle", src);
            Assert.Contains("BulkImportButton.IsEnabled = _vm.CanExportBattle", src);
        }

        [Fact]
        public void CodeBehind_BulkImport_QuantizesToSingleBankPalette()
        {
            var src = CodeBehind;
            // #989 SAFE policy: the bulk import quantizes to a SINGLE palette bank
            // (LoadAndQuantizeFromFile with maxColors = BULK_MAX_COLORS + 1 so a
            // >16-color source can be DETECTED) and rejects >16-color images.
            Assert.Contains("LoadAndQuantizeFromFile", src);
            Assert.Contains("BULK_MAX_COLORS + 1", src);
        }

        [Fact]
        public void CodeBehind_BulkImport_RejectsMultiBankSource()
        {
            var src = CodeBehind;
            // The view must reject a source needing >16 colors (single-bank guard,
            // #989) with NO mutation (early return before the undo scope).
            Assert.Matches(new Regex(
                @"loadResult\.ColorCount > ImageBattleScreenCore\.BULK_MAX_COLORS[\s\S]{0,600}return;",
                RegexOptions.Singleline), src);
        }

        // -----------------------------------------------------------------
        // Window sizing: the TabControl (which hosts the bulk Import/Export
        // buttons) lives in a star (*) row. Under SizeToContent="WidthAndHeight"
        // that star row collapsed to ~0, so the whole tab strip — Palette / Main
        // Image / Left Side / Right Side / Import-Export and ALL their content —
        // was clipped below the auto-sized window and unreachable. The window
        // now uses a bounded Manual size with a MinHeight so the tab row gets
        // real height and the (now-enabled) bulk buttons are actually visible.
        // -----------------------------------------------------------------

        [Fact]
        public void Window_DoesNotUseCollapsingSizeToContent()
        {
            // SizeToContent="WidthAndHeight" + a star tab row collapses the tabs.
            Assert.DoesNotContain("SizeToContent=\"WidthAndHeight\"", Axaml);
        }

        [Fact]
        public void Window_HasBoundedMinHeight_SoTabStripIsReachable()
        {
            var src = CodeBehind;
            // A Descriptor MinHeight large enough to reveal the tab content
            // below the preview row. The hosted EditorHostWindow applies this
            // value after the root moved from Window to UserControl.
            var m = Regex.Match(src, @"Descriptor\s*=>\s*new\([^;]*MinHeight:\s*(\d+)");
            Assert.True(m.Success, "Battle Screen descriptor must declare a MinHeight so the tab strip is reachable.");
            Assert.True(int.Parse(m.Groups[1].Value) >= 700,
                "MinHeight must be tall enough (>=700) to render the tab content (preview row + tabs).");
        }
    }
}
