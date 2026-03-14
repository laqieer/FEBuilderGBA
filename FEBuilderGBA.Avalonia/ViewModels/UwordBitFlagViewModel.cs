namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Four-byte (32-bit) bit flag editor popup.
    /// WinForms: UwordBitFlagForm — B40/B41/B42/B43 (4 hex bytes),
    /// J_40 "Traits 1" through J_43 "Traits 4" groups with L_4x_BIT_01..80.
    /// Pure in-memory popup — not ROM-backed, so not data-verifiable.
    /// </summary>
    public class UwordBitFlagViewModel : ViewModelBase
    {
        uint _value;
        bool _isLoaded;
        string _messageText = "Word Bit Flags (32-bit)";

        public uint Value { get => _value; set { SetField(ref _value, value); OnPropertyChanged(nameof(ValueHex)); NotifyAllBits(); } }
        public string ValueHex => $"0x{_value:X08}";
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        // Byte 0 (Traits 1) — maps to WinForms L_40_BIT_xx
        public bool Byte0Bit01 { get => GetBit(0); set => SetBit(0, value); }
        public bool Byte0Bit02 { get => GetBit(1); set => SetBit(1, value); }
        public bool Byte0Bit04 { get => GetBit(2); set => SetBit(2, value); }
        public bool Byte0Bit08 { get => GetBit(3); set => SetBit(3, value); }
        public bool Byte0Bit10 { get => GetBit(4); set => SetBit(4, value); }
        public bool Byte0Bit20 { get => GetBit(5); set => SetBit(5, value); }
        public bool Byte0Bit40 { get => GetBit(6); set => SetBit(6, value); }
        public bool Byte0Bit80 { get => GetBit(7); set => SetBit(7, value); }

        // Byte 1 (Traits 2) — maps to WinForms L_41_BIT_xx
        public bool Byte1Bit01 { get => GetBit(8);  set => SetBit(8, value); }
        public bool Byte1Bit02 { get => GetBit(9);  set => SetBit(9, value); }
        public bool Byte1Bit04 { get => GetBit(10); set => SetBit(10, value); }
        public bool Byte1Bit08 { get => GetBit(11); set => SetBit(11, value); }
        public bool Byte1Bit10 { get => GetBit(12); set => SetBit(12, value); }
        public bool Byte1Bit20 { get => GetBit(13); set => SetBit(13, value); }
        public bool Byte1Bit40 { get => GetBit(14); set => SetBit(14, value); }
        public bool Byte1Bit80 { get => GetBit(15); set => SetBit(15, value); }

        // Byte 2 (Traits 3) — maps to WinForms L_42_BIT_xx
        public bool Byte2Bit01 { get => GetBit(16); set => SetBit(16, value); }
        public bool Byte2Bit02 { get => GetBit(17); set => SetBit(17, value); }
        public bool Byte2Bit04 { get => GetBit(18); set => SetBit(18, value); }
        public bool Byte2Bit08 { get => GetBit(19); set => SetBit(19, value); }
        public bool Byte2Bit10 { get => GetBit(20); set => SetBit(20, value); }
        public bool Byte2Bit20 { get => GetBit(21); set => SetBit(21, value); }
        public bool Byte2Bit40 { get => GetBit(22); set => SetBit(22, value); }
        public bool Byte2Bit80 { get => GetBit(23); set => SetBit(23, value); }

        // Byte 3 (Traits 4) — maps to WinForms L_43_BIT_xx
        public bool Byte3Bit01 { get => GetBit(24); set => SetBit(24, value); }
        public bool Byte3Bit02 { get => GetBit(25); set => SetBit(25, value); }
        public bool Byte3Bit04 { get => GetBit(26); set => SetBit(26, value); }
        public bool Byte3Bit08 { get => GetBit(27); set => SetBit(27, value); }
        public bool Byte3Bit10 { get => GetBit(28); set => SetBit(28, value); }
        public bool Byte3Bit20 { get => GetBit(29); set => SetBit(29, value); }
        public bool Byte3Bit40 { get => GetBit(30); set => SetBit(30, value); }
        public bool Byte3Bit80 { get => GetBit(31); set => SetBit(31, value); }

        // Legacy aliases
        public bool Bit0  { get => Byte0Bit01; set => Byte0Bit01 = value; }
        public bool Bit1  { get => Byte0Bit02; set => Byte0Bit02 = value; }
        public bool Bit2  { get => Byte0Bit04; set => Byte0Bit04 = value; }
        public bool Bit3  { get => Byte0Bit08; set => Byte0Bit08 = value; }
        public bool Bit4  { get => Byte0Bit10; set => Byte0Bit10 = value; }
        public bool Bit5  { get => Byte0Bit20; set => Byte0Bit20 = value; }
        public bool Bit6  { get => Byte0Bit40; set => Byte0Bit40 = value; }
        public bool Bit7  { get => Byte0Bit80; set => Byte0Bit80 = value; }
        public bool Bit8  { get => Byte1Bit01; set => Byte1Bit01 = value; }
        public bool Bit9  { get => Byte1Bit02; set => Byte1Bit02 = value; }
        public bool Bit10 { get => Byte1Bit04; set => Byte1Bit04 = value; }
        public bool Bit11 { get => Byte1Bit08; set => Byte1Bit08 = value; }
        public bool Bit12 { get => Byte1Bit10; set => Byte1Bit10 = value; }
        public bool Bit13 { get => Byte1Bit20; set => Byte1Bit20 = value; }
        public bool Bit14 { get => Byte1Bit40; set => Byte1Bit40 = value; }
        public bool Bit15 { get => Byte1Bit80; set => Byte1Bit80 = value; }
        public bool Bit16 { get => Byte2Bit01; set => Byte2Bit01 = value; }
        public bool Bit17 { get => Byte2Bit02; set => Byte2Bit02 = value; }
        public bool Bit18 { get => Byte2Bit04; set => Byte2Bit04 = value; }
        public bool Bit19 { get => Byte2Bit08; set => Byte2Bit08 = value; }
        public bool Bit20 { get => Byte2Bit10; set => Byte2Bit10 = value; }
        public bool Bit21 { get => Byte2Bit20; set => Byte2Bit20 = value; }
        public bool Bit22 { get => Byte2Bit40; set => Byte2Bit40 = value; }
        public bool Bit23 { get => Byte2Bit80; set => Byte2Bit80 = value; }
        public bool Bit24 { get => Byte3Bit01; set => Byte3Bit01 = value; }
        public bool Bit25 { get => Byte3Bit02; set => Byte3Bit02 = value; }
        public bool Bit26 { get => Byte3Bit04; set => Byte3Bit04 = value; }
        public bool Bit27 { get => Byte3Bit08; set => Byte3Bit08 = value; }
        public bool Bit28 { get => Byte3Bit10; set => Byte3Bit10 = value; }
        public bool Bit29 { get => Byte3Bit20; set => Byte3Bit20 = value; }
        public bool Bit30 { get => Byte3Bit40; set => Byte3Bit40 = value; }
        public bool Bit31 { get => Byte3Bit80; set => Byte3Bit80 = value; }

        // Byte-level aliases matching WinForms B40/B41/B42/B43 control names
        public uint B40 { get => Value & 0xFF; }
        public uint B41 { get => (Value >> 8) & 0xFF; }
        public uint B42 { get => (Value >> 16) & 0xFF; }
        public uint B43 { get => (Value >> 24) & 0xFF; }

        bool GetBit(int bit) => (_value & (1u << bit)) != 0;

        void SetBit(int bit, bool on)
        {
            if (on) _value |= (1u << bit);
            else _value &= ~(1u << bit);
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

        void NotifyAllBits()
        {
            for (int i = 0; i < 4; i++)
            {
                string prefix = $"Byte{i}Bit";
                OnPropertyChanged(prefix + "01"); OnPropertyChanged(prefix + "02");
                OnPropertyChanged(prefix + "04"); OnPropertyChanged(prefix + "08");
                OnPropertyChanged(prefix + "10"); OnPropertyChanged(prefix + "20");
                OnPropertyChanged(prefix + "40"); OnPropertyChanged(prefix + "80");
            }
        }

        public void Load(uint initialValue)
        {
            _value = initialValue;
            IsLoaded = true;
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

    }
}
