namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verify that --export-editor-images CLI mode is properly wired in both
    /// Avalonia and WinForms apps, and that EditorImageExporter.cs exists.
    /// </summary>
    public class EditorImageExportTests
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

        private string AppSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "App.axaml.cs"));

        private string MainWindowSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

        private string WinFormsProgramSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA", "Program.cs"));

        private string EditorImageExporterSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA", "EditorImageExporter.cs"));

        // --- Avalonia side ---

        [Fact]
        public void Avalonia_App_HasExportEditorImagesModeProperty()
        {
            Assert.Contains("ExportEditorImagesMode", AppSource);
            Assert.Contains("public static bool ExportEditorImagesMode", AppSource);
        }

        [Fact]
        public void Avalonia_App_ParsesExportEditorImagesArg()
        {
            Assert.Contains("--export-editor-images", AppSource);
            Assert.Contains("ExportEditorImagesMode = true", AppSource);
        }

        [Fact]
        public void Avalonia_MainWindow_HasRunExportEditorImagesMethod()
        {
            Assert.Contains("RunExportEditorImages()", MainWindowSource);
            Assert.Contains("private void RunExportEditorImages()", MainWindowSource);
        }

        [Fact]
        public void Avalonia_MainWindow_RunSmokeTestCallsExportEditorImages()
        {
            Assert.Contains("App.ExportEditorImagesMode", MainWindowSource);
        }

        [Fact]
        public void Avalonia_MainWindow_HasGraphicsEditorExporters()
        {
            Assert.Contains("GetGraphicsEditorExporters()", MainWindowSource);
            Assert.Contains("PortraitViewer", MainWindowSource);
            Assert.Contains("BattleBGViewer", MainWindowSource);
            Assert.Contains("BigCGViewer", MainWindowSource);
            Assert.Contains("ItemIconViewer", MainWindowSource);
        }

        [Fact]
        public void Avalonia_MainWindow_ExportOutputsMarkers()
        {
            Assert.Contains("EXPORT_IMAGES:", MainWindowSource);
            Assert.Contains("EXPORT_IMAGES: Results:", MainWindowSource);
        }

        // --- WinForms side ---

        [Fact]
        public void WinForms_Program_HasExportEditorImagesRouting()
        {
            Assert.Contains("--export-editor-images", WinFormsProgramSource);
            Assert.Contains("EditorImageExporter.Export", WinFormsProgramSource);
        }

        [Fact]
        public void WinForms_Program_HasCommandLineCheck()
        {
            // HasCommandLineCommand should include --export-editor-images
            Assert.Contains("\"--export-editor-images\"", WinFormsProgramSource);
        }

        [Fact]
        public void WinForms_EditorImageExporter_Exists()
        {
            Assert.Contains("static class EditorImageExporter", EditorImageExporterSource);
            Assert.Contains("public static int Export(string outputDir)", EditorImageExporterSource);
        }

        [Fact]
        public void WinForms_EditorImageExporter_HasAllEditors()
        {
            // Original 10 editors
            Assert.Contains("PortraitViewer", EditorImageExporterSource);
            Assert.Contains("BattleBGViewer", EditorImageExporterSource);
            Assert.Contains("BattleTerrainViewer", EditorImageExporterSource);
            Assert.Contains("BigCGViewer", EditorImageExporterSource);
            Assert.Contains("ChapterTitleViewer", EditorImageExporterSource);
            Assert.Contains("ItemIconViewer", EditorImageExporterSource);
            Assert.Contains("SystemIconViewer", EditorImageExporterSource);
            Assert.Contains("OPClassFontViewer", EditorImageExporterSource);
            Assert.Contains("OPPrologueViewer", EditorImageExporterSource);
            // 6 new editors
            Assert.Contains("ImagePortraitFE6", EditorImageExporterSource);
            Assert.Contains("ImageBG", EditorImageExporterSource);
            Assert.Contains("ImageCG", EditorImageExporterSource);
            Assert.Contains("ImageCGFE7U", EditorImageExporterSource);
            Assert.Contains("ImageTSAAnime", EditorImageExporterSource);
            Assert.Contains("ImageBattleBG", EditorImageExporterSource);
        }

        [Fact]
        public void WinForms_EditorImageExporter_OutputsMarkers()
        {
            Assert.Contains("EXPORT_IMAGES:", EditorImageExporterSource);
            Assert.Contains("EXPORT_IMAGES: Results:", EditorImageExporterSource);
        }

        // --- Rendering correctness ---

        [Fact]
        public void Avalonia_OPClassFontViewer_UsesSingleDereference()
        {
            // OPClassFontViewer must use p32 (single dereference), NOT p32p (double dereference)
            // This matches WinForms ClassOPFontForm.Init which uses p32()
            var vmSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "OPClassFontViewerViewModel.cs"));
            Assert.DoesNotContain("p32p(", vmSource);
            Assert.Contains("rom.p32(ptrAddr)", vmSource);
        }

        [Fact]
        public void Avalonia_BigCGViewer_DoesNotDecompressTSA()
        {
            // BigCG TSA data is read directly from ROM, not LZ77-decompressed
            // This matches WinForms BigCGForm which passes Program.ROM.Data at TSA offset
            var vmSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "BigCGViewerViewModel.cs"));
            Assert.Contains("Array.Copy(rom.Data, tsaAddr", vmSource);
            // Should NOT decompress TSA for BigCG
            Assert.DoesNotContain("LZ77.decompress(rom.Data, tsaAddr)", vmSource);
        }

        [Fact]
        public void Core_DecodeHeaderTSA_MatchesWinFormsAlgorithm()
        {
            // DecodeHeaderTSA should implement bottom-to-top fill matching WinForms ByteToHeaderTSA
            var coreSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Core", "ImageUtilCore.cs"));
            // Key WinForms algorithm signature: n = masterHeaderY << 5, and n -= 0x42/2
            Assert.Contains("masterHeaderY << 5", coreSource);
            Assert.Contains("0x42 / 2", coreSource);
        }

        // --- Cross-platform naming convention ---

        [Fact]
        public void NamingConvention_Matches()
        {
            // Both platforms should use the same editor names for matching
            var allEditors = new[] {
                "PortraitViewer", "BattleBGViewer", "BattleTerrainViewer",
                "BigCGViewer", "ChapterTitleViewer", "ChapterTitleFE7Viewer",
                "ItemIconViewer", "SystemIconViewer", "OPClassFontViewer", "OPPrologueViewer",
                "ImagePortraitFE6", "ImageBG", "ImageCG", "ImageCGFE7U",
                "ImageTSAAnime", "ImageBattleBG"
            };

            foreach (var editor in allEditors)
            {
                Assert.Contains(editor, MainWindowSource);
                Assert.Contains(editor, EditorImageExporterSource);
            }
        }
    }
}
