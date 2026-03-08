using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolAnimationCreatorViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _animationName = string.Empty;
        string _frameCount = string.Empty;
        string _imageSource = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AnimationName { get => _animationName; set => SetField(ref _animationName, value); }
        public string FrameCount { get => _frameCount; set => SetField(ref _frameCount, value); }
        public string ImageSource { get => _imageSource; set => SetField(ref _imageSource, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
