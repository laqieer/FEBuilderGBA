using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainFloorLookupView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainFloorLookupTableViewModel _vm = new();

        public string ViewTitle => "Terrain Floor Lookup";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTerrainFloorLookupView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = new System.Collections.Generic.List<AddrResult>();
                items.Add(new AddrResult(0, "Terrain Floor Lookup", 0));
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTerrainFloorLookupView.LoadList failed: {0}", ex.Message);
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
                Log.Error("MapTerrainFloorLookupView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            TerrainBattleFloorBox.Value = _vm.TerrainBattleFloor;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _vm.TerrainBattleFloor = (uint)(TerrainBattleFloorBox.Value ?? 0);
            _vm.Write();
            CoreState.Services?.ShowInfo("Terrain Floor Lookup data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
