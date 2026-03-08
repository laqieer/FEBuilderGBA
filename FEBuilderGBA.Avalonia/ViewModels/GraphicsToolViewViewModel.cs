using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Tool browser. Select images from the categorized list.\nCategories: Portraits, Battle Animations, Map Sprites, Icons, CGs, Title Screen, etc.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
