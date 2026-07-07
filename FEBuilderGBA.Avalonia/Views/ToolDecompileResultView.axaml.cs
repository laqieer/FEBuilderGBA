using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolDecompileResultView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolDecompileResultViewViewModel _vm = new();
        public string ViewTitle => "Decompile Result";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Decompile Result", 802, 845, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolDecompileResultView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void CopyToClipboard_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
