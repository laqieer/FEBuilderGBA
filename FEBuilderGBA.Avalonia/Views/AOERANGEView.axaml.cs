using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AOERANGEView : Window, IEditorView
    {
        readonly AOERANGEViewModel _vm = new();

        public string ViewTitle => "Area of Effect Range";
        public bool IsLoaded => _vm.IsLoaded;

        public AOERANGEView()
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
                Log.Error("AOERANGEView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AOERANGEView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            WidthBox.Value = _vm.Width;
            HeightBox.Value = _vm.Height;
            CenterXBox.Value = _vm.CenterX;
            CenterYBox.Value = _vm.CenterY;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Width = (uint)(WidthBox.Value ?? 0);
                _vm.Height = (uint)(HeightBox.Value ?? 0);
                _vm.CenterX = (uint)(CenterXBox.Value ?? 0);
                _vm.CenterY = (uint)(CenterYBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AOERANGEView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
