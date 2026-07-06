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

        // ---- #1122: INavigationService abstraction + platform impls ----

        [Fact]
        public void INavigationService_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "INavigationService.cs")));
        }

        [Fact]
        public void DesktopNavigationService_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "DesktopNavigationService.cs")));
        }

        [Fact]
        public void AndroidNavigationService_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "AndroidNavigationService.cs")));
        }

        [Fact]
        public void NavigationStack_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Services", "NavigationStack.cs")));
        }

        [Fact]
        public void WindowManager_DelegatesToNavigationService()
        {
            // The facade routes every public method to the active INavigationService.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "WindowManager.cs"));
            Assert.Contains("INavigationService", src);
            Assert.Contains("_service.Open<T>()", src);
            Assert.Contains("_service.PickFromEditor", src);
        }

        [Fact]
        public void App_HasSingleViewLifetimeBranch()
        {
            // The Android single-view boot fix: App sets MainView under
            // ISingleViewApplicationLifetime (alongside the unchanged desktop branch).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "App.axaml.cs"));
            Assert.Contains("ISingleViewApplicationLifetime", src);
            Assert.Contains("singleView.MainView = new Views.MainView()", src);
            // The desktop branch must still be present and unchanged.
            Assert.Contains("desktop.MainWindow = new Views.MainWindow()", src);
        }

        [Fact]
        public void MainView_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", "MainView.axaml")));
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", "MainView.axaml.cs")));
        }

        [Fact]
        public void WindowManager_PickFromEditor_WiresEventBeforeShow()
        {
            // #1122: the desktop pick-wiring body moved (verbatim) from
            // WindowManager.cs into the behavior-identical DesktopNavigationService.cs
            // (WindowManager is now a thin facade that delegates). This
            // race-condition guard now reads the desktop service.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "DesktopNavigationService.cs"));
            // Search within PickFromEditor method only
            int pickMethodStart = src.IndexOf("PickFromEditor");
            Assert.True(pickMethodStart > 0, "PickFromEditor method not found");
            int eventWirePos = src.IndexOf("SelectionConfirmed +=", pickMethodStart);
            int showDialogPos = src.IndexOf(".ShowDialog(", pickMethodStart);
            int showPos = src.IndexOf("window.Show()", pickMethodStart);
            Assert.True(eventWirePos > 0, "SelectionConfirmed event wiring not found");
            Assert.True(showDialogPos > 0, "ShowDialog call not found");
            Assert.True(eventWirePos < showDialogPos,
                "SelectionConfirmed must be wired BEFORE ShowDialog to prevent race condition");
            if (showPos > 0)
            {
                Assert.True(eventWirePos < showPos,
                    "SelectionConfirmed must be wired BEFORE Show to prevent race condition");
            }
        }

        [Fact]
        public void WindowManager_PickFromEditor_NoShowDialogContinueWith()
        {
            // The race condition was caused by .ShowDialog().ContinueWith() which could
            // set tcs to null before SelectionConfirmed had a chance to fire.
            // The Closed event handler already covers the "no selection" case.
            // #1122: body lives in DesktopNavigationService.cs now (verbatim).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "DesktopNavigationService.cs"));
            // Extract just the PickFromEditor method body
            int methodStart = src.IndexOf("PickFromEditor");
            int returnPos = src.IndexOf("return await tcs.Task;", methodStart);
            string methodBody = src.Substring(methodStart, returnPos - methodStart);
            // ShowDialog should not be chained with .ContinueWith
            Assert.DoesNotContain("ShowDialog(parent).ContinueWith", methodBody);
        }

        [Fact]
        public void WindowManager_PickFromEditor_ClosedEventSetsTcsNull()
        {
            // Verify the Closed event handler provides the fallback null result
            // #1122: body lives in DesktopNavigationService.cs now (verbatim).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "DesktopNavigationService.cs"));
            int methodStart = src.IndexOf("PickFromEditor");
            int returnPos = src.IndexOf("return await tcs.Task;", methodStart);
            string methodBody = src.Substring(methodStart, returnPos - methodStart);
            Assert.Contains(".Closed +=", methodBody);
            Assert.Contains("tcs.TrySetResult(null)", methodBody);
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

        [Fact]
        public void GbaImageControl_HasZoomProperty()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            Assert.Contains("public int Zoom", src);
            Assert.Contains("ZoomMin", src);
            Assert.Contains("ZoomMax", src);
        }

        [Fact]
        public void GbaImageControl_HasZoomHandlers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            Assert.Contains("OnPointerWheelChanged", src);
            Assert.Contains("OnZoomInClick", src);
            Assert.Contains("OnZoomOutClick", src);
            Assert.Contains("OnZoomResetClick", src);
        }

        [Fact]
        public void GbaImageControl_HasScrollViewer()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml"));
            Assert.Contains("ScrollViewer", axaml);
            Assert.Contains("ImageScroller", axaml);
        }

        [Fact]
        public void GbaImageControl_HasZoomButtons()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml"));
            Assert.Contains("ZoomInButton", axaml);
            Assert.Contains("ZoomOutButton", axaml);
            Assert.Contains("ZoomResetButton", axaml);
            Assert.Contains("ZoomLabel", axaml);
        }

        [Fact]
        public void GbaImageControl_HasCursorCenteredZoom()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            // Cursor-centered zoom: must read cursor position and adjust scroll offset.
            // The offset math is shared by wheel (cursor) and pinch (gesture origin)
            // zoom via the ZoomCenteredOn(newZoom, centerInScroller) helper (#1726).
            Assert.Contains("GetPosition", src);
            Assert.Contains("ImageScroller.Offset", src);
            Assert.Contains("centerInScroller", src);
        }

        [Fact]
        public void GbaImageControl_HasDragPanHandlers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            Assert.Contains("OnScrollerPointerPressed", src);
            Assert.Contains("OnScrollerPointerMoved", src);
            Assert.Contains("OnScrollerPointerReleased", src);
            Assert.Contains("_isPanning", src);
        }

        [Fact]
        public void GbaImageControl_DragPanTracksStartPosition()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            // Must track pan start and scroll start for delta calculation
            Assert.Contains("_panStart", src);
            Assert.Contains("_scrollStartX", src);
            Assert.Contains("_scrollStartY", src);
        }

        [Fact]
        public void GbaImageControl_MiddleButtonAndLeftZoomedPan()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            // Pan should work on middle button or left button when zoomed
            Assert.Contains("IsMiddleButtonPressed", src);
            Assert.Contains("IsLeftButtonPressed", src);
        }

        [Fact]
        public void GbaImageControl_HasIsPanningProperty()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            Assert.Contains("public bool IsPanning", src);
        }

        [Fact]
        public void GbaImageControl_WheelZoomScalesOffset()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "GbaImageControl.axaml.cs"));
            // Must compute scale ratio and apply to content coordinate
            Assert.Contains("(double)newZoom / oldZoom", src);
            Assert.Contains("contentX * scale", src);
            Assert.Contains("contentY * scale", src);
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
        public void ImageImportValidator_BattleBGViewer_UsesMultiPaletteImport()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageImportValidator.cs"));
            // BattleBGViewer uses multi-palette import (Import3PtrMultiPal), standard 10% threshold
            Assert.Contains("Import3PtrMultiPal", src);
            Assert.Contains("MaxDiffPercent = 10.0", src);
        }

        [Fact]
        public void ImageImportValidator_UsesEditorMaxDiffPercent()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageImportValidator.cs"));
            // Verify it uses the per-editor threshold, not a hardcoded value
            Assert.Contains("editor.MaxDiffPercent", src);
        }

        // ---- ImageExportService overwrite confirmation (#66) ----

        [Fact]
        public void ImageExportService_ChecksFileExists_BeforeOverwrite()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageExportService.cs"));
            Assert.Contains("File.Exists(path)", src);
        }

        [Fact]
        public void ImageExportService_ShowsYesNoDialog_ForOverwrite()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageExportService.cs"));
            Assert.Contains("MessageBoxMode.YesNo", src);
        }

        [Fact]
        public void ImageExportService_CancelsExport_WhenUserDeclinesOverwrite()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageExportService.cs"));
            // Verify the method returns early when user does not confirm
            Assert.Contains("MessageBoxResult.Yes", src);
            Assert.Contains("if (confirm != MessageBoxResult.Yes) return", src);
        }

        [Fact]
        public void ImageExportService_OverwritePrompt_ContainsFileName()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ImageExportService.cs"));
            // The overwrite prompt should display the filename
            Assert.Contains("already exists. Overwrite?", src);
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
