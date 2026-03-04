using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolAnimationCreatorViewViewModel : ViewModelBase, IDataVerifiable
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

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["AnimationName"] = AnimationName,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
