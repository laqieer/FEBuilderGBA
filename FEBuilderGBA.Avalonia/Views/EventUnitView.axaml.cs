using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitView : Window, IEditorView
    {
        readonly EventUnitViewModel _vm = new();

        public string ViewTitle => "Event Unit Placement";
        public bool IsLoaded => _vm.IsLoaded;

        public EventUnitView()
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
                Log.Error("EventUnitView.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventUnitView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            W4Box.Value = _vm.W4;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            P8Box.Text = string.Format("0x{0:X08}", _vm.P8);
            B12Box.Value = _vm.B12;
            B13Box.Value = _vm.B13;
            B14Box.Value = _vm.B14;
            B15Box.Value = _vm.B15;
            B16Box.Value = _vm.B16;
            B17Box.Value = _vm.B17;
            B18Box.Value = _vm.B18;
            B19Box.Value = _vm.B19;
        }

        void ReadFromUI()
        {
            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.W4 = (uint)(W4Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.P8 = U.atoh(P8Box.Text ?? "");
            _vm.B12 = (uint)(B12Box.Value ?? 0);
            _vm.B13 = (uint)(B13Box.Value ?? 0);
            _vm.B14 = (uint)(B14Box.Value ?? 0);
            _vm.B15 = (uint)(B15Box.Value ?? 0);
            _vm.B16 = (uint)(B16Box.Value ?? 0);
            _vm.B17 = (uint)(B17Box.Value ?? 0);
            _vm.B18 = (uint)(B18Box.Value ?? 0);
            _vm.B19 = (uint)(B19Box.Value ?? 0);
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
                Log.Error("EventUnitView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
