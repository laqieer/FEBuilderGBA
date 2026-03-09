namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolSubtitleSettingDialogViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _translateFromRomFilename = "";
        string _translateToRomFilename = "";
        string _translateDataFilename = "";
        bool _showAlways;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Path to the source ROM for subtitle translation (FROM).
        /// WinForms: SimpleTranslateFromROMFilename TextBoxEx.
        /// </summary>
        public string TranslateFromRomFilename { get => _translateFromRomFilename; set => SetField(ref _translateFromRomFilename, value); }

        /// <summary>
        /// Path to the destination ROM for subtitle translation (TO).
        /// WinForms: SimpleTranslateToROMFilename TextBoxEx.
        /// </summary>
        public string TranslateToRomFilename { get => _translateToRomFilename; set => SetField(ref _translateToRomFilename, value); }

        /// <summary>
        /// Path to the translation hint/data file.
        /// WinForms: SimpleTranslateToTranslateDataFilename TextBoxEx.
        /// </summary>
        public string TranslateDataFilename { get => _translateDataFilename; set => SetField(ref _translateDataFilename, value); }

        /// <summary>
        /// Whether to show the subtitle window even when there are no subtitles.
        /// WinForms: ShowAlways CheckBox.
        /// </summary>
        public bool ShowAlways { get => _showAlways; set => SetField(ref _showAlways, value); }

        /// <summary>
        /// Dialog result: "show" (show subtitles), "hide" (hide subtitles), or empty.
        /// WinForms: ShowButton / HideButton clicks.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
