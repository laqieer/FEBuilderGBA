namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Two-byte (16-bit) bit flag editor popup.
    /// WinForms: UshortBitFlagForm — B40 (low byte), B41 (high byte),
    /// J_40 "Traits 1" group with L_40_BIT_01..80, J_41 "Traits 2" group with L_41_BIT_01..80.
    /// Pure in-memory popup — not ROM-backed, so not data-verifiable.
    /// </summary>
    public class UshortBitFlagViewModel : ViewModelBase
    {
        uint _value;
        bool _isLoaded;
        string _messageText = "Short Bit Flags (16-bit)";

        public uint Value { get => _value; set { SetField(ref _value, value & 0xFFFF); OnPropertyChanged(nameof(ValueHex)); NotifyAllBits(); } }
        public string ValueHex => $"0x{_value:X04}";
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }

        // Low byte (Traits 1) — maps to WinForms L_40_BIT_xx
        public bool LowBit01 { get => GetBit(0); set => SetBit(0, value); }
        public bool LowBit02 { get => GetBit(1); set => SetBit(1, value); }
        public bool LowBit04 { get => GetBit(2); set => SetBit(2, value); }
        public bool LowBit08 { get => GetBit(3); set => SetBit(3, value); }
        public bool LowBit10 { get => GetBit(4); set => SetBit(4, value); }
        public bool LowBit20 { get => GetBit(5); set => SetBit(5, value); }
        public bool LowBit40 { get => GetBit(6); set => SetBit(6, value); }
        public bool LowBit80 { get => GetBit(7); set => SetBit(7, value); }

        // High byte (Traits 2) — maps to WinForms L_41_BIT_xx
        public bool HighBit01 { get => GetBit(8);  set => SetBit(8, value); }
        public bool HighBit02 { get => GetBit(9);  set => SetBit(9, value); }
        public bool HighBit04 { get => GetBit(10); set => SetBit(10, value); }
        public bool HighBit08 { get => GetBit(11); set => SetBit(11, value); }
        public bool HighBit10 { get => GetBit(12); set => SetBit(12, value); }
        public bool HighBit20 { get => GetBit(13); set => SetBit(13, value); }
        public bool HighBit40 { get => GetBit(14); set => SetBit(14, value); }
        public bool HighBit80 { get => GetBit(15); set => SetBit(15, value); }

        // Legacy aliases
        public bool Bit0  { get => LowBit01;  set => LowBit01 = value; }
        public bool Bit1  { get => LowBit02;  set => LowBit02 = value; }
        public bool Bit2  { get => LowBit04;  set => LowBit04 = value; }
        public bool Bit3  { get => LowBit08;  set => LowBit08 = value; }
        public bool Bit4  { get => LowBit10;  set => LowBit10 = value; }
        public bool Bit5  { get => LowBit20;  set => LowBit20 = value; }
        public bool Bit6  { get => LowBit40;  set => LowBit40 = value; }
        public bool Bit7  { get => LowBit80;  set => LowBit80 = value; }
        public bool Bit8  { get => HighBit01; set => HighBit01 = value; }
        public bool Bit9  { get => HighBit02; set => HighBit02 = value; }
        public bool Bit10 { get => HighBit04; set => HighBit04 = value; }
        public bool Bit11 { get => HighBit08; set => HighBit08 = value; }
        public bool Bit12 { get => HighBit10; set => HighBit10 = value; }
        public bool Bit13 { get => HighBit20; set => HighBit20 = value; }
        public bool Bit14 { get => HighBit40; set => HighBit40 = value; }
        public bool Bit15 { get => HighBit80; set => HighBit80 = value; }

        // Byte-level aliases matching WinForms B40/B41 control names
        public uint B40 { get => Value & 0xFF; }
        public uint B41 { get => (Value >> 8) & 0xFF; }

        bool GetBit(int bit) => (_value & (1u << bit)) != 0;

        void SetBit(int bit, bool on)
        {
            if (on) _value |= (1u << bit);
            else _value &= ~(1u << bit);
            _value &= 0xFFFF;
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

        void NotifyAllBits()
        {
            OnPropertyChanged(nameof(LowBit01)); OnPropertyChanged(nameof(LowBit02));
            OnPropertyChanged(nameof(LowBit04)); OnPropertyChanged(nameof(LowBit08));
            OnPropertyChanged(nameof(LowBit10)); OnPropertyChanged(nameof(LowBit20));
            OnPropertyChanged(nameof(LowBit40)); OnPropertyChanged(nameof(LowBit80));
            OnPropertyChanged(nameof(HighBit01)); OnPropertyChanged(nameof(HighBit02));
            OnPropertyChanged(nameof(HighBit04)); OnPropertyChanged(nameof(HighBit08));
            OnPropertyChanged(nameof(HighBit10)); OnPropertyChanged(nameof(HighBit20));
            OnPropertyChanged(nameof(HighBit40)); OnPropertyChanged(nameof(HighBit80));
        }

        public void Load(uint initialValue)
        {
            _value = initialValue & 0xFFFF;
            IsLoaded = true;
            NotifyAllBits();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

    }
}
