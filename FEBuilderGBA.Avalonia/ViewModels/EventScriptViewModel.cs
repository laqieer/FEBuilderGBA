using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Event / Procs / AI script editor view-model. Backed by the cross-platform
    /// <see cref="EventScriptEditorCore"/> engine (#1435), so beyond the read-only
    /// disassembly viewer it now supports structural authoring: insert (from the
    /// command catalog or raw hex), delete, move up/down, import-from-text, and a
    /// Write-All that re-serializes the resized script and writes it back under one
    /// undo scope (in place when it fits, else relocate + repoint).
    /// </summary>
    public class EventScriptViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _addressText = "";
        string _statusText = "";
        string _disassembledText = "";
        ObservableCollection<string> _commands = new();
        int _selectedCommandIndex = -1;

        // Structural-editing engine (null until a script type is bound + disassembled).
        EventScriptEditorCore _editor;
        EventScript _es;
        bool _dirty;

        // Insert support: the command catalog (names) + raw-hex entry.
        ObservableCollection<string> _availableCommands = new();
        int _selectedCommandCatalogIndex = -1;
        string _insertHexText = "";
        string _importText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AddressText { get => _addressText; set => SetField(ref _addressText, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        /// <summary>The script type this editor operates on (Event, Procs, or AI).</summary>
        public EventScript.EventScriptType ScriptType { get; set; } = EventScript.EventScriptType.Event;

        // ── event-kind state ───────────────────────────────────────────
        // The active kind is what the CURRENTLY-LOADED script was scanned with; it drives
        // WriteAll's terminator selection. Because EventScriptView is a CACHED singleton
        // (WindowManager.Open<T> returns the same instance), the kind MUST NOT persist
        // across openings or a world-map/top-level jump would leak its kind into a later
        // normal-script open and append the wrong terminator (Copilot PR review #1510 —
        // ROM-corruption risk). So the kind is applied ONE-SHOT: SetEventKind() stages a
        // pending kind for the NEXT DisassembleAt only; DisassembleAt consumes it and then
        // resets the pending kind to the chapter-event default. A manual Disassemble (user
        // typing an address) therefore always uses the default unless a jump set a pending
        // kind immediately beforehand.

        bool _pendingWorldMap;
        bool _pendingTopLevel;

        /// <summary>
        /// The kind the CURRENTLY-LOADED script was scanned with (set by the last
        /// <see cref="DisassembleAt"/>). Read by <see cref="WriteAll"/> for terminator
        /// selection. Settable for tests; production code stages the kind via
        /// <see cref="StageEventKind"/>.
        /// </summary>
        public bool IsWorldMapEvent { get; set; }

        /// <summary>The top-level kind the currently-loaded script was scanned with
        /// (see <see cref="IsWorldMapEvent"/>). Ignored when world-map wins.</summary>
        public bool IsTopLevelEvent { get; set; }

        /// <summary>
        /// Stage the event kind for the NEXT disassembly only (one-shot). A world-map /
        /// top-level jump calls this immediately before <c>NavigateTo</c>; the very next
        /// <see cref="DisassembleAt"/> applies it and then clears the pending kind so a
        /// later reuse of this cached editor for a normal script defaults to chapter-event
        /// semantics. (Copilot PR review #1510 — prevents stale-kind leak.)
        /// </summary>
        public void StageEventKind(bool isWorldMapEvent, bool isTopLevelEvent)
        {
            _pendingWorldMap = isWorldMapEvent;
            _pendingTopLevel = isTopLevelEvent;
        }

        /// <summary>True when there are unsaved structural edits (mirrors the WinForms
        /// "yellow write button" dirty flag).</summary>
        public bool IsDirty { get => _dirty; set => SetField(ref _dirty, value); }

        /// <summary>Full disassembled script text for display in a read-only TextBox.</summary>
        public string DisassembledText { get => _disassembledText; set => SetField(ref _disassembledText, value); }

        /// <summary>List of command line strings for the ListBox (rebuilt from the engine).</summary>
        public ObservableCollection<string> Commands { get => _commands; set => SetField(ref _commands, value); }

        public int SelectedCommandIndex
        {
            get => _selectedCommandIndex;
            set => SetField(ref _selectedCommandIndex, value);
        }

        /// <summary>Names of every command in the loaded script vocabulary (for the
        /// Insert command picker). Populated on disassemble.</summary>
        public ObservableCollection<string> AvailableCommands { get => _availableCommands; set => SetField(ref _availableCommands, value); }

        /// <summary>Index of the command chosen in the Insert command-picker.</summary>
        public int SelectedCommandCatalogIndex { get => _selectedCommandCatalogIndex; set => SetField(ref _selectedCommandCatalogIndex, value); }

        /// <summary>Raw hex text for the "Insert (hex)" path (e.g. "0100 4200").</summary>
        public string InsertHexText { get => _insertHexText; set => SetField(ref _insertHexText, value); }

        /// <summary>Multi-line hex text for the Import path.</summary>
        public string ImportText { get => _importText; set => SetField(ref _importText, value); }

        const int MaxCommands = 200;

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
        /// Resolve (or lazily build) the <see cref="EventScript"/> instance for the
        /// current <see cref="ScriptType"/>, mirroring the popup VM's caching logic.
        /// Returns null on failure (status text is set).
        /// </summary>
        EventScript ResolveEventScript()
        {
            EventScript es;
            switch (ScriptType)
            {
                case EventScript.EventScriptType.Procs: es = CoreState.ProcsScript; break;
                case EventScript.EventScriptType.AI: es = CoreState.AIScript; break;
                default: es = CoreState.EventScript; break;
            }

            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = ScriptType == EventScript.EventScriptType.AI
                        ? new EventScript(16)
                        : new EventScript();
                    es.Load(ScriptType);
                    switch (ScriptType)
                    {
                        case EventScript.EventScriptType.Procs: CoreState.ProcsScript = es; break;
                        case EventScript.EventScriptType.AI: CoreState.AIScript = es; break;
                        default: CoreState.EventScript = es; break;
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"Error loading {ScriptType} script definitions: {ex.Message}";
                    return null;
                }
            }
            return es;
        }

        /// <summary>
        /// Disassemble the script at the given address into the editable engine and
        /// refresh the display.
        /// </summary>
        public void DisassembleAt(uint address)
        {
            Commands.Clear();
            DisassembledText = "";
            StatusText = "";

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null)
            {
                StatusText = "Error: No ROM loaded.";
                return;
            }

            _es = ResolveEventScript();
            if (_es == null) return;

            uint offset = U.toOffset(address);
            if (offset >= (uint)rom.Data.Length)
            {
                StatusText = $"Error: Address 0x{address:X08} is outside ROM bounds.";
                return;
            }

            // Apply the ONE-SHOT staged kind (set by a jump just before navigation), then
            // clear it so a later reuse of this cached editor defaults to chapter-event
            // semantics — preventing a world-map/top-level kind from leaking into a normal
            // open (Copilot PR review #1510).
            IsWorldMapEvent = _pendingWorldMap;
            IsTopLevelEvent = _pendingTopLevel;
            _pendingWorldMap = false;
            _pendingTopLevel = false;

            _editor = new EventScriptEditorCore(_es);
            _editor.BuildFromRom(rom, offset, IsWorldMapEvent);

            // Set CurrentAddr BEFORE RefreshDisplay — RefreshDisplay formats every command's
            // offset column from CurrentAddr, so a stale value would render wrong offsets on
            // the very first disassembly / on re-navigation (Copilot PR review #1510 finding #1).
            CurrentAddr = address;

            BuildCommandCatalog();
            RefreshDisplay();

            IsDirty = false;
            StatusText = $"Disassembled {_editor.Count} command(s) at 0x{offset:X06}";
        }

        void BuildCommandCatalog()
        {
            AvailableCommands.Clear();
            if (_es?.Scripts == null) return;
            foreach (var sc in _es.Scripts)
            {
                if (sc == null) continue;
                AvailableCommands.Add(EventScript.makeCommandComboText(sc, false));
            }
        }

        /// <summary>Rebuild the <see cref="Commands"/> list + <see cref="DisassembledText"/>
        /// from the current editor state. Offsets are recomputed from the base address.</summary>
        void RefreshDisplay()
        {
            Commands.Clear();
            var sb = new StringBuilder();
            if (_editor == null)
            {
                DisassembledText = "";
                return;
            }

            uint offset = U.toOffset(CurrentAddr);
            for (int i = 0; i < _editor.Count; i++)
            {
                var code = _editor.Codes[i];
                string indent = new string(' ', (int)Math.Min(code.JisageCount, 8) * 2);
                string cmdName = EventScript.makeCommandComboText(code.Script, false);
                string hexBytes = U.HexDumpLiner(code.ByteData).Trim();
                string line = $"0x{offset:X06}: {indent}{cmdName}  [{hexBytes}]";
                if (!string.IsNullOrEmpty(code.Comment))
                    line += $"  // {code.Comment}";
                Commands.Add(line);
                sb.AppendLine(line);

                uint size = (uint)code.Script.Size;
                if (size == 0) size = 4;
                offset += size;
            }
            DisassembledText = sb.ToString();
        }

        // ── structural-edit operations ─────────────────────────────────

        /// <summary>Insert the command currently chosen in the catalog
        /// (<see cref="SelectedCommandCatalogIndex"/>) relative to the selected list row.</summary>
        public bool InsertSelectedCatalogCommand()
        {
            if (_editor == null || _es?.Scripts == null) { StatusText = "Disassemble a script first."; return false; }
            int catalog = SelectedCommandCatalogIndex;
            if (catalog < 0 || catalog >= _es.Scripts.Length) { StatusText = "Choose a command to insert."; return false; }

            var code = _editor.NewCodeFromScript(_es.Scripts[catalog]);
            int sel = _editor.Insert(SelectedCommandIndex, code);
            AfterEdit(sel, "Inserted command.");
            return true;
        }

        /// <summary>Insert a command parsed from the raw-hex entry box.</summary>
        public bool InsertHexCommand()
        {
            if (_editor == null) { StatusText = "Disassemble a script first."; return false; }
            // Strip whitespace so the watermark example "0100 4200" works — LineToEventByte
            // stops at the first non-hex char (faithful to WinForms for the line-based text
            // import), but the single-field Insert box should be whitespace-tolerant
            // (Copilot PR review inline #4).
            string hex = new string((InsertHexText ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            byte[] bytes = EventScriptEditorCore.LineToEventByte(hex);
            if (bytes.Length < 4) { StatusText = "Enter at least 4 hex bytes (e.g. 0100 4200)."; return false; }

            var code = _editor.NewCodeFromBytes(bytes);
            int sel = _editor.Insert(SelectedCommandIndex, code);
            AfterEdit(sel, "Inserted command from hex.");
            return true;
        }

        /// <summary>Insert a whole template (a list of codes) at the selection.</summary>
        public bool InsertTemplate(IList<EventScript.OneCode> codes)
        {
            if (_editor == null) { StatusText = "Disassemble a script first."; return false; }
            if (codes == null || codes.Count == 0) { StatusText = "Template is empty."; return false; }
            int sel = _editor.InsertRange(SelectedCommandIndex, codes);
            AfterEdit(sel, $"Inserted template ({codes.Count} command(s)).");
            return true;
        }

        /// <summary>Delete the selected command.</summary>
        public bool DeleteSelected()
        {
            if (_editor == null || SelectedCommandIndex < 0) { StatusText = "Select a command to delete."; return false; }
            int sel = _editor.Delete(SelectedCommandIndex);
            AfterEdit(sel, "Deleted command.");
            return true;
        }

        /// <summary>Move the selected command up one row.</summary>
        public bool MoveSelectedUp()
        {
            if (_editor == null || SelectedCommandIndex < 1) return false;
            int sel = _editor.MoveUp(SelectedCommandIndex);
            AfterEdit(sel, "Moved command up.");
            return true;
        }

        /// <summary>Move the selected command down one row.</summary>
        public bool MoveSelectedDown()
        {
            if (_editor == null || SelectedCommandIndex < 0) return false;
            int sel = _editor.MoveDown(SelectedCommandIndex);
            AfterEdit(sel, "Moved command down.");
            return true;
        }

        /// <summary>Import commands from the multi-line hex <see cref="ImportText"/>.</summary>
        public bool ImportFromText(bool clear)
        {
            if (_editor == null) { StatusText = "Disassemble a script first."; return false; }
            int n = _editor.ImportFromText(ImportText ?? "", SelectedCommandIndex, clear);
            AfterEdit(Math.Min(SelectedCommandIndex, _editor.Count - 1), $"Imported {n} command(s).");
            return n > 0;
        }

        void AfterEdit(int newSelection, string status)
        {
            RefreshDisplay();
            SelectedCommandIndex = newSelection;
            IsDirty = true;
            StatusText = status;
        }

        /// <summary>
        /// Serialize the edited list and write it back to ROM at the current base address
        /// under one undo scope (delegates to <see cref="EventScriptEditorCore.WriteAll"/>).
        /// Returns true on a successful write.
        /// </summary>
        public bool WriteAll()
        {
            if (_editor == null || _editor.Count <= 0) { StatusText = "Nothing to write."; return false; }
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) { StatusText = "Error: No ROM loaded."; return false; }

            uint addr = U.toOffset(CurrentAddr);

            // WriteAll opens its OWN ambient ROM.BeginUndoScope(undo). ROM.BeginUndoScope
            // is thread-static and non-reentrant, so we must NOT also call
            // UndoService.Begin() here (that would nest a second scope — Copilot review
            // finding #3). Instead build the UndoData directly, let WriteAll record into
            // it, then push it via CommitExternal (the documented "tool passes UndoData
            // explicitly to the Core helper" pattern).
            var undoService = new UndoService();
            Undo undoMgr = CoreState.Undo;
            Undo.UndoData undo = undoMgr != null
                ? undoMgr.NewUndoData("Event Script Write All")
                : new Undo.UndoData { name = "Event Script Write All", list = new List<Undo.UndoPostion>() };

            try
            {
                var result = _editor.WriteAll(rom, addr, IsWorldMapEvent,
                    IsTopLevelEvent, undo, out uint newAddr);

                switch (result)
                {
                    case EventScriptEditorCore.WriteResult.InPlace:
                    case EventScriptEditorCore.WriteResult.Relocated:
                        undoService.CommitExternal(undo);
                        CurrentAddr = newAddr;
                        AddressText = $"0x{U.toPointer(newAddr):X08}";
                        IsDirty = false;
                        ReDisassembleAfterWrite(newAddr);
                        StatusText = result == EventScriptEditorCore.WriteResult.InPlace
                            ? $"Wrote script in place at 0x{newAddr:X06}."
                            : $"Relocated and wrote script to 0x{newAddr:X06} (references repointed).";
                        return true;

                    case EventScriptEditorCore.WriteResult.NoReferenceRefused:
                        // WriteAll already rolled back any stray writes and left the ROM
                        // unchanged (undo.list is empty); nothing to push.
                        StatusText = "Refused: the grown script must relocate but NO reference to its " +
                                     "base address was found. The script may be reached via an event-table / " +
                                     "struct / hardcoded path; relocating would orphan it. ROM unchanged.";
                        return false;

                    case EventScriptEditorCore.WriteResult.NoFreeSpace:
                        StatusText = "Could not find free space for the grown script. ROM unchanged.";
                        return false;

                    case EventScriptEditorCore.WriteResult.UnsafeAddress:
                        StatusText = "Unsafe target address: must be 4-byte aligned and within the " +
                                     "safe ROM range (>= 0x200, not the header/BIOS). ROM unchanged.";
                        return false;

                    default:
                        StatusText = "Nothing was written (empty list or invalid address).";
                        return false;
                }
            }
            catch (Exception ex)
            {
                // WriteAll restores byte-identical on a fault; undo.list is rolled back.
                StatusText = $"Error writing script: {ex.Message}";
                return false;
            }
        }

        void ReDisassembleAfterWrite(uint newOffset)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _es == null) return;
            _editor = new EventScriptEditorCore(_es);
            _editor.BuildFromRom(rom, newOffset, IsWorldMapEvent);
            RefreshDisplay();
        }

        /// <summary>Number of commands currently in the editable list (for tests/UI gating).</summary>
        public int CommandCount => _editor?.Count ?? 0;

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
