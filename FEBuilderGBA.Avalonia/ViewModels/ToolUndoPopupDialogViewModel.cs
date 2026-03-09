namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolUndoPopupDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "";
        string _infoText = "";
        string _versionName = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Dialog result: "TestPlay" (test in emulator), "RunUndo" (revert to this version), "Cancel".
        /// Maps to WinForms DialogResult.Retry / Yes / Cancel.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// The informational text shown in the read-only multiline textbox.
        /// WinForms: Info TextBox (629x117, readonly, multiline).
        /// </summary>
        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }

        /// <summary>
        /// The version name passed via Init().
        /// WinForms: ToolUndoPopupDialogForm.Init(version) sets Info.Text.
        /// </summary>
        public string VersionName { get => _versionName; set => SetField(ref _versionName, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Initialize the dialog with a specific undo version name.
        /// Mirrors WinForms Init(string version).
        /// </summary>
        public void Init(string version)
        {
            VersionName = version;
            InfoText = $"Revert to this version ({version})?";
        }
    }
}
