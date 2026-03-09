using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIASMRangeView : Window, IEditorView
    {
        readonly AIASMRangeViewModel _vm = new();

        public string ViewTitle => "AI Range Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public AIASMRangeView()
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
                Log.Error("AIASMRangeView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIASMRangeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            X1Box.Value = _vm.X1;
            Y1Box.Value = _vm.Y1;
            X2Box.Value = _vm.X2;
            Y2Box.Value = _vm.Y2;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.X1 = (uint)(X1Box.Value ?? 0);
                _vm.Y1 = (uint)(Y1Box.Value ?? 0);
                _vm.X2 = (uint)(X2Box.Value ?? 0);
                _vm.Y2 = (uint)(Y2Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AIASMRangeView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
