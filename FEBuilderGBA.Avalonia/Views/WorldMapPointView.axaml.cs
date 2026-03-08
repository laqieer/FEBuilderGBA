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
        public bool IsLoaded => _vm.CanWrite;

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
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            W6Box.Value = _vm.W6;
            NameTextIdBox.Value = _vm.NameTextId;
            D12Box.Value = _vm.D12;
            D16Box.Value = _vm.D16;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.W6 = (uint)(W6Box.Value ?? 0);
            _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
            _vm.D12 = (uint)(D12Box.Value ?? 0);
            _vm.D16 = (uint)(D16Box.Value ?? 0);
            _vm.WriteWorldMapPoint();
            CoreState.Services?.ShowInfo("World map point data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
