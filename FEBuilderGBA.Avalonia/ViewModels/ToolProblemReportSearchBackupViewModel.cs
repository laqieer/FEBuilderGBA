namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchBackupViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _backupFilename = "";
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string BackupFilename { get => _backupFilename; set => SetField(ref _backupFilename, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public string GetFilename() => BackupFilename;
    }
}
