using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerView : Window, IEditorView
    {
        readonly MapPointerViewModel _vm = new();

        public string ViewTitle => "Map Pointer";
        public bool IsLoaded => _vm.IsLoaded;

        public MapPointerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMapPointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMapPointer(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapPointerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            MapDataPointerLabel.Text = $"0x{_vm.MapDataPointer:X08}";
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
