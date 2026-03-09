using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteSwapViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Palette Swap swaps palette assignments between entries.\nSelect source and destination palette slots to exchange their color data.";
        int _sourceSlot;
        int _destinationSlot;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Source palette slot index (0-15) to swap from.</summary>
        public int SourceSlot { get => _sourceSlot; set => SetField(ref _sourceSlot, value); }
        /// <summary>Destination palette slot index (0-15) to swap to.</summary>
        public int DestinationSlot { get => _destinationSlot; set => SetField(ref _destinationSlot, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
