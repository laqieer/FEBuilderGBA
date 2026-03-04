using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Two-byte (16-bit) bit flag editor popup.</summary>
    public class UshortBitFlagViewModel : ViewModelBase, IDataVerifiable
    {
        uint _value;
        bool _isLoaded;

        public uint Value { get => _value; set { SetField(ref _value, value & 0xFFFF); OnPropertyChanged(nameof(ValueHex)); } }
        public string ValueHex => $"0x{_value:X04}";
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public bool Bit0  { get => GetBit(0);  set => SetBit(0, value); }
        public bool Bit1  { get => GetBit(1);  set => SetBit(1, value); }
        public bool Bit2  { get => GetBit(2);  set => SetBit(2, value); }
        public bool Bit3  { get => GetBit(3);  set => SetBit(3, value); }
        public bool Bit4  { get => GetBit(4);  set => SetBit(4, value); }
        public bool Bit5  { get => GetBit(5);  set => SetBit(5, value); }
        public bool Bit6  { get => GetBit(6);  set => SetBit(6, value); }
        public bool Bit7  { get => GetBit(7);  set => SetBit(7, value); }
        public bool Bit8  { get => GetBit(8);  set => SetBit(8, value); }
        public bool Bit9  { get => GetBit(9);  set => SetBit(9, value); }
        public bool Bit10 { get => GetBit(10); set => SetBit(10, value); }
        public bool Bit11 { get => GetBit(11); set => SetBit(11, value); }
        public bool Bit12 { get => GetBit(12); set => SetBit(12, value); }
        public bool Bit13 { get => GetBit(13); set => SetBit(13, value); }
        public bool Bit14 { get => GetBit(14); set => SetBit(14, value); }
        public bool Bit15 { get => GetBit(15); set => SetBit(15, value); }

        bool GetBit(int bit) => (_value & (1u << bit)) != 0;

        void SetBit(int bit, bool on)
        {
            if (on) _value |= (1u << bit);
            else _value &= ~(1u << bit);
            _value &= 0xFFFF;
            OnPropertyChanged($"Bit{bit}");
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

        public void Load(uint initialValue)
        {
            _value = initialValue & 0xFFFF;
            IsLoaded = true;
            for (int i = 0; i < 16; i++)
                OnPropertyChanged($"Bit{i}");
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueHex));
        }

        public int GetListCount() => 1;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Value"] = $"0x{Value:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
