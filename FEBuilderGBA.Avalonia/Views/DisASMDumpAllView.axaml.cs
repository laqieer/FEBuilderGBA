using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
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
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void Run_Click(object? sender, RoutedEventArgs e)
        {
            if (IDAOption.IsChecked == true)
                _vm.SelectedAction = 1;
            else if (NoCashOption.IsChecked == true)
                _vm.SelectedAction = 2;
            else
                _vm.SelectedAction = 0;

            if (CoreState.ROM == null)
            {
                _vm.Output = "Error: No ROM loaded.";
                OutputBox.Text = _vm.Output;
                return;
            }

            _vm.StatusMessage = "Disassembling... please wait.";
            _vm.Output = "Working...";
            OutputBox.Text = _vm.Output;

            int action = _vm.SelectedAction;
            List<string>? lines = null;
            string? error = null;

            await Task.Run(() =>
            {
                try
                {
                    var core = new DisassemblerCore();
                    lines = action switch
                    {
                        1 => core.ExportIDAMapLines(),
                        2 => core.ExportNoCashSymLines(),
                        _ => core.DisassembleToLines(),
                    };
                }
                catch (Exception ex)
                {
                    error = $"Error: {ex.Message}";
                }
            });

            if (error != null)
            {
                _vm.Output = error;
                _vm.StatusMessage = "Failed.";
            }
            else if (lines != null)
            {
                _vm.Output = string.Join(Environment.NewLine, lines);
                _vm.StatusMessage = $"Done. {lines.Count} lines generated.";
            }
            OutputBox.Text = _vm.Output;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
