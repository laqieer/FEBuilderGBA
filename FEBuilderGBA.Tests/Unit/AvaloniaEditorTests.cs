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

        [Fact]
        public void AddressListControl_SetItemsCallsSelectFirst()
        {
            // SetItems must auto-select the first item after loading
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            // Verify SelectFirst() is called inside SetItems after RefreshDisplay
            var setItemsStart = src.IndexOf("public void SetItems(");
            Assert.True(setItemsStart >= 0, "SetItems method not found");
            var methodBody = src.Substring(setItemsStart, src.IndexOf('}', setItemsStart) - setItemsStart + 1);
            Assert.Contains("RefreshDisplay()", methodBody);
            Assert.Contains("SelectFirst()", methodBody);
            // SelectFirst must come after RefreshDisplay
            Assert.True(methodBody.IndexOf("SelectFirst()") > methodBody.IndexOf("RefreshDisplay()"),
                "SelectFirst() must be called after RefreshDisplay()");
        }

        [Fact]
        public void AddressListControl_SearchBoxKeyDownHandlerExists()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("SearchBox.KeyDown += SearchBox_KeyDown", src);
        }

        [Fact]
        public void AddressListControl_SearchBoxKeyDownInvokesFilter()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("void SearchBox_KeyDown(object? sender, KeyEventArgs e)", src);
            Assert.Contains("ApplySearchFilter()", src);
        }

        [Fact]
        public void MainWindow_FilterTextBoxKeyDownHandlerExists()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("FilterTextBox.KeyDown += FilterTextBox_KeyDown", src);
        }

        [Fact]
        public void MainWindow_FilterTextBoxKeyDownInvokesApplyFilter()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("void FilterTextBox_KeyDown(object? sender, KeyEventArgs e)", src);
            // The handler must call ApplyFilter
            var handlerStart = src.IndexOf("void FilterTextBox_KeyDown(");
            Assert.True(handlerStart >= 0);
            var handlerBody = src.Substring(handlerStart, src.IndexOf('}', src.IndexOf('}', handlerStart) + 1) - handlerStart + 1);
            Assert.Contains("ApplyFilter(", handlerBody);
        }

        // ------------------------------------------------------------------ ViewModel bounds checks

        [Fact]
        public void UnitEditorViewModel_LoadUnitHasBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("addr + dataSize > (uint)rom.Data.Length", src);
        }

        [Fact]
        public void ItemEditorViewModel_LoadItemHasBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEditorViewModel.cs"));
            Assert.Contains("addr + dataSize > (uint)rom.Data.Length", src);
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
            Assert.Contains("addr + dataSize > (uint)rom.Data.Length", src);
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

        [Fact]
        public void OpenMapSettings_Click_DispatchesByVersion()
        {
            // The "Map Settings" button handler must dispatch to version-specific views
            // instead of always opening the generic MapSettingView
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));

            // Extract the OpenMapSettings_Click method body
            int methodStart = src.IndexOf("private void OpenMapSettings_Click");
            Assert.True(methodStart >= 0, "OpenMapSettings_Click not found");

            // Must check ver == 6 for FE6 dispatch
            string afterMethod = src.Substring(methodStart, Math.Min(1200, src.Length - methodStart));
            Assert.Contains("ver == 6", afterMethod);
            Assert.Contains("MapSettingFE6View", afterMethod);

            // Must check FE7U (version 7, !isMultibyte) before generic FE7
            int fe7uIndex = afterMethod.IndexOf("MapSettingFE7UView", System.StringComparison.Ordinal);
            int fe7Index = afterMethod.IndexOf("MapSettingFE7View", System.StringComparison.Ordinal);
            Assert.True(fe7uIndex >= 0, "FE7U-specific Map settings dispatch not found");
            Assert.True(fe7Index >= 0, "FE7 generic Map settings dispatch not found");
            Assert.True(fe7uIndex < fe7Index, "FE7U dispatch must appear before generic FE7 dispatch");
        }

        [Fact]
        public void MapSettingViewModel_GuardsFE6Version()
        {
            // The generic MapSettingViewModel must reject FE6 ROMs to prevent data corruption
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapSettingViewModel.cs"));
            Assert.Contains("rom.RomInfo.version == 6", src);
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

        // ------------------------------------------------------------------ Text Viewer Bounds Checks (issue #79)

        [Fact]
        public void TextViewerViewModel_HasRomDataNullCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TextViewerViewModel.cs"));
            // LoadTextList should check rom.Data == null
            Assert.Contains("rom.Data == null", src);
        }

        [Fact]
        public void TextViewerViewModel_HasPointerBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TextViewerViewModel.cs"));
            // Should have bounds checks like ptr + 4 > rom.Data.Length
            Assert.Contains("ptr + 4 > (uint)rom.Data.Length", src);
        }

        [Fact]
        public void TextViewerViewModel_HasTryCatchWithLogError()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TextViewerViewModel.cs"));
            Assert.Contains("Log.Error(\"TextViewerViewModel.LoadTextList\"", src);
            Assert.Contains("Log.Error(\"TextViewerViewModel.SearchTexts\"", src);
            Assert.Contains("Log.Error(\"TextViewerViewModel.LoadText\"", src);
        }

        [Fact]
        public void TextViewerViewModel_HasEntryAddrBoundsCheck()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "TextViewerViewModel.cs"));
            // Loop should check entryAddr + 4 > rom.Data.Length (not +3 >=)
            Assert.Contains("entryAddr + 4 > (uint)rom.Data.Length", src);
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

        // Issue #368 — Effectiveness view now uses item-driven master/detail.
        // The viewmodel walks the item table by +16 effectiveness pointer
        // (mirroring WinForms ItemEffectivenessForm.Init), so the source must
        // reference the item-pointer scan rather than the obsolete flat
        // weapon_effectiveness_2x3x_address-only loader.
        [Fact]
        public void ItemEffectivenessViewModel_UsesItemPointerAndPlusSixteen()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemEffectivenessViewerViewModel.cs"));
            Assert.Contains("item_pointer", src);
            Assert.Contains("item_datasize", src);
            Assert.Contains("+ 16", src); // +16 is the effectiveness pointer offset
        }

        // Issue #368 — the rewritten view must expose the WinForms control set.
        // #360 final: ClassIdInput + ClassNameLabel merged into a single
        // IdFieldControl ClassIdBox (which provides hyperlink label + value +
        // inline name preview + Jump + Pick). The icon stays separate.
        [Fact]
        public void ItemEffectivenessView_HasItemDrivenLayout()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEffectivenessViewerView.axaml"));
            Assert.Contains("InnerList", src);
            Assert.Contains("ClassIdBox", src); // was ClassIdInput before #360 final
            Assert.Contains("ClassIconImage", src);
            Assert.Contains("IndependenceButton", src);
            Assert.Contains("ListExpandsButton", src);
            // #649: ReloadListButton was migrated to EditorTopBar — check the
            // preserved legacy AutomationId instead of the obsolete Name.
            Assert.Contains("ItemEffectivenessViewer_ReloadList_Button", src);
            Assert.Contains("ItemListBox", src);
        }

        // ------------------------------------------------------------------ Item Promotion Viewer
        [Fact]
        public void ItemPromotionView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemPromotionViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        // Issue #368 — Promotion view now uses item-driven master/detail
        // sourcing the fixed CC items from cc_*_pointer in RomInfo.
        [Fact]
        public void ItemPromotionViewModel_UsesCCItemPointers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemPromotionViewerViewModel.cs"));
            Assert.Contains("cc_item_hero_crest_pointer", src);
            Assert.Contains("cc_item_knight_crest_pointer", src);
            Assert.Contains("cc_guiding_ring_pointer", src);
        }

        // Issue #368 — the rewritten view must expose the WinForms control set
        // plus the X_IER_Patch warning label.
        // #360 final: ClassIdInput + ClassNameLabel merged into a single
        // IdFieldControl ClassIdBox (which provides hyperlink label + value +
        // inline name preview + Jump + Pick). The icon stays separate.
        [Fact]
        public void ItemPromotionView_HasItemDrivenLayout()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemPromotionViewerView.axaml"));
            Assert.Contains("InnerList", src);
            Assert.Contains("ClassIdBox", src); // was ClassIdInput before #360 final
            Assert.Contains("ClassIconImage", src);
            Assert.Contains("ListExpandsButton", src);
            // #649: ReloadListButton was migrated to EditorTopBar — check the
            // preserved legacy AutomationId instead of the obsolete Name.
            Assert.Contains("ItemPromotionViewer_ReloadList_Button", src);
            Assert.Contains("X_IER_Patch", src);
        }

        // ------------------------------------------------------------------ Item Shop Viewer
        // After the #369 parity refactor, the VM delegates shop enumeration to
        // ItemShopCore. The `item_shop_hensei_pointer` reference moved into
        // ItemShopCore (covered by ItemShopCoreTests.ItemShopCore_References_HenseiPointer).
        [Fact]
        public void ItemShopView_HasSelectFirstItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemShopViewerView.axaml.cs"));
            Assert.Contains("public void SelectFirstItem()", src);
        }

        [Fact]
        public void ItemShopViewModel_UsesShopCore()
        {
            // Replaces the pre-#369 ItemShopViewModel_UsesShopPointer test —
            // the hensei pointer reference moved into ItemShopCore.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemShopViewerViewModel.cs"));
            Assert.Contains("ItemShopCore.MakeShopList", src);
        }

        [Fact]
        public void ItemShopView_HasShopList()
        {
            // #369 parity: the view must have a dedicated ShopList control
            // (the 3-region WinForms layout). A view that only has SlotList is
            // the pre-#369 flat layout.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemShopViewerView.axaml"));
            Assert.Contains("Name=\"ShopList\"", src);
            Assert.Contains("Name=\"SlotList\"", src);
        }

        [Fact]
        public void ItemShopView_HasSlotControls()
        {
            // #369 parity: Append Slot / Remove Last Slot / Reload buttons.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemShopViewerView.axaml"));
            Assert.Contains("Name=\"AppendSlotButton\"", src);
            Assert.Contains("Name=\"RemoveLastSlotButton\"", src);
            Assert.Contains("Name=\"ReloadButton\"", src);
        }

        [Fact]
        public void ItemShopViewModel_HasAppendAndRemoveSlot()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemShopViewerViewModel.cs"));
            Assert.Contains("TryAppendSlotInPlace", src);
            Assert.Contains("AppendSlotWithRelocation", src);
            Assert.Contains("RemoveLastSlot", src);
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

        // Issue #370 regression guards — see PR description.

        [Fact]
        public void ItemWeaponTriangleViewModel_UsesSignedFieldsForBonusPenalty()
        {
            // Bytes 2 (atk-bonus) and 3 (hit-bonus) are SIGNED (sbyte). The
            // ViewModel must declare them with the EditorFormRef "S" prefix
            // (FieldType.SByte). Catches regressions where someone reverts to
            // unsigned `B2`/`B3` and breaks negative-value display.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemWeaponTriangleViewerViewModel.cs"));
            Assert.Contains("\"S2\"", src);
            Assert.Contains("\"S3\"", src);
            Assert.Contains("int Bonus", src);
            Assert.Contains("int Penalty", src);
        }

        [Fact]
        public void ItemWeaponTriangleView_NumericUpDownAcceptsNegativeBonus()
        {
            // XAML must allow -128..127 for bonus/penalty so the NumericUpDown
            // does not clamp 0xF1 to 0 (issue #370 root cause).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemWeaponTriangleViewerView.axaml"));
            Assert.Contains("Name=\"BonusBox\"", src);
            Assert.Contains("Name=\"PenaltyBox\"", src);
            // Both boxes must have Minimum="-128" and Maximum="127"
            int bonusIdx = src.IndexOf("Name=\"BonusBox\"", System.StringComparison.Ordinal);
            int penaltyIdx = src.IndexOf("Name=\"PenaltyBox\"", System.StringComparison.Ordinal);
            // Find the start of each NumericUpDown declaration containing those names.
            int bonusStart = src.LastIndexOf("<NumericUpDown", bonusIdx, System.StringComparison.Ordinal);
            int penaltyStart = src.LastIndexOf("<NumericUpDown", penaltyIdx, System.StringComparison.Ordinal);
            string bonusDecl = src.Substring(bonusStart, src.IndexOf("/>", bonusStart, System.StringComparison.Ordinal) - bonusStart);
            string penaltyDecl = src.Substring(penaltyStart, src.IndexOf("/>", penaltyStart, System.StringComparison.Ordinal) - penaltyStart);
            Assert.Contains("Minimum=\"-128\"", bonusDecl);
            Assert.Contains("Maximum=\"127\"", bonusDecl);
            Assert.Contains("Minimum=\"-128\"", penaltyDecl);
            Assert.Contains("Maximum=\"127\"", penaltyDecl);
        }

        [Fact]
        public void ItemWeaponTriangleView_UsesWeaponTypePairIconLoader()
        {
            // The list-icon loader must read weapon-type bytes from ROM, NOT
            // load item icons via name-prefix parsing.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemWeaponTriangleViewerView.axaml.cs"));
            Assert.Contains("WeaponTypePairFromAddrU8Loader", src);
            // Negative assertion: the old ItemIconLoader call must be gone.
            Assert.DoesNotContain("ListIconLoaders.ItemIconLoader(items", src);
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
            // #440 — VM now delegates list construction to
            // FEBuilderGBA.Core.ItemUsagePointerCore which holds the
            // RomInfo.item_usability_array_pointer reference. Assert the
            // Core type is consumed by the VM (Promotion/StatBooster/IER
            // editor calls share the same dispatch table).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemUsagePointerViewerViewModel.cs"));
            Assert.Contains("ItemUsagePointerCore", src);
            // FilterKind.Usability is the canonical "item_usability_array_pointer"
            // slot in the new dispatch — the VM must reference it (or the enum).
            Assert.Contains("FilterKind", src);
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
            // After #952 the default (typeIndex 0) MAP filter resolves its base
            // pointer through the shared MapChangeCore.GetPlistBasePointer seam
            // (PlistType.MAP → RomInfo.map_map_pointer_pointer) instead of
            // referencing the RomInfo field literally, and builds the list with
            // the map-name resolver (resolved "MAP {name}" labels, not raw 0x…).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "MapPointerViewModel.cs"));
            // The VM routes the default filter through the canonical MAP base.
            Assert.Contains("PlistType.MAP", src);
            // ...and builds resolved labels via the shared resolver seam.
            Assert.Contains("MapPListResolverCore.ResolveLabel", src);
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
        public void WorldMapEventPointerViewModel_UsesFE8StageEventPointers()
        {
            // Pre-#432 this assertion accepted `map_worldmapevent_pointer` —
            // an FE6-only slot that is 0 on FE8, so the editor was dead on
            // its target platform. The rewrite uses the correct FE8 pointers.
            // Strip C# line comments before the negative assertion — comments
            // may legitimately mention the old pointer name to explain the
            // change history.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "WorldMapEventPointerViewModel.cs"));
            Assert.Contains("worldmap_event_on_stageclear_pointer", src);
            Assert.Contains("worldmap_event_on_stageselect_pointer", src);
            var codeOnly = System.Text.RegularExpressions.Regex.Replace(src, @"//[^\n]*", "");
            Assert.DoesNotContain("map_worldmapevent_pointer", codeOnly);
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
        public void ItemIconViewModel_UsesIconPointer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "ItemIconViewerViewModel.cs"));
            Assert.Contains("icon_pointer", src);
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
        [InlineData("FontZH")]
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

        [Fact]
        public void MainWindow_AIScriptButton_OpensAIScriptView()
        {
            // PR #410 re-wires the main "AI Script" button to open the
            // master/detail AIScriptView (parity with WF MainFE8Form ->
            // AIScriptForm). The previous wiring incorrectly opened
            // AIScriptCategorySelectView, which is the WF
            // AIScriptCategorySelectForm — a sub-dialog of the master
            // editor, NOT the front-door entry point. This test asserts
            // the corrected wiring.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("OpenAIScript_Click", src);
            Assert.Contains("WindowManager.Instance.Open<AIScriptView>()", src);
        }

        [Fact]
        public void MainWindow_ProcsScriptButton_OpensCategorySelectView()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            // The Procs Script main entry point must open the category select view, not the placeholder
            Assert.Contains("Open<ProcsScriptCategorySelectView>", src);
            // Ensure the old placeholder wiring is gone from the click handler
            Assert.DoesNotContain("OpenProcsScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ProcsScriptView>", src);
        }

        [Fact]
        public void MainWindow_FormRegistry_WiresAIScriptViewDirectly_ProcsScriptViewToCategorySelect()
        {
            // PR #410: the registry entry for AIScriptView now opens
            // AIScriptView (the master/detail editor), parity with WF
            // MainFE8Form's "AI Script" button. ProcsScript still uses
            // the category-select front-door because its master/detail
            // editor has not yet been rebuilt — that's a separate
            // follow-up.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("(\"AIScriptView\", () => wm.Open<AIScriptView>())", src);
            Assert.Contains("(\"ProcsScriptView\", () => wm.Open<ProcsScriptCategorySelectView>())", src);
        }

        // ------------------------------------------------------------------ NumericUpDown FormatString regression

        /// <summary>
        /// Avalonia NumericUpDown.Value is decimal? — ANY hex format specifier
        /// ("X", "X2", "X4", "X8", …) is not supported for decimal type and causes
        /// FormatException during rendering, which prevents ALL NumericUpDown
        /// controls assigned after the throwing one from displaying values.
        ///
        /// Original guard (issue #58) only caught the bare "X" format. The
        /// 2026-05-22 scheduled E2E failures (#498/#502/#509/#514/#515) showed
        /// "X8" was still leaking through. This guard parses each AXAML as XML
        /// and inspects every NumericUpDown element's FormatString attribute,
        /// so it catches every hex variant AND is immune to false positives
        /// from narrative XML comments that quote `FormatString="X*"`.
        ///
        /// Scans both `FEBuilderGBA.Avalonia/Views/` AND
        /// `FEBuilderGBA.Avalonia/Controls/` so shared controls like
        /// `IdFieldControl` are covered.
        /// </summary>
        [Fact]
        public void NoAxamlFile_UsesHexFormatString_OnNumericUpDown()
        {
            var viewsDir = Path.Combine(AvaloniaDir, "Views");
            var controlsDir = Path.Combine(AvaloniaDir, "Controls");
            var violations = new List<string>();

            // Loud assert that both target directories exist. Without this,
            // a miscomputed AvaloniaDir would silently leave axamlFiles empty
            // and the test would falsely pass (Copilot PR #545 review #1).
            Assert.True(Directory.Exists(viewsDir),
                $"Avalonia Views directory not found at '{viewsDir}'. " +
                $"AvaloniaDir resolves to '{AvaloniaDir}' — verify the test fixture.");
            Assert.True(Directory.Exists(controlsDir),
                $"Avalonia Controls directory not found at '{controlsDir}'. " +
                $"AvaloniaDir resolves to '{AvaloniaDir}' — verify the test fixture.");

            // Compose the list of AXAML files from both directories.
            var axamlFiles = new List<string>();
            axamlFiles.AddRange(Directory.GetFiles(viewsDir, "*.axaml"));
            axamlFiles.AddRange(Directory.GetFiles(controlsDir, "*.axaml"));

            // Sanity: at least one AXAML file must exist; otherwise the scan
            // is a no-op and the test fails loudly (Copilot PR #545 review #1).
            Assert.True(axamlFiles.Count > 0,
                $"No AXAML files found under {viewsDir} or {controlsDir} — the guard would scan nothing.");

            // Hex format pattern — matches "X" or "x" optionally followed by digits.
            // Case-insensitive because `decimal.ToString("x*")` throws the same
            // FormatException as the uppercase variant (Copilot PR #545 review).
            var hexFormat = new System.Text.RegularExpressions.Regex(
                "^X\\d*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var file in axamlFiles)
            {
                // Parse as XML to inspect actual attributes (not raw text). This
                // automatically excludes comments and inner text, which is what
                // Copilot CLI review point 3 flagged on plan v1.
                System.Xml.Linq.XDocument doc;
                try
                {
                    doc = System.Xml.Linq.XDocument.Load(file);
                }
                catch (System.Xml.XmlException ex)
                {
                    // Malformed AXAML must fail the test — otherwise a broken
                    // file could hide a hex FormatString and the guard would
                    // silently miss it (Copilot PR #545 review #2).
                    violations.Add($"{Path.GetFileName(file)}: malformed XML — {ex.Message}");
                    continue;
                }

                // Find any NumericUpDown element (regardless of namespace) whose
                // FormatString attribute matches the hex pattern.
                foreach (var el in doc.Descendants())
                {
                    if (el.Name.LocalName != "NumericUpDown") continue;
                    var fmt = el.Attribute("FormatString")?.Value;
                    if (fmt == null) continue;
                    if (hexFormat.IsMatch(fmt))
                    {
                        var name = el.Attribute("Name")?.Value
                                ?? el.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                                ?? "(unnamed)";
                        violations.Add($"{Path.GetFileName(file)}:{name} FormatString=\"{fmt}\"");
                    }
                }
            }

            Assert.True(violations.Count == 0,
                $"NumericUpDown FormatString guard found {violations.Count} violation(s) " +
                $"(hex format on decimal NumericUpDown OR malformed AXAML): " +
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
            Assert.Contains("ClassIdCombo", src);
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
            // CCBranch's Promotion Class 1/2 NumericUpDowns moved into the
            // reusable IdFieldControl (#366). The view file no longer hosts
            // the NumericUpDown literally; assert against IdFieldControl
            // markup + the shared IdFieldControl.axaml instead. The
            // Promo1Box/Promo2Box names are preserved as the IdFieldControl
            // Name= attribute so existing tests and E2E selectors that look
            // up by name continue to resolve the right host control.
            var viewSrc = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "CCBranchEditorView.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", viewSrc);
            Assert.Contains("IdFieldControl", viewSrc);
            Assert.Contains("Promo1Box", viewSrc);
            // The actual NumericUpDown markup now lives in IdFieldControl.axaml.
            var idFieldSrc = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "IdFieldControl.axaml"));
            Assert.DoesNotContain("FormatString=\"X\"", idFieldSrc);
            Assert.Contains("NumericUpDown", idFieldSrc);
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
            Assert.Contains("ClassIdCombo.SelectedIndex", src);
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
            Assert.Contains("WeaponTypeCombo.SelectedIndex", src);
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
        // ================================================================== Version Filtering

        [Fact]
        public void MainWindow_HasUpdateEditorVisibility()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("void UpdateEditorVisibility()", src);
        }

        [Fact]
        public void MainWindow_LoadRomFileCallsUpdateEditorVisibility()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            // Find LoadRomFile method and ensure it calls UpdateEditorVisibility
            int loadRomStart = src.IndexOf("public bool LoadRomFile(");
            Assert.True(loadRomStart >= 0, "LoadRomFile method not found");
            // Check UpdateEditorVisibility appears after LoadRomFile definition
            int callSite = src.IndexOf("UpdateEditorVisibility()", loadRomStart);
            Assert.True(callSite > loadRomStart, "UpdateEditorVisibility() must be called in LoadRomFile");
        }

        [Fact]
        public void MainWindow_MonstersSection_HasExpander()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("Name=\"MonstersExpander\"", axaml);
            Assert.Contains("Name=\"MonstersPanel\"", axaml);
        }

        [Fact]
        public void MainWindow_SummonsSection_HasExpander()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("Name=\"SummonsExpander\"", axaml);
            Assert.Contains("Name=\"SummonsPanel\"", axaml);
        }

        [Fact]
        public void MainWindow_SkillsSection_HasExpanders()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("Name=\"SkillsExpander\"", axaml);
            Assert.Contains("Name=\"SkillsPanel\"", axaml);
            Assert.Contains("Name=\"SkillsExtExpander\"", axaml);
            Assert.Contains("Name=\"SkillsExtPanel\"", axaml);
        }

        [Fact]
        public void MainWindow_SensekiCommentButton_HasName()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("Name=\"SensekiCommentButton\"", axaml);
        }

        [Fact]
        public void MainWindow_VersionTagOrder_FE7U_BeforeFE7()
        {
            // GetVersionVisibility must check FE7U before FE7 to avoid false match
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            int fe7uPos = src.IndexOf("content.Contains(\"(FE7U)\")");
            int fe7Pos = src.IndexOf("content.Contains(\"(FE7)\")");
            Assert.True(fe7uPos >= 0, "FE7U check not found");
            Assert.True(fe7Pos >= 0, "FE7 check not found");
            Assert.True(fe7uPos < fe7Pos, "(FE7U) must be checked before (FE7) to avoid false match");
        }

        [Fact]
        public void MainWindow_HasResetAllButtonVisibility()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("ResetAllButtonVisibility(", src);
        }

        // ------------------------------------------------------------------ AddressListItem & Icons

        [Fact]
        public void AddressListItem_ClassExists_WithTextAndIconProperties()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public class AddressListItem", src);
            Assert.Contains("public string Text { get; set; }", src);
            Assert.Contains("public Bitmap? Icon { get; set; }", src);
        }

        [Fact]
        public void AddressListControl_HasSetItemsWithIconsMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void SetItemsWithIcons(List<AddrResult> items, Func<int, Bitmap?> iconLoader)", src);
        }

        [Fact]
        public void AddressListControl_SetItemsStillExists_BackwardCompatible()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void SetItems(List<AddrResult> items)", src);
            // SetItems should clear icon loader
            Assert.Contains("_iconLoader = null;", src);
        }

        [Fact]
        public void AddressListControl_DisplayItemsUseAddressListItem()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("ObservableCollection<AddressListItem>", src);
        }

        [Fact]
        public void AddressListControl_Axaml_HasDataTemplate_WithImageAndTextBlock()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml"));
            Assert.Contains("<DataTemplate>", axaml);
            Assert.Contains("Source=\"{Binding Icon}\"", axaml);
            Assert.Contains("Text=\"{Binding Text}\"", axaml);
            // #654: the icon image MUST NOT bind IsVisible to "Icon != null".
            // Doing so collapses the 32x32 slot when the loader returns null
            // (e.g. the first row whose prefix parses to ID 0), shifting the
            // row's text left and visually losing the icon column. WinForms
            // reserves OWNER_DRAW_ICON_SIZE unconditionally; the Avalonia
            // template now does the same by omitting the IsVisible binding.
            Assert.DoesNotContain("ObjectConverters.IsNotNull", axaml);
        }

        [Fact]
        public void ImageConversionHelper_ExistsInPreviewIconHelper()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("public static class ImageConversionHelper", src);
            Assert.Contains("public static Bitmap? ToAvaloniaBitmap(IImage? image)", src);
        }

        [Fact]
        public void ImageConversionHelper_HandlesNullInput()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("if (image == null) return null;", src);
        }

        [Fact]
        public void ImageConversionHelper_UsesPngEncoding()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("EncodePng()", src);
            Assert.Contains("new MemoryStream(pngData)", src);
            Assert.Contains("new Bitmap(ms)", src);
        }

        [Fact]
        public void UnitEditorView_UsesSetItemsWithIcons()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("ListIconLoaders.UnitPortraitLoader", src);
        }

        [Fact]
        public void UnitEditorView_PortraitThumbnailUsesResolveHelper()
        {
            // Portrait ID resolution is now centralized in ListIconLoaders
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ListIconLoaders.cs"));
            Assert.Contains("PreviewIconHelper.ResolveUnitPortraitId(addr)", src);
            Assert.Contains("PreviewIconHelper.LoadPortraitMini(portraitId)", src);
            Assert.Contains("ImageConversionHelper.ToAvaloniaBitmap(img)", src);
        }

        [Fact]
        public void UnitEditorView_PortraitThumbnailDisposesIImage()
        {
            // The IImage from PreviewIconHelper should be disposed after conversion in ListIconLoaders
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ListIconLoaders.cs"));
            Assert.Contains("using var img = PreviewIconHelper.LoadPortraitMini", src);
        }

        [Fact]
        public void AddressListControl_RefreshDisplay_InvokesIconLoader()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_iconLoader?.Invoke(i)", src);
        }

        // ------------------------------------------------------------------ Issue #56/#140 — Portrait Fix

        [Fact]
        public void UnitEditorViewModel_UsesPortraitRendererCore()
        {
            // LoadPortraitImage must use PortraitRendererCore.DrawPortraitUnit
            // instead of ImageUtilCore.LoadROMTiles4bpp for the main portrait (#140)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitEditorViewModel.cs"));
            Assert.Contains("PortraitRendererCore.DrawPortraitUnit(", src);
            Assert.DoesNotContain("LoadROMTiles4bpp(imgAddr, palette, 4, 4", src);
        }

        [Fact]
        public void UnitFE6ViewModel_UsesPortraitRendererCoreFE6()
        {
            // FE6 must use the FE6-specific renderer for full 96x80 portraits (#56)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitFE6ViewModel.cs"));
            Assert.Contains("PortraitRendererCoreFE6.DrawPortraitUnitFE6(", src);
            Assert.DoesNotContain("LoadROMTiles4bpp(imgAddr, palette, 4, 4", src);
        }

        [Fact]
        public void UnitFE6View_HasSetItemsWithIcons()
        {
            // FE6 view must also show portrait icons in the list (#56)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitFE6View.axaml.cs"));
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("ListIconLoaders.UnitPortraitLoader", src);
        }

        [Fact]
        public void UnitEditorView_HasPortraitFallbackViaResolveHelper()
        {
            // Portrait fallback via ResolveUnitPortraitId now in centralized ListIconLoaders (#56)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "ListIconLoaders.cs"));
            Assert.Contains("PreviewIconHelper.ResolveUnitPortraitId", src);
        }

        [Fact]
        public void UnitFE6View_HasPortraitFallbackViaResolveHelper()
        {
            // FE6 view uses centralized ListIconLoaders which calls the shared resolve helper (#56)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitFE6View.axaml.cs"));
            Assert.Contains("ListIconLoaders.UnitPortraitLoader", src);
        }

        [Fact]
        public void PreviewIconHelper_HasResolveAndClassPortraitHelpers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("public static uint GetClassPortraitId(uint classId)", src);
            Assert.Contains("public static uint ResolveUnitPortraitId(uint unitAddr)", src);
            Assert.Contains("rom.u16(classAddr + 8)", src);
        }

        // ------------------------------------------------------------------ Issue #126 — Auto-Save

        [Fact]
        public void AutoSaveService_ComputeSidecarPath_HasCorrectNaming()
        {
            // Verify the source code computes {base}.autosave.gba (not {base}.gba.autosave)
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "AutoSaveService.cs"));
            Assert.Contains("baseName + \".autosave.gba\"", src);
            Assert.Contains("GetFileNameWithoutExtension", src);
        }

        [Fact]
        public void AutoSaveService_SidecarPath_NeverMatchesPrimary()
        {
            // Verify there's a guard against overwriting the primary ROM
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "AutoSaveService.cs"));
            Assert.Contains("sidecar, rom.Filename", src);
            Assert.Contains("sidecar, _romFilename", src);
        }

        [Fact]
        public void AutoSaveService_UsesAtomicWrite()
        {
            // Verify writes go to temp file first, then move
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "AutoSaveService.cs"));
            Assert.Contains("File.WriteAllBytes(tempPath", src);
            Assert.Contains("File.Move(tempPath", src);
        }

        [Fact]
        public void AutoSaveService_WritesOffUIThread()
        {
            // Verify disk write runs on background thread
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "AutoSaveService.cs"));
            Assert.Contains("Task.Run(", src);
        }

        [Fact]
        public void MainWindow_ReferencesAutoSaveService()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("AutoSaveService", src);
            Assert.Contains("TryStartAutoSave", src);
        }

        [Fact]
        public void OptionsView_HasAutoSaveControls()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OptionsView.axaml"));
            Assert.Contains("AutoSaveCheckBox", axaml);
            Assert.Contains("AutoSaveIntervalBox", axaml);
        }

        // ------------------------------------------------------------------ Issue #129 — Submodule Remote URLs

        [Fact]
        public void OptionsView_HasSubmoduleUrlFields()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OptionsView.axaml"));
            Assert.Contains("Patch2UrlTextBox", axaml);
            Assert.Contains("FERepoUrlTextBox", axaml);
            Assert.Contains("FERepoMusicUrlTextBox", axaml);
        }

        [Fact]
        public void OptionsViewModel_HasSubmoduleUrlProperties()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "OptionsViewModel.cs"));
            Assert.Contains("SubmodulePatch2Url", src);
            Assert.Contains("SubmoduleFERepoUrl", src);
            Assert.Contains("SubmoduleFERepoMusicUrl", src);
            Assert.Contains("ApplySubmoduleRemotes", src);
        }

        // ------------------------------------------------------------------ Preview Icons

        [Fact]
        public void ClassEditorView_HasTryShowListPreview()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml.cs"));
            Assert.Contains("TryShowListPreview()", src);
            Assert.Contains("PreviewIconHelper.LoadClassWaitIcon", src);
        }

        [Fact]
        public void ItemEditorView_HasTryShowListPreview()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml.cs"));
            Assert.Contains("TryShowListPreview()", src);
            Assert.Contains("PreviewIconHelper.LoadItemIcon", src);
        }

        [Fact]
        public void ItemFE6View_HasListPreviewBorder()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemFE6View.axaml"));
            Assert.Contains("ListPreviewBorder", axaml);
            Assert.Contains("ListPreviewImage", axaml);
            Assert.Contains("ListPreviewName", axaml);
        }

        [Fact]
        public void ItemFE6View_HasTryShowListPreview()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemFE6View.axaml.cs"));
            Assert.Contains("TryShowListPreview()", src);
            Assert.Contains("PreviewIconHelper.LoadItemIcon", src);
        }

        [Fact]
        public void PreviewIconHelper_HandlesNullROM()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("if (rom?.RomInfo == null", src);
        }

        [Fact]
        public void PreviewIconHelper_LoadItemIcon_Exists()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("public static IImage LoadItemIcon(uint iconIndex)", src);
        }

        [Fact]
        public void PreviewIconHelper_LoadClassWaitIcon_Uses8ByteEntries()
        {
            // #991: the wait-icon decode pipeline (8-byte stride + sprite pointer
            // at offset +4) MOVED VERBATIM into the cross-platform Core seam
            // FEBuilderGBA.Core/WaitIconRenderCore.cs (single source of truth);
            // PreviewIconHelper.LoadClassWaitIcon now DELEGATES to it. Assert the
            // same 8-byte-stride / +4-pointer CONTRACT in the new seam, and that
            // the helper still delegates (so behavior is byte-identical).
            var coreSrc = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "WaitIconRenderCore.cs"));
            Assert.Contains("waitIconIndex * 8", coreSrc);
            Assert.Contains("entryAddr + 4", coreSrc);

            var helperSrc = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("WaitIconRenderCore.RenderClassWaitIcon", helperSrc);
        }

        [Fact]
        public void PreviewIconHelper_LoadPortraitMini_ChecksZeroId()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Services", "PreviewIconHelper.cs"));
            Assert.Contains("portraitId == 0", src);
        }

        // ------------------------------------------------------------------ OAM Sprite Viewer

        [Fact]
        public void OAMSpriteViewerView_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", "OAMSpriteViewerView.axaml")));
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Views", "OAMSpriteViewerView.axaml.cs")));
        }

        [Fact]
        public void OAMSpriteViewerViewModel_Exists()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "ViewModels", "OAMSpriteViewerViewModel.cs")));
        }

        [Fact]
        public void OAMSpriteViewerView_HasSectionCombo()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OAMSpriteViewerView.axaml"));
            Assert.Contains("SectionCombo", axaml);
            Assert.Contains("FrameUpDown", axaml);
            Assert.Contains("FrameImageControl", axaml);
        }

        [Fact]
        public void OAMSpriteViewerView_HasFrameNavigation()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "OAMSpriteViewerView.axaml.cs"));
            Assert.Contains("PrevFrame_Click", src);
            Assert.Contains("NextFrame_Click", src);
            Assert.Contains("OnSectionChanged", src);
        }

        [Fact]
        public void OAMSpriteViewerViewModel_HasLoadEntry()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "OAMSpriteViewerViewModel.cs"));
            Assert.Contains("public void LoadEntry(uint addr)", src);
            Assert.Contains("public void LoadSectionFrames(int sectionIndex)", src);
            Assert.Contains("public void GoToFrame(int frameIndex)", src);
        }

        [Fact]
        public void EasyModePanel_HasOAMSpriteViewerButton()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EasyModePanel.axaml"));
            Assert.Contains("OAM Sprite Viewer", axaml);
        }

        // ------------------------------------------------------------------ Keyboard shortcuts

        [Fact]
        public void MainWindow_HasKeyboardShortcut_CtrlO_OpenRom()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("InputGesture=\"Ctrl+O\"", axaml);
        }

        [Fact]
        public void MainWindow_HasKeyboardShortcut_CtrlS_Save()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("InputGesture=\"Ctrl+S\"", axaml);
        }

        [Fact]
        public void MainWindow_HasKeyboardShortcut_CtrlZ_Undo()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("InputGesture=\"Ctrl+Z\"", axaml);
        }

        [Fact]
        public void MainWindow_HasKeyboardShortcut_CtrlY_Redo()
        {
            // Redo menu item was removed (PR #91), but Ctrl+Y is handled in code-behind
            // to show a "not supported" status message
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.DoesNotContain("RedoMenuItem", axaml);
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("Key.Y", src);
            Assert.Contains("Redo is not supported", src);
        }

        [Fact]
        public void MainWindow_HasKeyboardShortcut_F5_Refresh()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("InputGesture=\"F5\"", axaml);
            Assert.Contains("Refresh_Click", axaml);
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("Key.F5", src);
            Assert.Contains("OnAllFormsInvalidated", src);
            Assert.Contains("Refresh_Click", src);
        }

        [Fact]
        public void MainWindow_HasOnKeyDown_CtrlF_FocusFilter()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("OnKeyDown", src);
            Assert.Contains("Key.F", src);
            Assert.Contains("FilterTextBox.Focus()", src);
        }

        // ------------------------------------------------------------------ DisASMView is functional, not placeholder

        [Fact]
        public void DisASMView_HasDisassembleButton_NotPlaceholder()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "DisASMView.axaml"));
            // Must have a Disassemble button and output TextBox — not just an address label
            Assert.Contains("Disassemble_Click", axaml);
            Assert.Contains("OutputBox", axaml);
            Assert.Contains("AddressBox", axaml);
            Assert.Contains("LengthBox", axaml);
        }

        [Fact]
        public void DisASMView_CodeBehind_CallsRunDisassembly()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "DisASMView.axaml.cs"));
            Assert.Contains("RunDisassembly", src);
            Assert.Contains("Disassemble_Click", src);
        }

        [Fact]
        public void DisASMViewModel_UsesDisassemblerCore()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "DisASMViewModel.cs"));
            Assert.Contains("DisassemblerCore", src);
            Assert.Contains("DisassembleRange", src);
            Assert.Contains("RunDisassembly", src);
        }

        [Fact]
        public void MainWindow_DisASMButton_OpensDisASMView()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("OpenDisASM_Click", axaml);

            var cs = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("OpenDisASM_Click", cs);
            Assert.Contains("Open<DisASMView>", cs);
        }

        // ------------------------------------------------------------------ MIDI Import Metadata

        [Fact]
        public void SongTrackImportMidiView_HasMidiMetadataUI()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SongTrackImportMidiView.axaml"));
            Assert.Contains("MidiInfoBorder", axaml);
            Assert.Contains("MidiInfoLabel", axaml);
            Assert.Contains("BrowseMidi_Click", axaml);
            // #972: the stub note is gone — the dedicated window now performs the
            // real MIDI write-back via the Import to ROM button.
            Assert.DoesNotContain("not yet fully implemented", axaml);
            Assert.Contains("ImportMidi_Click", axaml);
            Assert.Contains("Import to ROM", axaml);
        }

        [Fact]
        public void SongTrackImportMidiView_CodeBehind_ParsesMidiInfo()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SongTrackImportMidiView.axaml.cs"));
            Assert.Contains("ParseMidiInfo", src);
            Assert.Contains("MidiInfoText", src);
            // #972: real write-back under an undo scope (no more stub dialog).
            Assert.DoesNotContain("not yet fully implemented", src);
            Assert.Contains("_vm.ImportMidi(", src);
            Assert.Contains("_undoService.Begin(", src);
        }

        [Fact]
        public void SongTrackImportMidiViewModel_HasFormatMidiMetadata()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SongTrackImportMidiViewModel.cs"));
            Assert.Contains("FormatMidiMetadata", src);
            Assert.Contains("SongMidiCore.ParseMidiFile", src);
            Assert.Contains("HasMidiInfo", src);
            Assert.Contains("MidiInfoText", src);
        }

        [Fact]
        public void SongTrackView_ImportMidi_PerformsUndoBackedWriteBack()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SongTrackView.axaml.cs"));
            // #972: the Import Music File button now previews + confirms, then
            // performs the real write-back via _vm.ImportMidi under a single
            // UndoService scope (the "not yet implemented" warning is gone).
            Assert.Contains("PreviewMidi", src);
            Assert.DoesNotContain("not yet fully implemented", src);
            Assert.Contains("_vm.ImportMidi(", src);
            Assert.Contains("_undoService.Begin(", src);
        }

        [Fact]
        public void SongTrackViewModel_HasPreviewMidiMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SongTrackViewModel.cs"));
            Assert.Contains("public string PreviewMidi(string filename)", src);
            Assert.Contains("FormatMidiMetadata", src);
        }

        // ------------------------------------------------------------------ Instrument Selection

        [Fact]
        public void SongTrackImportSelectInstrumentView_HasInfoPanels()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "SongTrackImportSelectInstrumentView.axaml"));
            Assert.Contains("InstrumentInfoLabel", axaml);
            Assert.Contains("About Instrument Selection", axaml);
            // #787: the instrument-set browser is now implemented (populated from
            // InstrumentSetCore), so the "Not Yet Implemented" banner is gone and
            // the panel reflects the selected set.
            Assert.DoesNotContain("Not Yet Implemented", axaml);
            Assert.Contains("Selected Instrument Set", axaml);
        }

        [Fact]
        public void SongTrackImportSelectInstrumentViewModel_HasBuildInstrumentInfo()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "SongTrackImportSelectInstrumentViewModel.cs"));
            Assert.Contains("BuildInstrumentInfo", src);
            Assert.Contains("InstrumentInfoText", src);
        }

        // ------------------------------------------------------------------ Keyboard Navigation (#75)

        [Fact]
        public void AddressListControl_HasHomeKeyHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.Home", src);
            Assert.Contains("SelectFirst()", src);
        }

        [Fact]
        public void AddressListControl_HasEndKeyHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.End", src);
            Assert.Contains("SelectLast()", src);
        }

        [Fact]
        public void AddressListControl_HasSelectLastMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void SelectLast()", src);
        }

        [Fact]
        public void AddressListControl_HasCtrlFHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.F", src);
            Assert.Contains("KeyModifiers.Control", src);
            Assert.Contains("FocusSearch()", src);
        }

        [Fact]
        public void AddressListControl_HasFocusSearchMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void FocusSearch()", src);
        }

        [Fact]
        public void AddressListControl_HasEnterKeyHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.Enter", src);
            Assert.Contains("FireSelectionConfirmed()", src);
        }

        [Fact]
        public void AddressListControl_IsFocusable()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml"));
            Assert.Contains("Focusable=\"True\"", axaml);
        }

        [Fact]
        public void AddressListControl_HasControlKeyDownHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Control_KeyDown", src);
            Assert.Contains("KeyDown += Control_KeyDown", src);
        }

        [Fact]
        public void AddressListControl_HasPageUpHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.PageUp", src);
            Assert.Contains("PageUp()", src);
        }

        [Fact]
        public void AddressListControl_HasPageDownHandler()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("Key.PageDown", src);
            Assert.Contains("PageDown()", src);
        }

        [Fact]
        public void AddressListControl_HasPageUpMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void PageUp()", src);
            // Should clamp to 0
            Assert.Contains("Math.Max(0,", src);
        }

        [Fact]
        public void AddressListControl_HasPageDownMethod()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("public void PageDown()", src);
            // Should clamp to last item
            Assert.Contains("Math.Min(_displayItems.Count - 1,", src);
        }

        [Fact]
        public void AddressListControl_PageSizeConstant()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("const int PageSize = 10", src);
        }

        [Fact]
        public void AddressListControl_HasTypeToSearchBuffer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("_typeSearchBuffer", src);
            Assert.Contains("TypeSearchBuffer", src);
        }

        [Fact]
        public void AddressListControl_HasTypeSearchTimeout()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("const int TypeSearchTimeoutMs = 500", src);
            Assert.Contains("DispatcherTimer", src);
        }

        [Fact]
        public void AddressListControl_HasHandleTypeToSearch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("HandleTypeToSearch(", src);
        }

        [Fact]
        public void AddressListControl_HasJumpToTypeSearchMatch()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("JumpToTypeSearchMatch(", src);
            // Should be case-insensitive
            Assert.Contains("StringComparison.OrdinalIgnoreCase", src);
        }

        [Fact]
        public void AddressListControl_HasKeyToCharHelper()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("KeyToChar(", src);
            // Should handle letters and digits
            Assert.Contains("Key.A", src);
            Assert.Contains("Key.Z", src);
            Assert.Contains("Key.D0", src);
            Assert.Contains("Key.D9", src);
        }

        [Fact]
        public void AddressListControl_TypeSearchResetsOnTimeout()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            // Timer resets the buffer
            Assert.Contains("_typeSearchBuffer = \"\"", src);
            Assert.Contains("_typeSearchTimer.Stop()", src);
            Assert.Contains("_typeSearchTimer.Start()", src);
        }

        [Fact]
        public void AddressListControl_PageUpScrollsIntoView()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            // PageUp/PageDown should call ScrollIntoView
            Assert.Contains("ScrollIntoView(target)", src);
        }

        [Fact]
        public void AddressListControl_HasResetTypeSearchBuffer()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "AddressListControl.axaml.cs"));
            Assert.Contains("void ResetTypeSearchBuffer()", src);
        }

        // ------------------------------------------------------------------ Collapsible Sections (#71)

        [Fact]
        public void MainWindow_HasExpanderElements()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            // All sections should use Expander instead of plain TextBlock headers
            Assert.Contains("<Expander", axaml);
            Assert.Contains("IsExpanded=\"True\"", axaml);
            // Check key named expanders exist
            Assert.Contains("Name=\"CharactersExpander\"", axaml);
            Assert.Contains("Name=\"ItemsExpander\"", axaml);
            Assert.Contains("Name=\"MapsExpander\"", axaml);
            Assert.Contains("Name=\"GraphicsExpander\"", axaml);
            Assert.Contains("Name=\"AudioExpander\"", axaml);
            Assert.Contains("Name=\"ToolsExpander\"", axaml);
        }

        [Fact]
        public void MainWindow_HasCollapseExpandAllMenuItems()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            Assert.Contains("CollapseAll_Click", axaml);
            Assert.Contains("ExpandAll_Click", axaml);
        }

        [Fact]
        public void MainWindow_HasCollapseExpandAllHandlers()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("void CollapseAll_Click(", src);
            Assert.Contains("void ExpandAll_Click(", src);
            Assert.Contains("SetAllExpandersExpanded(", src);
        }

        [Fact]
        public void MainWindow_ApplyFilter_MatchesSectionName()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));
            // Filter should match against Expander header text (section name)
            Assert.Contains("sectionName.Contains(filter", src);
        }

        [Fact]
        public void MainWindow_NoOldTextBlockSectionHeaders()
        {
            var axaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));
            // Old pattern: TextBlock with FontSize 16 as section header (outside Expanders)
            // These should no longer exist in EditorPanel -- all sections use Expander now
            // The only TextBlocks with FontSize should be the title area
            var lines = axaml.Split('\n');
            bool insideEditorPanel = false;
            foreach (var line in lines)
            {
                if (line.Contains("Name=\"EditorPanel\"")) insideEditorPanel = true;
                if (insideEditorPanel && line.Trim().StartsWith("<TextBlock") && line.Contains("FontSize=\"16\""))
                    Assert.Fail($"Found old-style TextBlock section header inside EditorPanel: {line.Trim()}");
                if (insideEditorPanel && line.Contains("</StackPanel>") && !line.Contains("<")) break;
            }
        }

        // ================================================================
        // Issue #59 — Weapon rank labels exist in Unit and Class editors
        // ================================================================

        [Fact]
        public void UnitEditorView_HasWeaponRankTextBlocks()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.Contains("Name=\"SwordRankText\"", src);
            Assert.Contains("Name=\"LanceRankText\"", src);
            Assert.Contains("Name=\"AxeRankText\"", src);
            Assert.Contains("Name=\"BowRankText\"", src);
            Assert.Contains("Name=\"StaffRankText\"", src);
            Assert.Contains("Name=\"AnimaRankText\"", src);
            Assert.Contains("Name=\"LightRankText\"", src);
            Assert.Contains("Name=\"DarkRankText\"", src);
        }

        [Fact]
        public void UnitEditorView_WiresWeaponRankLabels()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("WireWeaponRankLabels()", src);
            Assert.Contains("UpdateWeaponRankLabels()", src);
            Assert.Contains("WeaponRankUtil.GetRankLetter", src);
        }

        [Fact]
        public void ClassEditorView_HasWeaponRankTextBlocks()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            // B44-B51 rank texts (named by byte offset after field rename)
            Assert.Contains("Name=\"B44RankText\"", src);
            Assert.Contains("Name=\"B45RankText\"", src);
            Assert.Contains("Name=\"B46RankText\"", src);
            Assert.Contains("Name=\"B47RankText\"", src);
            Assert.Contains("Name=\"B48RankText\"", src);
            Assert.Contains("Name=\"B49RankText\"", src);
            Assert.Contains("Name=\"B50RankText\"", src);
            Assert.Contains("Name=\"B51RankText\"", src);
        }

        [Fact]
        public void ClassEditorView_WiresWeaponRankLabels()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml.cs"));
            Assert.Contains("UpdateWeaponRankLabels()", src);
            Assert.Contains("WeaponRankUtil.GetRankLetter", src);
        }

        // ================================================================
        // Issue #70 — Welcome screen shows recent files
        // ================================================================

        [Fact]
        public void WelcomeView_HasRecentFilesSection()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "WelcomeView.axaml"));
            Assert.Contains("Recent Files", src);
            Assert.Contains("Name=\"RecentFilesList\"", src);
            Assert.Contains("Name=\"NoRecentFilesLabel\"", src);
        }

        [Fact]
        public void WelcomeView_CodeBehindLoadsRecentFiles()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "WelcomeView.axaml.cs"));
            Assert.Contains("LoadRecentFiles()", src);
            Assert.Contains("RecentFile_Click", src);
            Assert.Contains("RecentFileKeyPrefix", src); // Uses shared constant from MainWindowViewModel
            Assert.Contains("LoadRomFile", src); // Directly loads ROM via MainWindow
        }

        // ================================================================
        // Issue #72 — Easy Mode has text export/import buttons
        // ================================================================

        [Fact]
        public void EasyModePanel_HasTextExportImportButtons()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EasyModePanel.axaml"));
            Assert.Contains("Export Text (TSV)", src);
            Assert.Contains("Import Text (TSV)", src);
            Assert.Contains("EasyExportText_Click", src);
            Assert.Contains("EasyImportText_Click", src);
        }

        [Fact]
        public void EasyModePanel_CodeBehindHasTextExportImport()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "EasyModePanel.axaml.cs"));
            Assert.Contains("EasyExportText_Click", src);
            Assert.Contains("EasyImportText_Click", src);
            Assert.Contains("ExportAllTexts", src);
            Assert.Contains("ImportAllTexts", src);
        }

        // ================================================================
        // Issue #73 — Collapsible Expanders and tooltips in editors
        // ================================================================

        [Fact]
        public void UnitEditorView_HasCollapsibleExpanders()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            // Use line-based checks so attribute order (e.g. AutomationProperties before Header) doesn't matter
            var lines = src.Split('\n');
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Identity\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Base Stats\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Weapon Levels\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Growth Rates"));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Ability Flags\""));
        }

        [Fact]
        public void UnitEditorView_HasTooltips()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            // At least 5 ToolTip.Tip attributes in the editor
            int tipCount = 0;
            int idx = 0;
            while ((idx = src.IndexOf("ToolTip.Tip=", idx)) >= 0)
            {
                tipCount++;
                idx++;
            }
            Assert.True(tipCount >= 5, $"Expected at least 5 ToolTip.Tip attributes in UnitEditorView, found {tipCount}");
        }

        [Fact]
        public void ClassEditorView_HasCollapsibleExpanders()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            var lines = src.Split('\n');
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Identity"));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Base Stats\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Weapon Rank Levels"));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Growth Rates\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Ability Flags\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Promotion Gains"));
        }

        [Fact]
        public void ClassEditorView_HasTooltips()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            int tipCount = 0;
            int idx = 0;
            while ((idx = src.IndexOf("ToolTip.Tip=", idx)) >= 0)
            {
                tipCount++;
                idx++;
            }
            Assert.True(tipCount >= 5, $"Expected at least 5 ToolTip.Tip attributes in ClassEditorView, found {tipCount}");
        }

        [Fact]
        public void ItemEditorView_HasCollapsibleExpanders()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml"));
            var lines = src.Split('\n');
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Basic Info\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Stats / Bonuses\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Weapon Properties\""));
            Assert.Contains(lines, l => l.Contains("<Expander") && l.Contains("Header=\"Trait Flags\""));
        }

        [Fact]
        public void ItemEditorView_HasTooltips()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemEditorView.axaml"));
            int tipCount = 0;
            int idx = 0;
            while ((idx = src.IndexOf("ToolTip.Tip=", idx)) >= 0)
            {
                tipCount++;
                idx++;
            }
            Assert.True(tipCount >= 5, $"Expected at least 5 ToolTip.Tip attributes in ItemEditorView, found {tipCount}");
        }

        // ------------------------------------------------------------------ UnitsShortText (#372)

        [Fact]
        public void UnitsShortTextViewModel_DoesNotUseEventHaikuFallback()
        {
            // Issue #372: event_haiku_pointer (death quotes, 12-byte struct array) was incorrectly
            // used as a fallback base address for unit short text (u16 text-id array). The two data
            // structures are not compatible — reading death-quote bytes as text IDs displays nonsense.
            // The fallback must be removed entirely; this view is pointer-driven only.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitsShortTextViewModel.cs"));
            Assert.DoesNotContain("event_haiku_pointer", src);
            Assert.DoesNotContain("FindDefaultBaseAddr", src);
        }

        [Fact]
        public void UnitsShortTextViewModel_GetListCountReturnsZeroWhenNoBaseAddr()
        {
            // GetListCount() must return 0 when _baseAddr == 0, instead of trying to auto-discover
            // a default base address. Standalone open with no NavigateTo() => empty list.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitsShortTextViewModel.cs"));
            // Either an explicit early return or HasData check
            bool hasZeroReturn = src.Contains("if (_baseAddr == 0) return 0")
                || src.Contains("if (_baseAddr == 0)\n                return 0")
                || src.Contains("_baseAddr == 0 ? 0");
            Assert.True(hasZeroReturn,
                "UnitsShortTextViewModel.GetListCount must return 0 when _baseAddr == 0");
        }

        [Fact]
        public void UnitsShortTextViewModel_HasHasDataProperty()
        {
            // Public HasData property — true iff a valid base address is loaded.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", "UnitsShortTextViewModel.cs"));
            Assert.Contains("public bool HasData", src);
        }

        [Fact]
        public void UnitsShortTextView_DoesNotHaveAutoInitEventHaiku()
        {
            // The AutoInitIfNeeded() shim that derived a base address from event_haiku_pointer
            // is the bug being fixed. It must not remain in the code-behind.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitsShortTextView.axaml.cs"));
            Assert.DoesNotContain("event_haiku_pointer", src);
            Assert.DoesNotContain("AutoInitIfNeeded", src);
        }

        [Fact]
        public void UnitsShortTextView_HasEmptyStateAndEditorGrid()
        {
            // The axaml must declare both the empty-state TextBlock and the editor grid container
            // so the code-behind can flip visibility between them.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitsShortTextView.axaml"));
            Assert.Contains("EmptyStateLabel", src);
            Assert.Contains("EditorGrid", src);
        }

        [Fact]
        public void UnitsShortTextView_HidesEditorGridWhenNoBaseAddr()
        {
            // Behavior assertion (Copilot review #3): when no base address is set, the editor grid
            // and Write button are hidden; the empty-state label is shown. NavigateTo() flips them.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitsShortTextView.axaml.cs"));
            Assert.Contains("EditorGrid.IsVisible", src);
            Assert.Contains("EmptyStateLabel.IsVisible", src);
            Assert.Contains("WriteButton.IsVisible", src);
        }

        // ---------------------------------------------- #648 dead-button fixes

        [Fact]
        public void UnitEditorView_RemovesCalculateGrowthButton()
        {
            // #648: the redundant "Calculate Growth" button was removed because
            // WireGrowthAutoRecalc() already wires ValueChanged on every NUD
            // (including SimLevelBox) and the ClassIdCombo, so the simulator
            // auto-updates on any relevant input change.
            var xaml = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.DoesNotContain("UnitEditor_CalculateGrowth_Button", xaml);
            Assert.DoesNotContain("Click=\"CalculateGrowth_Click\"", xaml);
        }

        [Fact]
        public void UnitEditorView_RemovesCalculateGrowthHandler()
        {
            // #648: no orphan handler should remain in the code-behind.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.DoesNotContain("void CalculateGrowth_Click(", src);
        }

        [Fact]
        public void UnitEditorView_AutoRecalcWiredForSimLevel()
        {
            // #648: SimLevelBox must remain wired through WireGrowthAutoRecalc so
            // that removing the button does not regress auto-recalculation.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("WireGrowthAutoRecalc", src);
            Assert.Contains("SimLevelBox", src);
        }

        [Fact]
        public void UnitEditorView_JumpToSupportUnit_UsesParseHexText()
        {
            // #648: the Open Support handler must parse the displayed "0x..."
            // pointer via ViewHelpers.ParseHexText. U.atoh truncates at the 'x'
            // and would silently return 0.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml.cs"));
            Assert.Contains("ViewHelpers.ParseHexText(SupportPtrBox.Text)", src);
            Assert.DoesNotContain("U.atoh(SupportPtrBox.Text", src);
        }

        [Fact]
        public void UnitFE6View_JumpToSupportUnit_UsesParseHexText()
        {
            // #648: same fix in the FE6-specific Unit editor.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitFE6View.axaml.cs"));
            Assert.Contains("ViewHelpers.ParseHexText(SupportPtrBox.Text)", src);
            Assert.DoesNotContain("U.atoh(SupportPtrBox.Text", src);
        }

        [Fact]
        public void UnitFE7View_JumpToSupportUnit_UsesParseHexText()
        {
            // #648: same fix in the FE7/FE8-specific Unit editor (navigation
            // only; write-back was already safe via local ParseHexText).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitFE7View.axaml.cs"));
            Assert.Contains("ViewHelpers.ParseHexText(SupportPtrBox.Text)", src);
            Assert.DoesNotContain("U.atoh(SupportPtrBox.Text", src);
        }

        [Fact]
        public void ViewHelpers_ParseHexText_HandlesZeroXPrefix()
        {
            // #648: regression guard - ViewHelpers.ParseHexText must accept the
            // exact "0x{...:X08}" form that UnitEditorView writes into the
            // SupportPtr text box. ParseHexText is internal, so we exercise it
            // indirectly by asserting the helper file documents both prefix
            // forms via the case-insensitive 0x/0X startswith check.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ViewHelpers.cs"));
            Assert.Contains("StartsWith(\"0x\", StringComparison.OrdinalIgnoreCase)", src);
            Assert.Contains("NumberStyles.HexNumber", src);
        }

        // ------------------------------------------------------------------ #650 truncated preview tooltips
        // Every TextBlock that uses TextTrimming="CharacterEllipsis" with a fixed
        // MaxWidth in an editor view MUST expose the full text via a
        // ToolTip.Tip self-binding so the user can read the truncated content on
        // hover. The MainWindow status bar is intentionally exempt — its tooltip
        // is set dynamically in code-behind and the bar must stay one-line.
        //
        // The check is intentionally narrow: it scans for the *self-binding*
        // pattern (ToolTip.Tip="{Binding #...}" or ToolTip.Tip="{Binding}")
        // anywhere in the file, so adding new truncated previews without a
        // matching tooltip will fail the assertion.

        // Matches a single <TextBlock ... /> element (self-closing), allowing the
        // attributes to span multiple lines. The PR only adds self-binding
        // ToolTip.Tip to self-closing TextBlock elements, so this is sufficient.
        // RegexOptions.Singleline makes "." match newlines.
        private static readonly System.Text.RegularExpressions.Regex TextBlockElementRegex =
            new System.Text.RegularExpressions.Regex(
                @"<TextBlock\b[^>]*?/>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        // Element-name self ref: ToolTip.Tip="{Binding #ElementName.Text}"
        private static readonly System.Text.RegularExpressions.Regex ToolTipElementSelfBindingRegex =
            new System.Text.RegularExpressions.Regex(
                @"ToolTip\.Tip=""\{Binding\s+#(?<el>[A-Za-z_][A-Za-z0-9_]*)\.Text\}""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        // DataContext self ref: ToolTip.Tip="{Binding}"
        private const string ToolTipDataContextBinding = @"ToolTip.Tip=""{Binding}""";

        // Property self ref: ToolTip.Tip="{Binding PropName}" (must match Text="{Binding PropName}")
        private static readonly System.Text.RegularExpressions.Regex ToolTipPropertyBindingRegex =
            new System.Text.RegularExpressions.Regex(
                @"ToolTip\.Tip=""\{Binding\s+(?<prop>[A-Za-z_][A-Za-z0-9_]*)\}""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex NameAttrRegex =
            new System.Text.RegularExpressions.Regex(
                @"\bName=""(?<name>[A-Za-z_][A-Za-z0-9_]*)""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex TextPropertyBindingRegex =
            new System.Text.RegularExpressions.Regex(
                @"\bText=""\{Binding\s+(?<prop>[A-Za-z_][A-Za-z0-9_]*)\}""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        /// <summary>
        /// Returns true iff <paramref name="textBlockXml"/> declares a ToolTip.Tip
        /// that genuinely SELF-binds to the same source as the visible Text. The
        /// three accepted patterns are:
        ///   1. ToolTip.Tip="{Binding #&lt;SelfName&gt;.Text}"         where SelfName matches the
        ///      element's own Name="..." attribute (element-name self ref).
        ///   2. ToolTip.Tip="{Binding}"                          DataContext IS the string
        ///      (only valid when the TextBlock has no Text="{Binding Foo}" attribute,
        ///      i.e. it inherits its content from the DataContext).
        ///   3. ToolTip.Tip="{Binding &lt;Prop&gt;}"                  where the same TextBlock's
        ///      Text="{Binding &lt;Prop&gt;}" binds the SAME property (DataTemplate self ref).
        /// A binding that targets a DIFFERENT element or a DIFFERENT property is
        /// REJECTED, because it does not display the truncated content on hover.
        /// </summary>
        static bool IsSelfBindingToolTip(string textBlockXml)
        {
            // Case 2: literal "{Binding}" — empty path re-binds to DataContext.
            // This is only a self-binding when the TextBlock's VISIBLE Text ALSO
            // comes directly from DataContext. If the TextBlock declares
            // Text="{Binding Foo}", the tooltip ({Binding}) would route to the
            // DataContext root while Text routes to a property — that's NOT a
            // self ref and must be rejected. Tightened per Copilot bot review
            // round 2 on PR #666.
            if (textBlockXml.Contains(ToolTipDataContextBinding, StringComparison.Ordinal))
            {
                // Reject if Text binds to a specific property on the DataContext
                // (Text="{Binding PropName}") — tooltip and text would diverge.
                if (TextPropertyBindingRegex.IsMatch(textBlockXml))
                    return false;
                return true;
            }

            // Case 1: element-name self ref. The element name in the Binding MUST
            // match the TextBlock's own Name attribute.
            var elementMatch = ToolTipElementSelfBindingRegex.Match(textBlockXml);
            if (elementMatch.Success)
            {
                var nameMatch = NameAttrRegex.Match(textBlockXml);
                if (!nameMatch.Success)
                    return false;
                return string.Equals(
                    elementMatch.Groups["el"].Value,
                    nameMatch.Groups["name"].Value,
                    StringComparison.Ordinal);
            }

            // Case 3: property self ref. The property in ToolTip.Tip binding MUST
            // match the property in Text binding on the same TextBlock.
            var propMatch = ToolTipPropertyBindingRegex.Match(textBlockXml);
            if (propMatch.Success)
            {
                var textMatch = TextPropertyBindingRegex.Match(textBlockXml);
                if (!textMatch.Success)
                    return false;
                return string.Equals(
                    propMatch.Groups["prop"].Value,
                    textMatch.Groups["prop"].Value,
                    StringComparison.Ordinal);
            }

            return false;
        }

        static void AssertHasSelfBindingToolTip(string file, string axaml)
        {
            // Find every self-closing <TextBlock /> with TextTrimming="CharacterEllipsis"
            // and verify each one declares a self-binding ToolTip.Tip via one of the
            // three accepted forms (see IsSelfBindingToolTip). Tightened per Copilot
            // review on PR #666: the previous check was too permissive — any
            // {Binding ...} ToolTip would pass, even ones bound to unrelated
            // sources. The strict form catches future regressions where someone
            // copies a tooltip from another control and forgets to rewrite the
            // binding target.

            var trimmedBlocks = 0;
            var coveredBlocks = 0;
            var uncoveredSample = string.Empty;

            foreach (System.Text.RegularExpressions.Match m in TextBlockElementRegex.Matches(axaml))
            {
                var block = m.Value;
                if (!block.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal))
                    continue;
                trimmedBlocks++;
                if (IsSelfBindingToolTip(block))
                {
                    coveredBlocks++;
                }
                else if (uncoveredSample.Length == 0)
                {
                    // Capture the first uncovered block for the failure message.
                    uncoveredSample = block.Length > 240 ? block.Substring(0, 240) + "..." : block;
                }
            }

            // Every trimmed TextBlock must be covered by a self-binding tooltip.
            // The audit is intentionally per-element: each <TextBlock /> with
            // TextTrimming="CharacterEllipsis" must carry its own valid
            // ToolTip.Tip self-binding (see IsSelfBindingToolTip for the three
            // accepted forms). We do NOT compare file-wide attribute counts —
            // those couple unrelated things (e.g. a Button tooltip bound to a
            // VM property would inflate the count and break the assertion even
            // though it has nothing to do with truncated previews). Per Copilot
            // bot review round 2 on PR #666: dropping the file-wide count
            // assertion in favor of per-element validation keeps the intent
            // (every ellipsis has a tooltip) without blocking unrelated UI work.
            Assert.True(trimmedBlocks > 0,
                $"#650: {file} was scanned for truncated previews but no self-closing TextBlock with TextTrimming=\"CharacterEllipsis\" was found.");
            Assert.True(coveredBlocks == trimmedBlocks,
                $"#650: {file} has {trimmedBlocks} truncated TextBlock(s) but only {coveredBlocks} carry a valid self-binding ToolTip.Tip. First uncovered block:\n{uncoveredSample}");
        }

        [Fact]
        public void IsSelfBindingToolTip_RejectsDataContextBindingWhenTextBindsToProperty_Issue650()
        {
            // Regression guard for Copilot bot review round 2 on PR #666:
            // a TextBlock with Text="{Binding Name}" and ToolTip.Tip="{Binding}"
            // is NOT a self-binding — the tooltip would resolve to the
            // DataContext root while Text resolves to the Name property.
            // IsSelfBindingToolTip must reject this case (the XML doc on the
            // helper explicitly states this).
            var divergent = "<TextBlock Text=\"{Binding Name}\" ToolTip.Tip=\"{Binding}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.False(IsSelfBindingToolTip(divergent),
                "ToolTip.Tip=\"{Binding}\" with Text=\"{Binding Foo}\" must be REJECTED — tooltip and text resolve to different sources.");

            // Conversely, an element with ToolTip.Tip="{Binding}" and NO
            // Text="{Binding Foo}" attribute IS a valid self-binding: the
            // DataContext is the string and the TextBlock inherits it as content.
            var dataContextOnly = "<TextBlock ToolTip.Tip=\"{Binding}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.True(IsSelfBindingToolTip(dataContextOnly),
                "ToolTip.Tip=\"{Binding}\" with no Text=\"{Binding Foo}\" must be ACCEPTED — DataContext is the displayed string.");

            // Element-name self ref must match the TextBlock's own Name.
            var elementSelfRef = "<TextBlock Name=\"NameLabel\" ToolTip.Tip=\"{Binding #NameLabel.Text}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.True(IsSelfBindingToolTip(elementSelfRef),
                "ToolTip.Tip=\"{Binding #SelfName.Text}\" matching Name=\"SelfName\" must be ACCEPTED.");

            var elementWrongRef = "<TextBlock Name=\"NameLabel\" ToolTip.Tip=\"{Binding #OtherLabel.Text}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.False(IsSelfBindingToolTip(elementWrongRef),
                "ToolTip.Tip=\"{Binding #OtherName.Text}\" not matching the element's Name must be REJECTED.");

            // Property self ref must match the same property on both sides.
            var propSelfRef = "<TextBlock Text=\"{Binding Title}\" ToolTip.Tip=\"{Binding Title}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.True(IsSelfBindingToolTip(propSelfRef),
                "ToolTip.Tip=\"{Binding Prop}\" matching Text=\"{Binding Prop}\" must be ACCEPTED.");

            var propWrongRef = "<TextBlock Text=\"{Binding Title}\" ToolTip.Tip=\"{Binding Subtitle}\" TextTrimming=\"CharacterEllipsis\" />";
            Assert.False(IsSelfBindingToolTip(propWrongRef),
                "ToolTip.Tip=\"{Binding OtherProp}\" not matching Text=\"{Binding Prop}\" must be REJECTED.");
        }

        [Theory]
        [InlineData("UnitEditorView.axaml")]
        [InlineData("UnitFE6View.axaml")]
        [InlineData("UnitFE7View.axaml")]
        [InlineData("ClassEditorView.axaml")]
        [InlineData("ItemEditorView.axaml")]
        [InlineData("ItemFE6View.axaml")]
        [InlineData("EventTalkGroupFE7View.axaml")]
        [InlineData("MenuCommandView.axaml")]
        [InlineData("OPClassDemoFE7View.axaml")]
        [InlineData("OPClassDemoFE7UView.axaml")]
        [InlineData("OPClassDemoFE8UView.axaml")]
        [InlineData("SoundRoomViewerView.axaml")]
        [InlineData("StatusOptionView.axaml")]
        [InlineData("StatusRMenuView.axaml")]
        [InlineData("StatusUnitsMenuView.axaml")]
        [InlineData("WorldMapPointView.axaml")]
        [InlineData("ToolAnimationCreatorView.axaml")]
        [InlineData("PatchManagerView.axaml")]
        public void TruncatedPreview_HasSelfBindingToolTip_Issue650(string viewFile)
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", viewFile));
            // Sanity: file still contains the trimming attribute (otherwise the
            // test would silently pass and miss future regressions).
            Assert.Contains("TextTrimming=\"CharacterEllipsis\"", src);
            AssertHasSelfBindingToolTip(viewFile, src);
        }

        [Fact]
        public void IdFieldControl_NameLabelHasSelfBindingToolTip_Issue650()
        {
            // The reusable type-ID control's name preview is also truncated; its
            // ToolTip.Tip must self-bind so 6+ editor call sites all get the fix.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "IdFieldControl.axaml"));
            Assert.Contains("TextTrimming=\"CharacterEllipsis\"", src);
            Assert.Contains("ToolTip.Tip=\"{Binding #NameLabel.Text}\"", src);
        }

        [Fact]
        public void MainWindowStatusBar_KeepsEllipsisWithoutSelfBindingToolTip_Issue650()
        {
            // The status bar TextBlock keeps its ellipsis trimming but its tooltip
            // is set DYNAMICALLY in code-behind based on the current message — it
            // must NOT carry a static ToolTip.Tip="{Binding ...}" self-binding in
            // the AXAML, because that would freeze the tooltip text to whatever
            // the binding resolves to and silently override the code-behind
            // updates. The status bar must remain one-line; wrapping would break
            // the layout. Tightened per Copilot review on PR #666: the previous
            // version of this test only asserted the StatusText element exists
            // and has ellipsis trimming — it did NOT verify the exemption (the
            // "without self-binding ToolTip" claim in the test name).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml"));

            // Locate the StatusText TextBlock element. It is written as a
            // self-closing tag, so we can use the same regex used elsewhere.
            System.Text.RegularExpressions.Match? statusBlockMatch = null;
            foreach (System.Text.RegularExpressions.Match m in TextBlockElementRegex.Matches(src))
            {
                if (m.Value.Contains("Name=\"StatusText\"", StringComparison.Ordinal))
                {
                    statusBlockMatch = m;
                    break;
                }
            }
            Assert.True(statusBlockMatch != null,
                "#650 exemption: MainWindow.axaml must contain a self-closing <TextBlock Name=\"StatusText\" ... /> element.");
            var statusBlock = statusBlockMatch!.Value;

            // 1. Keeps ellipsis trimming (must stay one-line).
            Assert.Contains("TextTrimming=\"CharacterEllipsis\"", statusBlock);

            // 2. ACTIVELY does NOT wire a ToolTip.Tip in the AXAML. Any form
            //    (static text, element self-binding, DataContext binding, or
            //    property binding) would override the code-behind. The whole
            //    point of the exemption is that the tooltip is set dynamically.
            Assert.DoesNotContain("ToolTip.Tip", statusBlock);
            Assert.DoesNotContain("ToolTip.Tip=\"{Binding}\"", statusBlock);

            // 3. Helper sanity: the strict self-binding check must agree that
            //    this block is NOT covered (otherwise the exemption would be a
            //    no-op and the central audit would silently pass).
            Assert.False(IsSelfBindingToolTip(statusBlock),
                "#650 exemption: MainWindow status bar must NOT have a self-binding ToolTip in the AXAML (the tooltip is set in code-behind based on the current message).");
        }

        [Fact]
        public void MainWindowStatusBar_TooltipIsSetDynamicallyInCodeBehind_Issue650()
        {
            // The status bar exemption claims its tooltip is set DYNAMICALLY in
            // code-behind. Tightened per Copilot review on PR #666: verify the
            // claim is actually true. MainWindow.axaml.cs must
            //   (1) define a SetStatusText helper that mirrors the new text
            //       into the StatusText TextBlock's ToolTip.Tip via
            //       ToolTip.SetTip, and
            //   (2) route ALL StatusText updates through that helper — no
            //       remaining bare `StatusText.Text =` assignments outside the
            //       helper itself, otherwise the tooltip would drift out of
            //       sync with the displayed message.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "MainWindow.axaml.cs"));

            // (1) Helper exists and updates both Text and ToolTip.
            Assert.Contains("void SetStatusText(string text)", src);
            Assert.Contains("ToolTip.SetTip(StatusText", src);

            // (2) No bare StatusText.Text = assignments outside the helper.
            // The helper itself is the single allowed writer.
            var bareAssignmentRegex = new System.Text.RegularExpressions.Regex(
                @"StatusText\.Text\s*=");
            int bareAssignments = bareAssignmentRegex.Matches(src).Count;
            Assert.Equal(1, bareAssignments);
        }

        [Fact]
        public void Issue650_AllTruncatedPreviewsHaveTooltipsExceptStatusBar()
        {
            // Enumerate every .axaml in Views/ and Controls/. For each file
            // that contains TextTrimming="CharacterEllipsis", verify it also
            // contains at least one self-binding tooltip — unless the file is
            // MainWindow.axaml (status bar exemption).
            var dirs = new[]
            {
                Path.Combine(AvaloniaDir, "Views"),
                Path.Combine(AvaloniaDir, "Controls"),
            };
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*.axaml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(f);
                    if (string.Equals(name, "MainWindow.axaml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var src = File.ReadAllText(f);
                    if (!src.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal))
                        continue;
                    AssertHasSelfBindingToolTip(name, src);
                }
            }
        }
    }
}
