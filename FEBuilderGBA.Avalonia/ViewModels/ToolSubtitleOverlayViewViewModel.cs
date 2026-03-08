using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolSubtitleOverlayViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _subtitleText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SubtitleText { get => _subtitleText; set => SetField(ref _subtitleText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
