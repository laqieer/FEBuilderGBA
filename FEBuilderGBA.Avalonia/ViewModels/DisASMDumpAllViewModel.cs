using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _selectedAction; // 0=DisASM, 1=IDA MAP, 2=No$GBA SYM
        string _output = string.Empty;
        string _statusMessage = string.Empty;
        string _addressInput = string.Empty;
        string _lengthInput = "0x100";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Selected dump action: 0=Full Disassembly, 1=IDA MAP File, 2=No$GBA SYM File, 3=Address Range.</summary>
        public int SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }
        /// <summary>Output text from the dump operation.</summary>
        public string Output { get => _output; set => SetField(ref _output, value); }
        /// <summary>Status message during operation.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Address input for range-based disassembly (hex, e.g. "0x080000" or "80000").</summary>
        public string AddressInput { get => _addressInput; set => SetField(ref _addressInput, value); }
        /// <summary>Length input for range-based disassembly (hex, e.g. "0x100").</summary>
        public string LengthInput { get => _lengthInput; set => SetField(ref _lengthInput, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Run the disassembly using DisassemblerCore based on SelectedAction.
        /// Returns (output lines, error message or null).
        /// </summary>
        public (List<string>? lines, string? error) RunDisassembly()
        {
            if (CoreState.ROM == null)
                return (null, "Error: No ROM loaded.");

            try
            {
                var core = new DisassemblerCore();

                switch (SelectedAction)
                {
                    case 1:
                        return (core.ExportIDAMapLines(), null);
                    case 2:
                        return (core.ExportNoCashSymLines(), null);
                    case 3:
                        return RunRangeDisassembly(core);
                    default:
                        return (core.DisassembleToLines(), null);
                }
            }
            catch (Exception ex)
            {
                return (null, $"Error: {ex.Message}");
            }
        }

        (List<string>? lines, string? error) RunRangeDisassembly(DisassemblerCore core)
        {
            uint address = ParseHexInput(AddressInput);
            uint length = ParseHexInput(LengthInput);

            if (length == 0)
                return (null, "Error: Invalid length. Enter a hex value like 0x100.");

            // Auto-convert GBA pointer to ROM offset
            if (address >= 0x08000000 && address < 0x0A000000)
                address -= 0x08000000;

            return (core.DisassembleRange(address, length), null);
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
