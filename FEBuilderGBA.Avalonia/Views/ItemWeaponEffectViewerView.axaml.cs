using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemWeaponEffectViewerView : Window, IEditorView
    {
        readonly ItemWeaponEffectViewerViewModel _vm = new();

        public string ViewTitle => "Item Weapon Effect";
        public bool IsLoaded => _vm.IsLoaded;

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
            ItemIdLabel.Text = $"0x{_vm.ItemId:X02}";
            AnimTypeLabel.Text = _vm.AnimType.ToString();
            EffectIdLabel.Text = $"0x{_vm.EffectId:X04}";
            MapEffectLabel.Text = $"0x{_vm.MapEffectPointer:X08}";
            DamageEffectLabel.Text = $"0x{_vm.DamageEffect:X02}";
            MotionLabel.Text = $"0x{_vm.Motion:X02}";
            HitColorLabel.Text = $"0x{_vm.HitColor:X02}";
            Unknown1Label.Text = $"0x{_vm.Unknown1:X02}";
            Unknown3Label.Text = $"0x{_vm.Unknown3:X02}";
            Unknown6Label.Text = $"0x{_vm.Unknown6:X04}";
            Unknown15Label.Text = $"0x{_vm.Unknown15:X02}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
