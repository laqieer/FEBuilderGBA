using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemWeaponEffectViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemWeaponEffectViewerViewModel _vm = new();

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
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemWeaponEffectViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemWeaponEffect(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemWeaponEffectViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
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
            _vm.ItemId = (uint)(ItemIdBox.Value ?? 0);
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
            _vm.WriteItemWeaponEffect();
            CoreState.Services.ShowInfo("Item Weapon Effect data written.");
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
