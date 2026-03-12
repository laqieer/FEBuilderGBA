using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteSwapView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteSwapViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Swap";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteSwapView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Swap_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Palette Swap");
            try
            {
                // Read source and destination palette slot indices from UI
                int srcSlot = (int)(SourceInput.Value ?? 0);
                int dstSlot = (int)(DestInput.Value ?? 0);

                if (srcSlot == dstSlot)
                {
                    _vm.StatusMessage = "Source and destination slots are the same. No swap needed.";
                    _undoService.Rollback();
                    return;
                }

                // Store the selected slots in the VM for the caller to use
                _vm.SourceSlot = srcSlot;
                _vm.DestinationSlot = dstSlot;
                _vm.DialogResult = "Swap";

                _undoService.Commit();
                _vm.MarkClean();
                _vm.StatusMessage = $"Swap requested: slot {srcSlot} <-> slot {dstSlot}.";
                Close(true);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("PaletteSwapView.Swap", ex.ToString());
                _vm.StatusMessage = $"Swap failed: {ex.Message}";
            }
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
