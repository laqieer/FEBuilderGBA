namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests that verify crash protection in Avalonia editor views and ViewModels.
    /// Source-code verification tests.
    /// </summary>
    public class AvaloniaEditorCrashFixTests
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

        // ---- UnitEditorView crash protection ----

        [Fact]
        public void UnitEditorView_LoadList_HasTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("UnitEditorView.LoadList failed", src);
        }

        [Fact]
        public void UnitEditorView_OnUnitSelected_HasTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("UnitEditorView.OnUnitSelected failed", src);
        }

        // ---- ItemEditorView crash protection ----

        [Fact]
        public void ItemEditorView_LoadList_HasTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("ItemEditorView.LoadList failed", src);
        }

        [Fact]
        public void ItemEditorView_OnItemSelected_HasTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("ItemEditorView.OnItemSelected failed", src);
        }

        // ---- UnitEditorViewModel crash protection ----

        [Fact]
        public void UnitEditorViewModel_LoadUnitList_HasFETextDecodeTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("try { decoded = NameResolver.GetTextById(nameId); }", src);
            Assert.Contains("catch { decoded = \"???\"; }", src);
        }

        [Fact]
        public void UnitEditorViewModel_LoadUnit_HasFETextDecodeTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("try { Name = NameResolver.GetTextById(NameId); }", src);
            Assert.Contains("catch { Name = \"???\"; }", src);
        }

        // ---- ItemEditorViewModel crash protection ----

        [Fact]
        public void ItemEditorViewModel_LoadItemList_HasFETextDecodeTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("try { decoded = NameResolver.GetTextById(nameId); }", src);
            Assert.Contains("catch { decoded = \"???\"; }", src);
        }

        [Fact]
        public void ItemEditorViewModel_LoadItem_HasFETextDecodeTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("try { Name = NameResolver.GetTextById(NameId); }", src);
            Assert.Contains("catch { Name = \"???\"; }", src);
        }

        // ---- Core-level crash fixes ----

        [Fact]
        public void PatchDetection_SearchPriorityCode_HasNullGuard()
        {
            var src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "PatchDetection.cs"));
            // The no-arg overload must check for null ROM
            Assert.Contains("if (CoreState.ROM?.RomInfo == null) return PRIORITY_CODE.LAT1;", src);
        }

        [Fact]
        public void PatchDetection_SearchDrawFontPatch_HasNullGuard()
        {
            var src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "PatchDetection.cs"));
            Assert.Contains("if (CoreState.ROM?.RomInfo == null) return draw_font_enum.NO;", src);
        }

        [Fact]
        public void FETextDecode_Direct_HasTryCatch()
        {
            var src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "FETextDecode.cs"));
            Assert.Contains("catch (Exception", src);
            Assert.Contains("return \"???\";", src);
        }

        [Fact]
        public void FETextDecode_Constructor_HasNullROMGuard()
        {
            var src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "FETextDecode.cs"));
            Assert.Contains("if (CoreState.ROM?.RomInfo == null)", src);
            Assert.Contains("this.PriorityCode = PatchDetection.PRIORITY_CODE.LAT1;", src);
        }

        // ---- Combo initialization order (fixes #52) ----

        [Theory]
        [InlineData("UnitEditorView.axaml.cs", "ClassIdCombo.ItemsSource", ".SetItems(items)")]
        [InlineData("UnitEditorView.axaml.cs", "AffinityCombo.ItemsSource", ".SetItems(items)")]
        [InlineData("ItemEditorView.axaml.cs", "WeaponTypeCombo.ItemsSource", ".SetItems(items)")]
        public void ComboItemsSource_SetBeforeSetItems(string fileName, string comboAssign, string setItemsCall)
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", fileName));
            int comboPos = src.IndexOf(comboAssign);
            int setItemsPos = src.IndexOf(setItemsCall);
            Assert.True(comboPos >= 0, $"{comboAssign} not found in {fileName}");
            Assert.True(setItemsPos >= 0, $"{setItemsCall} not found in {fileName}");
            Assert.True(comboPos < setItemsPos,
                $"In {fileName}, {comboAssign} (pos {comboPos}) must appear before {setItemsCall} (pos {setItemsPos}) to fix #52");
        }
    }
}
