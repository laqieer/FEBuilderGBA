using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _addressInput = "0x08000000";
        string _lengthInput = "0x200";
        string _statusMessage = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Address input for disassembly (hex, e.g. "0x08000000").</summary>
        public string AddressInput { get => _addressInput; set => SetField(ref _addressInput, value); }

        /// <summary>Length input for disassembly (hex, e.g. "0x200").</summary>
        public string LengthInput { get => _lengthInput; set => SetField(ref _lengthInput, value); }

        /// <summary>Status message displayed during operation.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Run disassembly using DisassemblerCore.DisassembleRange.
        /// Returns (output lines, error message or null).
        /// </summary>
        public (List<string>? lines, string? error) RunDisassembly()
        {
            if (CoreState.ROM == null)
                return (null, "Error: No ROM loaded.");

            try
            {
                uint address = ParseHexInput(AddressInput);
                uint length = ParseHexInput(LengthInput);

                if (length == 0)
                    return (null, "Error: Invalid length. Enter a hex value like 0x200.");

                // Auto-convert GBA pointer to ROM offset
                if (address >= 0x08000000 && address < 0x0A000000)
                    address -= 0x08000000;

                var core = new DisassemblerCore();
                return (core.DisassembleRange(address, length), null);
            }
            catch (Exception ex)
            {
                return (null, $"Error: {ex.Message}");
            }
        }

        /// <summary>Parse a hex string like "0x80000", "80000", or "0x08080000".</summary>
        internal static uint ParseHexInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            input = input.Trim();
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(2);

            if (uint.TryParse(input, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint result))
                return result;

            return 0;
        }
    }
}
