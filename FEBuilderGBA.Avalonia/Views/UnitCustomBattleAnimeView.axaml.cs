using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitCustomBattleAnimeView : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitCustomBattleAnimeViewModel _vm = new();

        public string ViewTitle => "Custom Battle Animation";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitCustomBattleAnimeView()
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
                Log.Error("UnitCustomBattleAnimeView.LoadList failed: {0}", ex.Message);
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
                Log.Error("UnitCustomBattleAnimeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimeNumberBox.Value = _vm.AnimeNumber;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Special = (uint)(SpecialBox.Value ?? 0);
            _vm.AnimeNumber = (uint)(AnimeNumberBox.Value ?? 0);

            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("Custom battle animation data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
