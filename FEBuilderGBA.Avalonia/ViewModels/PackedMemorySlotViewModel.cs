using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PackedMemorySlotViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _slotInfo = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SlotInfo { get => _slotInfo; set => SetField(ref _slotInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
