using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PackedMemorySlotView : Window, IEditorView
    {
        readonly PackedMemorySlotViewModel _vm = new();
        public string ViewTitle => "Packed Memory Slot";
        public bool IsLoaded => _vm.IsLoaded;

        public PackedMemorySlotView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Refresh_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
