using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia "Add-via-Event-Assembler" tool (WF
    /// <c>EventAssemblerForm</c>). Assembles/inserts an EA event script into the
    /// ROM via ColorzCore / Event-Assembler, with free-area selection
    /// (Program / Data / None), a debug-symbol store choice and undo. (Uninstall is
    /// deferred to a follow-up; revert an applied insert via undo for now.)
    ///
    /// The actual compile+insert work lives in the GUI-free Core helper
    /// <c>EventAssemblerCompileCore</c> (shared with the CLI
    /// <c>--compile-event</c>). This VM only holds the form fields and forwards
    /// to that helper. It reads no ROM bytes for display (it only WRITES via the
    /// helper) so it is a read-no-ROM tool VM (no data-verification contract).
    /// </summary>
    public class EventAssemblerViewModel : ViewModelBase
    {
        string _sourcePath = "";
        int _freeAreaIndex;                 // 0 Program, 1 Data, 2 None (WF FREEAREA_DEF_ENUM)
        int _debugSymbolIndex = 3;          // WF ctor: DebugSymbolComboBox.SelectedIndex = 3 (SaveBoth)
        bool _autoReCompile;
        bool _hasResult;
        bool _canUndo;
        string _statusMessage = "";

        /// <summary>Selected .event/.txt file to compile and insert.</summary>
        public string SourcePath { get => _sourcePath; set => SetField(ref _sourcePath, value); }

        /// <summary>Free-area mode index: 0 = Program, 1 = Data, 2 = None.</summary>
        public int FreeAreaIndex { get => _freeAreaIndex; set => SetField(ref _freeAreaIndex, value); }

        /// <summary>Debug-symbol store index (maps to <c>SymbolUtil.DebugSymbol</c>).</summary>
        public int DebugSymbolIndex { get => _debugSymbolIndex; set => SetField(ref _debugSymbolIndex, value); }

        /// <summary>
        /// AutoReCompile: rebuild updated .s/.asm sources before assembling
        /// (WF <c>RunAutoReCompile</c>). The asm rebuild engine is WinForms-only
        /// (devkitARM), so when this is checked the View notes it is unavailable
        /// in this build — the EA assemble step still runs.
        /// </summary>
        public bool AutoReCompile { get => _autoReCompile; set => SetField(ref _autoReCompile, value); }

        /// <summary>True once a compile has been applied (controls Undo button visibility).</summary>
        public bool HasResult { get => _hasResult; set => SetField(ref _hasResult, value); }

        /// <summary>True when an applied insert can be undone.</summary>
        public bool CanUndo { get => _canUndo; set => SetField(ref _canUndo, value); }

        /// <summary>Result / error text shown in the status area.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>The selected free-area mode for the Core helper.</summary>
        public EventAssemblerCompileCore.FreeAreaMode Mode =>
            (EventAssemblerCompileCore.FreeAreaMode)_freeAreaIndex;

        /// <summary>The selected debug-symbol store for the Core helper.</summary>
        public SymbolUtil.DebugSymbol StoreSymbol =>
            (SymbolUtil.DebugSymbol)_debugSymbolIndex;

        /// <summary>True when an EA/ColorzCore executable can be resolved.</summary>
        public bool IsEventAssemblerAvailable =>
            !string.IsNullOrEmpty(EventAssemblerCompileCore.ResolveExe());

        /// <summary>Localized "not found" message (for surfacing in the UI).</summary>
        public string NotFoundMessage => EventAssemblerCompileCore.GetNotFoundMessage();

        /// <summary>True when the selected source file exists on disk.</summary>
        public bool SourceExists => !string.IsNullOrEmpty(SourcePath) && File.Exists(SourcePath);

        /// <summary>
        /// Compile and insert <see cref="SourcePath"/> using the shared Core helper.
        /// The caller owns the undo scope (Begin/Commit) and passes its active
        /// <c>Undo.UndoData</c>.
        /// </summary>
        public EventAssemblerCompileCore.CompileResult Import(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            return EventAssemblerCompileCore.CompileAndInsert(
                rom, SourcePath, Mode, undo, StoreSymbol);
        }

        /// <summary>
        /// Build the EA arguments without running anything (for preview / status).
        /// Returns an empty string when no ROM or no exe is available.
        /// </summary>
        public string PreviewArgs()
        {
            ROM rom = CoreState.ROM;
            string exe = EventAssemblerCompileCore.ResolveExe();
            if (rom?.RomInfo == null || string.IsNullOrEmpty(exe))
                return "";
            bool isColorz = ToolPathResolver.IsColorzCore(exe);
            return EventAssemblerCompileCore.BuildArgs(
                rom.RomInfo.TitleToFilename, "<wrapper>.event", "<rom>.gba", "<sym>", isColorz);
        }
    }
}
