namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text reference add dialog ViewModel.</summary>
    public class TextRefAddDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _refText = "";
        int _refId;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string RefText { get => _refText; set => SetField(ref _refText, value); }
        public int RefId { get => _refId; set => SetField(ref _refId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
