using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveToFreeSpaceViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentAddress = string.Empty;
        string _freeSpaceAddress = string.Empty;
        string _dataSize = string.Empty;
        string _newAddress = string.Empty;
        string _statusMessage = "Free Space Manager finds and manages unused ROM space.\nUse this tool to relocate data to free areas when expanding content.";
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Current data address in the ROM.</summary>
        public string CurrentAddress { get => _currentAddress; set => SetField(ref _currentAddress, value); }
        /// <summary>Address of found free space.</summary>
        public string FreeSpaceAddress { get => _freeSpaceAddress; set => SetField(ref _freeSpaceAddress, value); }
        /// <summary>Size of data to be moved (in bytes).</summary>
        public string DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }
        /// <summary>New destination address after move.</summary>
        public string NewAddress { get => _newAddress; set => SetField(ref _newAddress, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
