using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolSubtitleSettingDialogViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _fontName = "Arial";
        string _fontSize = "16";
        string _fontColor = "White";
        string _backgroundColor = "Black";
        bool _showBackground = true;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string FontName { get => _fontName; set => SetField(ref _fontName, value); }
        public string FontSize { get => _fontSize; set => SetField(ref _fontSize, value); }
        public string FontColor { get => _fontColor; set => SetField(ref _fontColor, value); }
        public string BackgroundColor { get => _backgroundColor; set => SetField(ref _backgroundColor, value); }
        public bool ShowBackground { get => _showBackground; set => SetField(ref _showBackground, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
