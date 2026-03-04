namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolProblemReportSearchSavViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _savFilename = "";
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SavFilename { get => _savFilename; set => SetField(ref _savFilename, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public string GetFilename() => SavFilename;
    }
}
