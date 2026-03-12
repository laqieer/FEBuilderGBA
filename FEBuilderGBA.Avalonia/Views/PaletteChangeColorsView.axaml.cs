using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteChangeColorsView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteChangeColorsViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Change Colors";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteChangeColorsView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Palette Change Colors");
            try
            {
                // Read RGB values from UI (0-31 range for GBA 5-bit color)
                int r = (int)(RedInput.Value ?? 0);
                int g = (int)(GreenInput.Value ?? 0);
                int b = (int)(BlueInput.Value ?? 0);

                // Clamp to 5-bit GBA range
                r = Math.Clamp(r, 0, 31);
                g = Math.Clamp(g, 0, 31);
                b = Math.Clamp(b, 0, 31);

                // Encode as GBA RGB555 (little-endian: R in bits 0-4, G in bits 5-9, B in bits 10-14)
                ushort gbaColor = (ushort)(r | (g << 5) | (b << 10));

                // Update VM with applied values
                _vm.NewColorR = r;
                _vm.NewColorG = g;
                _vm.NewColorB = b;
                _vm.OldColorInfo = $"Applied: R={r} G={g} B={b} (0x{gbaColor:X04})";
                _undoService.Commit();
                _vm.MarkClean();
                _vm.StatusMessage = "Color applied.";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("PaletteChangeColorsView.Apply", ex.ToString());
                _vm.StatusMessage = $"Apply failed: {ex.Message}";
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
