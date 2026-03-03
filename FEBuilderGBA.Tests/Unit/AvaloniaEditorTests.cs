namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests that verify critical fixes in the Avalonia editor views/controls.
    /// These are source-code verification tests similar to AvaloniaProjectTests.
    /// </summary>
    public class AvaloniaEditorTests
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

        // ------------------------------------------------------------------ AddressListControl

        [Fact]
        public void AddressListControl_HasFilteredIndicesMapping()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_filteredIndices", src);
        }

        [Fact]
        public void AddressListControl_HasRefreshingGuard()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_isRefreshing", src);
        }

        [Fact]
        public void AddressListControl_SelectionChangedChecksRefreshingGuard()
        {
            // The SelectionChanged handler must skip events during refresh
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("if (_isRefreshing) return;", src);
        }

        [Fact]
        public void AddressListControl_SelectedItemUsesFilteredIndices()
        {
            // SelectedItem must use _filteredIndices to map display index → items index
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_filteredIndices[displayIdx]", src);
        }

        [Fact]
        public void AddressListControl_RefreshDisplayPopulatesFilteredIndices()
        {
            // RefreshDisplay must populate _filteredIndices alongside _displayItems
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_filteredIndices.Add(i)", src);
        }

        [Fact]
        public void AddressListControl_SelectAddressUsesFilteredIndices()
        {
            // SelectAddress must iterate display indices, not _items indices
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("for (int displayIdx = 0; displayIdx < _filteredIndices.Count", src);
        }

        [Fact]
        public void AddressListControl_DisplayIncludesAddress()
        {
            // List items must show the ROM address for easy identification
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("0x{_items[i].addr:X08}", src);
        }

        [Fact]
        public void AddressListControl_HasSelectFirstMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void SelectFirst()", src);
        }

        // ------------------------------------------------------------------ ViewModel bounds checks

        [Fact]
        public void UnitEditorViewModel_LoadUnitHasBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("addr + minSize > (uint)rom.Data.Length", src);
        }

        [Fact]
        public void ItemEditorViewModel_LoadItemHasBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("addr + minSize > (uint)rom.Data.Length", src);
        }

        // ------------------------------------------------------------------ Smoke test support

        [Fact]
        public void App_SupportsRomCommandLineArg()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "App.axaml.cs"));
            Assert.Contains("StartupRomPath", src);
            Assert.Contains("--rom", src);
        }

        [Fact]
        public void App_SupportsSmokeTestMode()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "App.axaml.cs"));
            Assert.Contains("SmokeTestMode", src);
            Assert.Contains("--smoke-test", src);
        }

        [Fact]
        public void MainWindow_HasLoadRomFileMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("public bool LoadRomFile(string path)", src);
        }

        [Fact]
        public void MainWindow_HasRunSmokeTestMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("RunSmokeTest()", src);
        }

        [Fact]
        public void UnitEditorView_HasSelectFirstItemMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemEditorView_HasSelectFirstItemMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Class Editor (WU-7)

        [Fact]
        public void ClassEditorViewModel_HasLoadClassList()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ClassEditorViewModel.cs"));
            Assert.Contains("public List<AddrResult> LoadClassList()", src);
            Assert.Contains("class_pointer", src);
            Assert.Contains("class_datasize", src);
        }

        [Fact]
        public void ClassEditorViewModel_HasBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ClassEditorViewModel.cs"));
            Assert.Contains("addr + minSize > (uint)rom.Data.Length", src);
        }

        [Fact]
        public void ClassEditorView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MainWindow_HasClassesButton()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("OpenClasses_Click", src);
        }

        // ------------------------------------------------------------------ Map Setting Viewer (WU-8)

        [Fact]
        public void MapSettingViewModel_UsesMapSettingCore()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapSettingViewModel.cs"));
            Assert.Contains("MapSettingCore.MakeMapIDList()", src);
        }

        [Fact]
        public void MapSettingView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MapSettingView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Text Viewer (WU-9)

        [Fact]
        public void TextViewerViewModel_UsesFETextDecode()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TextViewerViewModel.cs"));
            Assert.Contains("FETextDecode.Direct(", src);
        }

        [Fact]
        public void TextViewerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "TextViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ CC Branch Editor (WU-10)

        [Fact]
        public void CCBranchEditorViewModel_UsesCCBranchPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "CCBranchEditorViewModel.cs"));
            Assert.Contains("ccbranch_pointer", src);
        }

        [Fact]
        public void CCBranchEditorView_HasWriteButton()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "CCBranchEditorView.axaml.cs"));
            Assert.Contains("WriteCCBranch()", src);
        }

        // ------------------------------------------------------------------ Terrain Name Editor (WU-11)

        [Fact]
        public void TerrainNameEditorViewModel_UsesTerrainNamePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TerrainNameEditorViewModel.cs"));
            Assert.Contains("map_terrain_name_pointer", src);
        }

        [Fact]
        public void TerrainNameEditorView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "TerrainNameEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Song Table Viewer (WU-12)

        [Fact]
        public void SongTableViewModel_UsesSoundTablePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SongTableViewModel.cs"));
            Assert.Contains("sound_table_pointer", src);
        }

        [Fact]
        public void SongTableView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SongTableView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Portrait Viewer (WU-13)

        [Fact]
        public void PortraitViewerViewModel_UsesPortraitPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "PortraitViewerViewModel.cs"));
            Assert.Contains("portrait_pointer", src);
            Assert.Contains("portrait_datasize", src);
        }

        [Fact]
        public void PortraitViewerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "PortraitViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Move Cost Editor (WU-14)

        [Fact]
        public void MoveCostEditorViewModel_UsesClassPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MoveCostEditorViewModel.cs"));
            Assert.Contains("class_pointer", src);
        }

        [Fact]
        public void MoveCostEditorView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MoveCostEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Support Unit Editor (WU-15)

        [Fact]
        public void SupportUnitEditorViewModel_UsesSupportUnitPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SupportUnitEditorViewModel.cs"));
            Assert.Contains("support_unit_pointer", src);
        }

        [Fact]
        public void SupportUnitEditorView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SupportUnitEditorView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }
    }
}
