using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `MapTerrainFloorLookupTableForm`.
    /// Phase 4 gap-fix (#442): exposes the missing FilterComboBox, address/read
    /// indicators, reload + jump buttons, and the patch-install affordance —
    /// so the view density matches WinForms within the 25% MEDIUM verdict.
    /// </summary>
    public partial class MapTerrainFloorLookupTableView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainFloorLookupTableViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressFilterChange;

        public string ViewTitle => "Terrain Floor Lookup Table";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTerrainFloorLookupTableView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            FilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            Opened += (_, _) => InitialLoad();
        }

        void InitialLoad()
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadFilterEntries();
                _suppressFilterChange = true;
                FilterComboBox.ItemsSource = _vm.FilterEntries;
                if (_vm.FilterEntries.Count > 0)
                    FilterComboBox.SelectedIndex = 0;
                _suppressFilterChange = false;

                // Patch-install button visibility mirrors the WF
                // ERROR_Not_Allocated label that only shows when the patch is
                // installed AND the current slot is unallocated. We default
                // to "visible only when patch is on and not allocated" below.
                PatchInstallButton.IsVisible = _vm.IsExtendsPatchInstalled && !_vm.IsAllocated;

                LoadListForFilter(0);
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainFloorLookupTableView.InitialLoad failed: {ex.Message}");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void LoadListForFilter(int filterIndex)
        {
            try
            {
                var items = _vm.LoadList(filterIndex);
                EntryList.SetItems(items);
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
                // The patch-install button stays visible when the patch is
                // installed AND the current slot has no allocated data.
                PatchInstallButton.IsVisible = _vm.IsExtendsPatchInstalled && !_vm.IsAllocated;
                if (items.Count > 0)
                {
                    EntryList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainFloorLookupTableView.LoadListForFilter failed: {ex.Message}");
            }
        }

        void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterChange) return;
            int idx = FilterComboBox.SelectedIndex;
            if (idx < 0) return;
            LoadListForFilter(idx);
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            int idx = Math.Max(0, FilterComboBox.SelectedIndex);
            LoadListForFilter(idx);
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainFloorLookupTableView.OnSelected failed: {ex.Message}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Label uses Content (object), not Text — Avalonia Label is a
            // ContentControl, unlike WinForms.
            AddrLabel.Content = $"0x{_vm.CurrentAddr:X08}";
            TerrainBattleFloorBox.Value = _vm.TerrainBattleFloor;
            ItemAddressBox.Value = _vm.ItemAddress;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Terrain Floor Lookup");
            try
            {
                _vm.TerrainBattleFloor = (uint)(TerrainBattleFloorBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain Floor lookup data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MapTerrainFloorLookupTableView.Write: {ex.Message}"); }
        }

        void JumpToBG_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `X_JUMP_BG_Click`: open the BG editor at the
            // SAME filter index and selected row. Preserves both filter and
            // address per Copilot CLI review (point 2 — non-zero filter
            // must navigate to the correct table on the BG side).
            int filterIdx = Math.Max(0, FilterComboBox.SelectedIndex);
            int rowIdx = Math.Max(0, EntryList.SelectedOriginalIndex);
            var bg = WindowManager.Instance.Open<MapTerrainBGLookupTableView>();
            bg.NavigateToFilterAndRow((uint)filterIdx, (uint)rowIdx);
        }

        void PatchInstall_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `ERROR_Not_Allocated_Click`: open PatchManager.
            // The Avalonia PatchManagerView doesn't accept a "jump-to-patch"
            // address (no inline search like the WinForms PatchForm.JumpTo),
            // so we just open it; the user picks the ExtendsBattleBG row.
            WindowManager.Instance.Open<PatchManagerView>();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// Filter+row navigation. Used by the BG sister view's "Jump to Floor"
        /// button and by deep-link callers (gap-sweep parity rounds preserve
        /// both axes per Copilot CLI review point 2).
        /// </summary>
        public void NavigateToFilterAndRow(uint filterIndex, uint rowIndex)
        {
            _suppressFilterChange = true;
            try
            {
                if (filterIndex < _vm.FilterEntries.Count)
                    FilterComboBox.SelectedIndex = (int)filterIndex;
            }
            finally { _suppressFilterChange = false; }
            LoadListForFilter((int)filterIndex);
            EntryList.SelectByIndex((int)rowIndex);
        }

        /// <summary>
        /// Parse a `filter:row` reference string (mirrors WinForms
        /// `MapTerrainFloorLookupTableForm.JumpToRef`) and navigate. The
        /// regex accepts e.g. `"0x03 ... 0x01:"` and pulls filter=0x01,
        /// row=0x03 (same semantics as the WF implementation).
        /// </summary>
        public static MapTerrainFloorLookupTableView JumpToRef(string text)
        {
            // Mirror WinForms: RegexCache.Split(text, @"([0-9a-zA-Z]+) .+? ([0-9a-zA-Z]+):")
            // ptrn[1] = list-row hex, ptrn[2] = filter hex. Defensive when
            // parsing fails — open the view without preselection.
            uint filter = 0, row = 0;
            try
            {
                var parts = FEBuilderGBA.RegexCache.Split(text ?? "", @"([0-9a-zA-Z]+) .+? ([0-9a-zA-Z]+):");
                if (parts.Length > 2)
                {
                    row = U.atoh(parts[1]);
                    filter = U.atoh(parts[2]);
                }
            }
            catch { /* defensive — open without preselection */ }
            var v = WindowManager.Instance.Open<MapTerrainFloorLookupTableView>();
            v.NavigateToFilterAndRow(filter, row);
            return v;
        }
    }
}
