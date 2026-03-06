namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolBGMMuteDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        bool _isMuted;
        string _trackInfoText = "";
        string _toggleButtonText = "Toggle Mute";
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool IsMuted { get => _isMuted; set => SetField(ref _isMuted, value); }
        public string TrackInfoText { get => _trackInfoText; set => SetField(ref _trackInfoText, value); }
        public string ToggleButtonText { get => _toggleButtonText; set => SetField(ref _toggleButtonText, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            TrackInfoText = "Select a track action:";
            IsLoaded = true;
        }

        public void Init(int trackNumber, bool isMute, string instName)
        {
            IsMuted = isMute;
            if (isMute)
            {
                TrackInfoText = $"Unmute track ({trackNumber})?\n{instName}";
                ToggleButtonText = "Unmute this track";
            }
            else
            {
                TrackInfoText = $"Mute track ({trackNumber})?\n{instName}";
                ToggleButtonText = "Mute this track";
            }
        }
    }
}
