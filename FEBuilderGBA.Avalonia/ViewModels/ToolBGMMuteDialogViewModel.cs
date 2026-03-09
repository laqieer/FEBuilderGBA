namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolBGMMuteDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        bool _isMuted;
        int _trackNumber;
        string _trackInfoText = "";
        string _toggleButtonText = "Toggle Mute";
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Whether the track is currently muted.
        /// </summary>
        public bool IsMuted { get => _isMuted; set => SetField(ref _isMuted, value); }

        /// <summary>
        /// The track number being toggled.
        /// </summary>
        public int TrackNumber { get => _trackNumber; set => SetField(ref _trackNumber, value); }

        /// <summary>
        /// Info text shown at the top of the dialog.
        /// WinForms: label1 - shows track number, instrument name, and mute question.
        /// </summary>
        public string TrackInfoText { get => _trackInfoText; set => SetField(ref _trackInfoText, value); }

        /// <summary>
        /// Text on the toggle button (changes based on mute state).
        /// WinForms: ToggleButton.Text - "Mute" or "Unmute".
        /// </summary>
        public string ToggleButtonText { get => _toggleButtonText; set => SetField(ref _toggleButtonText, value); }

        /// <summary>
        /// Dialog result: "toggle" (mute/unmute), "onlyplay" (solo this track), "playall" (play all tracks).
        /// Maps to WinForms DialogResult.Yes / Ignore / Retry.
        /// </summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            TrackInfoText = "Select a track action:";
            IsLoaded = true;
        }

        public void Init(int trackNumber, bool isMute, string instName)
        {
            TrackNumber = trackNumber;
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
