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
        public void AddressListControl_DisplayShowsLabelOnly()
        {
            // List items show label/name only (no hex address prefix)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("string display = label;", src);
            Assert.DoesNotContain("0x{_items[i].addr:X08}", src);
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

        // ================================================================== Batch 2 editors

        // ------------------------------------------------------------------ Item Weapon Effect Viewer
        [Fact]
        public void ItemWeaponEffectView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemWeaponEffectViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemWeaponEffectViewModel_UsesItemEffectPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemWeaponEffectViewerViewModel.cs"));
            Assert.Contains("item_effect_pointer", src);
        }

        // ------------------------------------------------------------------ Item Stat Bonuses Viewer
        [Fact]
        public void ItemStatBonusesView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemStatBonusesViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemStatBonusesViewModel_UsesItemPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemStatBonusesViewerViewModel.cs"));
            Assert.Contains("item_pointer", src);
        }

        // ------------------------------------------------------------------ Item Effectiveness Viewer
        [Fact]
        public void ItemEffectivenessView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEffectivenessViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemEffectivenessViewModel_UsesEffectivenessAddress()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEffectivenessViewerViewModel.cs"));
            Assert.Contains("weapon_effectiveness_2x3x_address", src);
        }

        // ------------------------------------------------------------------ Item Promotion Viewer
        [Fact]
        public void ItemPromotionView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemPromotionViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemPromotionViewModel_UsesPromotionPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemPromotionViewerViewModel.cs"));
            Assert.Contains("item_promotion1_array_pointer", src);
        }

        // ------------------------------------------------------------------ Item Shop Viewer
        [Fact]
        public void ItemShopView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemShopViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemShopViewModel_UsesShopPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemShopViewerViewModel.cs"));
            Assert.Contains("item_shop_hensei_pointer", src);
        }

        // ------------------------------------------------------------------ Item Weapon Triangle Viewer
        [Fact]
        public void ItemWeaponTriangleView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemWeaponTriangleViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemWeaponTriangleViewModel_UsesCorneredPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemWeaponTriangleViewerViewModel.cs"));
            Assert.Contains("item_cornered_pointer", src);
        }

        // ------------------------------------------------------------------ Item Usage Pointer Viewer
        [Fact]
        public void ItemUsagePointerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemUsagePointerViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemUsagePointerViewModel_UsesUsabilityPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemUsagePointerViewerViewModel.cs"));
            Assert.Contains("item_usability_array_pointer", src);
        }

        // ------------------------------------------------------------------ Item Effect Pointer Viewer
        [Fact]
        public void ItemEffectPointerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEffectPointerViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemEffectPointerViewModel_UsesEffectPointerTable()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEffectPointerViewerViewModel.cs"));
            Assert.Contains("item_effect_pointer_table_pointer", src);
        }

        // ------------------------------------------------------------------ Support Attribute Viewer
        [Fact]
        public void SupportAttributeView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SupportAttributeView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SupportAttributeViewModel_UsesSupportAttributePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SupportAttributeViewModel.cs"));
            Assert.Contains("support_attribute_pointer", src);
        }

        // ------------------------------------------------------------------ Support Talk Viewer
        [Fact]
        public void SupportTalkView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SupportTalkView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SupportTalkViewModel_UsesSupportTalkPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SupportTalkViewModel.cs"));
            Assert.Contains("support_talk_pointer", src);
        }

        // ------------------------------------------------------------------ Event Condition Viewer
        [Fact]
        public void EventCondView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EventCondView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void EventCondViewModel_UsesMapSettingCore()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "EventCondViewModel.cs"));
            Assert.Contains("MapSettingCore", src);
        }

        // ------------------------------------------------------------------ Map Change Viewer
        [Fact]
        public void MapChangeView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MapChangeView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MapChangeViewModel_UsesMapChangePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapChangeViewModel.cs"));
            Assert.Contains("map_mapchange_pointer", src);
        }

        // ------------------------------------------------------------------ Map Exit Point Viewer
        [Fact]
        public void MapExitPointView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MapExitPointView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MapExitPointViewModel_UsesExitPointPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapExitPointViewModel.cs"));
            Assert.Contains("map_exit_point_pointer", src);
        }

        // ------------------------------------------------------------------ Map Pointer Viewer
        [Fact]
        public void MapPointerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MapPointerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MapPointerViewModel_UsesMapPointerPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapPointerViewModel.cs"));
            Assert.Contains("map_map_pointer_pointer", src);
        }

        // ------------------------------------------------------------------ Map Tile Animation Viewer
        [Fact]
        public void MapTileAnimationView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MapTileAnimationView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MapTileAnimationViewModel_UsesMapTileAnimePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapTileAnimationViewModel.cs"));
            Assert.Contains("map_tileanime1_pointer", src);
        }

        // ------------------------------------------------------------------ Arena Class Viewer
        [Fact]
        public void ArenaClassView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ArenaClassViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ArenaClassViewModel_UsesArenaPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ArenaClassViewerViewModel.cs"));
            Assert.Contains("arena_class_near_weapon_pointer", src);
        }

        // ------------------------------------------------------------------ Arena Enemy Weapon Viewer
        [Fact]
        public void ArenaEnemyWeaponView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ArenaEnemyWeaponViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ArenaEnemyWeaponViewModel_UsesArenaWeaponPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ArenaEnemyWeaponViewerViewModel.cs"));
            Assert.Contains("arena_enemy_weapon_basic_pointer", src);
        }

        // ------------------------------------------------------------------ Link Arena Deny Unit Viewer
        [Fact]
        public void LinkArenaDenyUnitView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "LinkArenaDenyUnitViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void LinkArenaDenyUnitViewModel_UsesDenyUnitPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "LinkArenaDenyUnitViewerViewModel.cs"));
            Assert.Contains("link_arena_deny_unit_pointer", src);
        }

        // ------------------------------------------------------------------ Monster Probability Viewer
        [Fact]
        public void MonsterProbabilityView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MonsterProbabilityViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MonsterProbabilityViewModel_UsesMonsterPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MonsterProbabilityViewerViewModel.cs"));
            Assert.Contains("monster_probability_pointer", src);
        }

        // ------------------------------------------------------------------ Monster Item Viewer
        [Fact]
        public void MonsterItemView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MonsterItemViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MonsterItemViewModel_UsesMonsterItemPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MonsterItemViewerViewModel.cs"));
            Assert.Contains("monster_item_item_pointer", src);
        }

        // ------------------------------------------------------------------ Monster WMap Probability Viewer
        [Fact]
        public void MonsterWMapProbabilityView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MonsterWMapProbabilityViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MonsterWMapProbabilityViewModel_UsesWMapPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MonsterWMapProbabilityViewerViewModel.cs"));
            Assert.Contains("monster_wmap_base_point_pointer", src);
        }

        // ------------------------------------------------------------------ Summon Unit Viewer
        [Fact]
        public void SummonUnitView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SummonUnitViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SummonUnitViewModel_UsesSummonPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SummonUnitViewerViewModel.cs"));
            Assert.Contains("summon_unit_pointer", src);
        }

        // ------------------------------------------------------------------ Summons Demon King Viewer
        [Fact]
        public void SummonsDemonKingView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SummonsDemonKingViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SummonsDemonKingViewModel_UsesDemonKingPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SummonsDemonKingViewerViewModel.cs"));
            Assert.Contains("summons_demon_king_pointer", src);
        }

        // ------------------------------------------------------------------ Sound Boss BGM Viewer
        [Fact]
        public void SoundBossBGMView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SoundBossBGMViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SoundBossBGMViewModel_UsesBossBGMPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SoundBossBGMViewerViewModel.cs"));
            Assert.Contains("sound_boss_bgm_pointer", src);
        }

        // ------------------------------------------------------------------ Sound Foot Steps Viewer
        [Fact]
        public void SoundFootStepsView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SoundFootStepsViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SoundFootStepsViewModel_UsesFootStepsPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SoundFootStepsViewerViewModel.cs"));
            Assert.Contains("sound_foot_steps_pointer", src);
        }

        // ------------------------------------------------------------------ Sound Room Viewer
        [Fact]
        public void SoundRoomView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SoundRoomViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SoundRoomViewModel_UsesSoundRoomDatasize()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SoundRoomViewerViewModel.cs"));
            Assert.Contains("sound_room_datasize", src);
        }

        // ------------------------------------------------------------------ Menu Definition Viewer
        [Fact]
        public void MenuDefinitionView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MenuDefinitionView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MenuDefinitionViewModel_UsesMenuDefinitionPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MenuDefinitionViewModel.cs"));
            Assert.Contains("menu_definiton_pointer", src);
        }

        // ------------------------------------------------------------------ Menu Command Viewer
        [Fact]
        public void MenuCommandView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MenuCommandView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void MenuCommandViewModel_UsesMenuCommand()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MenuCommandViewModel.cs"));
            Assert.Contains("MenuCommand_", src);
        }

        // ------------------------------------------------------------------ ED Viewer
        [Fact]
        public void EDView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EDView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void EDViewModel_UsesEDPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "EDViewModel.cs"));
            Assert.Contains("ed_1_pointer", src);
        }

        // ------------------------------------------------------------------ ED Staff Roll Viewer
        [Fact]
        public void EDStaffRollView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EDStaffRollView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void EDStaffRollViewModel_UsesStaffRollPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "EDStaffRollViewModel.cs"));
            Assert.Contains("ed_staffroll_image_pointer", src);
        }

        // ------------------------------------------------------------------ World Map Point Viewer
        [Fact]
        public void WorldMapPointView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "WorldMapPointView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void WorldMapPointViewModel_UsesWorldMapPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "WorldMapPointViewModel.cs"));
            Assert.Contains("worldmap_point_pointer", src);
        }

        // ------------------------------------------------------------------ World Map BGM Viewer
        [Fact]
        public void WorldMapBGMView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "WorldMapBGMView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void WorldMapBGMViewModel_UsesWorldMapBGMPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "WorldMapBGMViewModel.cs"));
            Assert.Contains("worldmap_bgm_pointer", src);
        }

        // ------------------------------------------------------------------ World Map Event Pointer Viewer
        [Fact]
        public void WorldMapEventPointerView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "WorldMapEventPointerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void WorldMapEventPointerViewModel_UsesWorldMapEventPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "WorldMapEventPointerViewModel.cs"));
            Assert.Contains("map_worldmapevent_pointer", src);
        }

        // ------------------------------------------------------------------ System Icon Viewer
        [Fact]
        public void SystemIconView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SystemIconViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void SystemIconViewModel_UsesSystemIconPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SystemIconViewerViewModel.cs"));
            Assert.Contains("system_icon_pointer", src);
        }

        // ------------------------------------------------------------------ Item Icon Viewer
        [Fact]
        public void ItemIconView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemIconViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemIconViewModel_UsesWeaponIconPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemIconViewerViewModel.cs"));
            Assert.Contains("system_weapon_icon_pointer", src);
        }

        // ------------------------------------------------------------------ System Hover Color Viewer
        [Fact]
        public void SystemHoverColorView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SystemHoverColorViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // ------------------------------------------------------------------ Battle BG Viewer
        [Fact]
        public void BattleBGView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "BattleBGViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void BattleBGViewModel_UsesBattleBGPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "BattleBGViewerViewModel.cs"));
            Assert.Contains("battle_bg_pointer", src);
        }

        // ------------------------------------------------------------------ Battle Terrain Viewer
        [Fact]
        public void BattleTerrainView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "BattleTerrainViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void BattleTerrainViewModel_UsesBattleTerrainPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "BattleTerrainViewerViewModel.cs"));
            Assert.Contains("battle_terrain_pointer", src);
        }

        // ------------------------------------------------------------------ Chapter Title Viewer
        [Fact]
        public void ChapterTitleView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ChapterTitleViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ChapterTitleViewModel_UsesChapterTitlePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ChapterTitleViewerViewModel.cs"));
            Assert.Contains("image_chapter_title_pointer", src);
        }

        // ------------------------------------------------------------------ Big CG Viewer
        [Fact]
        public void BigCGView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "BigCGViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void BigCGViewModel_UsesBigCGPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "BigCGViewerViewModel.cs"));
            Assert.Contains("bigcg_pointer", src);
        }

        // ------------------------------------------------------------------ OP Class Demo Viewer
        [Fact]
        public void OPClassDemoView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OPClassDemoViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void OPClassDemoViewModel_UsesOPClassDemoPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "OPClassDemoViewerViewModel.cs"));
            Assert.Contains("op_class_demo_pointer", src);
        }

        // ------------------------------------------------------------------ OP Class Font Viewer
        [Fact]
        public void OPClassFontView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OPClassFontViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void OPClassFontViewModel_UsesOPClassFontPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "OPClassFontViewerViewModel.cs"));
            Assert.Contains("op_class_font_pointer", src);
        }

        // ------------------------------------------------------------------ OP Prologue Viewer
        [Fact]
        public void OPPrologueView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OPPrologueViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void OPPrologueViewModel_UsesOPProloguePointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "OPPrologueViewerViewModel.cs"));
            Assert.Contains("op_prologue_image_pointer", src);
        }

        // ------------------------------------------------------------------ MainWindow has all new buttons
        [Fact]
        public void MainWindow_HasAllBatch2Buttons()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("OpenItemWeaponEffect_Click", src);
            Assert.Contains("OpenArenaClass_Click", src);
            Assert.Contains("OpenMonsterProbability_Click", src);
            Assert.Contains("OpenSummonUnit_Click", src);
            Assert.Contains("OpenSoundBossBGM_Click", src);
            Assert.Contains("OpenMenuDefinition_Click", src);
            Assert.Contains("OpenED_Click", src);
            Assert.Contains("OpenWorldMapPoint_Click", src);
            Assert.Contains("OpenSystemIcon_Click", src);
            Assert.Contains("OpenBattleBG_Click", src);
            Assert.Contains("OpenChapterTitle_Click", src);
            Assert.Contains("OpenOPClassDemo_Click", src);
        }

        // ================================================================== Migration batch - all new editors

        [Theory]
        [InlineData("ImagePortrait")]
        [InlineData("ImagePortraitFE6")]
        [InlineData("ImageBG")]
        [InlineData("ImageBattleAnime")]
        [InlineData("ImageCG")]
        [InlineData("ImageTSAEditor")]
        [InlineData("ImagePallet")]
        [InlineData("EventScript")]
        [InlineData("EventUnit")]
        [InlineData("EventBattleTalk")]
        [InlineData("EventHaiku")]
        [InlineData("EventForceSortie")]
        [InlineData("EventAssembler")]
        [InlineData("ProcsScript")]
        [InlineData("AIScript")]
        [InlineData("AIASMCoordinate")]
        [InlineData("AITarget")]
        [InlineData("MapEditor")]
        [InlineData("MapStyleEditor")]
        [InlineData("MapTerrainBGLookup")]
        [InlineData("SongTrack")]
        [InlineData("SongInstrument")]
        [InlineData("SongExchange")]
        [InlineData("UnitFE6")]
        [InlineData("ClassFE6")]
        [InlineData("ExtraUnit")]
        [InlineData("TextMain")]
        [InlineData("CString")]
        [InlineData("FontEditor")]
        [InlineData("PatchManager")]
        [InlineData("SkillAssignmentUnitSkillSystem")]
        [InlineData("SkillConfigSkillSystem")]
        [InlineData("WorldMapPath")]
        [InlineData("WorldMapImage")]
        [InlineData("Command85Pointer")]
        [InlineData("OAMSP")]
        [InlineData("ToolUndo")]
        [InlineData("ToolFELint")]
        [InlineData("HexEditor")]
        [InlineData("DisASM")]
        [InlineData("GrowSimulator")]
        [InlineData("Options")]
        public void MigratedEditor_ViewExists(string editorName)
        {
            string viewPath = Path.Combine(AvaloniaDir, "Views", $"{editorName}View.axaml.cs");
            Assert.True(File.Exists(viewPath), $"View file missing: {viewPath}");
        }

        [Theory]
        [InlineData("ImagePortrait")]
        [InlineData("EventScript")]
        [InlineData("AIScript")]
        [InlineData("MapEditor")]
        [InlineData("SongTrack")]
        [InlineData("UnitFE6")]
        [InlineData("TextMain")]
        [InlineData("PatchManager")]
        [InlineData("SkillAssignmentUnitSkillSystem")]
        [InlineData("WorldMapPath")]
        [InlineData("Command85Pointer")]
        [InlineData("ToolUndo")]
        [InlineData("HexEditor")]
        [InlineData("GrowSimulator")]
        public void MigratedEditor_ViewModelExists(string editorName)
        {
            string vmPath = Path.Combine(AvaloniaDir, "ViewModels", $"{editorName}ViewModel.cs");
            Assert.True(File.Exists(vmPath), $"ViewModel file missing: {vmPath}");
        }

        [Theory]
        [InlineData("ImagePortrait")]
        [InlineData("EventScript")]
        [InlineData("AIScript")]
        [InlineData("MapEditor")]
        [InlineData("SongTrack")]
        [InlineData("UnitFE6")]
        [InlineData("TextMain")]
        [InlineData("PatchManager")]
        [InlineData("WorldMapPath")]
        [InlineData("HexEditor")]
        public void MigratedEditor_ImplementsIEditorView(string editorName)
        {
            string viewPath = Path.Combine(AvaloniaDir, "Views", $"{editorName}View.axaml.cs");
            var src = File.ReadAllText(viewPath);
            Assert.Contains("IEditorView", src);
            Assert.Contains("SelectFirstItem", src);
            Assert.Contains("NavigateTo", src);
        }

        [Theory]
        [InlineData("ImagePortrait")]
        [InlineData("EventScript")]
        [InlineData("AIScript")]
        [InlineData("MapEditor")]
        [InlineData("SongTrack")]
        [InlineData("UnitFE6")]
        [InlineData("TextMain")]
        [InlineData("PatchManager")]
        [InlineData("WorldMapPath")]
        [InlineData("HexEditor")]
        public void MigratedEditor_HasAxamlFile(string editorName)
        {
            string axamlPath = Path.Combine(AvaloniaDir, "Views", $"{editorName}View.axaml");
            Assert.True(File.Exists(axamlPath), $"AXAML file missing: {axamlPath}");
        }

        [Fact]
        public void MainWindow_HasAllMigratedEditorButtons()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            // Image editors
            Assert.Contains("OpenImagePortrait_Click", src);
            Assert.Contains("OpenImageBG_Click", src);
            Assert.Contains("OpenImageTSAEditor_Click", src);
            // Event editors
            Assert.Contains("OpenEventScript_Click", src);
            Assert.Contains("OpenEventUnit_Click", src);
            Assert.Contains("OpenProcsScript_Click", src);
            // AI editors
            Assert.Contains("OpenAIScript_Click", src);
            Assert.Contains("OpenAITarget_Click", src);
            // Map editors
            Assert.Contains("OpenMapEditor_Click", src);
            Assert.Contains("OpenMapStyleEditor_Click", src);
            // Audio
            Assert.Contains("OpenSongTrack_Click", src);
            Assert.Contains("OpenSongExchange_Click", src);
            // Tools
            Assert.Contains("OpenToolUndo_Click", src);
            Assert.Contains("OpenHexEditor_Click", src);
            Assert.Contains("OpenDisASM_Click", src);
            Assert.Contains("OpenOptions_Click", src);
        }

        [Fact]
        public void MainWindow_HasAllMigratedEditorClickHandlers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            // Verify click handlers exist for migrated categories
            Assert.Contains("OpenImagePortrait_Click", src);
            Assert.Contains("OpenEventScript_Click", src);
            Assert.Contains("OpenAIScript_Click", src);
            Assert.Contains("OpenMapEditor_Click", src);
            Assert.Contains("OpenSongTrack_Click", src);
            Assert.Contains("OpenUnitFE6_Click", src);
            Assert.Contains("OpenTextMain_Click", src);
            Assert.Contains("OpenPatchManager_Click", src);
            Assert.Contains("OpenSkillAssignmentUnitSkillSystem_Click", src);
            Assert.Contains("OpenWorldMapPath_Click", src);
            Assert.Contains("OpenCommand85Pointer_Click", src);
            Assert.Contains("OpenToolUndo_Click", src);
            Assert.Contains("OpenHexEditor_Click", src);
            Assert.Contains("OpenOptions_Click", src);
        }

        // ------------------------------------------------------------------ NumericUpDown FormatString regression

        /// <summary>
        /// Avalonia NumericUpDown.Value is decimal? — the "X" hex format specifier
        /// is not supported for decimal type and causes FormatException during rendering,
        /// which prevents ALL NumericUpDown controls in the panel from displaying values.
        /// This test ensures no AXAML file uses FormatString="X".
        /// </summary>
        [Fact]
        public void NoAxamlFile_UsesHexFormatString_OnNumericUpDown()
        {
            var viewsDir = Path.Combine(AvaloniaDir, "Views");
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                var content = File.ReadAllText(file);
                if (content.Contains("FormatString=\"X\"") && content.Contains("NumericUpDown"))
                {
                    violations.Add(Path.GetFileName(file));
                }
            }

            Assert.True(violations.Count == 0,
                $"These AXAML files use FormatString=\"X\" on NumericUpDown (incompatible with decimal type): " +
                string.Join(", ", violations));
        }

        [Fact]
        public void UnitEditorView_NumericUpDownsHaveNoHexFormat()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", src);
            // Must still have NumericUpDown controls
            Assert.Contains("NumericUpDown", src);
            Assert.Contains("NameIdBox", src);
            Assert.Contains("ClassIdBox", src);
            Assert.Contains("LevelBox", src);
            Assert.Contains("HPBox", src);
        }

        [Fact]
        public void ItemEditorView_NumericUpDownsHaveNoHexFormat()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", src);
            Assert.Contains("NumericUpDown", src);
            Assert.Contains("NameIdBox", src);
            Assert.Contains("MightBox", src);
        }

        [Fact]
        public void ClassEditorView_NumericUpDownsHaveNoHexFormat()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", src);
            Assert.Contains("NumericUpDown", src);
            Assert.Contains("NameIdBox", src);
            Assert.Contains("BaseHpBox", src);
        }

        [Fact]
        public void CCBranchEditorView_NumericUpDownsHaveNoHexFormat()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "CCBranchEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", src);
            Assert.Contains("NumericUpDown", src);
            Assert.Contains("Promo1Box", src);
        }

        [Fact]
        public void TerrainNameEditorView_NumericUpDownsHaveNoHexFormat()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "TerrainNameEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", src);
            Assert.Contains("NumericUpDown", src);
            Assert.Contains("TextIdBox", src);
        }

        // ------------------------------------------------------------------ UpdateUI sets NumericUpDown values

        [Fact]
        public void UnitEditorView_UpdateUISetsNumericUpDownValues()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            // UpdateUI must set Value on each NumericUpDown
            Assert.Contains("NameIdBox.Value = ", src);
            Assert.Contains("ClassIdBox.Value = ", src);
            Assert.Contains("LevelBox.Value = ", src);
            Assert.Contains("HPBox.Value = ", src);
            Assert.Contains("StrBox.Value = ", src);
            Assert.Contains("SklBox.Value = ", src);
            Assert.Contains("SpdBox.Value = ", src);
            Assert.Contains("DefBox.Value = ", src);
            Assert.Contains("ResBox.Value = ", src);
            Assert.Contains("LckBox.Value = ", src);
            Assert.Contains("ConBox.Value = ", src);
        }

        [Fact]
        public void ItemEditorView_UpdateUISetsNumericUpDownValues()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("NameIdBox.Value = ", src);
            Assert.Contains("WeaponTypeBox.Value = ", src);
            Assert.Contains("MightBox.Value = ", src);
            Assert.Contains("HitBox.Value = ", src);
            Assert.Contains("PriceBox.Value = ", src);
        }

        [Fact]
        public void ClassEditorView_UpdateUISetsNumericUpDownValues()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml.cs"));
            Assert.Contains("NameIdBox.Value = ", src);
            Assert.Contains("ClassNumberBox.Value = ", src);
            Assert.Contains("BaseHpBox.Value = ", src);
            Assert.Contains("MovBox.Value = ", src);
            Assert.Contains("GrowHpBox.Value = ", src);
        }
    }
}
