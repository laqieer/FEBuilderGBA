using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveToFreeSpaceView : Window, IEditorView
    {
        readonly MoveToFreeSpaceViewViewModel _vm = new();
        public string ViewTitle => "Move to Free Space";
        public bool IsLoaded => _vm.IsLoaded;

        public MoveToFreeSpaceView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Move_Click(object? sender, RoutedEventArgs e) { }
        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
