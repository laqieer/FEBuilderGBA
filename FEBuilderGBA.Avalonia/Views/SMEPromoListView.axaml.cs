using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SMEPromoListView : Window, IEditorView
    {
        readonly SMEPromoListViewModel _vm = new();
        public string ViewTitle => "SME Promo List";
        public bool IsLoaded => _vm.IsLoaded;

        public SMEPromoListView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Reload();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Write placeholder - would write B0/B1 to ROM
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
