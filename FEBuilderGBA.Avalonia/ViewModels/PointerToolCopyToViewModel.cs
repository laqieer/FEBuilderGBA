using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolCopyToViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _sourceAddress = string.Empty;
        string _copyMode = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The address value being copied.</summary>
        public string SourceAddress { get => _sourceAddress; set => SetField(ref _sourceAddress, value); }
        /// <summary>Copy mode: "Pointer", "Clipboard", "LittleEndian", "Hex", or "NoDoll".</summary>
        public string CopyMode { get => _copyMode; set => SetField(ref _copyMode, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Get the source address formatted as a GBA pointer string.</summary>
        public string GetAsPointer()
        {
            string text = (SourceAddress ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return $"0x{(val + 0x08000000):X08}";
            return SourceAddress;
        }

        /// <summary>Get the source address as little-endian byte string.</summary>
        public string GetAsLittleEndian()
        {
            string text = (SourceAddress ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
            {
                byte b0 = (byte)(val & 0xFF);
                byte b1 = (byte)((val >> 8) & 0xFF);
                byte b2 = (byte)((val >> 16) & 0xFF);
                byte b3 = (byte)((val >> 24) & 0xFF);
                return $"{b0:X02} {b1:X02} {b2:X02} {b3:X02}";
            }
            return SourceAddress;
        }
    }
}
