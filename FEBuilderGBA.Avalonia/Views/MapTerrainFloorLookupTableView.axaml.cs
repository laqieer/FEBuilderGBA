using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainFloorLookupTableView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainFloorLookupTableViewModel _vm = new();

        public string ViewTitle => "Terrain Floor Lookup Table";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTerrainFloorLookupTableView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
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
                Log.Error("MapTerrainFloorLookupTableView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TerrainBattleFloorBox.Value = _vm.TerrainBattleFloor;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _vm.TerrainBattleFloor = (uint)(TerrainBattleFloorBox.Value ?? 0);
            _vm.Write();
            CoreState.Services?.ShowInfo("Terrain Floor lookup data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
