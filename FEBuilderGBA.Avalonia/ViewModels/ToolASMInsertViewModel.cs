using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia "Add-via-ASM/C" tool (WF
    /// <c>ToolASMInsertForm</c>). Compiles a C/C++/ASM source through the devkitARM
    /// chain (gcc/g++/as → ELF → raw binary, or lyn → EA event) and inserts the
    /// result into the ROM via one of three methods (write-at-address, hook-inject,
    /// or compile-only).
    ///
    /// The actual compile+insert work lives in the GUI-free Core helper
    /// <see cref="AsmCompileCore"/>. This VM only holds the form fields and forwards
    /// to that helper. It reads no ROM bytes for display (it only WRITES via the
    /// helper) so it is a read-no-ROM tool VM (no data-verification contract).
    ///
    /// Scope: the GoldRoad assembler path and the "Make Patch" text generator are
    /// deferred to a follow-up (both WinForms-coupled); the devkitARM compile +
    /// the three insert methods are the parity surface here.
    /// </summary>
    public class ToolASMInsertViewModel : ViewModelBase
    {
        string _sourcePath = "";
        int _compileMethodIndex;            // 0 DumpBinary, 1 KeepElf, 2 ConvertLyn
        int _insertMethodIndex;             // 0 CompileOnly, 1 WriteAtAddress, 2 HookInject
        string _addressHex = "0x00000000";
        string _freeAreaHex = "0x00000000";
        int _hookRegisterIndex = 3;         // WF ctor: HookRegister.SelectedIndex = 3 (r3)
        int _debugSymbolIndex = 3;          // WF ctor: DebugSymbolComboBox.SelectedIndex = 3 (SaveBoth)
        bool _checkMissingLabel = true;     // WF ctor: CheckMissingLabelComboBox.SelectedIndex = 1 (on)
        bool _canUndo;
        string _statusMessage = "";

        /// <summary>Selected C/C++/ASM source file to compile and insert.</summary>
        public string SourcePath { get => _sourcePath; set => SetField(ref _sourcePath, value); }

        /// <summary>Compile method index: 0 = dump binary, 1 = keep ELF, 2 = convert to lyn.event.</summary>
        public int CompileMethodIndex { get => _compileMethodIndex; set => SetField(ref _compileMethodIndex, value); }

        /// <summary>Insert method index: 0 = compile-only, 1 = write at address, 2 = hook-inject.</summary>
        public int InsertMethodIndex { get => _insertMethodIndex; set => SetField(ref _insertMethodIndex, value); }

        /// <summary>Target address (hex), used by write-at-address and hook-inject.</summary>
        public string AddressHex { get => _addressHex; set => SetField(ref _addressHex, value); }

        /// <summary>Free-area address (hex), used by hook-inject for the routine body.</summary>
        public string FreeAreaHex { get => _freeAreaHex; set => SetField(ref _freeAreaHex, value); }

        /// <summary>Hook register index (r0..r8) for the injected thumb jump.</summary>
        public int HookRegisterIndex { get => _hookRegisterIndex; set => SetField(ref _hookRegisterIndex, value); }

        /// <summary>Debug-symbol store index (maps to <c>SymbolUtil.DebugSymbol</c>).</summary>
        public int DebugSymbolIndex { get => _debugSymbolIndex; set => SetField(ref _debugSymbolIndex, value); }

        /// <summary>When true, a reference to a missing label aborts the compile (WF check).</summary>
        public bool CheckMissingLabel { get => _checkMissingLabel; set => SetField(ref _checkMissingLabel, value); }

        /// <summary>True when an applied insert can be undone.</summary>
        public bool CanUndo { get => _canUndo; set => SetField(ref _canUndo, value); }

        /// <summary>Result / error text shown in the status area.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>The selected compile method for the Core helper.</summary>
        public AsmCompileCore.CompileMethod CompileMethod =>
            (AsmCompileCore.CompileMethod)_compileMethodIndex;

        /// <summary>The selected insert method for the Core helper.</summary>
        public AsmCompileCore.InsertMethod InsertMethod =>
            (AsmCompileCore.InsertMethod)_insertMethodIndex;

        /// <summary>The selected debug-symbol store for the Core helper.</summary>
        public SymbolUtil.DebugSymbol StoreSymbol =>
            (SymbolUtil.DebugSymbol)_debugSymbolIndex;

        /// <summary>True when the devkitARM EABI tool path is configured and exists.</summary>
        public bool IsDevkitProAvailable => AsmCompileCore.IsDevkitProAvailable();

        /// <summary>Localized "devkitpro_eabi not configured" message (for the UI).</summary>
        public string NotFoundMessage => AsmCompileCore.GetNotFoundMessage();

        /// <summary>True when the selected source file exists on disk.</summary>
        public bool SourceExists => !string.IsNullOrEmpty(SourcePath) && File.Exists(SourcePath);

        /// <summary>
        /// Parse a hex address field. Accepts "0x..." or bare hex; returns 0 on a
        /// blank/invalid value (the WF NumericUpDown defaults to 0).
        /// </summary>
        public static uint ParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string s = text.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint v) ? v : 0u;
        }

        /// <summary>The hook register value (r0..r8) from the selected index.</summary>
        public uint HookRegister => AsmCompileCore.ClampHookRegister((uint)Math.Max(0, _hookRegisterIndex));

        /// <summary>
        /// Compile and insert <see cref="SourcePath"/> using the shared Core helper.
        /// The caller owns the undo scope and passes its active <c>Undo.UndoData</c>.
        /// </summary>
        public AsmCompileCore.CompileResult Run(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            uint addr = U.toOffset(ParseHex(AddressHex));
            uint freeArea = U.toOffset(ParseHex(FreeAreaHex));
            return AsmCompileCore.CompileAndInsert(
                rom, SourcePath, CompileMethod, InsertMethod,
                addr, freeArea, HookRegister, StoreSymbol, CheckMissingLabel, undo);
        }
    }
}
