namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TextBadCharPopupViewModel : ViewModelBase
    {
        string _warningText = "";
        string _selectedAction = "";
        bool _isLoaded;
        string _dialogResult = "";

        /// <summary>Warning message about bad characters found in the text.</summary>
        public string WarningText { get => _warningText; set => SetField(ref _warningText, value); }
        /// <summary>User-selected action: "Remove", "Replace", or "" (cancel).</summary>
        public string SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Load(string warningText = "")
        {
            if (string.IsNullOrEmpty(warningText))
            {
                WarningText = "Bad characters were detected in the text.";
            }
            else
            {
                WarningText = warningText;
            }
            IsLoaded = true;
        }
    }
}
