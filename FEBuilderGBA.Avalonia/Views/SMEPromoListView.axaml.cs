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
        readonly UndoService _undoService = new();
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
            _undoService.Begin("Edit SME Promo");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("SME Promo data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SMEPromoListView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            _vm.InitializeWithAddress(address);
        }

        public void SelectFirstItem()
        {
            if (_vm.AddressList.Count > 0)
                _vm.SelectedIndex = 0;
        }
    }
}
