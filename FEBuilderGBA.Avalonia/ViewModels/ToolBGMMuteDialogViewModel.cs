namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolBGMMuteDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        bool _isMuted;
        bool _dialogConfirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool IsMuted { get => _isMuted; set => SetField(ref _isMuted, value); }
        public bool DialogConfirmed { get => _dialogConfirmed; set => SetField(ref _dialogConfirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
