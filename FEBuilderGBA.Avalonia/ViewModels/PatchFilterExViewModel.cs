namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PatchFilterExViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _filterText = string.Empty;
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string FilterText { get => _filterText; set => SetField(ref _filterText, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
