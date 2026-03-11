using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMDumpAllArgGrepView : Window, IEditorView, IDataVerifiableView
    {
        readonly DisASMDumpAllArgGrepViewModel _vm = new();

        public string ViewTitle => "Disassembly Argument Grep";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public DisASMDumpAllArgGrepView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SearchPattern = GrepPatternInput.Text ?? string.Empty;
            _vm.Results = $"[Grep not yet connected to backend — pattern: {_vm.SearchPattern}]";
            ResultsBox.Text = _vm.Results;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
