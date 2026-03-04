namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolThreeMargeCloseAlertViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "cancel";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
