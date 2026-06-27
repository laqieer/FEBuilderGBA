using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia Item Shop Editor view — 3-region parity with WinForms ItemShopForm (#369).
    /// Left: ShopList (all shops in the ROM). Middle: SlotList (items in selected shop).
    /// Right: per-slot editor + slot management (Write, Append Slot, Remove Last Slot, Reload).
    /// </summary>
    public partial class ItemShopViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemShopViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        List<AddrResult> _currentShopList = new();
        List<AddrResult> _currentSlotList = new();

        public string ViewTitle => "Item Shop";
        public bool IsLoaded => _vm.CanWrite;

        public ItemShopViewerView()
        {
            InitializeComponent();
            ShopList.SelectedAddressChanged += OnShopSelected;
            SlotList.SelectedAddressChanged += OnSlotSelected;
            Opened += (_, _) => LoadShopList();
        }

        // ===================================================================
        // List loading
        // ===================================================================

        void LoadShopList()
        {
            try
            {
                // Clear the slot list FIRST so the SetItems->SelectFirst chain on
                // the shop list (which fires OnShopSelected synchronously) can
                // populate the slot list without it being overwritten afterwards.
                _currentSlotList = new List<AddrResult>();
                SlotList.SetItems(_currentSlotList);

                _currentShopList = _vm.LoadShopList();
                // SetItems() selects the first item internally, which fires
                // OnShopSelected and populates the slot list.
                ShopList.SetItems(_currentShopList);
                StatusLabel.Text = $"{_currentShopList.Count} shop(s) found.";
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemShopViewerView.LoadShopList: {0}", ex.Message);
                StatusLabel.Text = $"Failed to load shop list: {ex.Message}";
            }
        }

        /// <summary>
        /// Reload the shop list (after a relocation that changes shop addresses),
        /// then reselect the shop at <paramref name="shopAddrToSelect"/> and the
        /// slot at the given slot index inside that shop. Preserves the user's
        /// editing context across the relocate.
        /// </summary>
        void ReloadShopListAndSelect(uint shopAddrToSelect, int slotIndexToSelect)
        {
            try
            {
                _currentSlotList = new List<AddrResult>();
                SlotList.SetItems(_currentSlotList);

                _currentShopList = _vm.LoadShopList();
                // SetItems() triggers SelectFirst() -> OnShopSelected on the first
                // shop, but that's transient — SelectAddress() below moves the
                // selection (and re-fires OnShopSelected for the right shop).
                ShopList.SetItems(_currentShopList);
                ShopList.SelectAddress(shopAddrToSelect);

                // After OnShopSelected populates _currentSlotList, jump to the
                // appended slot.
                if (slotIndexToSelect >= 0 && slotIndexToSelect < _currentSlotList.Count)
                {
                    var slot = _currentSlotList[slotIndexToSelect];
                    SlotList.SelectAddress(slot.addr);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemShopViewerView.ReloadShopListAndSelect: {0}", ex.Message);
                StatusLabel.Text = $"Reload failed after relocation: {ex.Message}";
            }
        }

        void OnShopSelected(uint shopAddr)
        {
            try
            {
                // Find the shop entry so we can pass its name + pointer slot to the VM.
                AddrResult? entry = FindShopByAddr(shopAddr);
                if (entry == null) return;

                _vm.IsLoading = true;
                _currentSlotList = _vm.LoadShopItems(shopAddr, entry.tag, entry.name);
                SlotList.SetItemsWithIcons(_currentSlotList,
                    i => ListIconLoaders.ItemIconLoader(_currentSlotList, i));
                ShopAddrLabel.Text = $"0x{shopAddr:X08}";
                ShopNameLabel.Text = _vm.CurrentShopName;

                // If the selected shop has zero slots, OnSlotSelected will never
                // fire (the SlotList is empty), so the VM keeps the previous
                // shop's CurrentAddr / CanWrite and a Write click could
                // accidentally edit a slot from a different shop. Explicitly
                // clear the editor state and disable Write until a slot is
                // selected. (Copilot bot review on PR #465.)
                if (_currentSlotList.Count == 0)
                {
                    _vm.CurrentAddr = 0;
                    _vm.ItemId = 0;
                    _vm.Quantity = 0;
                    _vm.CanWrite = false;
                    AddrLabel.Text = "(empty shop — append a slot first)";
                    ItemIdBox.Value = 0;
                    QuantityBox.Value = 0;
                    ItemIdBox.NameText = "";
                }

                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemShopViewerView.OnShopSelected: {0}", ex.Message);
            }
        }

        AddrResult? FindShopByAddr(uint shopAddr)
        {
            for (int i = 0; i < _currentShopList.Count; i++)
            {
                if (_currentShopList[i].addr == shopAddr)
                    return _currentShopList[i];
            }
            return null;
        }

        void OnSlotSelected(uint slotAddr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemShop(slotAddr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemShopViewerView.OnSlotSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            // NameResolver returns a fallback on failure (Copilot review #638).
            ItemIdBox.NameText = NameResolver.GetItemName(_vm.ItemId);
            QuantityBox.Value = _vm.Quantity;
        }

        // ===================================================================
        // Slot management click handlers
        // ===================================================================

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // #1347 Slice 5a: in decomp mode, route to the owning decomp source list when
            // the shop's ROM address resolves to a manifest u16-list owner; otherwise keep
            // the #1149 ROM-only guard (no clobber). Apply the SAME explicit precondition
            // guard first so an invalid action does not become a misleading source-route.
            if (CoreState.IsDecompMode)
            {
                if (!_vm.CanWrite || _vm.CurrentAddr == 0)
                {
                    StatusLabel.Text = R._("Select a slot first (or use Append Slot to add one).");
                    return;
                }
                _vm.ItemId = ItemIdBox.Value;
                _vm.Quantity = (uint)(QuantityBox.Value ?? 0);
                TryRouteShopSaveToSource(_vm.BuildVectorForWrite(), R._("Select a slot first (or use Append Slot to add one)."));
                return;
            }

            // Guard: refuse to write when no slot is currently selected (e.g.
            // user clicked Write on an empty shop after the editor was cleared
            // by OnShopSelected).
            if (!_vm.CanWrite || _vm.CurrentAddr == 0)
            {
                StatusLabel.Text = "Select a slot first (or use Append Slot to add one).";
                return;
            }
            _vm.ItemId = ItemIdBox.Value;
            _vm.Quantity = (uint)(QuantityBox.Value ?? 0);

            _undoService.Begin("Edit Item Shop Slot");
            try
            {
                _vm.WriteItemShop();
                _undoService.Commit();
                _vm.MarkClean();
                StatusLabel.Text = "Slot saved.";
                // Refresh the slot list so the name/icon update.
                ReloadSlotList();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
                StatusLabel.Text = $"Write failed: {ex.Message}";
            }
        }

        void AppendSlot_Click(object? sender, RoutedEventArgs e)
        {
            // #1347 Slice 5a: in decomp mode, route to source when owned; else #1149 guard.
            if (CoreState.IsDecompMode)
            {
                if (_vm.CurrentShopAddr == 0)
                {
                    StatusLabel.Text = R._("Select a shop first.");
                    return;
                }
                TryRouteShopSaveToSource(_vm.BuildVectorForAppend(), R._("Select a shop first."));
                return;
            }

            if (_vm.CurrentShopAddr == 0)
            {
                StatusLabel.Text = "Select a shop first.";
                return;
            }

            _undoService.Begin("Append Item Shop Slot");
            try
            {
                var inPlace = _vm.TryAppendSlotInPlace(out _);
                if (inPlace == ItemShopViewerViewModel.AppendOutcome.AppendedInPlace)
                {
                    _undoService.Commit();
                    StatusLabel.Text = "Slot appended in place.";
                    ReloadSlotList();
                    return;
                }

                // Need relocation — confirm with user.
                bool relocate = CoreState.Services.ShowYesNo(
                    "Item Shop — Append: No free slack after this shop's item list. " +
                    "Relocate the list to ROM free space (this consumes free space)?");
                if (!relocate)
                {
                    _undoService.Rollback();
                    StatusLabel.Text = "Append cancelled (no slack, no relocation).";
                    return;
                }

                // Capture the slot count BEFORE the relocate so we know which
                // slot is the newly appended one (it's always at index oldCount).
                int oldSlotCount = _currentSlotList.Count;
                var reloc = _vm.AppendSlotWithRelocation(out uint newShopAddr);
                if (reloc == ItemShopViewerViewModel.AppendOutcome.Relocated)
                {
                    _undoService.Commit();

                    // Reload shop list — but DO NOT let SetItems' SelectFirst()
                    // jump us away from the relocated shop. Per the accepted plan
                    // (WU2) and Copilot bot follow-up review: after relocation we
                    // must reselect the new shop address, then the appended slot
                    // (at index oldSlotCount in the refreshed slot list).
                    ReloadShopListAndSelect(newShopAddr, oldSlotCount);
                    StatusLabel.Text = $"Shop relocated to 0x{newShopAddr:X08}; appended slot selected.";
                    return;
                }

                _undoService.Rollback();
                StatusLabel.Text = "Could not find ROM free space — append failed.";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("AppendSlot failed: {0}", ex.Message);
                StatusLabel.Text = $"Append failed: {ex.Message}";
            }
        }

        void RemoveLastSlot_Click(object? sender, RoutedEventArgs e)
        {
            // #1347 Slice 5a: in decomp mode, route to source when owned; else #1149 guard.
            if (CoreState.IsDecompMode)
            {
                if (_vm.CurrentShopAddr == 0)
                {
                    StatusLabel.Text = R._("Select a shop first.");
                    return;
                }
                // BuildVectorForRemoveLast returns null when the list is already empty.
                ushort[] desired = _vm.BuildVectorForRemoveLast();
                if (desired == null)
                {
                    StatusLabel.Text = R._("Nothing to remove (shop is empty).");
                    return;
                }
                TryRouteShopSaveToSource(desired, R._("Select a shop first."));
                return;
            }

            if (_vm.CurrentShopAddr == 0)
            {
                StatusLabel.Text = "Select a shop first.";
                return;
            }
            _undoService.Begin("Remove Last Item Shop Slot");
            try
            {
                bool ok = _vm.RemoveLastSlot();
                if (ok)
                {
                    _undoService.Commit();
                    StatusLabel.Text = "Last slot removed.";
                    ReloadSlotList();
                }
                else
                {
                    _undoService.Rollback();
                    StatusLabel.Text = "Nothing to remove (shop is empty).";
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("RemoveLastSlot failed: {0}", ex.Message);
                StatusLabel.Text = $"Remove failed: {ex.Message}";
            }
        }

        // ===================================================================
        // Decomp source-routing (#1347 Slice 5a)
        // ===================================================================

        /// <summary>
        /// Decomp-mode shop-save source-routing (#1347 Slice 5a). Attempts to write the
        /// edit to the owning decomp source list; on a non-Routed result keeps the #1149
        /// ROM-only guard (NO ROM write, NO clobber). Never touches the undo service / ROM
        /// — the source rewrite is a disk operation outside the preview ROM.
        /// </summary>
        /// <param name="desired">The desired item vector; null means a precondition failed.</param>
        /// <param name="nullPreconditionMessage">Status to show when <paramref name="desired"/> is null.</param>
        void TryRouteShopSaveToSource(ushort[] desired, string nullPreconditionMessage)
        {
            if (desired == null)
            {
                StatusLabel.Text = nullPreconditionMessage;
                return;
            }

            DecompShopRouteResult r;
            try
            {
                r = _vm.TryRouteCurrentShopToSource(desired);
            }
            catch (Exception ex)
            {
                // Defensive: the helper is itself never-throwing, but keep the guard.
                Log.Error("Shop source routing failed: " + ex.Message);
                r = null;
            }

            if (r != null && r.Routed)
            {
                // Re-baseline the dirty flag exactly like the ROM-save success path
                // (Write_Click) so the UI does not show a misleading "unsaved changes"
                // after a successful source route. Applies to all three routed handlers
                // (Write / Append / RemoveLast) since they share this helper.
                _vm.MarkClean();
                string ok = R._("Wrote shop list to source. Rebuild to refresh the preview.");
                StatusLabel.Text = ok + " " + r.SourceFile;
                CoreState.Services?.ShowInfo(ok + " " + r.SourceFile);
                return;
            }

            // Not routed / error: keep the #1149 ROM-only guard message and append the
            // carried reason so the user understands WHY it stayed ROM-only.
            string reason = (r == null || string.IsNullOrEmpty(r.Message)) ? "" : " (" + r.Message + ")";
            string guard = R._("Item shop data is ROM-only in decomp mode. Edit the source/event scripts and rebuild.");
            StatusLabel.Text = guard;
            CoreState.Services?.ShowInfo(guard + reason);
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            LoadShopList();
        }

        void ReloadSlotList()
        {
            if (_vm.CurrentShopAddr == 0) return;
            _currentSlotList = _vm.LoadShopItems(_vm.CurrentShopAddr,
                _vm.CurrentShopPointerAddr, _vm.CurrentShopName);
            SlotList.SetItemsWithIcons(_currentSlotList,
                i => ListIconLoaders.ItemIconLoader(_currentSlotList, i));
        }

        // ===================================================================
        // IdFieldControl handlers (#360 final)
        // ===================================================================

        static uint ItemAddrFor(uint itemId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return 0;
            uint baseAddr = rom.p32(itemPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + itemId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void ItemId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ItemAddrFor(ItemIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ItemFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("ItemShopViewerView.ItemId_Jump failed: {0}", ex.Message); }
        }

        async void ItemId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ItemAddrFor(ItemIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ItemFE6View>(addr, this);
                else
                    result = await WindowManager.Instance.PickFromEditor<ItemEditorView>(addr, this);
                if (result != null) ItemIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("ItemShopViewerView.ItemId_Pick failed: {0}", ex.Message); }
        }

        void ItemId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // NameResolver returns a fallback on failure (Copilot review #638).
            ItemIdBox.NameText = NameResolver.GetItemName(e.NewValue);
        }

        // ===================================================================
        // IEditorView
        // ===================================================================

        public void NavigateTo(uint address)
        {
            // Try to interpret `address` as a shop address first; otherwise treat
            // it as a slot address inside the currently selected shop.
            for (int i = 0; i < _currentShopList.Count; i++)
            {
                if (_currentShopList[i].addr == address)
                {
                    ShopList.SelectAddress(address);
                    return;
                }
            }
            SlotList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            ShopList.SelectFirst();
            SlotList.SelectFirst();
        }
    }
}
