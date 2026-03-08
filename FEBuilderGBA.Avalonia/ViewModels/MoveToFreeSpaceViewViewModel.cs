using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveToFreeSpaceViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentAddress = string.Empty;
        string _freeSpaceAddress = string.Empty;
        string _dataSize = string.Empty;
        string _statusMessage = "Free Space Manager finds and manages unused ROM space.\nUse this tool to relocate data to free areas when expanding content.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string CurrentAddress { get => _currentAddress; set => SetField(ref _currentAddress, value); }
        public string FreeSpaceAddress { get => _freeSpaceAddress; set => SetField(ref _freeSpaceAddress, value); }
        public string DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
