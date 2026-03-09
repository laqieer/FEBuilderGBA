namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolClickWriteFloatControlPanelButtonViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "";
        string _messageText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Dialog result: "update" (overwrite existing), "new" (insert new).
        /// Maps to WinForms DialogResult.OK / Yes.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// The question message displayed in the dialog.
        /// WinForms: Message Label - "Which button would you click?"
        /// </summary>
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        public void Initialize()
        {
            MessageText = "Which button would you click?";
            IsLoaded = true;
        }
    }
}
