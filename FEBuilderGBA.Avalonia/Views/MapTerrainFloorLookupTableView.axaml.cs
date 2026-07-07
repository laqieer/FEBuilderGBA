using System;
using global::Avalonia;
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
    public partial class MapTerrainFloorLookupTableView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapTerrainFloorLookupTableViewModel _vm = new();
        readonly UndoService _undoService = new();

        bool _hasLoadedList;
        bool _suppressFilterChange;

        public string ViewTitle => "Terrain Floor Lookup Table";
        public new bool IsLoaded => _vm.IsLoaded;

        public EditorDescriptor Descriptor => new("Terrain Floor Lookup Table", 1253, 742, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapTerrainFloorLookupTableView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            FilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                InitialLoad();

            }

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
                // #649: display via the unified EditorTopBar read-only slots.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();
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

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
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
        /// both axes per Copilot CLI review point 2). Both indices are clamped
        /// to valid ranges to mirror the WinForms `SelectedIndexSafety` helper;
        /// out-of-range arguments fall back to filter 0 / row 0 instead of
        /// silently emptying the list.
        /// </summary>
        public void NavigateToFilterAndRow(uint filterIndex, uint rowIndex)
        {
            // Mirror WinForms SelectedIndexSafety: clamp filter to a valid
            // slot. When the combo is empty we keep filter=0 — there's
            // nothing to load but we still set the row safely.
            int filterCount = _vm.FilterEntries.Count;
            int safeFilter = filterCount == 0 ? 0
                : (int)Math.Min(filterIndex, (uint)(filterCount - 1));

            _suppressFilterChange = true;
            try
            {
                if (filterCount > 0)
                    FilterComboBox.SelectedIndex = safeFilter;
            }
            finally { _suppressFilterChange = false; }

            LoadListForFilter(safeFilter);

            // Clamp the row to the list count we just loaded.
            int listCount = EntryList.GetItems().Count;
            int safeRow = listCount == 0 ? 0
                : (int)Math.Min(rowIndex, (uint)(listCount - 1));
            EntryList.SelectByIndex(safeRow);
        }

        /// <summary>
        /// Parse a `filter:row` reference string (mirrors WinForms
        /// `MapTerrainFloorLookupTableForm.JumpToRef`) and navigate. The
        /// regex accepts e.g. `"0x03 ... 0x01:"` and pulls filter=0x01,
        /// row=0x03 (same semantics as the WF implementation). Returns
        /// null when the input doesn't match the WinForms expected shape —
        /// callers should NOT see a window pop up for malformed references.
        /// </summary>
        public static MapTerrainFloorLookupTableView? JumpToRef(string text)
        {
            // Mirror WinForms: RegexCache.Split(text, @"([0-9a-zA-Z]+) .+? ([0-9a-zA-Z]+):")
            // — returns without opening when the pattern doesn't yield both
            // captures, so partial/malformed references don't open a stray
            // window. (Matches the WF early-return semantics.)
            uint filter, row;
            try
            {
                var parts = FEBuilderGBA.RegexCache.Split(text ?? "", @"([0-9a-zA-Z]+) .+? ([0-9a-zA-Z]+):");
                if (parts.Length <= 2)
                    return null;
                row = U.atoh(parts[1]);
                filter = U.atoh(parts[2]);
            }
            catch
            {
                return null;
            }
            var v = WindowManager.Instance.Open<MapTerrainFloorLookupTableView>();
            v.NavigateToFilterAndRow(filter, row);
            return v;
        }
    }
}
