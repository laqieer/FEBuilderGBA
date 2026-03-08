using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapExitPointView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapExitPointViewModel _vm = new();

        public string ViewTitle => "Map Exit Point Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapExitPointView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMapExitPointList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapExitPointView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMapExitPoint(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapExitPointView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ExitPointerBox.Value = _vm.ExitPointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ExitPointer = (uint)(ExitPointerBox.Value ?? 0);
            _vm.WriteMapExitPoint();
            CoreState.Services?.ShowInfo("Map Exit Point data written.");
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
