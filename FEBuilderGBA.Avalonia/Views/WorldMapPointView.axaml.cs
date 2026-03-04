using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPointView : Window, IEditorView, IDataVerifiableView
    {
        readonly WorldMapPointViewModel _vm = new();

        public string ViewTitle => "World Map Point";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapPointView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadWorldMapPointList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPointView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadWorldMapPoint(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPointView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            XLabel.Text = $"{_vm.X} (0x{_vm.X:X04})";
            YLabel.Text = $"{_vm.Y} (0x{_vm.Y:X04})";
            NameTextIdLabel.Text = $"0x{_vm.NameTextId:X04} ({_vm.NameTextId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
