using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitFE7View : Window, IEditorView
    {
        readonly EventUnitFE7ViewModel _vm = new();

        public string ViewTitle => "Event Unit (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventUnitFE7View()
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
                Log.Error("EventUnitFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventUnitFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            B8Box.Value = _vm.B8;
            B9Box.Value = _vm.B9;
            B10Box.Value = _vm.B10;
            B11Box.Value = _vm.B11;
            B12Box.Value = _vm.B12;
            B13Box.Value = _vm.B13;
            B14Box.Value = _vm.B14;
            B15Box.Value = _vm.B15;
        }

        void ReadFromUI()
        {
            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.B8 = (uint)(B8Box.Value ?? 0);
            _vm.B9 = (uint)(B9Box.Value ?? 0);
            _vm.B10 = (uint)(B10Box.Value ?? 0);
            _vm.B11 = (uint)(B11Box.Value ?? 0);
            _vm.B12 = (uint)(B12Box.Value ?? 0);
            _vm.B13 = (uint)(B13Box.Value ?? 0);
            _vm.B14 = (uint)(B14Box.Value ?? 0);
            _vm.B15 = (uint)(B15Box.Value ?? 0);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReadFromUI();
                _vm.WriteEntry();
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitFE7View.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
