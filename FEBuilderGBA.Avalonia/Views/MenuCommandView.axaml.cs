using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuCommandView : Window, IEditorView, IDataVerifiableView
    {
        readonly MenuCommandViewModel _vm = new();

        public string ViewTitle => "Menu Command";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MenuCommandView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMenuCommandList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMenuCommand(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            P0Box.Value = _vm.P0;
            W4Box.Value = _vm.W4;
            W6Box.Value = _vm.W6;
            D8Box.Value = _vm.D8;
            P12Box.Value = _vm.P12;
            P16Box.Value = _vm.P16;
            P20Box.Value = _vm.P20;
            P24Box.Value = _vm.P24;
            P28Box.Value = _vm.P28;
            P32Box.Value = _vm.P32;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.P0 = (uint)(P0Box.Value ?? 0);
            _vm.W4 = (uint)(W4Box.Value ?? 0);
            _vm.W6 = (uint)(W6Box.Value ?? 0);
            _vm.D8 = (uint)(D8Box.Value ?? 0);
            _vm.P12 = (uint)(P12Box.Value ?? 0);
            _vm.P16 = (uint)(P16Box.Value ?? 0);
            _vm.P20 = (uint)(P20Box.Value ?? 0);
            _vm.P24 = (uint)(P24Box.Value ?? 0);
            _vm.P28 = (uint)(P28Box.Value ?? 0);
            _vm.P32 = (uint)(P32Box.Value ?? 0);
            _vm.WriteMenuCommand();
            CoreState.Services?.ShowInfo("Menu command data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
