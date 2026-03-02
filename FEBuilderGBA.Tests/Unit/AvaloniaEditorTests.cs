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
    }
}
