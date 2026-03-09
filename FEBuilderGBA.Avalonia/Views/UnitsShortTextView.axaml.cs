using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitsShortTextView : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitsShortTextViewModel _vm = new();

        public string ViewTitle => "Units Short Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitsShortTextView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address)
        {
            _vm.LoadEntry(address);
            UpdateUI();
        }

        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TextIdBox.Value = _vm.TextId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("Units short text data written.");
        }
    }
}
