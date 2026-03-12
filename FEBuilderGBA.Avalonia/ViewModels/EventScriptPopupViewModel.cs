using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Represents one editable argument of an event script command.</summary>
    public class CommandArgEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        string _name = "";
        EventScript.ArgType _argType;
        int _byteOffset;
        int _byteSize;
        uint _value;
        string _displayName = "";
        bool _isPointer;
        bool _isDecimal;
        bool _isFixed;

        /// <summary>Argument name from script definition (e.g. "Unit", "X", "Text").</summary>
        public string Name { get => _name; set { _name = value; OnChanged(); } }

        /// <summary>The ArgType enum value.</summary>
        public EventScript.ArgType ArgType { get => _argType; set { _argType = value; OnChanged(); } }

        /// <summary>Byte offset within the command's byte data.</summary>
        public int ByteOffset { get => _byteOffset; set { _byteOffset = value; OnChanged(); } }

        /// <summary>Number of bytes this argument occupies (1, 2, 3, or 4).</summary>
        public int ByteSize { get => _byteSize; set { _byteSize = value; OnChanged(); } }

        /// <summary>Current numeric value of this argument.</summary>
        public uint Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnChanged();
                    UpdateDisplayName();
                }
            }
        }

        /// <summary>Resolved display name (e.g. unit/class/item name).</summary>
        public string DisplayName { get => _displayName; set { _displayName = value; OnChanged(); } }

        /// <summary>True if this is a pointer-type arg.</summary>
        public bool IsPointer { get => _isPointer; set { _isPointer = value; OnChanged(); } }

        /// <summary>True if this arg should display in decimal.</summary>
        public bool IsDecimal { get => _isDecimal; set { _isDecimal = value; OnChanged(); } }

        /// <summary>True if this is a FIXED (constant) byte — not editable.</summary>
        public bool IsFixed { get => _isFixed; set { _isFixed = value; OnChanged(); } }

        /// <summary>True if this is an editable argument (not FIXED).</summary>
        public bool IsEditable => !IsFixed;

        /// <summary>Human-readable type label for display.</summary>
        public string TypeLabel => ArgType == EventScript.ArgType.FIXED ? "FIXED" : ArgType.ToString();

        /// <summary>Max value for the numeric control.</summary>
        public uint MaxValue
        {
            get
            {
                return ByteSize switch
                {
                    1 => 0xFF,
                    2 => 0xFFFF,
                    3 => 0xFFFFFF,
                    _ => 0xFFFFFFFF
                };
            }
        }

        /// <summary>Value formatted for display in hex.</summary>
        public string HexValueText
        {
            get => $"0x{Value:X}";
            set
            {
                string txt = (value ?? "").Trim();
                if (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    txt = txt.Substring(2);
                if (uint.TryParse(txt, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                    Value = v;
            }
        }

        /// <summary>Value formatted for display in decimal.</summary>
        public string DecValueText
        {
            get => Value.ToString();
            set
            {
                if (uint.TryParse(value, out uint v))
                    Value = v;
            }
        }

        void UpdateDisplayName()
        {
            DisplayName = ResolveDisplayName(ArgType, Value);
        }

        /// <summary>Resolve a human-readable name for certain arg types.</summary>
        public static string ResolveDisplayName(EventScript.ArgType type, uint value)
        {
            try
            {
                switch (type)
                {
                    case EventScript.ArgType.UNIT:
                        return NameResolver.GetUnitName(value);
                    case EventScript.ArgType.CLASS:
                        return NameResolver.GetClassName(value);
                    case EventScript.ArgType.ITEM:
                        return NameResolver.GetItemName(value);
                    case EventScript.ArgType.MUSIC:
                    case EventScript.ArgType.MAPMUSIC:
                        return NameResolver.GetSongName(value);
                    case EventScript.ArgType.TEXT:
                    case EventScript.ArgType.CONVERSATION_TEXT:
                    case EventScript.ArgType.SYSTEM_TEXT:
                    case EventScript.ArgType.ONELINE_TEXT:
                        if (value == 0) return "";
                        string decoded = NameResolver.GetTextById(value);
                        if (decoded != null && decoded.Length > 40)
                            decoded = decoded.Substring(0, 40) + "...";
                        return decoded ?? "";
                    case EventScript.ArgType.PORTRAIT:
                    case EventScript.ArgType.REVPORTRAIT:
                        return NameResolver.GetUnitName(value); // portraits often map to unit IDs
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Event script disassembly viewer and command parameter editor.</summary>
    public class EventScriptPopupViewModel : ViewModelBase
    {
        string _infoText = "";
        bool _isLoaded;
        string _addressText = "";
        string _statusText = "";
        ObservableCollection<string> _commands = new();
        int _selectedCommandIndex = -1;
        string _selectedCommandName = "";
        bool _hasSelectedCommand;
        ObservableCollection<CommandArgEntry> _commandArgs = new();

        // Internal data: list of disassembled codes + their offsets
        readonly List<EventScript.OneCode> _disassembledCodes = new();
        readonly List<uint> _commandOffsets = new();
        uint _scriptBaseAddress;

        /// <summary>The script type this editor operates on (Event, Procs, or AI).</summary>
        public EventScript.EventScriptType ScriptType { get; set; } = EventScript.EventScriptType.Event;

        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AddressText { get => _addressText; set => SetField(ref _addressText, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public ObservableCollection<string> Commands { get => _commands; set => SetField(ref _commands, value); }

        /// <summary>Index of the selected command in the disassembly list.</summary>
        public int SelectedCommandIndex
        {
            get => _selectedCommandIndex;
            set
            {
                if (SetField(ref _selectedCommandIndex, value))
                    OnCommandSelected(value);
            }
        }

        /// <summary>Name of the currently selected command.</summary>
        public string SelectedCommandName
        {
            get => _selectedCommandName;
            set => SetField(ref _selectedCommandName, value);
        }

        /// <summary>True when a valid, editable command is selected.</summary>
        public bool HasSelectedCommand
        {
            get => _hasSelectedCommand;
            set => SetField(ref _hasSelectedCommand, value);
        }

        /// <summary>Arguments of the currently selected command.</summary>
        public ObservableCollection<CommandArgEntry> CommandArgs
        {
            get => _commandArgs;
            set => SetField(ref _commandArgs, value);
        }

        const int MaxCommands = 200;
        const int MaxConsecutiveUnknown = 10;

        public void Load()
        {
            switch (ScriptType)
            {
                case EventScript.EventScriptType.Procs:
                    InfoText =
                        "Procs Script Command Reference\n" +
                        "==============================\n\n" +
                        "Procs scripts manage process/coroutine execution for animations, menus,\n" +
                        "battle sequences, and UI flows.\n\n" +
                        "Common commands:\n" +
                        "  PROC_CALL      - Call a subroutine\n" +
                        "  PROC_GOTO      - Jump to another proc\n" +
                        "  PROC_YIELD     - Yield execution for one frame\n" +
                        "  PROC_SLEEP     - Sleep for N frames\n" +
                        "  PROC_MARK      - Set a label/mark\n" +
                        "  PROC_BLOCK     - Block until condition met\n" +
                        "  PROC_END       - End proc execution\n" +
                        "  PROC_START     - Start a child proc\n\n" +
                        "Enter a ROM address above and click Disassemble to parse Procs script commands.\n" +
                        "Select a command to view and edit its parameters.";
                    break;
                case EventScript.EventScriptType.AI:
                    InfoText =
                        "AI Script Command Reference\n" +
                        "===========================\n\n" +
                        "AI scripts control enemy unit behavior: movement, targeting, item usage,\n" +
                        "staff usage, stealing, and special actions.\n\n" +
                        "Common commands:\n" +
                        "  AI_MOVE        - Movement decision\n" +
                        "  AI_ATTACK      - Attack target selection\n" +
                        "  AI_HEAL        - Healing/staff usage\n" +
                        "  AI_ITEM        - Item usage decision\n" +
                        "  AI_STEAL       - Steal item logic\n" +
                        "  AI_ESCAPE      - Escape/retreat behavior\n" +
                        "  AI_END         - End AI script\n\n" +
                        "Enter a ROM address above and click Disassemble to parse AI script commands.\n" +
                        "Select a command to view and edit its parameters.";
                    break;
                default:
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
                        "Enter a ROM address above and click Disassemble to parse event script commands.\n" +
                        "Select a command to view and edit its parameters.";
                    break;
            }
            IsLoaded = true;
        }

        /// <summary>
        /// Disassemble event script commands starting at the given ROM address.
        /// </summary>
        public void DisassembleAt(uint address)
        {
            Commands.Clear();
            CommandArgs.Clear();
            HasSelectedCommand = false;
            SelectedCommandName = "";
            _disassembledCodes.Clear();
            _commandOffsets.Clear();
            StatusText = "";

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                StatusText = "Error: No ROM loaded.";
                return;
            }

            // Get or create the appropriate EventScript instance for the script type
            EventScript es;
            switch (ScriptType)
            {
                case EventScript.EventScriptType.Procs:
                    es = CoreState.ProcsScript;
                    break;
                case EventScript.EventScriptType.AI:
                    es = CoreState.AIScript;
                    break;
                default:
                    es = CoreState.EventScript;
                    break;
            }

            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = new EventScript();
                    es.Load(ScriptType);
                    // Cache for future use
                    switch (ScriptType)
                    {
                        case EventScript.EventScriptType.Procs:
                            CoreState.ProcsScript = es;
                            break;
                        case EventScript.EventScriptType.AI:
                            CoreState.AIScript = es;
                            break;
                        default:
                            CoreState.EventScript = es;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"Error loading {ScriptType} script definitions: {ex.Message}";
                    return;
                }
            }

            _scriptBaseAddress = address;
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

                // Store the code and its offset for later parameter editing
                _disassembledCodes.Add(code);
                _commandOffsets.Add(offset);

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
                    // Don't add end marker as a separate line — it confuses index mapping
                    break;
                }

                // Track consecutive unknowns
                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    consecutiveUnknown++;
                    if (consecutiveUnknown >= MaxConsecutiveUnknown)
                    {
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

            foreach (var line in lines)
            {
                Commands.Add(line);
            }

            StatusText = $"Disassembled {lines.Count} command(s) starting at 0x{U.toOffset(_scriptBaseAddress):X06}";
        }

        /// <summary>
        /// Called when the user selects a command in the list.
        /// Parses its arguments and populates CommandArgs.
        /// </summary>
        void OnCommandSelected(int index)
        {
            CommandArgs.Clear();
            HasSelectedCommand = false;
            SelectedCommandName = "";

            if (index < 0 || index >= _disassembledCodes.Count)
                return;

            var code = _disassembledCodes[index];
            if (code?.Script?.Args == null)
                return;

            SelectedCommandName = EventScript.makeCommandComboText(code.Script, false);
            HasSelectedCommand = true;

            foreach (var arg in code.Script.Args)
            {
                uint value = EventScript.GetArgValue(code, arg);

                var entry = new CommandArgEntry
                {
                    Name = arg.Name ?? arg.Symbol.ToString(),
                    ArgType = arg.Type,
                    ByteOffset = arg.Position,
                    ByteSize = arg.Size,
                    IsPointer = EventScript.IsPointerArgs(arg.Type),
                    IsDecimal = EventScript.IsDecimal(arg.Type),
                    IsFixed = (arg.Type == EventScript.ArgType.FIXED),
                    Value = value,
                    DisplayName = CommandArgEntry.ResolveDisplayName(arg.Type, value),
                };

                CommandArgs.Add(entry);
            }
        }

        /// <summary>
        /// Write the current parameter values back to ROM for the selected command.
        /// Returns true on success.
        /// </summary>
        public bool WriteCommand()
        {
            int index = SelectedCommandIndex;
            if (index < 0 || index >= _disassembledCodes.Count)
                return false;

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                StatusText = "Error: No ROM loaded.";
                return false;
            }

            var code = _disassembledCodes[index];
            uint cmdOffset = _commandOffsets[index];

            if (code?.Script?.Args == null)
                return false;

            // Validate that CommandArgs count matches non-alias args
            if (CommandArgs.Count != code.Script.Args.Length)
            {
                StatusText = "Error: Argument count mismatch.";
                return false;
            }

            // Use UndoService for tracked writes
            var undoService = new UndoService();
            undoService.Begin("Edit EventScript Command");

            try
            {
                for (int i = 0; i < CommandArgs.Count; i++)
                {
                    var entry = CommandArgs[i];
                    var arg = code.Script.Args[i];

                    // Skip FIXED args — they are constants, not editable
                    if (arg.Type == EventScript.ArgType.FIXED)
                        continue;

                    uint writeAddr = cmdOffset + (uint)arg.Position;
                    uint val = entry.Value;

                    switch (arg.Size)
                    {
                        case 1:
                            rom.write_u8(writeAddr, val & 0xFF);
                            break;
                        case 2:
                            rom.write_u16(writeAddr, val & 0xFFFF);
                            break;
                        case 3:
                            // Write 3 bytes manually
                            rom.write_u8(writeAddr, val & 0xFF);
                            rom.write_u8(writeAddr + 1, (val >> 8) & 0xFF);
                            rom.write_u8(writeAddr + 2, (val >> 16) & 0xFF);
                            break;
                        case 4:
                            rom.write_u32(writeAddr, val);
                            break;
                    }
                }

                undoService.Commit();

                // Re-read the command's byte data from ROM to refresh display
                var newByteData = U.getBinaryData(rom.Data, cmdOffset, code.Script.Size);
                code.ByteData = newByteData;

                // Refresh the display line for this command
                string cmdName = EventScript.makeCommandComboText(code.Script, false);
                string hexBytes = U.HexDumpLiner(newByteData).Trim();
                string line = $"0x{cmdOffset:X06}: {cmdName}  [{hexBytes}]";
                if (!string.IsNullOrEmpty(code.Comment))
                    line += $"  // {code.Comment}";

                if (index < Commands.Count)
                    Commands[index] = line;

                StatusText = $"Written {CommandArgs.Count} arg(s) at 0x{cmdOffset:X06}";
                return true;
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                StatusText = $"Error writing command: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Get the ROM offset of the currently selected command (for Jump navigation).
        /// Returns U.NOT_FOUND if no valid selection.
        /// </summary>
        public uint GetSelectedCommandOffset()
        {
            int index = SelectedCommandIndex;
            if (index < 0 || index >= _commandOffsets.Count)
                return U.NOT_FOUND;
            return _commandOffsets[index];
        }

        /// <summary>
        /// Get the pointer value of a specific argument (for Jump to pointer).
        /// </summary>
        public uint GetArgPointerValue(int argIndex)
        {
            if (argIndex < 0 || argIndex >= CommandArgs.Count)
                return U.NOT_FOUND;

            var entry = CommandArgs[argIndex];
            if (!entry.IsPointer || entry.ByteSize != 4)
                return U.NOT_FOUND;

            uint ptr = entry.Value;
            if (U.isSafetyPointer(ptr))
                return ptr;

            return U.NOT_FOUND;
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
