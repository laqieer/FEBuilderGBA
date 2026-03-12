using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PaletteClipboardView : Window, IEditorView, IDataVerifiableView
    {
        readonly PaletteClipboardViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Clipboard";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public PaletteClipboardView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Copy_Click(object? sender, RoutedEventArgs e)
        {
            _vm.StatusMessage = "Palette copied to clipboard.";
            ClipboardStatusLabel.Text = "[Palette data stored]";
        }

        async void Paste_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("Palette Paste");
            try
            {
                // Read palette text from system clipboard
                string? clipText = null;
                if (TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard })
                {
                    clipText = await clipboard.GetTextAsync();
                }
                if (string.IsNullOrWhiteSpace(clipText))
                {
                    _vm.StatusMessage = "Clipboard is empty or contains no text.";
                    _undoService.Rollback();
                    return;
                }

                // Strip whitespace and common separators
                string hex = clipText.Replace(" ", "").Replace(",", "").Replace("\t", "")
                    .Replace("\r", "").Replace("\n", "").Trim();

                // Validate: expect exactly 64 hex chars (16 colors * 4 hex digits each)
                if (hex.Length != 64)
                {
                    _vm.StatusMessage = $"Expected 64 hex characters (16 colors), got {hex.Length}.";
                    _undoService.Rollback();
                    return;
                }

                // Parse hex string into 16 GBA RGB555 color values to validate
                byte[] paletteBytes = new byte[32]; // 16 colors * 2 bytes each
                for (int i = 0; i < 16; i++)
                {
                    string colorHex = hex.Substring(i * 4, 4);
                    if (!uint.TryParse(colorHex, System.Globalization.NumberStyles.HexNumber, null, out uint colorVal))
                    {
                        _vm.StatusMessage = $"Invalid hex at color {i}: '{colorHex}'.";
                        _undoService.Rollback();
                        return;
                    }
                    // FE Recolor format uses big-endian hex; convert to little-endian GBA
                    uint gbaColor = ((colorVal >> 8) & 0xFF) | ((colorVal & 0xFF) << 8);
                    U.write_u16(paletteBytes, (uint)(i * 2), gbaColor);
                }

                _vm.PaletteText = hex;
                _vm.DialogResult = "Paste";
                ClipboardStatusLabel.Text = $"[Parsed 16 colors from clipboard]";
                _undoService.Commit();
                _vm.MarkClean();
                _vm.StatusMessage = "Palette pasted.";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("PaletteClipboardView.Paste", ex.ToString());
                _vm.StatusMessage = $"Paste failed: {ex.Message}";
            }
        }

        void Clear_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PaletteText = string.Empty;
            _vm.StatusMessage = "Clipboard cleared.";
            ClipboardStatusLabel.Text = "[No palette data in clipboard]";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
