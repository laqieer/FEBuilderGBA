using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapEventPointerView : Window, IEditorView, IDataVerifiableView
    {
        readonly WorldMapEventPointerViewModel _vm = new();

        public string ViewTitle => "World Map Event";
        public bool IsLoaded => _vm.CanWrite;

        public WorldMapEventPointerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadWorldMapEventList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadWorldMapEvent(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EventPtrBox.Value = _vm.EventPointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.EventPointer = (uint)(EventPtrBox.Value ?? 0);
            _vm.WriteWorldMapEvent();
            CoreState.Services?.ShowInfo("World map event pointer written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
