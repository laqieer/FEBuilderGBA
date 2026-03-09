namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PatchFormUninstallDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _patchName = string.Empty;
        string _originalFilename = "";
        bool _userConfirmed;
        string _dialogResult = "";
        string _warningText = "Please select the ROM from before this patch was installed for recovery.\n\n" +
            "This feature does not guarantee a reliable uninstallation.\n" +
            "It may fail, so please make a backup beforehand.\n" +
            "Also, while the patch code can be removed, associated data may not always be removable.\n" +
            "In that case, there may be a loss of a few hundred bytes.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Name of the patch being uninstalled.</summary>
        public string PatchName { get => _patchName; set => SetField(ref _patchName, value); }
        /// <summary>Path to the original (pre-patch) ROM file.</summary>
        public string OriginalFilename { get => _originalFilename; set => SetField(ref _originalFilename, value); }
        /// <summary>True if the user confirmed uninstallation.</summary>
        public bool UserConfirmed { get => _userConfirmed; set => SetField(ref _userConfirmed, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>Warning text explaining uninstall risks.</summary>
        public string WarningText { get => _warningText; set => SetField(ref _warningText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
