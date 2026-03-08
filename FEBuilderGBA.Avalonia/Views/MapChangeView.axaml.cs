using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapChangeView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapChangeViewModel _vm = new();

        public string ViewTitle => "Map Change Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MapChangeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMapChangeList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapChangeView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMapChange(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapChangeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ChangePointerBox.Value = _vm.ChangePointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ChangePointer = (uint)(ChangePointerBox.Value ?? 0);
            _vm.WriteMapChange();
            CoreState.Services?.ShowInfo("Map Change data written.");
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
