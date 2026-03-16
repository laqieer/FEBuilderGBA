using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventScriptViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _addressText = "";
        string _statusText = "";
        string _disassembledText = "";
        ObservableCollection<string> _commands = new();
        int _selectedCommandIndex = -1;

        // Internal data for disassembled codes
        readonly List<EventScript.OneCode> _disassembledCodes = new();
        readonly List<uint> _commandOffsets = new();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AddressText { get => _addressText; set => SetField(ref _addressText, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        /// <summary>Full disassembled script text for display in a read-only TextBox.</summary>
        public string DisassembledText { get => _disassembledText; set => SetField(ref _disassembledText, value); }

        /// <summary>List of command line strings for the ListBox.</summary>
        public ObservableCollection<string> Commands { get => _commands; set => SetField(ref _commands, value); }

        public int SelectedCommandIndex
        {
            get => _selectedCommandIndex;
            set => SetField(ref _selectedCommandIndex, value);
        }

        const int MaxCommands = 200;
        const int MaxConsecutiveUnknown = 10;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Event Script Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }

        /// <summary>Parse an address string. Supports "0x" prefix and plain hex.</summary>
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

        /// <summary>
        /// Disassemble event script at the given ROM address using CoreState.EventScript.
        /// Populates Commands list and DisassembledText.
        /// </summary>
        public void DisassembleAt(uint address)
        {
            Commands.Clear();
            _disassembledCodes.Clear();
            _commandOffsets.Clear();
            DisassembledText = "";
            StatusText = "";

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                StatusText = "Error: No ROM loaded.";
                return;
            }

            EventScript es = CoreState.EventScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = new EventScript();
                    es.Load(EventScript.EventScriptType.Event);
                    CoreState.EventScript = es;
                }
                catch (Exception ex)
                {
                    StatusText = $"Error loading script definitions: {ex.Message}";
                    return;
                }
            }

            uint offset = U.toOffset(address);
            if (offset >= (uint)rom.Data.Length)
            {
                StatusText = $"Error: Address 0x{address:X08} is outside ROM bounds.";
                return;
            }

            int consecutiveUnknown = 0;
            int commandCount = 0;
            var lines = new List<string>();
            var sb = new StringBuilder();

            while (commandCount < MaxCommands && offset < (uint)rom.Data.Length)
            {
                EventScript.OneCode code;
                try
                {
                    code = es.DisAseemble(rom.Data, offset);
                }
                catch
                {
                    string errLine = $"0x{offset:X06}: [Disassembly error]";
                    lines.Add(errLine);
                    sb.AppendLine(errLine);
                    break;
                }

                if (code == null || code.Script == null || code.ByteData == null)
                {
                    string errLine = $"0x{offset:X06}: [Failed to decode]";
                    lines.Add(errLine);
                    sb.AppendLine(errLine);
                    break;
                }

                _disassembledCodes.Add(code);
                _commandOffsets.Add(offset);

                // Build display line
                string cmdName = EventScript.makeCommandComboText(code.Script, false);
                string hexBytes = U.HexDumpLiner(code.ByteData).Trim();
                string line = $"0x{offset:X06}: {cmdName}  [{hexBytes}]";

                // Append argument values
                if (code.Script.Args != null && code.Script.Args.Length > 0)
                {
                    var argParts = new List<string>();
                    for (int i = 0; i < code.Script.Args.Length; i++)
                    {
                        var arg = code.Script.Args[i];
                        if (arg.Type == EventScript.ArgType.FIXED) continue;
                        string argStr = EventScript.GetArg(code, i, out _);
                        string name = arg.Name ?? arg.Symbol.ToString();
                        argParts.Add($"{name}={argStr}");
                    }
                    if (argParts.Count > 0)
                        line += "  " + string.Join(", ", argParts);
                }

                if (!string.IsNullOrEmpty(code.Comment))
                    line += $"  // {code.Comment}";

                lines.Add(line);
                sb.AppendLine(line);

                // Check terminator
                if (code.Script.Has == EventScript.ScriptHas.TERM ||
                    code.Script.Has == EventScript.ScriptHas.MAPTERM)
                    break;

                // Track unknowns
                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    consecutiveUnknown++;
                    if (consecutiveUnknown >= MaxConsecutiveUnknown) break;
                }
                else
                {
                    consecutiveUnknown = 0;
                }

                uint size = (uint)code.Script.Size;
                if (size == 0) size = 4;
                offset += size;
                commandCount++;
            }

            foreach (var line in lines)
                Commands.Add(line);

            DisassembledText = sb.ToString();
            StatusText = $"Disassembled {lines.Count} command(s) at 0x{U.toOffset(address):X06}";
            CurrentAddr = address;
        }

        /// <summary>
        /// Disassemble event script at the given address and return the text representation.
        /// Static helper for testing without UI.
        /// </summary>
        public static string DisassembleToText(uint address)
        {
            var vm = new EventScriptViewModel();
            vm.DisassembleAt(address);
            return vm.DisassembledText;
        }
    }
}
