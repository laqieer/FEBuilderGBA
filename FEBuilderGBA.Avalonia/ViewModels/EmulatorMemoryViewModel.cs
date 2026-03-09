namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EmulatorMemoryViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _noticeText = "Emulator memory reading requires Windows P/Invoke and is not available in the cross-platform Avalonia version.\n\nThis feature uses Windows-specific APIs to read the memory of a running GBA emulator process for live debugging. Please use the Windows (WinForms) version of FEBuilderGBA for this functionality.";
        bool _autoUpdate;
        bool _isConnected;
        string _connectionStatus = "Not Connected";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Notice text explaining platform limitation.</summary>
        public string NoticeText { get => _noticeText; set => SetField(ref _noticeText, value); }
        /// <summary>Whether auto-update polling is enabled.</summary>
        public bool AutoUpdate { get => _autoUpdate; set => SetField(ref _autoUpdate, value); }
        /// <summary>Whether the emulator connection is active.</summary>
        public bool IsConnected { get => _isConnected; set => SetField(ref _isConnected, value); }
        /// <summary>Current connection status text.</summary>
        public string ConnectionStatus { get => _connectionStatus; set => SetField(ref _connectionStatus, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
