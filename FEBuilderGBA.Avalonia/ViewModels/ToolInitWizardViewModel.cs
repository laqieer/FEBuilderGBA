namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolInitWizardViewModel : ViewModelBase
    {
        bool _isLoaded;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
