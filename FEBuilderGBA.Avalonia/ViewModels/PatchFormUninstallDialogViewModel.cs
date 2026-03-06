namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PatchFormUninstallDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _patchName = string.Empty;
        string _originalFilename = "";
        bool _userConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string PatchName { get => _patchName; set => SetField(ref _patchName, value); }
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }
        public bool UserConfirmed { get => _userConfirmed; set => SetField(ref _userConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
