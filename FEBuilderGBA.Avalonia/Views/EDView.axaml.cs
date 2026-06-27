// SPDX-License-Identifier: GPL-3.0-or-later
// EDView code-behind - gap-sweep #411 parity raise.
//
// Wires the three Avalonia tabs (Retreat / Epithet / Epilogue) to the
// per-tab ViewModel surfaces. Each Write_*_Click handler opens its own
// distinctly-named UndoService scope so undo for the three tabs is
// independent. Each ListExpand handler routes through the VM's
// DataExpansionCore.ExpandTable wrapper (Copilot CLI v1 plan-review C4).
//
// Field-width tests in EDParityTests.cs verify the codec choices
// (W0/D4 for Epithet, B0/B1/B2/B3/D4 for Epilogue) match the WF
// designer's NumericUpDown widths exactly.
//
// Backward-compat: legacy `NavigateTo` / `SelectFirstItem` point at
// the Retreat tab list (the original surface), so any pre-existing
// ListParityHelper / INavigationTargetSource callers keep working.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EDViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressEpilogueDesignationSync;

        public string ViewTitle => "Ending Event Editor";
        public bool IsLoaded => _vm.RetreatCanWrite || _vm.EpithetCanWrite || _vm.EpilogueCanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public EDView()
        {
            InitializeComponent();
            Retreat_EntryList.SelectedAddressChanged += OnRetreatSelected;
            Epithet_EntryList.SelectedAddressChanged += OnEpithetSelected;
            Epilogue_EntryList.SelectedAddressChanged += OnEpilogueSelected;
            Opened += (_, _) => { LoadAllLists(); UpdateEpilogueAvailability(); };
        }

        // ============================================================
        // Common load entry-point
        // ============================================================

        void LoadAllLists()
        {
            try { LoadRetreatList(); }
            catch (Exception ex) { Log.ErrorF("EDView.LoadRetreatList failed: {0}", ex.Message); }
            try { LoadEpithetList(); }
            catch (Exception ex) { Log.ErrorF("EDView.LoadEpithetList failed: {0}", ex.Message); }
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDView.LoadEpilogueList failed: {0}", ex.Message); }
        }

        /// <summary>
        /// Bind the Eirika/Ephraim combo's per-item IsEnabled per the
        /// current ROM's EpilogueAvailability (Copilot CLI v1 plan
        /// review C3 - FE6JP has only the Eirika route).
        /// </summary>
        void UpdateEpilogueAvailability()
        {
            try
            {
                var avail = _vm.EpilogueAvailability;
                bool ephraimEnabled = avail == EDViewModel.EpilogueAvailabilityKind.BothRoutes;
                Epilogue_FilterItem_Ephraim.IsEnabled = ephraimEnabled;
                if (!ephraimEnabled && Epilogue_FilterCombo.SelectedIndex == 1)
                    Epilogue_FilterCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EDView.UpdateEpilogueAvailability failed: {0}", ex.Message);
            }
        }

        // ============================================================
        // Retreat tab
        // ============================================================

        void LoadRetreatList()
        {
            var items = _vm.LoadRetreatList();
            Retreat_EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            if (Retreat_TopBar != null)
            {
                Retreat_TopBar.ReadCountText = items.Count.ToString();
                var rom = CoreState.ROM;
                if (rom?.RomInfo != null && rom.RomInfo.ed_1_pointer != 0)
                    Retreat_TopBar.StartAddressText = $"0x{rom.p32(rom.RomInfo.ed_1_pointer):X08}";
            }
        }

        void OnRetreatSelected(uint addr)
        {
            try
            {
                _vm.LoadRetreat(addr);
                Retreat_AddressBox.Value = addr;
                Retreat_SelectedAddressLabel.Content = $"0x{addr:X08}";
                Retreat_UnitIdBox.Value = _vm.RetreatUnitId;
                try { Retreat_UnitIdBox.NameText = ResolveUnitNameForUid(_vm.RetreatUnitId); }
                catch { /* leave prior text */ }
                Retreat_ConditionBox.Value = _vm.RetreatCondition;
                Retreat_B2Box.Value = _vm.RetreatB2;
                Retreat_B3Box.Value = _vm.RetreatB3;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EDView.OnRetreatSelected failed: {0}", ex.Message);
            }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnRetreatTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadRetreatList(); }
            catch (Exception ex) { Log.ErrorF("EDView.ReloadRetreat failed: {0}", ex.Message); }
        }

        void WriteRetreat_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.RetreatCanWrite) return;

            _vm.RetreatUnitId = Retreat_UnitIdBox.Value;
            _vm.RetreatCondition = (uint)(Retreat_ConditionBox.Value ?? 0);
            _vm.RetreatB2 = (uint)(Retreat_B2Box.Value ?? 0);
            _vm.RetreatB3 = (uint)(Retreat_B3Box.Value ?? 0);
            _undoService.Begin("Edit ED Retreat");
            try
            {
                _vm.WriteRetreat();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Ending retreat entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.WriteRetreat failed: {0}", ex.Message);
            }
        }

        void ExpandRetreat_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand ED Retreat");
            try
            {
                var result = _vm.ExpandRetreatList();
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "List expand failed.");
                    return;
                }
                _undoService.Commit();
                LoadRetreatList();
                CoreState.Services?.ShowInfo($"Retreat table expanded to {result.NewCount} entries at 0x{result.NewBaseAddress:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.ExpandRetreat failed: {0}", ex.Message);
            }
        }

        void RetreatUnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Retreat_UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDView.RetreatUnitId_Jump failed: {0}", ex.Message); }
        }

        async void RetreatUnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Retreat_UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) Retreat_UnitIdBox.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDView.RetreatUnitId_Pick failed: {0}", ex.Message); }
        }

        void RetreatUnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Retreat_UnitIdBox.NameText = ResolveUnitNameForUid(e.NewValue); }
            catch { /* leave prior text */ }
        }

        // ============================================================
        // Epithet tab
        // ============================================================

        void LoadEpithetList()
        {
            var items = _vm.LoadEpithetList();
            Epithet_EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            if (Epithet_TopBar != null)
            {
                Epithet_TopBar.ReadCountText = items.Count.ToString();
                var rom = CoreState.ROM;
                if (rom?.RomInfo != null && rom.RomInfo.ed_2_pointer != 0)
                    Epithet_TopBar.StartAddressText = $"0x{rom.p32(rom.RomInfo.ed_2_pointer):X08}";
            }
        }

        void OnEpithetSelected(uint addr)
        {
            try
            {
                _vm.LoadEpithet(addr);
                Epithet_AddressBox.Value = addr;
                Epithet_SelectedAddressLabel.Content = $"0x{addr:X08}";
                Epithet_UnitIdBox.Value = _vm.EpithetUnitId;
                try { Epithet_UnitIdBox.NameText = ResolveUnitNameForUid(_vm.EpithetUnitId); }
                catch { /* leave prior text */ }
                Epithet_EpithetTextIdBox.Value = _vm.EpithetTextId;
                RefreshEpithetTextPreview(_vm.EpithetTextId);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EDView.OnEpithetSelected failed: {0}", ex.Message);
            }
        }

        void EpithetTextId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(Epithet_EpithetTextIdBox.Value ?? 0);
            RefreshEpithetTextPreview(id);
        }

        void RefreshEpithetTextPreview(uint id)
        {
            try { Epithet_EpithetTextLabel.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { Epithet_EpithetTextLabel.Text = ""; }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnEpithetTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadEpithetList(); }
            catch (Exception ex) { Log.ErrorF("EDView.ReloadEpithet failed: {0}", ex.Message); }
        }

        void WriteEpithet_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.EpithetCanWrite) return;

            _vm.EpithetUnitId = Epithet_UnitIdBox.Value;
            _vm.EpithetTextId = (uint)(Epithet_EpithetTextIdBox.Value ?? 0);
            _undoService.Begin("Edit ED Epithet");
            try
            {
                _vm.WriteEpithet();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Ending epithet entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.WriteEpithet failed: {0}", ex.Message);
            }
        }

        void ExpandEpithet_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand ED Epithet");
            try
            {
                var result = _vm.ExpandEpithetList();
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "List expand failed.");
                    return;
                }
                _undoService.Commit();
                LoadEpithetList();
                CoreState.Services?.ShowInfo($"Epithet table expanded to {result.NewCount} entries at 0x{result.NewBaseAddress:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.ExpandEpithet failed: {0}", ex.Message);
            }
        }

        void EpithetUnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epithet_UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpithetUnitId_Jump failed: {0}", ex.Message); }
        }

        async void EpithetUnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epithet_UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) Epithet_UnitIdBox.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpithetUnitId_Pick failed: {0}", ex.Message); }
        }

        void EpithetUnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Epithet_UnitIdBox.NameText = ResolveUnitNameForUid(e.NewValue); }
            catch { /* leave prior text */ }
        }

        // ============================================================
        // Epilogue tab
        // ============================================================

        void LoadEpilogueList()
        {
            var items = _vm.LoadEpilogueList();
            Epilogue_EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            if (Epilogue_TopBar != null)
            {
                Epilogue_TopBar.ReadCountText = items.Count.ToString();
                var rom = CoreState.ROM;
                if (rom?.RomInfo != null)
                {
                    uint ptr = _vm.EpilogueRoute == EDViewModel.EpilogueRouteKind.Ephraim
                        ? rom.RomInfo.ed_3b_pointer
                        : rom.RomInfo.ed_3a_pointer;
                    uint resolved = ptr != 0 ? rom.p32(ptr) : 0;
                    Epilogue_TopBar.StartAddressText = $"0x{resolved:X08}";
                }
            }
        }

        void OnEpilogueSelected(uint addr)
        {
            try
            {
                _vm.LoadEpilogue(addr);
                Epilogue_AddressBox.Value = addr;
                Epilogue_SelectedAddressLabel.Content = $"0x{addr:X08}";

                _suppressEpilogueDesignationSync = true;
                try
                {
                    // PairFlag: 1=Solo, 2=Support; other values shown
                    // as "??" by the WF lambda. Index 0 = Solo, 1 = Support.
                    if (_vm.EpiloguePairFlag == 2)
                        Epilogue_DesignationCombo.SelectedIndex = 1;
                    else if (_vm.EpiloguePairFlag == 1)
                        Epilogue_DesignationCombo.SelectedIndex = 0;
                    else
                        Epilogue_DesignationCombo.SelectedIndex = -1;
                }
                finally { _suppressEpilogueDesignationSync = false; }

                Epilogue_UnitId1Box.Value = _vm.EpilogueUnitId1;
                Epilogue_UnitId2Box.Value = _vm.EpilogueUnitId2;
                try { Epilogue_UnitId1Box.NameText = ResolveUnitNameForUid(_vm.EpilogueUnitId1); }
                catch { /* leave prior text */ }
                try { Epilogue_UnitId2Box.NameText = ResolveUnitNameForUid(_vm.EpilogueUnitId2); }
                catch { /* leave prior text */ }
                Epilogue_StoryFlagBox.Value = _vm.EpilogueStoryFlag;
                Epilogue_EpilogueTextIdBox.Value = _vm.EpilogueTextId;
                RefreshEpilogueTextPreview(_vm.EpilogueTextId);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EDView.OnEpilogueSelected failed: {0}", ex.Message);
            }
        }

        void EpilogueTextId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(Epilogue_EpilogueTextIdBox.Value ?? 0);
            RefreshEpilogueTextPreview(id);
        }

        void RefreshEpilogueTextPreview(uint id)
        {
            try { Epilogue_EpilogueTextLabel.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { Epilogue_EpilogueTextLabel.Text = ""; }
        }

        void EpilogueDesignation_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEpilogueDesignationSync) return;
            int idx = Epilogue_DesignationCombo.SelectedIndex;
            if (idx == 0) _vm.EpiloguePairFlag = 1;
            else if (idx == 1) _vm.EpiloguePairFlag = 2;
        }

        void EpilogueFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // SelectionChanged can fire during XAML init (before VM is
            // wired up via Opened). Guard against the early call to
            // avoid a NullReferenceException - LoadEpilogueList during
            // Opened will reload the list anyway.
            if (Epilogue_FilterCombo == null) return;
            _vm.EpilogueRoute = Epilogue_FilterCombo.SelectedIndex == 1
                ? EDViewModel.EpilogueRouteKind.Ephraim
                : EDViewModel.EpilogueRouteKind.Eirika;
            // Only reload if the entry list has been wired up (post Opened).
            if (!IsLoaded && CoreState.ROM == null) return;
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDView.EpilogueFilter_SelectionChanged failed: {0}", ex.Message); }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnEpilogueTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDView.ReloadEpilogue failed: {0}", ex.Message); }
        }

        void WriteEpilogue_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.EpilogueCanWrite) return;

            // Re-read fields from the UI in case the user typed but the
            // combo / numeric ValueChanged events haven't propagated.
            int idx = Epilogue_DesignationCombo.SelectedIndex;
            if (idx == 0) _vm.EpiloguePairFlag = 1;
            else if (idx == 1) _vm.EpiloguePairFlag = 2;
            // else keep whatever the VM already had (e.g. raw value
            // outside 1/2, matching the WF "??" path).

            _vm.EpilogueUnitId1 = Epilogue_UnitId1Box.Value;
            _vm.EpilogueUnitId2 = Epilogue_UnitId2Box.Value;
            _vm.EpilogueStoryFlag = (uint)(Epilogue_StoryFlagBox.Value ?? 0);
            _vm.EpilogueTextId = (uint)(Epilogue_EpilogueTextIdBox.Value ?? 0);

            _undoService.Begin("Edit ED Epilogue");
            try
            {
                _vm.WriteEpilogue();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Ending epilogue entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.WriteEpilogue failed: {0}", ex.Message);
            }
        }

        void ExpandEpilogue_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand ED Epilogue");
            try
            {
                var result = _vm.ExpandEpilogueList();
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "List expand failed.");
                    return;
                }
                _undoService.Commit();
                LoadEpilogueList();
                CoreState.Services?.ShowInfo($"Epilogue table expanded to {result.NewCount} entries at 0x{result.NewBaseAddress:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDView.ExpandEpilogue failed: {0}", ex.Message);
            }
        }

        void EpilogueUnitId1_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId1Box.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpilogueUnitId1_Jump failed: {0}", ex.Message); }
        }

        async void EpilogueUnitId1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId1Box.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) Epilogue_UnitId1Box.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpilogueUnitId1_Pick failed: {0}", ex.Message); }
        }

        void EpilogueUnitId1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Epilogue_UnitId1Box.NameText = ResolveUnitNameForUid(e.NewValue); }
            catch { /* leave prior text */ }
        }

        void EpilogueUnitId2_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId2Box.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpilogueUnitId2_Jump failed: {0}", ex.Message); }
        }

        async void EpilogueUnitId2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId2Box.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) Epilogue_UnitId2Box.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDView.EpilogueUnitId2_Pick failed: {0}", ex.Message); }
        }

        void EpilogueUnitId2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Epilogue_UnitId2Box.NameText = ResolveUnitNameForUid(e.NewValue); }
            catch { /* leave prior text */ }
        }

        // ============================================================
        // Shared helpers
        // ============================================================

        /// <summary>
        /// Resolve a stored ED <c>unitId</c> (1-based, where <c>0</c> is
        /// the table terminator) to its ROM entry address for the
        /// IdFieldControl Jump/Pick affordances. The unit-table index is
        /// <c>unitId - 1</c> - this matches WF <c>UnitForm.GetUnitName</c>
        /// and <c>InputFormRef.IDToAddr</c> which both decrement
        /// internally. Returns 0 when ROM is unavailable, <c>unitId == 0</c>,
        /// or the entry would fall outside ROM bounds. Copilot PR #561
        /// bot review (off-by-one finding).
        /// </summary>
        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            if (unitId == 0) return 0;
            uint tableIndex = unitId - 1;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            uint entryAddr = baseAddr + tableIndex * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        /// <summary>
        /// Resolve a stored ED UnitId to its display name. ED tables
        /// store UnitIds as 1-based (0 = terminator); the unit-table
        /// index is uid-1. <see cref="SupportUnitNavigation.ResolveUnitTableName"/>
        /// expects a 0-based table index, so we decrement first and
        /// guard the 0 case. Mirrors <see cref="EDViewModel.GetUnitNameForUid"/>.
        /// </summary>
        static string ResolveUnitNameForUid(uint uid)
        {
            if (uid == 0) return "";
            return SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, uid - 1);
        }

        // ============================================================
        // IEditorView / IDataVerifiableView - backward-compat
        // ============================================================

        public void NavigateTo(uint address) => Retreat_EntryList.SelectAddress(address);
        public void SelectFirstItem() => Retreat_EntryList.SelectFirst();
    }
}
