using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMDumpAllView : Window, IEditorView, IDataVerifiableView
    {
        readonly DisASMDumpAllViewModel _vm = new();

        public string ViewTitle => "Disassembly Dump All";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public DisASMDumpAllView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Run_Click(object? sender, RoutedEventArgs e)
        {
            if (IDAOption.IsChecked == true)
                _vm.SelectedAction = 1;
            else if (NoCashOption.IsChecked == true)
                _vm.SelectedAction = 2;
            else
                _vm.SelectedAction = 0;

            _vm.Output = $"[Disassembly dump not yet connected to backend — selected format: {_vm.SelectedAction}]";
            OutputBox.Text = _vm.Output;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
