// SPDX-License-Identifier: GPL-3.0-or-later
// EDFE7View code-behind - gap-sweep #403 parity raise.
//
// Wires the four Avalonia tabs (Lyn / Retreat / Epithet / Epilogue) to the
// per-tab ViewModel surfaces. Each Write_*_Click handler opens its own
// distinctly-named UndoService scope so undo for the four tabs is
// independent. The Lyn tab has NO list-expand handler because the
// ed_3c_pointer table is direct-base (Copilot CLI v1 plan-review C1).
//
// Field-width tests in EDFE7ParityTests.cs verify the codec choices
// (D0/D4/D8 for Lyn, B0/B1/B2/B3 for Retreat, D0/D4 for Epithet,
// B0/B1/B2/B3/D4 for Epilogue) match the WF designer's NumericUpDown
// widths exactly.
//
// Backward-compat: legacy `NavigateTo` / `SelectFirstItem` point at
// the Lyn tab list (the original surface), so any pre-existing
// ListParityHelper / INavigationTargetSource callers keep working.
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EDFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressEpilogueDesignationSync;

        public string ViewTitle => "ED (FE7)";
        public new bool IsLoaded =>
            _vm.LynCanWrite || _vm.RetreatCanWrite || _vm.EpithetCanWrite || _vm.EpilogueCanWrite;
        public EditorDescriptor Descriptor => new("ED (FE7)", 1280, 820, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EDFE7View()
        {
            InitializeComponent();
            Lyn_EntryList.SelectedAddressChanged += OnLynSelected;
            Retreat_EntryList.SelectedAddressChanged += OnRetreatSelected;
            Epithet_EntryList.SelectedAddressChanged += OnEpithetSelected;
            Epilogue_EntryList.SelectedAddressChanged += OnEpilogueSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadAllLists();
            }
        }

        // ============================================================
        // Common load entry-point
        // ============================================================

        void LoadAllLists()
        {
            try { LoadLynList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LoadLynList failed: {0}", ex.Message); }
            try { LoadRetreatList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LoadRetreatList failed: {0}", ex.Message); }
            try { LoadEpithetList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LoadEpithetList failed: {0}", ex.Message); }
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LoadEpilogueList failed: {0}", ex.Message); }
        }

        // ============================================================
        // Lyn tab (ed_3c_pointer USED AS DIRECT BASE - NO list expand)
        // ============================================================

        void LoadLynList()
        {
            var items = _vm.LoadLynList();
            Lyn_EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            if (Lyn_TopBar != null)
            {
                Lyn_TopBar.ReadCountText = items.Count.ToString();
                var rom = CoreState.ROM;
                // For Lyn ed_3c_pointer IS the table base (NOT a pointer field);
                // display it as the top address directly without p32().
                if (rom?.RomInfo != null && rom.RomInfo.ed_3c_pointer != 0)
                    Lyn_TopBar.StartAddressText = $"0x{rom.RomInfo.ed_3c_pointer:X08}";
            }
        }

        void OnLynSelected(uint addr)
        {
            try
            {
                _vm.LoadLyn(addr);
                Lyn_AddressBox.Value = addr;
                Lyn_SelectedAddressLabel.Content = $"0x{addr:X08}";
                Lyn_UnitIdBox.Value = _vm.LynUnitId;
                try { Lyn_UnitIdBox.NameText = ResolveUnitNameForUid(_vm.LynUnitId); }
                catch { /* leave prior text */ }
                Lyn_ClearedTextIdBox.Value = _vm.LynClearedTextId;
                Lyn_RetreatTextIdBox.Value = _vm.LynRetreatTextId;
                RefreshLynClearedTextPreview(_vm.LynClearedTextId);
                RefreshLynRetreatTextPreview(_vm.LynRetreatTextId);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EDFE7View.OnLynSelected failed: {0}", ex.Message);
            }
        }

        void LynClearedTextId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(Lyn_ClearedTextIdBox.Value ?? 0);
            RefreshLynClearedTextPreview(id);
        }

        void LynRetreatTextId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(Lyn_RetreatTextIdBox.Value ?? 0);
            RefreshLynRetreatTextPreview(id);
        }

        void RefreshLynClearedTextPreview(uint id)
        {
            try { Lyn_ClearedTextLabel.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { Lyn_ClearedTextLabel.Text = ""; }
        }

        void RefreshLynRetreatTextPreview(uint id)
        {
            try { Lyn_RetreatTextLabel.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { Lyn_RetreatTextLabel.Text = ""; }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnLynTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadLynList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.ReloadLyn failed: {0}", ex.Message); }
        }

        void WriteLyn_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.LynCanWrite) return;

            _vm.LynUnitId = Lyn_UnitIdBox.Value;
            _vm.LynClearedTextId = (uint)(Lyn_ClearedTextIdBox.Value ?? 0);
            _vm.LynRetreatTextId = (uint)(Lyn_RetreatTextIdBox.Value ?? 0);
            _undoService.Begin("Edit EDFE7 Lyn");
            try
            {
                _vm.WriteLyn();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Lyn arc entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDFE7View.WriteLyn failed: {0}", ex.Message);
            }
        }

        void LynUnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Lyn_UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LynUnitId_Jump failed: {0}", ex.Message); }
        }

        async void LynUnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Lyn_UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) Lyn_UnitIdBox.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.LynUnitId_Pick failed: {0}", ex.Message); }
        }

        void LynUnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Lyn_UnitIdBox.NameText = ResolveUnitNameForUid(e.NewValue); }
            catch { /* leave prior text */ }
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
                Log.ErrorF("EDFE7View.OnRetreatSelected failed: {0}", ex.Message);
            }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnRetreatTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadRetreatList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.ReloadRetreat failed: {0}", ex.Message); }
        }

        void WriteRetreat_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.RetreatCanWrite) return;

            _vm.RetreatUnitId = Retreat_UnitIdBox.Value;
            _vm.RetreatCondition = (uint)(Retreat_ConditionBox.Value ?? 0);
            _vm.RetreatB2 = (uint)(Retreat_B2Box.Value ?? 0);
            _vm.RetreatB3 = (uint)(Retreat_B3Box.Value ?? 0);
            _undoService.Begin("Edit EDFE7 Retreat");
            try
            {
                _vm.WriteRetreat();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Retreat entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDFE7View.WriteRetreat failed: {0}", ex.Message);
            }
        }

        void ExpandRetreat_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand EDFE7 Retreat");
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
                Log.ErrorF("EDFE7View.ExpandRetreat failed: {0}", ex.Message);
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
            catch (Exception ex) { Log.ErrorF("EDFE7View.RetreatUnitId_Jump failed: {0}", ex.Message); }
        }

        async void RetreatUnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Retreat_UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) Retreat_UnitIdBox.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.RetreatUnitId_Pick failed: {0}", ex.Message); }
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
                Log.ErrorF("EDFE7View.OnEpithetSelected failed: {0}", ex.Message);
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
            catch (Exception ex) { Log.ErrorF("EDFE7View.ReloadEpithet failed: {0}", ex.Message); }
        }

        void WriteEpithet_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.EpithetCanWrite) return;

            _vm.EpithetUnitId = Epithet_UnitIdBox.Value;
            _vm.EpithetTextId = (uint)(Epithet_EpithetTextIdBox.Value ?? 0);
            _undoService.Begin("Edit EDFE7 Epithet");
            try
            {
                _vm.WriteEpithet();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Epithet entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDFE7View.WriteEpithet failed: {0}", ex.Message);
            }
        }

        void ExpandEpithet_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand EDFE7 Epithet");
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
                Log.ErrorF("EDFE7View.ExpandEpithet failed: {0}", ex.Message);
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
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpithetUnitId_Jump failed: {0}", ex.Message); }
        }

        async void EpithetUnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epithet_UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) Epithet_UnitIdBox.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpithetUnitId_Pick failed: {0}", ex.Message); }
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
                Epilogue_TopBar.ReadCountText = items.Count.ToString();
            var rom = CoreState.ROM;
            if (rom?.RomInfo != null && Epilogue_TopBar != null)
            {
                uint ptr = _vm.EpilogueRoute == EDFE7ViewModel.EpilogueRouteKind.Hector
                    ? rom.RomInfo.ed_3b_pointer
                    : rom.RomInfo.ed_3a_pointer;
                uint resolved = ptr != 0 ? rom.p32(ptr) : 0;
                Epilogue_TopBar.StartAddressText = $"0x{resolved:X08}";
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
                Log.ErrorF("EDFE7View.OnEpilogueSelected failed: {0}", ex.Message);
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
            if (Epilogue_FilterCombo == null) return;
            _vm.EpilogueRoute = Epilogue_FilterCombo.SelectedIndex == 1
                ? EDFE7ViewModel.EpilogueRouteKind.Hector
                : EDFE7ViewModel.EpilogueRouteKind.Eliwood;
            if (!IsLoaded && CoreState.ROM == null) return;
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpilogueFilter_SelectionChanged failed: {0}", ex.Message); }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnEpilogueTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadEpilogueList(); }
            catch (Exception ex) { Log.ErrorF("EDFE7View.ReloadEpilogue failed: {0}", ex.Message); }
        }

        void WriteEpilogue_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.EpilogueCanWrite) return;

            int idx = Epilogue_DesignationCombo.SelectedIndex;
            if (idx == 0) _vm.EpiloguePairFlag = 1;
            else if (idx == 1) _vm.EpiloguePairFlag = 2;

            _vm.EpilogueUnitId1 = Epilogue_UnitId1Box.Value;
            _vm.EpilogueUnitId2 = Epilogue_UnitId2Box.Value;
            _vm.EpilogueStoryFlag = (uint)(Epilogue_StoryFlagBox.Value ?? 0);
            _vm.EpilogueTextId = (uint)(Epilogue_EpilogueTextIdBox.Value ?? 0);

            _undoService.Begin("Edit EDFE7 Epilogue");
            try
            {
                _vm.WriteEpilogue();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Epilogue entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDFE7View.WriteEpilogue failed: {0}", ex.Message);
            }
        }

        void ExpandEpilogue_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand EDFE7 Epilogue");
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
                Log.ErrorF("EDFE7View.ExpandEpilogue failed: {0}", ex.Message);
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
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpilogueUnitId1_Jump failed: {0}", ex.Message); }
        }

        async void EpilogueUnitId1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId1Box.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) Epilogue_UnitId1Box.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpilogueUnitId1_Pick failed: {0}", ex.Message); }
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
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpilogueUnitId2_Jump failed: {0}", ex.Message); }
        }

        async void EpilogueUnitId2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(Epilogue_UnitId2Box.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) Epilogue_UnitId2Box.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("EDFE7View.EpilogueUnitId2_Pick failed: {0}", ex.Message); }
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
        /// Resolve a stored ED <c>unitId</c> to its ROM entry address. Mirrors
        /// EDView's helper.
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
        /// Resolve a stored ED UnitId to its display name (1-based ED uid
        /// -> 0-based unit-table index via uid-1).
        /// </summary>
        static string ResolveUnitNameForUid(uint uid)
        {
            if (uid == 0) return "";
            return SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, uid - 1);
        }

        // ============================================================
        // IEditorView / IDataVerifiableView - backward-compat
        // ============================================================

        public void NavigateTo(uint address) => Lyn_EntryList.SelectAddress(address);
        public void SelectFirstItem() => Lyn_EntryList.SelectFirst();
    }
}
