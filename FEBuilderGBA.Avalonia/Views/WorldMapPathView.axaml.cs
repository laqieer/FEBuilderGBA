using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPathView : Window, IEditorView
    {
        readonly WorldMapPathViewModel _vm = new();

        public string ViewTitle => "World Map Paths";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapPathView()
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
                Log.Error("WorldMapPathView.LoadList failed: {0}", ex.Message);
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
                Log.Error("WorldMapPathView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            P0Box.Text = string.Format("0x{0:X08}", _vm.P0);
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            P8Box.Text = string.Format("0x{0:X08}", _vm.P8);
        }

        void ReadFromUI()
        {
            _vm.P0 = U.atoh(P0Box.Text ?? "");
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.P8 = U.atoh(P8Box.Text ?? "");
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
                Log.Error("WorldMapPathView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
