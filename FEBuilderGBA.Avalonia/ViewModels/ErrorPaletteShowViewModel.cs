namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorPaletteShowViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _paletteInfo = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string PaletteInfo { get => _paletteInfo; set => SetField(ref _paletteInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
