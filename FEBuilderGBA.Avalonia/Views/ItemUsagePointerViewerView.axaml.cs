// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #440 rebuild — exposes the 10-filter dispatch, address/count
// indicators, expand button, related-link panels, and the IER red bar
// missing from the pre-#440 view (which only handled the Usability slot).
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
    /// Avalonia counterpart of WinForms <c>ItemUsagePointerForm</c>.
    /// Phase 1 + 4 gap-fix (#440): exposes the missing FilterComboBox,
    /// address/read indicators, list-expansion + reload buttons, two
    /// related-link panels (Promotion / Stat Booster), and the IER
    /// patch-install affordance — so the view density matches WinForms
    /// within the 25% MEDIUM verdict.
    /// </summary>
    public partial class ItemUsagePointerViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemUsagePointerViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        bool _hasLoadedList;
        bool _suppressFilterChange;
        bool _suppressFunctionComboChange;
        List<string> _currentFunctionLines = new();

        public string ViewTitle => "Item Usage Pointer";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Item Usage Pointer Editor", 1253, 801, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ItemUsagePointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            FilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            FunctionCombo.SelectionChanged += FunctionCombo_SelectionChanged;        }


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
                _vm.RefreshPatchState();
                _suppressFilterChange = true;
                FilterComboBox.ItemsSource = _vm.FilterEntries;
                if (_vm.FilterEntries.Count > 0)
                    FilterComboBox.SelectedIndex = 0;
                _suppressFilterChange = false;

                LoadListForFilter(0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.InitialLoad: {0}", ex.Message);
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

                // #943 bug #4: populate the L_0_COMBO equivalent BEFORE loading
                // the address list. SetItemsWithIcons() calls SelectFirst()
                // internally, which synchronously fires the row-selection chain
                // (OnSelected -> UpdateUI) that needs _currentFunctionLines and
                // FunctionCombo.ItemsSource already in place to resolve the
                // matching function entry. If we populated the combo AFTER
                // SetItemsWithIcons, UpdateUI ran with an empty
                // _currentFunctionLines and a null ItemsSource, so the combo
                // stayed blank (the later EntryList.SelectFirst() does not
                // re-fire OnSelected because the index is already 0).
                _currentFunctionLines = _vm.LoadFunctionLines(filterIndex);
                _suppressFunctionComboChange = true;
                try
                {
                    FunctionCombo.ItemsSource = _currentFunctionLines;
                    FunctionCombo.SelectedItem = null;
                }
                finally
                {
                    // try/finally so an exception during the ItemsSource/SelectedItem
                    // assignment can't leave the suppress flag stuck true, which would
                    // permanently mute user-driven FunctionCombo writes for this view.
                    _suppressFunctionComboChange = false;
                }

                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));

                // Refresh the read-only top/select bar indicators from VM state.
                // #649: display via the unified EditorTopBar read-only slots.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();
                AsmSwitchBox.Text = _vm.AsmSwitchText;
                BlockSizeBox.Text = _vm.BlockSize.ToString();
                ItemAddressBox.Value = (decimal)_vm.CurrentArrayAddr;

                // Promotion/StatBooster panels visibility — mirrors WinForms.
                bool isPromo = filterIndex == (int)ItemUsagePointerCore.FilterKind.Promotion1
                            || filterIndex == (int)ItemUsagePointerCore.FilterKind.Promotion2;
                bool isStatBooster = filterIndex == (int)ItemUsagePointerCore.FilterKind.StatBooster1
                                  || filterIndex == (int)ItemUsagePointerCore.FilterKind.StatBooster2
                                  || filterIndex == (int)ItemUsagePointerCore.FilterKind.ErrorMessage;
                PromotionItemExplainPanel.IsVisible = isPromo;
                StatBoosterItemExplainPanel.IsVisible = isStatBooster;

                // IER and X_NOT_FOUND panels — mirrors WF logic.
                if (!_vm.IsEnabledForCurrentFilter)
                {
                    SwitchListExpandsButton.IsVisible = false;
                    WriteButton.IsVisible = false;

                    if (_vm.IsIERPatchInstalled)
                    {
                        NotFoundLabel.IsVisible = false;
                        IerPatchPanel.IsVisible = true;
                    }
                    else
                    {
                        NotFoundLabel.IsVisible = true;
                        IerPatchPanel.IsVisible = false;
                    }
                }
                else
                {
                    SwitchListExpandsButton.IsVisible = true;
                    WriteButton.IsVisible = true;
                    NotFoundLabel.IsVisible = false;
                    IerPatchPanel.IsVisible = false;
                }

                if (items.Count > 0)
                {
                    EntryList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.LoadListForFilter: {0}", ex.Message);
            }
        }

        void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterChange) return;
            int idx = FilterComboBox.SelectedIndex;
            if (idx < 0) return;
            LoadListForFilter(idx);
        }

        void FunctionCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Mirror WinForms FormUtil.WriteComboBoxLinkInfo behavior — when the
            // user picks a named function from the dropdown, parse its hex key
            // (the left side of `=`) and update UsabilityPointer.
            if (_suppressFunctionComboChange) return;
            int idx = FunctionCombo.SelectedIndex;
            if (idx < 0 || idx >= _currentFunctionLines.Count) return;
            string line = _currentFunctionLines[idx];
            // Each line is `0xHEX[=Name1,Name2]` — pull the leading hex token.
            int eq = line.IndexOf('=');
            string hexToken = eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim();
            uint ptr = U.atoh(hexToken);
            _vm.UsabilityPointer = ptr;
            UsabilityPointerBox.Value = (decimal)ptr;
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
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.OnSelected: {0}", ex.Message);
            }
            finally
            {
                // Always reset IsLoading — leaving it stuck true would
                // suppress dirty tracking + UI updates elsewhere
                // (Copilot CLI re-review fix).
                _vm.IsLoading = false;
            }
        }

        void UpdateUI()
        {
            SelectedAddressBox.Text = $"0x{_vm.CurrentSelectedAddr:X08}";
            UsabilityPointerBox.Value = (decimal)_vm.UsabilityPointer;

            // Sync the FunctionCombo selection to the current pointer (if a
            // line in the config data matches). Mirrors WF L_0_COMBO behavior.
            ApplyFunctionComboSelection();
        }

        /// <summary>
        /// Select the FunctionCombo entry whose hex key matches the current
        /// UsabilityPointer (or clear it when nothing matches). Mirrors the
        /// WinForms L_0_COMBO read-link behavior.
        ///
        /// #943 bug #4: this relies on <see cref="_currentFunctionLines"/> and
        /// <c>FunctionCombo.ItemsSource</c> already being populated — which
        /// LoadListForFilter now guarantees by setting them up BEFORE the
        /// address-list load that triggers the first row selection. We select
        /// by item reference (SelectedItem), which the ComboBox resolves by
        /// value against its ItemsSource.
        /// </summary>
        void ApplyFunctionComboSelection()
        {
            string? matchLine = null;
            for (int i = 0; i < _currentFunctionLines.Count; i++)
            {
                string line = _currentFunctionLines[i];
                int eq = line.IndexOf('=');
                string hexToken = eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim();
                if (U.atoh(hexToken) == _vm.UsabilityPointer)
                {
                    matchLine = line;
                    break;
                }
            }

            _suppressFunctionComboChange = true;
            try
            {
                FunctionCombo.SelectedItem = matchLine;
            }
            finally { _suppressFunctionComboChange = false; }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UsabilityPointer = (uint)(UsabilityPointerBox.Value ?? 0);

            _undoService.Begin("Edit Item Usage Pointer");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Item Usage Pointer data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ItemUsagePointerViewerView.Write_Click: {0}", ex.Message);
            }
        }

        void SwitchListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms `ItemUsagePointerForm.SwitchListExpandsButton_Click`:
            // newCount = ItemForm.DataCount() (the total number of items in
            // the ROM); defAddr = the first FunctionCombo entry that is NOT a
            // "-" placeholder (i.e. the first valid named function in the
            // config data), falling back to the first entry if no "-" exists.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            // WF aborts expansion when `L_0_COMBO.Items.Count <= 0`. Mirror
            // that guard so we never write a table filled with NULL pointers
            // because the config data file was missing. (Copilot CLI re-
            // review fix — issue 3 of PR #497.)
            if (_currentFunctionLines.Count == 0)
            {
                CoreState.Services?.ShowError(
                    "No function definitions loaded for this filter. " +
                    "Cannot expand the list without a default fill pointer.");
                return;
            }

            uint newCount = ItemListPredicate.GetItemDataCount(rom);
            uint defAddr = ResolveDefaultExpansionFillPointer();

            _undoService.Begin("Item Usage Switch2 Expand");
            try
            {
                var undoData = _undoService.GetActiveUndoData();
                uint newAddr = undoData != null
                    ? _vm.ExpandList(newCount, defAddr, undoData)
                    : U.NOT_FOUND;
                if (newAddr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                LoadListForFilter(_vm.FilterIndex);
                CoreState.Services?.ShowInfo("Switch2 array expanded.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ItemUsagePointerViewerView.SwitchListExpands_Click: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Resolve the default fill pointer the WinForms expansion path uses.
        /// Mirrors WF `U.FindComboSelectHexFromValueWhereName(L_0_COMBO, "-")`
        /// — find the entry whose name is "-" (the canonical "no-op" entry)
        /// and return its hex key. Falls back to the first entry's hex key
        /// when no "-" exists. Returns 0 when the combo is empty.
        /// </summary>
        uint ResolveDefaultExpansionFillPointer()
        {
            if (_currentFunctionLines.Count == 0) return 0;
            foreach (var line in _currentFunctionLines)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string nameSide = line.Substring(eq + 1).Trim();
                // WF: U.FindComboSelectHexFromValueWhereName matches when the
                // name (right side, before any comma) is exactly "-".
                int comma = nameSide.IndexOf(',');
                string first = (comma >= 0 ? nameSide.Substring(0, comma) : nameSide).Trim();
                if (first == "-")
                {
                    return U.atoh(line.Substring(0, eq).Trim());
                }
            }
            // Fallback — first entry's hex key.
            string firstLine = _currentFunctionLines[0];
            int firstEq = firstLine.IndexOf('=');
            string firstHex = firstEq >= 0 ? firstLine.Substring(0, firstEq).Trim() : firstLine.Trim();
            return U.atoh(firstHex);
        }

        void PromotionItemLink_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint itemId = _vm.SelectedItemId;
                WindowManager.Instance.Navigate<ItemPromotionViewerView>(itemId);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.PromotionItemLink_Click: {0}", ex.Message);
            }
        }

        void StatBoosterItemLink_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint itemId = _vm.SelectedItemId;
                WindowManager.Instance.Navigate<ItemStatBonusesViewerView>(itemId);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.StatBoosterItemLink_Click: {0}", ex.Message);
            }
        }

        void IerPatch_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                WindowManager.Instance.Open<PatchManagerView>();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemUsagePointerViewerView.IerPatch_Click: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
