using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIPerformStaffView : Window, IEditorView
    {
        readonly AIPerformStaffViewModel _vm = new();

        public string ViewTitle => "AI Staff Performance";
        public bool IsLoaded => _vm.IsLoaded;

        public AIPerformStaffView()
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
                Log.Error("AIPerformStaffView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIPerformStaffView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            ItemBox.Value = _vm.Item;
            Unused2Box.Value = _vm.Unused2;
            AsmPointerBox.Value = _vm.AsmPointer;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Item = (uint)(ItemBox.Value ?? 0);
                _vm.Unused2 = (uint)(Unused2Box.Value ?? 0);
                _vm.AsmPointer = (uint)(AsmPointerBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AIPerformStaffView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
