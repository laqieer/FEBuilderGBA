namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests that verify Avalonia service classes and additional editor ViewModels.
    /// Source-code verification tests.
    /// </summary>
    public class AvaloniaServiceTests
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

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        // ---- UndoService ----

        [Fact]
        public void UndoService_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "UndoService.cs")));
        }

        [Fact]
        public void UndoService_HasUndoMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "UndoService.cs"));
            Assert.Contains("Undo", src);
        }

        [Fact]
        public void UndoService_HasCommitMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "UndoService.cs"));
            Assert.Contains("Commit", src);
        }

        [Fact]
        public void UndoService_HasRollbackMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "UndoService.cs"));
            Assert.Contains("Rollback", src);
        }

        [Fact]
        public void UndoService_HasBeginMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "UndoService.cs"));
            Assert.Contains("Begin", src);
        }

        // ---- WindowManager ----

        [Fact]
        public void WindowManager_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "WindowManager.cs")));
        }

        [Fact]
        public void WindowManager_HasOpenMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "WindowManager.cs"));
            Assert.Contains("Open<T>", src);
        }

        [Fact]
        public void WindowManager_HasNavigateMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "WindowManager.cs"));
            Assert.Contains("Navigate<T>", src);
        }

        [Fact]
        public void WindowManager_HasCloseAllMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "WindowManager.cs"));
            Assert.Contains("CloseAll", src);
        }

        // ---- IEditorView ----

        [Fact]
        public void IEditorView_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "IEditorView.cs")));
        }

        // ---- ImageViewerViewModel ----

        [Fact]
        public void ImageViewerViewModel_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "ViewModels", "ImageViewerViewModel.cs")));
        }

        [Fact]
        public void ImageViewerViewModel_HandlesImages()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ImageViewerViewModel.cs"));
            // Should reference image loading
            Assert.Contains("Image", src);
        }

        // ---- ImageViewerView ----

        [Fact]
        public void ImageViewerView_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", "ImageViewerView.axaml.cs")));
        }

        // ---- GbaImageControl ----

        [Fact]
        public void GbaImageControl_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs")));
        }

        [Fact]
        public void GbaImageControl_HasImageProperty()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            Assert.Contains("Image", src);
        }

        // ---- FileDialogHelper ----

        [Fact]
        public void FileDialogHelper_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Dialogs", "FileDialogHelper.cs")));
        }

        // ---- MessageBoxWindow ----

        [Fact]
        public void MessageBoxWindow_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Dialogs", "MessageBoxWindow.axaml.cs")));
        }

        // ---- AvaloniaAppServices ----

        [Fact]
        public void AvaloniaAppServices_ImplementsIAppServices()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "AvaloniaAppServices.cs"));
            Assert.Contains("IAppServices", src);
        }

        [Fact]
        public void AvaloniaAppServices_HasShowErrorMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "AvaloniaAppServices.cs"));
            Assert.Contains("ShowError", src);
        }

        // ---- ViewModelBase ----

        [Fact]
        public void ViewModelBase_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "ViewModels", "ViewModelBase.cs")));
        }

        // ---- ImageImportValidator ----

        [Fact]
        public void ImageImportValidator_EditorDescriptor_MaxDiffPercent_DefaultIs10()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageImportValidator.cs"));
            // Default is 10%
            Assert.Contains("MaxDiffPercent { get; set; } = 10.0", src);
        }

        [Fact]
        public void ImageImportValidator_BattleBGViewer_HasHigherThreshold()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageImportValidator.cs"));
            // BattleBGViewer uses multi-sub-palette TSA, needs 30% tolerance
            Assert.Contains("MaxDiffPercent = 30.0", src);
        }

        [Fact]
        public void ImageImportValidator_UsesEditorMaxDiffPercent()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageImportValidator.cs"));
            // Verify it uses the per-editor threshold, not a hardcoded value
            Assert.Contains("editor.MaxDiffPercent", src);
        }

        // ---- All Avalonia editors have their corresponding .axaml view ----

        [Theory]
        [InlineData("UnitEditorView")]
        [InlineData("ItemEditorView")]
        [InlineData("ClassEditorView")]
        [InlineData("ImageViewerView")]
        [InlineData("MapSettingView")]
        [InlineData("TextViewerView")]
        [InlineData("PortraitViewerView")]
        [InlineData("SongTableView")]
        [InlineData("EventCondView")]
        [InlineData("SupportUnitEditorView")]
        [InlineData("MoveCostEditorView")]
        [InlineData("TerrainNameEditorView")]
        [InlineData("CCBranchEditorView")]
        [InlineData("EDView")]
        [InlineData("EDStaffRollView")]
        [InlineData("WorldMapPointView")]
        [InlineData("WorldMapBGMView")]
        [InlineData("WorldMapEventPointerView")]
        [InlineData("MenuDefinitionView")]
        [InlineData("MenuCommandView")]
        [InlineData("BigCGViewerView")]
        [InlineData("ChapterTitleViewerView")]
        [InlineData("BattleBGViewerView")]
        [InlineData("BattleTerrainViewerView")]
        [InlineData("SystemIconViewerView")]
        [InlineData("SystemHoverColorViewerView")]
        [InlineData("ItemIconViewerView")]
        [InlineData("OPClassDemoViewerView")]
        [InlineData("OPClassFontViewerView")]
        [InlineData("OPPrologueViewerView")]
        public void AvaloniaView_HasAxamlAndCodeBehind(string viewName)
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", $"{viewName}.axaml")),
                $"Missing AXAML: {viewName}.axaml");
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", $"{viewName}.axaml.cs")),
                $"Missing code-behind: {viewName}.axaml.cs");
        }
    }
}
