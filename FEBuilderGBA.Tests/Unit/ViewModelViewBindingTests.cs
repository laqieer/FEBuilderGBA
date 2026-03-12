using System.IO;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that each Avalonia View's UpdateUI() method reads from the correct
    /// ViewModel properties, ensuring the bindings between View and ViewModel are correct.
    /// </summary>
    public class ViewModelViewBindingTests
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

        private string ReadView(string name) =>
            File.ReadAllText(Path.Combine(AvaloniaDir, "Views", name));

        // ---------------------------------------------------------------
        // UnitEditorView
        // ---------------------------------------------------------------

        [Fact]
        public void UnitEditorView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("UnitEditorView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.Name", src);
            Assert.Contains("_vm.NameId", src);
            Assert.Contains("_vm.ClassId", src);
            Assert.Contains("_vm.Level", src);
            Assert.Contains("_vm.HP", src);
            Assert.Contains("_vm.Str", src);
            Assert.Contains("_vm.Skl", src);
            Assert.Contains("_vm.Spd", src);
            Assert.Contains("_vm.Def", src);
            Assert.Contains("_vm.Res", src);
            Assert.Contains("_vm.Lck", src);
            Assert.Contains("_vm.Con", src);
        }

        [Fact]
        public void UnitEditorView_ReadFromUIWritesBackAllProperties()
        {
            var src = ReadView("UnitEditorView.axaml.cs");
            // ReadFromUI should write back to all ViewModel properties
            Assert.Contains("_vm.NameId = ", src);
            Assert.Contains("_vm.ClassId = ", src);
            Assert.Contains("_vm.Level = ", src);
            Assert.Contains("_vm.HP = ", src);
            Assert.Contains("_vm.Str = ", src);
            Assert.Contains("_vm.Skl = ", src);
            Assert.Contains("_vm.Spd = ", src);
            Assert.Contains("_vm.Def = ", src);
            Assert.Contains("_vm.Res = ", src);
            Assert.Contains("_vm.Lck = ", src);
            Assert.Contains("_vm.Con = ", src);
        }

        [Fact]
        public void UnitEditorView_CallsWriteUnit()
        {
            var src = ReadView("UnitEditorView.axaml.cs");
            Assert.Contains("_vm.WriteUnit()", src);
        }

        [Fact]
        public void UnitEditorView_ImplementsIEditorView()
        {
            var src = ReadView("UnitEditorView.axaml.cs");
            // UnitEditorView implements IPickableEditor (which extends IEditorView)
            Assert.True(src.Contains("IEditorView") || src.Contains("IPickableEditor"),
                "UnitEditorView should implement IEditorView or IPickableEditor");
            Assert.Contains("IDataVerifiableView", src);
            Assert.Contains("NavigateTo", src);
            Assert.Contains("SelectFirstItem", src);
        }

        // ---------------------------------------------------------------
        // ItemEditorView
        // ---------------------------------------------------------------

        [Fact]
        public void ItemEditorView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("ItemEditorView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.Name", src);
            Assert.Contains("_vm.NameId", src);
            Assert.Contains("_vm.WeaponType", src);
            Assert.Contains("_vm.WeaponRank", src);
            Assert.Contains("_vm.Might", src);
            Assert.Contains("_vm.Hit", src);
            Assert.Contains("_vm.Weight", src);
            Assert.Contains("_vm.Crit", src);
            Assert.Contains("_vm.Range", src);
            Assert.Contains("_vm.Uses", src);
            Assert.Contains("_vm.Price", src);
        }

        [Fact]
        public void ItemEditorView_WritesBackAllProperties()
        {
            var src = ReadView("ItemEditorView.axaml.cs");
            Assert.Contains("_vm.NameId = ", src);
            Assert.Contains("_vm.WeaponType = ", src);
            Assert.Contains("_vm.WeaponRank = ", src);
            Assert.Contains("_vm.Might = ", src);
            Assert.Contains("_vm.Hit = ", src);
            Assert.Contains("_vm.Weight = ", src);
            Assert.Contains("_vm.Crit = ", src);
            Assert.Contains("_vm.Range = ", src);
            Assert.Contains("_vm.Uses = ", src);
            Assert.Contains("_vm.Price = ", src);
        }

        [Fact]
        public void ItemEditorView_CallsWriteItem()
        {
            var src = ReadView("ItemEditorView.axaml.cs");
            Assert.Contains("_vm.WriteItem()", src);
        }

        // ---------------------------------------------------------------
        // ClassEditorView
        // ---------------------------------------------------------------

        [Fact]
        public void ClassEditorView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("ClassEditorView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.Name", src);
            Assert.Contains("_vm.NameId", src);
            Assert.Contains("_vm.ClassNumber", src);
            // Base stats
            Assert.Contains("_vm.BaseHp", src);
            Assert.Contains("_vm.BaseStr", src);
            Assert.Contains("_vm.BaseSkl", src);
            Assert.Contains("_vm.BaseSpd", src);
            Assert.Contains("_vm.BaseDef", src);
            Assert.Contains("_vm.BaseRes", src);
            Assert.Contains("_vm.Mov", src);
            // Growth rates
            Assert.Contains("_vm.GrowHp", src);
            Assert.Contains("_vm.GrowStr", src);
            Assert.Contains("_vm.GrowSkl", src);
            Assert.Contains("_vm.GrowSpd", src);
            Assert.Contains("_vm.GrowDef", src);
            Assert.Contains("_vm.GrowRes", src);
            Assert.Contains("_vm.GrowLck", src);
        }

        [Fact]
        public void ClassEditorView_WritesBackAllProperties()
        {
            var src = ReadView("ClassEditorView.axaml.cs");
            Assert.Contains("_vm.NameId = ", src);
            Assert.Contains("_vm.ClassNumber = ", src);
            Assert.Contains("_vm.BaseHp = ", src);
            Assert.Contains("_vm.BaseStr = ", src);
            Assert.Contains("_vm.BaseSkl = ", src);
            Assert.Contains("_vm.BaseSpd = ", src);
            Assert.Contains("_vm.BaseDef = ", src);
            Assert.Contains("_vm.BaseRes = ", src);
            Assert.Contains("_vm.Mov = ", src);
            Assert.Contains("_vm.GrowHp = ", src);
            Assert.Contains("_vm.GrowStr = ", src);
            Assert.Contains("_vm.GrowSkl = ", src);
            Assert.Contains("_vm.GrowSpd = ", src);
            Assert.Contains("_vm.GrowDef = ", src);
            Assert.Contains("_vm.GrowRes = ", src);
            Assert.Contains("_vm.GrowLck = ", src);
        }

        [Fact]
        public void ClassEditorView_CallsWriteClass()
        {
            var src = ReadView("ClassEditorView.axaml.cs");
            Assert.Contains("_vm.WriteClass()", src);
        }

        // ---------------------------------------------------------------
        // ItemWeaponEffectViewerView
        // ---------------------------------------------------------------

        [Fact]
        public void ItemWeaponEffectView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("ItemWeaponEffectViewerView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.ItemId", src);
            Assert.Contains("_vm.AnimType", src);
            Assert.Contains("_vm.EffectId", src);
            Assert.Contains("_vm.MapEffectPointer", src);
            Assert.Contains("_vm.DamageEffect", src);
            Assert.Contains("_vm.Motion", src);
            Assert.Contains("_vm.HitColor", src);
            Assert.Contains("_vm.Unknown1", src);
            Assert.Contains("_vm.Unknown3", src);
            Assert.Contains("_vm.Unknown6", src);
            Assert.Contains("_vm.Unknown15", src);
        }

        // ---------------------------------------------------------------
        // MapSettingView
        // ---------------------------------------------------------------

        [Fact]
        public void MapSettingView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("MapSettingView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.DataSize", src);
            // All fields now use semantic names (renamed from D#/W#/B#)
            Assert.Contains("_vm.CpPointer", src);
            Assert.Contains("_vm.ObjectTypePLIST", src);
            Assert.Contains("_vm.PalettePLIST", src);
            Assert.Contains("_vm.MapPointerPLIST", src);
            Assert.Contains("_vm.Weather", src);
            Assert.Contains("_vm.MapNameText1", src);
            Assert.Contains("_vm.ClearConditionText", src);
            Assert.Contains("_vm.ChapterNumber", src);
            Assert.Contains("_vm.WriteMapSetting", src);
        }

        // ---------------------------------------------------------------
        // SongTableView
        // ---------------------------------------------------------------

        [Fact]
        public void SongTableView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("SongTableView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.SongHeaderPointer", src);
            Assert.Contains("_vm.TrackCount", src);
            Assert.Contains("_vm.HeaderPriority", src);
            Assert.Contains("_vm.HeaderReverb", src);
        }

        // ---------------------------------------------------------------
        // PortraitViewerView
        // ---------------------------------------------------------------

        [Fact]
        public void PortraitViewerView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("PortraitViewerView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.ImagePointer", src);
            Assert.Contains("_vm.MapPointer", src);
            Assert.Contains("_vm.PalettePointer", src);
        }

        [Fact]
        public void PortraitViewerView_CallsTryLoadPortraitImage()
        {
            var src = ReadView("PortraitViewerView.axaml.cs");
            // Verify all three portrait types are loaded
            Assert.Contains("_vm.TryLoadMainPortrait()", src);
            Assert.Contains("_vm.TryLoadMapPortrait()", src);
            Assert.Contains("_vm.TryLoadClassPortrait()", src);
            // Verify it uses SetImage (IImage) not SetRgbaData (byte[])
            Assert.Contains("SetImage", src);
        }

        // ---------------------------------------------------------------
        // EventCondView
        // ---------------------------------------------------------------

        [Fact]
        public void EventCondView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("EventCondView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.MapDataSize", src);
            Assert.Contains("_vm.RawBytes", src);
        }

        // ---------------------------------------------------------------
        // ArenaClassViewerView
        // ---------------------------------------------------------------

        [Fact]
        public void ArenaClassViewerView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("ArenaClassViewerView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.ClassId", src);
        }

        // ---------------------------------------------------------------
        // WorldMapPointView
        // ---------------------------------------------------------------

        [Fact]
        public void WorldMapPointView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("WorldMapPointView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.AlwaysAccessible", src);
            Assert.Contains("_vm.NameTextId", src);
            Assert.Contains("_vm.Write()", src);
        }

        // ---------------------------------------------------------------
        // SoundRoomViewerView
        // ---------------------------------------------------------------

        [Fact]
        public void SoundRoomViewerView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("SoundRoomViewerView.axaml.cs");
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.SongId", src);
            Assert.Contains("_vm.Raw4", src);
            Assert.Contains("_vm.Raw8", src);
            Assert.Contains("_vm.TextId", src);
        }

        // ---------------------------------------------------------------
        // MoveCostEditorView
        // ---------------------------------------------------------------

        [Fact]
        public void MoveCostEditorView_UpdateUIBindsCorrectProperties()
        {
            var src = ReadView("MoveCostEditorView.axaml.cs");
            Assert.Contains("_vm.ClassName", src);
            Assert.Contains("_vm.CurrentAddr", src);
            Assert.Contains("_vm.MoveCosts", src);
        }

        // ---------------------------------------------------------------
        // Cross-cutting: all Views follow the proper pattern
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("UnitEditorView.axaml.cs", "UnitEditorViewModel")]
        [InlineData("ItemEditorView.axaml.cs", "ItemEditorViewModel")]
        [InlineData("ClassEditorView.axaml.cs", "ClassEditorViewModel")]
        [InlineData("ItemWeaponEffectViewerView.axaml.cs", "ItemWeaponEffectViewerViewModel")]
        [InlineData("MapSettingView.axaml.cs", "MapSettingViewModel")]
        [InlineData("SongTableView.axaml.cs", "SongTableViewModel")]
        [InlineData("PortraitViewerView.axaml.cs", "PortraitViewerViewModel")]
        [InlineData("EventCondView.axaml.cs", "EventCondViewModel")]
        [InlineData("ArenaClassViewerView.axaml.cs", "ArenaClassViewerViewModel")]
        [InlineData("WorldMapPointView.axaml.cs", "WorldMapPointViewModel")]
        [InlineData("SoundRoomViewerView.axaml.cs", "SoundRoomViewerViewModel")]
        [InlineData("MoveCostEditorView.axaml.cs", "MoveCostEditorViewModel")]
        public void View_InstantiatesCorrectViewModel(string viewFile, string vmType)
        {
            var src = ReadView(viewFile);
            // Views declare their VM as a typed field — either "new VmType()" or target-typed "VmType _vm = new()"
            Assert.Contains(vmType, src);
            Assert.Contains("_vm", src);
            Assert.Contains("new()", src);
        }

        [Theory]
        [InlineData("UnitEditorView.axaml.cs")]
        [InlineData("ItemEditorView.axaml.cs")]
        [InlineData("ClassEditorView.axaml.cs")]
        [InlineData("ItemWeaponEffectViewerView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        [InlineData("SongTableView.axaml.cs")]
        [InlineData("PortraitViewerView.axaml.cs")]
        [InlineData("EventCondView.axaml.cs")]
        [InlineData("ArenaClassViewerView.axaml.cs")]
        [InlineData("WorldMapPointView.axaml.cs")]
        [InlineData("SoundRoomViewerView.axaml.cs")]
        [InlineData("MoveCostEditorView.axaml.cs")]
        public void View_HasUpdateUIMethod(string viewFile)
        {
            var src = ReadView(viewFile);
            Assert.Contains("void UpdateUI()", src);
        }

        [Theory]
        [InlineData("UnitEditorView.axaml.cs")]
        [InlineData("ItemEditorView.axaml.cs")]
        [InlineData("ClassEditorView.axaml.cs")]
        [InlineData("ItemWeaponEffectViewerView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        [InlineData("SongTableView.axaml.cs")]
        [InlineData("PortraitViewerView.axaml.cs")]
        [InlineData("EventCondView.axaml.cs")]
        [InlineData("ArenaClassViewerView.axaml.cs")]
        [InlineData("WorldMapPointView.axaml.cs")]
        [InlineData("SoundRoomViewerView.axaml.cs")]
        [InlineData("MoveCostEditorView.axaml.cs")]
        public void View_ImplementsIEditorView(string viewFile)
        {
            var src = ReadView(viewFile);
            // Views implement either IEditorView directly or IPickableEditor (which extends IEditorView)
            Assert.True(src.Contains("IEditorView") || src.Contains("IPickableEditor"),
                $"View {viewFile} should implement IEditorView or IPickableEditor");
            Assert.Contains("SelectFirstItem", src);
        }

        [Theory]
        [InlineData("UnitEditorView.axaml.cs")]
        [InlineData("ItemEditorView.axaml.cs")]
        [InlineData("ClassEditorView.axaml.cs")]
        [InlineData("ItemWeaponEffectViewerView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        [InlineData("SongTableView.axaml.cs")]
        [InlineData("PortraitViewerView.axaml.cs")]
        [InlineData("EventCondView.axaml.cs")]
        [InlineData("ArenaClassViewerView.axaml.cs")]
        [InlineData("WorldMapPointView.axaml.cs")]
        [InlineData("SoundRoomViewerView.axaml.cs")]
        [InlineData("MoveCostEditorView.axaml.cs")]
        public void View_HandlesLoadErrors(string viewFile)
        {
            var src = ReadView(viewFile);
            // All views should catch exceptions in their event handlers
            Assert.Contains("catch (Exception ex)", src);
            Assert.Contains("Log.Error", src);
        }

        [Theory]
        [InlineData("UnitEditorView.axaml.cs")]
        [InlineData("ItemEditorView.axaml.cs")]
        [InlineData("ClassEditorView.axaml.cs")]
        [InlineData("ItemWeaponEffectViewerView.axaml.cs")]
        [InlineData("MapSettingView.axaml.cs")]
        [InlineData("SongTableView.axaml.cs")]
        [InlineData("PortraitViewerView.axaml.cs")]
        [InlineData("EventCondView.axaml.cs")]
        [InlineData("ArenaClassViewerView.axaml.cs")]
        [InlineData("WorldMapPointView.axaml.cs")]
        [InlineData("SoundRoomViewerView.axaml.cs")]
        [InlineData("MoveCostEditorView.axaml.cs")]
        public void View_DisplaysAddressLabel(string viewFile)
        {
            var src = ReadView(viewFile);
            // All views display the current address
            Assert.Contains("AddrLabel.Text", src);
        }

        // ---------------------------------------------------------------
        // Graphics viewers use SetImage(IImage) not SetRgbaData(byte[])
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("BattleBGViewerView.axaml.cs")]
        [InlineData("BattleTerrainViewerView.axaml.cs")]
        [InlineData("BigCGViewerView.axaml.cs")]
        [InlineData("ChapterTitleViewerView.axaml.cs")]
        [InlineData("ImageChapterTitleFE7View.axaml.cs")]
        [InlineData("ItemIconViewerView.axaml.cs")]
        [InlineData("OPClassFontViewerView.axaml.cs")]
        [InlineData("OPPrologueViewerView.axaml.cs")]
        [InlineData("SystemIconViewerView.axaml.cs")]
        public void GraphicsView_UsesSetImage_NotSetRgbaData(string viewFile)
        {
            var src = ReadView(viewFile);
            Assert.Contains("SetImage", src);
            Assert.DoesNotContain("SetRgbaData", src);
        }

        private string ReadViewModel(string name) =>
            File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", name));

        [Theory]
        [InlineData("BattleBGViewerViewModel.cs")]
        [InlineData("BattleTerrainViewerViewModel.cs")]
        [InlineData("BigCGViewerViewModel.cs")]
        [InlineData("ChapterTitleViewerViewModel.cs")]
        [InlineData("ImageChapterTitleFE7ViewModel.cs")]
        [InlineData("ItemIconViewerViewModel.cs")]
        [InlineData("OPClassFontViewerViewModel.cs")]
        [InlineData("OPClassFontFE8UViewModel.cs")]
        [InlineData("OPPrologueViewerViewModel.cs")]
        [InlineData("SystemIconViewerViewModel.cs")]
        [InlineData("PortraitViewerViewModel.cs")]
        public void GraphicsViewModel_ReturnsIImage_NotByteArray(string vmFile)
        {
            var src = ReadViewModel(vmFile);
            // Should NOT call GetPixelData() — that returns indexed bytes
            Assert.DoesNotContain("GetPixelData()", src);
        }
    }
}
