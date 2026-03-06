using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolCopyToView : Window, IEditorView
    {
        readonly PointerToolCopyToViewModel _vm = new();
        public string ViewTitle => "Pointer Tool - Copy To";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolCopyToView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void CopyPointer_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Pointer";
            Close("Pointer");
        }

        void CopyClipboard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Clipboard";
            Close("Clipboard");
        }

        void CopyLittleEndian_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "LittleEndian";
            Close("LittleEndian");
        }

        void HexButton_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Hex";
            Close("Hex");
        }

        void CopyNoDoll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "NoDoll";
            Close("NoDoll");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
