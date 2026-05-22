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
    public partial class MonsterItemViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MonsterItemViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Monster Item";
        public bool IsLoaded => _vm.CanWrite;

        public MonsterItemViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterItemList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MonsterItemViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterItem(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MonsterItemViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            try { ItemIdBox.NameText = NameResolver.GetItemName(_vm.ItemId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
            DropRateBox.Value = _vm.DropRate;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown3Box.Value = _vm.Unknown3;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Monster Item");
            try
            {
                _vm.ItemId = ItemIdBox.Value;
                _vm.DropRate = (uint)(DropRateBox.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
                _vm.Unknown3 = (uint)(Unknown3Box.Value ?? 0);
                _vm.WriteMonsterItem();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Monster item data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MonsterItemViewerView.Write: {0}", ex.Message); }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        /// <summary>
        /// Compute the Item editor's ROM entry address for a given item id.
        /// Returns 0 when ROM is unavailable or the entry would fall outside
        /// ROM bounds.
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
            catch (Exception ex) { Log.Error("MonsterItemViewerView.ItemId_Jump failed: {0}", ex.Message); }
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
                }
            }
            catch (Exception ex) { Log.Error("MonsterItemViewerView.ItemId_Pick failed: {0}", ex.Message); }
        }

        void ItemId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ItemIdBox.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
