using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleAnimeView : Window, IEditorView
    {
        readonly ImageBattleAnimeViewModel _vm = new();

        public string ViewTitle => "Battle Animation Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBattleAnimeView()
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
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimationNumberBox.Value = _vm.AnimationNumber;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Special = (uint)(SpecialBox.Value ?? 0);
            _vm.AnimationNumber = (uint)(AnimationNumberBox.Value ?? 0);
            _vm.Write();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
