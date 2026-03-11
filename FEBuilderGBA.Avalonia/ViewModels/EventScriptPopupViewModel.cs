using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Event script disassembly viewer.</summary>
    public class EventScriptPopupViewModel : ViewModelBase
    {
        string _infoText = "";
        bool _isLoaded;
        string _addressText = "";
        string _statusText = "";
        ObservableCollection<string> _commands = new();

        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AddressText { get => _addressText; set => SetField(ref _addressText, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public ObservableCollection<string> Commands { get => _commands; set => SetField(ref _commands, value); }

        const int MaxCommands = 200;
        const int MaxConsecutiveUnknown = 10;

        public void Load()
        {
            InfoText =
                "Event Script Command Reference\n" +
                "==============================\n\n" +
                "Event scripts control story progression, map events, and gameplay triggers.\n\n" +
                "Common commands:\n" +
                "  LOAD1/LOAD2  - Load unit groups onto the map\n" +
                "  MOVE         - Move a unit on the map\n" +
                "  FIGHT        - Trigger a battle between units\n" +
                "  TEXT/TEXTSHOW - Display text dialogue\n" +
                "  GOTO/CALL    - Jump to another script\n" +
                "  IFEF/IFAT    - Conditional branching\n" +
                "  MUSC/MUSI    - Play/change music\n" +
                "  CAMERA       - Move camera view\n" +
                "  FADU/FADI    - Fade screen in/out\n" +
                "  ENDA         - End event script\n\n" +
                "Enter a ROM address above and click Disassemble to parse event script commands.";
            IsLoaded = true;
        }

        /// <summary>
        /// Disassemble event script commands starting at the given ROM address.
        /// </summary>
        public void DisassembleAt(uint address)
        {
            Commands.Clear();
            StatusText = "";

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                StatusText = "Error: No ROM loaded.";
                return;
            }

            // Get or create EventScript instance
            EventScript es = CoreState.EventScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = new EventScript();
                    es.Load(EventScript.EventScriptType.Event);
                }
                catch (Exception ex)
                {
                    StatusText = $"Error loading event script definitions: {ex.Message}";
                    return;
                }
            }

            uint offset = U.toOffset(address);
            if (offset >= (uint)rom.Data.Length)
            {
                StatusText = $"Error: Address 0x{address:X08} is outside ROM bounds (size: 0x{rom.Data.Length:X08}).";
                return;
            }

            int consecutiveUnknown = 0;
            int commandCount = 0;
            var lines = new List<string>();

            while (commandCount < MaxCommands && offset < (uint)rom.Data.Length)
            {
                EventScript.OneCode code;
                try
                {
                    code = es.DisAseemble(rom.Data, offset);
                }
                catch
                {
                    lines.Add($"0x{offset:X06}: [Disassembly error]");
                    break;
                }

                if (code == null || code.Script == null || code.ByteData == null)
                {
                    lines.Add($"0x{offset:X06}: [Failed to decode]");
                    break;
                }

                // Build display string
                string cmdName = EventScript.makeCommandComboText(code.Script, false);
                string hexBytes = U.HexDumpLiner(code.ByteData).Trim();
                string line = $"0x{offset:X06}: {cmdName}  [{hexBytes}]";
                if (!string.IsNullOrEmpty(code.Comment))
                {
                    line += $"  // {code.Comment}";
                }
                lines.Add(line);

                // Check for end commands
                if (code.Script.Has == EventScript.ScriptHas.TERM ||
                    code.Script.Has == EventScript.ScriptHas.MAPTERM)
                {
                    lines.Add($"--- End of script (TERM) ---");
                    break;
                }

                // Track consecutive unknowns
                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    consecutiveUnknown++;
                    if (consecutiveUnknown >= MaxConsecutiveUnknown)
                    {
                        lines.Add($"--- Stopped: {MaxConsecutiveUnknown} consecutive unknown commands ---");
                        break;
                    }
                }
                else
                {
                    consecutiveUnknown = 0;
                }

                // Advance offset
                uint size = (uint)code.Script.Size;
                if (size == 0) size = 4; // safety fallback
                offset += size;
                commandCount++;
            }

            if (commandCount >= MaxCommands)
            {
                lines.Add($"--- Stopped: reached max {MaxCommands} commands ---");
            }

            foreach (var line in lines)
            {
                Commands.Add(line);
            }

            StatusText = $"Disassembled {lines.Count} line(s) starting at 0x{U.toOffset(address):X06}";
        }

        /// <summary>
        /// Parse address text from the input field. Supports "0x" prefix and plain hex.
        /// </summary>
        public bool TryParseAddress(out uint address)
        {
            address = 0;
            string text = (AddressText ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out address);
        }
    }
}
