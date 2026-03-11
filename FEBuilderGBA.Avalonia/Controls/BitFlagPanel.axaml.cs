using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    public partial class BitFlagPanel : UserControl
    {
        readonly CheckBox[] _bits;
        bool _updating;

        public event Action<byte>? ValueChanged;

        public BitFlagPanel()
        {
            InitializeComponent();
            _bits = new[] { Bit0, Bit1, Bit2, Bit3, Bit4, Bit5, Bit6, Bit7 };
            foreach (var cb in _bits)
                cb.IsCheckedChanged += OnBitChanged;
        }

        public byte Value
        {
            get
            {
                byte val = 0;
                for (int i = 0; i < 8; i++)
                    if (_bits[i].IsChecked == true) val |= (byte)(1 << i);
                return val;
            }
            set
            {
                _updating = true;
                for (int i = 0; i < 8; i++)
                    _bits[i].IsChecked = (value & (1 << i)) != 0;
                _updating = false;
            }
        }

        /// <summary>Set display names for each bit (0-7). Null entries keep default "Bit N".</summary>
        public void SetBitNames(string?[] names)
        {
            for (int i = 0; i < 8 && i < names.Length; i++)
            {
                if (names[i] != null)
                    _bits[i].Content = names[i];
            }
        }

        void OnBitChanged(object? sender, RoutedEventArgs e)
        {
            if (!_updating)
                ValueChanged?.Invoke(Value);
        }
    }
}
