using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// MapExitPointView — the three-pane master-detail editor mirroring
    /// WinForms <c>MapExitPointForm</c> (#425). Rebuilt for gap-sweep parity:
    /// adds Filter combo (Enemy/NPC), Map list, per-map exit-point sub-list,
    /// detail panel (X/Y/Escape/Flag), notice panel, and the New Allocation
    /// + Expand List affordances.
    /// </summary>
    public partial class MapExitPointView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapExitPointViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressEscapeComboSync;
        bool _hasLoadedList;

        public string ViewTitle => "Map Exit Point Editor";
        public new bool IsLoaded => _vm.CanWrite;

        public EditorDescriptor Descriptor => new("Map Exit Point Editor", 1607, 777, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapExitPointView()
        {
            InitializeComponent();
            // Mirror WF MapExitPointForm constructor: hard-coded English in
            // the WF combo; preserve the same literal here. (These are the
            // ONLY two English-only literals WF didn't run through R._;
            // tracked for future locale work but kept identical to WF here.)
            FilterComboBox.Items.Add(R._("Enemy Escape Point"));
            FilterComboBox.Items.Add(R._("NPC Escape Point"));
            FilterComboBox.SelectionChanged += FilterCombo_SelectionChanged;
            // Disappearance method values (mirror WF N_L_2_COMBO). Use R._()
            // with the WF Japanese source keys so the items are localized
            // through the en/ja/zh translation chain at runtime — fixes the
            // Copilot PR #531 review thread on view-cs line 38 (ComboBox
            // items are not translated by ViewTranslationHelper).
            EscapeMethodCombo.Items.Add(R._("00=左2歩"));
            EscapeMethodCombo.Items.Add(R._("01=右2歩"));
            EscapeMethodCombo.Items.Add(R._("02=下2歩"));
            EscapeMethodCombo.Items.Add(R._("03=上2歩"));
            EscapeMethodCombo.Items.Add(R._("05=その場"));
            EscapeMethodCombo.SelectionChanged += EscapeCombo_SelectionChanged;
            EscapeMethodBox.ValueChanged += EscapeMethodBox_ValueChanged;

            MapList.SelectedAddressChanged += OnMapSelected;
            ExitList.SelectedAddressChanged += OnExitSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                FilterComboBox.SelectedIndex = 0;
            }
        }

        // -----------------------------------------------------------------
        // Filter / Map list loading
        // -----------------------------------------------------------------

        void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            ReloadMapList();
        }

        void ReloadMapList()
        {
            _vm.IsLoading = true;
            try
            {
                int filterIdx = Math.Max(0, FilterComboBox.SelectedIndex);
                var maps = _vm.LoadMapList(filterIdx);

                // Clear the exit sub-list BEFORE refreshing the map list.
                // MapList.SetItems(...) calls SelectFirst() synchronously,
                // which fires SelectedAddressChanged and invokes OnMapSelected
                // → that path populates ExitList for the newly-selected map.
                // If we cleared ExitList AFTER SetItems we'd wipe the populated
                // sub-list (Copilot PR #531 review thread on view-cs line 79).
                ExitList.SetItems(new List<AddrResult>());
                NewAllocButton.IsVisible = false;

                MapList.SetItems(maps);
                UpdateTopBar();
                FilterNoticeText.Text = _vm.Notice;
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapExitPointView.ReloadMapList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnMapSelected(uint mapSlotAddr)
        {
            _vm.IsLoading = true;
            try
            {
                var exits = _vm.LoadExitListForMap(mapSlotAddr);
                ExitList.SetItems(exits);
                // Update Exit Pointer numeric to reflect the dereferenced value.
                ExitPointerBox.Value = _vm.CurrentExitPointAddr == U.NOT_FOUND
                    ? 0
                    : _vm.CurrentExitPointAddr;
                // Show NewAlloc affordance when the slot is unallocated.
                NewAllocButton.IsVisible = _vm.IsBlank;
                // #9: when the map has no exit points the sub-list is empty and
                // no row-selection fires — clear the detail panel instead of
                // leaving the previous map's stale values.
                if (exits.Count == 0)
                {
                    _vm.ClearExitPointEntry();
                    UpdateDetailUI();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapExitPointView.OnMapSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnExitSelected(uint exitRowAddr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadExitPointEntry(exitRowAddr);
                UpdateDetailUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapExitPointView.OnExitSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        // -----------------------------------------------------------------
        // UI sync
        // -----------------------------------------------------------------

        void UpdateTopBar()
        {
            // #649: ReadStart displays via the unified EditorTopBar; ReadCount
            // lives in the second-row panel-2 ExitPointer bar (legacy
            // ReadCountBox NumericUpDown).
            if (_vm.ReadStartAddress != U.NOT_FOUND)
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
            ReadCountBox.Value = _vm.ReadCount;
        }

        void UpdateDetailUI()
        {
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Text = $"0x{_vm.BlockSize:X02}";
            SelectedAddressBox.Text = $"0x{_vm.SelectedAddressDisplay:X08}";
            ExitXBox.Value = _vm.ExitX;
            ExitYBox.Value = _vm.ExitY;
            _suppressEscapeComboSync = true;
            try
            {
                EscapeMethodBox.Value = _vm.EscapeMethod;
                EscapeMethodCombo.SelectedIndex = EscapeMethodValueToComboIndex(_vm.EscapeMethod);
            }
            finally { _suppressEscapeComboSync = false; }
            FlagBox.Value = _vm.FlagId;
            FlagNameBox.Text = $"Flag 0x{_vm.FlagId:X02}";
        }

        /// <summary>
        /// Map an escape-method byte (00,01,02,03,05) to its combo index
        /// (0..4). Anything else maps to -1 (no selection).
        /// </summary>
        internal static int EscapeMethodValueToComboIndex(uint value)
        {
            return value switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                5 => 4,
                _ => -1,
            };
        }

        /// <summary>
        /// Inverse of <see cref="EscapeMethodValueToComboIndex"/>.
        /// </summary>
        internal static uint EscapeMethodComboIndexToValue(int index)
        {
            return index switch
            {
                0 => 0u,
                1 => 1u,
                2 => 2u,
                3 => 3u,
                4 => 5u,
                _ => 0u,
            };
        }

        void EscapeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEscapeComboSync) return;
            int idx = EscapeMethodCombo.SelectedIndex;
            if (idx < 0) return;
            EscapeMethodBox.Value = EscapeMethodComboIndexToValue(idx);
        }

        void EscapeMethodBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEscapeComboSync) return;
            _suppressEscapeComboSync = true;
            try
            {
                uint v = e.NewValue.HasValue ? (uint)e.NewValue.Value : 0u;
                EscapeMethodCombo.SelectedIndex = EscapeMethodValueToComboIndex(v);
            }
            finally { _suppressEscapeComboSync = false; }
        }

        // -----------------------------------------------------------------
        // Click handlers — all ROM writes wrapped in _undoService
        // -----------------------------------------------------------------

        void ReloadList_Click(object? sender, RoutedEventArgs e) => ReloadMapList();

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => ReloadMapList();

        void ReloadExits_Click(object? sender, RoutedEventArgs e)
        {
            OnMapSelected(_vm.SelectedMapSlotAddr);
        }

        void WritePointer_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.SelectedMapSlotAddr == 0) return;
            _undoService.Begin("Edit Map Exit Pointer");
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) { _undoService.Rollback(); return; }
                // ExitPointerBox holds a ROM offset (per LoadExitListForMap →
                // CurrentExitPointAddr → U.toOffset(...) chain). The slot
                // table stores GBA pointers, so we MUST use write_p32 (which
                // applies the 0x08000000 base) rather than write_u32 on the
                // raw offset — writing the offset directly would corrupt the
                // pointer (Copilot PR #531 review thread on line 219).
                uint offset = ExitPointerBox.Value.HasValue ? (uint)ExitPointerBox.Value.Value : 0u;
                if (offset == 0u)
                {
                    // Treat as NULL — write zero bytes via raw u32 so the
                    // slot reads as "no exit data". write_p32 adds 0x08000000
                    // and would produce 0x08000000 for a zero offset, which
                    // is not the WF NULL convention.
                    rom.write_u32(_vm.SelectedMapSlotAddr, 0u);
                }
                else
                {
                    rom.write_p32(_vm.SelectedMapSlotAddr, offset);
                }
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Exit Pointer written.");
                // Reload sub-list to reflect the new pointer.
                OnMapSelected(_vm.SelectedMapSlotAddr);
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapExitPointView.WritePointer: {0}", ex.Message); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Map Exit Point");
            try
            {
                _vm.ExitX = (uint)(ExitXBox.Value ?? 0);
                _vm.ExitY = (uint)(ExitYBox.Value ?? 0);
                _vm.EscapeMethod = (uint)(EscapeMethodBox.Value ?? 0);
                _vm.FlagId = (uint)(FlagBox.Value ?? 0);
                _vm.WriteExitPoint();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Map Exit Point data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapExitPointView.Write: {0}", ex.Message); }
        }

        void NewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.SelectedMapSlotAddr == 0) return;
            _undoService.Begin("MapExit NewAlloc");
            try
            {
                uint newaddr = _vm.NewAlloc(_undoService.GetActiveUndoData());
                if (newaddr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Failed to allocate new exit-point block.");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                // Refresh the sub-list now that a new block exists.
                OnMapSelected(_vm.SelectedMapSlotAddr);
                CoreState.Services?.ShowInfo($"Allocated new exit-point block at 0x{newaddr:X08}.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapExitPointView.NewAlloc: {0}", ex.Message); }
        }

        void ExpandList_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.SelectedMapSlotAddr == 0) return;
            _undoService.Begin("MapExit ExpandList");
            try
            {
                var r = _vm.ExpandExitList(_undoService.GetActiveUndoData());
                if (!r.Success) { _undoService.Rollback(); CoreState.Services?.ShowError(r.Error); return; }
                _undoService.Commit();
                _vm.MarkClean();
                OnMapSelected(_vm.SelectedMapSlotAddr);
                CoreState.Services?.ShowInfo($"Expanded exit-point block to {r.NewCount} rows at 0x{r.NewBaseAddress:X08}.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapExitPointView.ExpandList: {0}", ex.Message); }
        }

        // -----------------------------------------------------------------
        // IEditorView
        // -----------------------------------------------------------------

        public void NavigateTo(uint address)
        {
            // `address` is a ROM address — specifically the map-list entry's
            // pointer-slot address as produced by
            // `MapExitPointCore.ListMapEntries` (each AddrResult.addr is the
            // 4-byte slot in the pointer table, not a map-id index). The
            // sibling SelectFirstItem() path covers the no-deep-link case.
            // (Copilot PR #531 third-pass review on view-cs line 317 —
            // comment said "map id" but the AddressListControl matches by
            // .addr.)
            MapList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            MapList.SelectFirst();
        }
    }
}
