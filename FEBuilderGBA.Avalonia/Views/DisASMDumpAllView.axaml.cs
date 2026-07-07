using global::Avalonia;
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
    public partial class DisASMDumpAllView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly DisASMDumpAllViewModel _vm = new();

        public string ViewTitle => "Disassembly Dump All";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Disassembly Dump All", 695, 721, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
            if (RangeOption.IsChecked == true)
                _vm.SelectedAction = 3;
            else if (IDAOption.IsChecked == true)
                _vm.SelectedAction = 1;
            else if (NoCashOption.IsChecked == true)
                _vm.SelectedAction = 2;
            else
                _vm.SelectedAction = 0;

            _vm.StatusMessage = "Disassembling... please wait.";
            _vm.Output = "Working...";
            OutputBox.Text = _vm.Output;

            List<string>? lines = null;
            string? error = null;

            await Task.Run(() =>
            {
                (lines, error) = _vm.RunDisassembly();
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

        void Close_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
