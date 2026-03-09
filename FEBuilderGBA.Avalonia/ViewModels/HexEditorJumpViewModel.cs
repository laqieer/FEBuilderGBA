using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorJumpViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _address = string.Empty;
        bool _isLittleEndian;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Hex address to jump to (e.g. "0x08000000").</summary>
        public string Address { get => _address; set => SetField(ref _address, value); }
        /// <summary>Force little-endian interpretation of the address.</summary>
        public bool IsLittleEndian { get => _isLittleEndian; set => SetField(ref _isLittleEndian, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>History of previously visited addresses.</summary>
        public ObservableCollection<string> AddressHistory { get; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Parse the address string to a uint, returning 0 on failure.</summary>
        public uint GetParsedAddress()
        {
            string text = (Address ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint result))
                return result;
            return 0;
        }
    }
}
