using System;
using System.Globalization;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveToFreeSpaceViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentAddress = string.Empty;
        string _freeSpaceAddress = string.Empty;
        string _dataSize = string.Empty;
        string _newAddress = string.Empty;
        string _statusMessage = "Free Space Manager finds and manages unused ROM space.\nUse this tool to relocate data to free areas when expanding content.";
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Current data address in the ROM.</summary>
        public string CurrentAddress { get => _currentAddress; set => SetField(ref _currentAddress, value); }
        /// <summary>Address of found free space.</summary>
        public string FreeSpaceAddress { get => _freeSpaceAddress; set => SetField(ref _freeSpaceAddress, value); }
        /// <summary>Size of data to be moved (in bytes).</summary>
        public string DataSize { get => _dataSize; set => SetField(ref _dataSize, value); }
        /// <summary>New destination address after move.</summary>
        public string NewAddress { get => _newAddress; set => SetField(ref _newAddress, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Scan the ROM for a contiguous block of free space (0xFF or 0x00 bytes).
        /// Returns the start address of the block, or null if not found.
        /// </summary>
        public uint? FindFreeSpace(uint requestedSize, byte fillByte = 0xFF)
        {
            var rom = CoreState.ROM;
            if (rom == null || requestedSize == 0) return null;
            uint consecutive = 0;
            for (uint i = 0; i < (uint)rom.Data.Length; i++)
            {
                if (rom.u8(i) == fillByte || rom.u8(i) == 0x00)
                {
                    consecutive++;
                    if (consecutive >= requestedSize)
                        return i - requestedSize + 1;
                }
                else
                {
                    consecutive = 0;
                }
            }
            return null;
        }

        /// <summary>Parse a hex string (with optional 0x prefix) into a uint.</summary>
        static bool TryParseHex(string input, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string text = input.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, NumberStyles.HexNumber, null, out value);
        }

        /// <summary>
        /// Execute the move operation: find free space for the requested size,
        /// update FreeSpaceAddress/NewAddress, and copy data + update pointer.
        /// </summary>
        public void ExecuteMove()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusMessage = "No ROM loaded.";
                return;
            }

            if (!TryParseHex(DataSize, out uint size) || size == 0)
            {
                StatusMessage = "Invalid or zero data size.";
                return;
            }

            if (!TryParseHex(CurrentAddress, out uint srcAddr))
            {
                StatusMessage = "Invalid current address.";
                return;
            }

            uint? freeAddr = FindFreeSpace(size);
            if (freeAddr == null)
            {
                StatusMessage = $"Could not find {size} bytes of free space.";
                FreeSpaceAddress = "";
                NewAddress = "";
                return;
            }

            uint dst = freeAddr.Value;
            FreeSpaceAddress = $"0x{dst:X08}";

            // Copy data from source to destination
            if (srcAddr + size <= (uint)rom.Data.Length && dst + size <= (uint)rom.Data.Length)
            {
                for (uint i = 0; i < size; i++)
                    rom.write_u8(dst + i, rom.u8(srcAddr + i));

                // Clear old location
                for (uint i = 0; i < size; i++)
                    rom.write_u8(srcAddr + i, 0xFF);

                NewAddress = $"0x{dst:X08}";
                StatusMessage = $"Moved {size} bytes from 0x{srcAddr:X08} to 0x{dst:X08}.";
                DialogResult = "Moved";
            }
            else
            {
                StatusMessage = "Address range exceeds ROM bounds.";
            }
        }
    }
}
