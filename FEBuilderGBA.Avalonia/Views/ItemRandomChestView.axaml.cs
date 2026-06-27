using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemRandomChestView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemRandomChestViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Random Chest Items";
        public bool IsLoaded => _vm.IsLoaded;

        public ItemRandomChestView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemRandomChestView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemRandomChestView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            // Push VM-resolved name; ValueChanged also refreshes on edit.
            try { ItemIdBox.NameText = NameResolver.GetItemName(_vm.ItemId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
            ProbabilityBox.Value = _vm.Probability;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ItemId = ItemIdBox.Value;
            _vm.Probability = (uint)(ProbabilityBox.Value ?? 0);

            _undoService.Begin("Edit Random Chest");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Random Chest data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        /// <summary>
        /// Compute the Item editor's ROM entry address for a given item id.
        /// Returns 0 when ROM is unavailable or the entry would fall outside
        /// ROM bounds. Mirrors the address math of the original
        /// OnItemIdLinkClick helper but factored for reuse by Jump and Pick.
        /// </summary>
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
            catch (Exception ex) { Log.ErrorF("ItemRandomChestView.ItemId_Jump failed: {0}", ex.Message); }
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
                if (result != null)
                {
                    ItemIdBox.Value = (uint)result.Index;
                    // NameText refresh happens via ValueChanged.
                }
            }
            catch (Exception ex) { Log.ErrorF("ItemRandomChestView.ItemId_Pick failed: {0}", ex.Message); }
        }

        void ItemId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ItemIdBox.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address)
        {
            _vm.SetBaseAddress(address);
            LoadList();
            EntryList.SelectFirst();
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
