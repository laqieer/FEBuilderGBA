using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorViewModel : ViewModelBase
    {
        uint _baseAddress;
        uint _viewSize = 0x100; // 256 bytes per page (16 rows of 16)
        string _hexDisplay = "";
        string _addressInfo = "";
        bool _isLoaded;

        public uint BaseAddress { get => _baseAddress; set { SetField(ref _baseAddress, value); RefreshDisplay(); } }
        public uint ViewSize { get => _viewSize; set { SetField(ref _viewSize, value); RefreshDisplay(); } }
        public string HexDisplay { get => _hexDisplay; set => SetField(ref _hexDisplay, value); }
        public string AddressInfo { get => _addressInfo; set => SetField(ref _addressInfo, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void RefreshDisplay()
        {
            var rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                HexDisplay = "(No ROM loaded)";
                AddressInfo = "";
                IsLoaded = false;
                return;
            }

            IsLoaded = true;
            var sb = new StringBuilder();
            sb.AppendLine("Address  | 00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F | ASCII");
            sb.AppendLine("---------|--------------------------------------------------|----------------");

            uint end = Math.Min(_baseAddress + _viewSize, (uint)rom.Data.Length);
            for (uint row = _baseAddress; row < end; row += 16)
            {
                sb.Append($"{row:X08} | ");
                var ascii = new StringBuilder();
                for (uint col = 0; col < 16; col++)
                {
                    uint addr = row + col;
                    if (addr < (uint)rom.Data.Length)
                    {
                        byte b = (byte)rom.u8(addr);
                        sb.Append($"{b:X02} ");
                        ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                    }
                    else
                    {
                        sb.Append("   ");
                        ascii.Append(' ');
                    }
                    if (col == 7) sb.Append(' ');
                }
                sb.Append("| ");
                sb.AppendLine(ascii.ToString());
            }

            HexDisplay = sb.ToString();
            AddressInfo = $"ROM: 0x{_baseAddress:X08} - 0x{end:X08} ({end - _baseAddress} bytes) | Total: 0x{rom.Data.Length:X} bytes";
        }

        public void JumpTo(uint address)
        {
            // Align to 16-byte boundary
            BaseAddress = address & 0xFFFFFFF0;
        }

        public uint? SearchBytes(byte[] pattern, uint startAddr)
        {
            var rom = CoreState.ROM;
            if (rom == null || pattern.Length == 0) return null;

            for (uint i = startAddr; i + pattern.Length <= (uint)rom.Data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if ((byte)rom.u8(i + (uint)j) != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return null;
        }

        public void PageUp() => BaseAddress = _baseAddress >= _viewSize ? _baseAddress - _viewSize : 0;
        public void PageDown() => BaseAddress = _baseAddress + _viewSize;
    }
}
