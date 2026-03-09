namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text reference add dialog ViewModel.</summary>
    public class TextRefAddDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _targetText = "";
        string _comment = "";
        int _refId;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The target text to add a reference for (read-only display).</summary>
        public string TargetText { get => _targetText; set => SetField(ref _targetText, value); }
        /// <summary>User comment for the text reference.</summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value); }
        /// <summary>Text ID being referenced.</summary>
        public int RefId { get => _refId; set => SetField(ref _refId, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
