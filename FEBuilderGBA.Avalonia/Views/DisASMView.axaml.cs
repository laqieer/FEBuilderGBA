using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly DisASMViewModel _vm = new();

        public string ViewTitle => "Disassembler";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Disassembler", 900, 700, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public DisASMView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoaded = true;
        }

        async void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Disassembling...";
            OutputBox.Text = "Working...";

            List<string>? lines = null;
            string? error = null;

            await Task.Run(() =>
            {
                (lines, error) = _vm.RunDisassembly();
            });

            if (error != null)
            {
                OutputBox.Text = error;
                _vm.StatusMessage = "Failed.";
            }
            else if (lines != null)
            {
                OutputBox.Text = string.Join(Environment.NewLine, lines);
                _vm.StatusMessage = $"Done. {lines.Count} lines.";
            }
        }

        public void NavigateTo(uint address)
        {
            _vm.AddressInput = $"0x{address:X08}";
        }

        public void SelectFirstItem() { }
    }
}
