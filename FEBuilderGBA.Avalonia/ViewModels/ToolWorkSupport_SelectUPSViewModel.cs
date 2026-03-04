namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupport_SelectUPSViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalFilename = "";
        string _upsFilename = "";
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }
        public string UpsFilename { get => _upsFilename; set => SetField(ref _upsFilename, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void OpenUPS(string upsFilename)
        {
            UpsFilename = upsFilename;
        }

        public string GetOriginalFilename() => OriginalFilename;
    }
}
