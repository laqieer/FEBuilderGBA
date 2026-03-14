namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Single-byte (8-bit) bit flag editor popup.
    /// WinForms: UbyteBitFlagForm — B40 (hex byte), L_40_BIT_01..80 (8 checkboxes).
    /// Pure in-memory popup — not ROM-backed, so not data-verifiable.
    /// </summary>
    public class UbyteBitFlagViewModel : ViewModelBase
    {
        uint _value;
        bool _isLoaded;
        string _messageText = "Byte Bit Flags (8-bit)";

        public uint Value { get => _value; set { SetField(ref _value, value & 0xFF); OnPropertyChanged(nameof(ValueHex)); NotifyAllBits(); } }
        public string ValueHex => $"0x{_value:X02}";
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        // Bit properties named for the WinForms checkbox bit positions
        public bool FlagBit01 { get => GetBit(0); set => SetBit(0, value); }
        public bool FlagBit02 { get => GetBit(1); set => SetBit(1, value); }
        public bool FlagBit04 { get => GetBit(2); set => SetBit(2, value); }
        public bool FlagBit08 { get => GetBit(3); set => SetBit(3, value); }
        public bool FlagBit10 { get => GetBit(4); set => SetBit(4, value); }
        public bool FlagBit20 { get => GetBit(5); set => SetBit(5, value); }
        public bool FlagBit40 { get => GetBit(6); set => SetBit(6, value); }
        public bool FlagBit80 { get => GetBit(7); set => SetBit(7, value); }

        // Legacy aliases
        public bool Bit0 { get => FlagBit01; set => FlagBit01 = value; }
        public bool Bit1 { get => FlagBit02; set => FlagBit02 = value; }
        public bool Bit2 { get => FlagBit04; set => FlagBit04 = value; }
        public bool Bit3 { get => FlagBit08; set => FlagBit08 = value; }
        public bool Bit4 { get => FlagBit10; set => FlagBit10 = value; }
        public bool Bit5 { get => FlagBit20; set => FlagBit20 = value; }
        public bool Bit6 { get => FlagBit40; set => FlagBit40 = value; }
        public bool Bit7 { get => FlagBit80; set => FlagBit80 = value; }

        // Byte-level alias matching WinForms B40 control name
        public uint B40 { get => Value; }

        bool GetBit(int bit) => (_value & (1u << bit)) != 0;

        void SetBit(int bit, bool on)
        {
            if (on) _value |= (1u << bit);
            else _value &= ~(1u << bit);
            _value &= 0xFF;
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

        void NotifyAllBits()
        {
            OnPropertyChanged(nameof(FlagBit01));
            OnPropertyChanged(nameof(FlagBit02));
            OnPropertyChanged(nameof(FlagBit04));
            OnPropertyChanged(nameof(FlagBit08));
            OnPropertyChanged(nameof(FlagBit10));
            OnPropertyChanged(nameof(FlagBit20));
            OnPropertyChanged(nameof(FlagBit40));
            OnPropertyChanged(nameof(FlagBit80));
        }

        public void Load(uint initialValue)
        {
            _value = initialValue & 0xFF;
            IsLoaded = true;
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

    }
}
