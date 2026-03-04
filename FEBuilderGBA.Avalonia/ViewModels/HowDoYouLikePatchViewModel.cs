namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HowDoYouLikePatchViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _patchInfo = string.Empty;
        bool _userApplied;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string PatchInfo { get => _patchInfo; set => SetField(ref _patchInfo, value); }
        public bool UserApplied { get => _userApplied; set => SetField(ref _userApplied, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
