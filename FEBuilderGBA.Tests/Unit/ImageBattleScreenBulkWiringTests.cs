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
        public void BulkImportButton_NoLongerDisabled()
        {
            // The KnownGap stub IsEnabled="False" + tooltip must be gone from the
            // BulkImport button element (it is now enabled at runtime by the
            // render-success gate in RefreshBattlePreview).
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
                @"void\s+BulkExport_Click[\s\S]{0,300}BattlePreview\.ExportPng\(this,\s*""battle_screen""\)",
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
        public void CodeBehind_BulkImport_QuantizesToFourBankPalette()
        {
            var src = CodeBehind;
            // CORRECTION 1: the bulk import quantizes the image to its OWN ≤4-bank
            // palette (LoadAndQuantizeFromFile with maxColors = banks*16).
            Assert.Contains("LoadAndQuantizeFromFile", src);
            Assert.Contains("BULK_PALETTE_BANKS * 16", src);
        }
    }
}
