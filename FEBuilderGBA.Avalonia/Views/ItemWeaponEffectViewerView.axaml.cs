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
    public partial class ItemWeaponEffectViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemWeaponEffectViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Item Weapon Effect";
        public bool IsLoaded => _vm.CanWrite;

        public ItemWeaponEffectViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemWeaponEffectList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemWeaponEffectViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemWeaponEffect(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemWeaponEffectViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            try { ItemIdBox.NameText = NameResolver.GetItemName(_vm.ItemId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
            AnimTypeBox.Value = _vm.AnimType;
            EffectIdBox.Value = _vm.EffectId;
            MapEffectBox.Text = $"0x{_vm.MapEffectPointer:X08}";
            DamageEffectBox.Value = _vm.DamageEffect;
            MotionBox.Value = _vm.Motion;
            HitColorBox.Value = _vm.HitColor;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown3Box.Value = _vm.Unknown3;
            Unknown6Box.Value = _vm.Unknown6;
            Unknown15Box.Value = _vm.Unknown15;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ItemId = ItemIdBox.Value;
            _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
            _vm.AnimType = (uint)(AnimTypeBox.Value ?? 0);
            _vm.Unknown3 = (uint)(Unknown3Box.Value ?? 0);
            _vm.EffectId = (uint)(EffectIdBox.Value ?? 0);
            _vm.Unknown6 = (uint)(Unknown6Box.Value ?? 0);
            _vm.MapEffectPointer = ParseHexText(MapEffectBox.Text);
            _vm.DamageEffect = (uint)(DamageEffectBox.Value ?? 0);
            _vm.Motion = (uint)(MotionBox.Value ?? 0);
            _vm.HitColor = (uint)(HitColorBox.Value ?? 0);
            _vm.Unknown15 = (uint)(Unknown15Box.Value ?? 0);

            _undoService.Begin("Edit Item Weapon Effect");
            try
            {
                _vm.WriteItemWeaponEffect();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Weapon Effect data written.");
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
            catch (Exception ex) { Log.ErrorF("ItemWeaponEffectViewerView.ItemId_Jump failed: {0}", ex.Message); }
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
            catch (Exception ex) { Log.ErrorF("ItemWeaponEffectViewerView.ItemId_Pick failed: {0}", ex.Message); }
        }

        void ItemId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ItemIdBox.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        private static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
    }
}
