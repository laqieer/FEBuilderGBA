using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteClipboardViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Palette Clipboard Manager stores and retrieves palette data.\nCopy palettes between different graphics entries or save them for later use.";
        string _paletteText = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Palette data as a text string (FE Recolor-compatible format).</summary>
        public string PaletteText { get => _paletteText; set => SetField(ref _paletteText, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
