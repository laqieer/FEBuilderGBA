using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIASMCoordinateView : Window, IEditorView
    {
        readonly AIASMCoordinateViewModel _vm = new();

        public string ViewTitle => "AI Coordinate Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public AIASMCoordinateView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
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
                Log.Error("AIASMCoordinateView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIASMCoordinateView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            XBox.Value = _vm.X;
            YBox.Value = _vm.Y;
            Unused2Box.Value = _vm.Unused2;
            Unused3Box.Value = _vm.Unused3;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.X = (uint)(XBox.Value ?? 0);
                _vm.Y = (uint)(YBox.Value ?? 0);
                _vm.Unused2 = (uint)(Unused2Box.Value ?? 0);
                _vm.Unused3 = (uint)(Unused3Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AIASMCoordinateView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
