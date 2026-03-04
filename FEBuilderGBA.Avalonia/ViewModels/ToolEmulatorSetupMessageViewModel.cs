namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolEmulatorSetupMessageViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _useInitWizardResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string UseInitWizardResult { get => _useInitWizardResult; set => SetField(ref _useInitWizardResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
