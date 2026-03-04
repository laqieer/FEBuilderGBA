namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorPaletteTransparentViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _transparencyInfo = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string TransparencyInfo { get => _transparencyInfo; set => SetField(ref _transparencyInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
