using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia "Add-via-ASM/C" tool (WF
    /// <c>ToolASMInsertForm</c>). Compiles a C/C++/ASM source through the devkitARM
    /// chain (gcc/g++/as → ELF → raw binary, or lyn → EA event) — or, for a legacy
    /// <c>@thumb</c> <c>.ASM</c> source, the GoldRoad assembler (auto-selected from
    /// the source content, mirroring WF <c>MainFormUtil.Compile</c>) — and inserts the
    /// result into the ROM via one of three methods (write-at-address, hook-inject,
    /// or compile-only).
    ///
    /// The actual compile+insert work lives in the GUI-free Core helper
    /// <see cref="AsmCompileCore"/>. This VM only holds the form fields and forwards
    /// to that helper. It reads no ROM bytes for display (it only WRITES via the
    /// helper) so it is a read-no-ROM tool VM (no data-verification contract).
    ///
    /// Scope: the devkitARM compile, the GoldRoad <c>@thumb</c> path (auto-detected,
    /// no extra UI), the three insert methods, and the "Make Patch" redistributable
    /// patch-definition text (#1243 — <see cref="MakePatchText"/>, opened in
    /// <c>AsmPatchTextView</c>) are the parity surface here.
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
        string _lastProductPath = "";
        bool _lastInsertWasPatchable;

        /// <summary>Selected C/C++/ASM source file to compile and insert.</summary>
        public string SourcePath { get => _sourcePath; set => SetField(ref _sourcePath, value); }

        /// <summary>Compile method index: 0 = dump binary, 1 = keep ELF, 2 = convert to lyn.event.</summary>
        public int CompileMethodIndex { get => _compileMethodIndex; set => SetField(ref _compileMethodIndex, value); }

        /// <summary>Insert method index: 0 = compile-only, 1 = write at address, 2 = hook-inject.</summary>
        public int InsertMethodIndex
        {
            get => _insertMethodIndex;
            set { if (SetField(ref _insertMethodIndex, value)) OnPropertyChanged(nameof(CanMakePatch)); }
        }

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

        /// <summary>
        /// Clamp a ComboBox SelectedIndex into [0, max] before casting to an enum: a
        /// ComboBox reports -1 when nothing is selected (e.g. mid-rebind), and a stray
        /// out-of-range value would cast to an undefined enum. Defaults to 0 (the first,
        /// safe item) in those cases.
        /// </summary>
        static int ClampIndex(int index, int max) => index < 0 ? 0 : (index > max ? max : index);

        /// <summary>The selected compile method for the Core helper.</summary>
        public AsmCompileCore.CompileMethod CompileMethod =>
            (AsmCompileCore.CompileMethod)ClampIndex(_compileMethodIndex, 2);

        /// <summary>The selected insert method for the Core helper.</summary>
        public AsmCompileCore.InsertMethod InsertMethod =>
            (AsmCompileCore.InsertMethod)ClampIndex(_insertMethodIndex, 2);

        /// <summary>The selected debug-symbol store for the Core helper.</summary>
        public SymbolUtil.DebugSymbol StoreSymbol =>
            (SymbolUtil.DebugSymbol)ClampIndex(_debugSymbolIndex, 3);

        /// <summary>True when the devkitARM EABI tool path is configured and exists.</summary>
        public bool IsDevkitProAvailable => AsmCompileCore.IsDevkitProAvailable();

        /// <summary>Localized "devkitpro_eabi not configured" message (for the UI).</summary>
        public string NotFoundMessage => AsmCompileCore.GetNotFoundMessage();

        /// <summary>True when the legacy GoldRoad assembler path is configured and exists.</summary>
        public bool IsGoldRoadAvailable => AsmCompileCore.IsGoldRoadAvailable();

        /// <summary>Localized "goldroad not configured" message (for the UI).</summary>
        public string GoldRoadNotFoundMessage => AsmCompileCore.GetGoldRoadNotFoundMessage();

        /// <summary>True when the selected source file exists on disk.</summary>
        public bool SourceExists => !string.IsNullOrEmpty(SourcePath) && File.Exists(SourcePath);

        /// <summary>
        /// True when the selected source is a legacy GoldRoad (<c>@thumb</c> <c>.ASM</c>)
        /// source — the assembler is auto-selected from the source content (WF
        /// <c>MainFormUtil.Compile</c>), so no compile-method UI is needed for it.
        /// </summary>
        public bool IsGoldRoadSource => SourceExists && AsmCompileCore.ShouldUseGoldRoad(SourcePath);

        /// <summary>
        /// The assembler the selected source will use is configured: GoldRoad for a
        /// <c>@thumb</c> <c>.ASM</c>, otherwise devkitARM. Used by the View to surface
        /// the right not-found message before running.
        /// </summary>
        public bool IsRequiredToolAvailable =>
            IsGoldRoadSource ? IsGoldRoadAvailable : IsDevkitProAvailable;

        /// <summary>The not-found message for whichever assembler the source requires.</summary>
        public string RequiredToolNotFoundMessage =>
            IsGoldRoadSource ? GoldRoadNotFoundMessage : NotFoundMessage;

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
        /// The compiled product (the .dmp/.bin) from the LAST successful run. The
        /// "Make Patch" action hex-dumps this product into the patch-definition text,
        /// mirroring how WF keeps <c>ComplieBinFilename</c> between Run and MakePatch.
        /// Empty until a compile succeeds.
        /// </summary>
        public string LastProductPath
        {
            get => _lastProductPath;
            set { if (SetField(ref _lastProductPath, value)) OnPropertyChanged(nameof(CanMakePatch)); }
        }

        /// <summary>
        /// True when the last successful run produced a raw, writable binary (i.e. NOT
        /// a compile-only / lyn.event product). A lyn.event is a script, not bytes, so
        /// no BIN-type patch can describe it — matching WF, where MakePatch only runs
        /// for the write-at-address / hook-inject methods.
        /// </summary>
        public bool LastInsertWasPatchable
        {
            get => _lastInsertWasPatchable;
            set { if (SetField(ref _lastInsertWasPatchable, value)) OnPropertyChanged(nameof(CanMakePatch)); }
        }

        /// <summary>
        /// True when a "Make Patch" can be produced: a successful compile left a real
        /// product on disk AND the CURRENT insert method is not compile-only (WF shows
        /// PatchMakerButton only for Method 1/2 and MakePatch needs a compiled bin).
        /// </summary>
        public bool CanMakePatch =>
            LastInsertWasPatchable
            && !string.IsNullOrEmpty(LastProductPath)
            && System.IO.File.Exists(LastProductPath)
            && InsertMethod != AsmCompileCore.InsertMethod.CompileOnly;

        /// <summary>
        /// Build the redistributable patch-definition text for the last successful
        /// compile, reading the CURRENT address / free-area / hook-register fields (WF
        /// MakePatch reads the live UI fields + the last compiled bin). Returns "" when
        /// there is nothing to patch (no product, compile-only, or a zero address); the
        /// caller surfaces a localized "nothing to make a patch from" message.
        /// </summary>
        public string MakePatchText()
        {
            uint addr = U.toOffset(ParseHex(AddressHex));
            uint freeArea = U.toOffset(ParseHex(FreeAreaHex));
            return AsmCompileCore.MakePatchText(LastProductPath, InsertMethod, addr, freeArea, HookRegister);
        }

        /// <summary>
        /// Compile and insert <see cref="SourcePath"/> using the shared Core helper.
        /// The caller owns the undo scope and passes its active <c>Undo.UndoData</c>.
        /// On success, records the product path + whether it is patchable so a later
        /// "Make Patch" can reuse it (mirrors WF keeping <c>ComplieBinFilename</c>).
        /// </summary>
        public AsmCompileCore.CompileResult Run(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            uint addr = U.toOffset(ParseHex(AddressHex));
            uint freeArea = U.toOffset(ParseHex(FreeAreaHex));
            var result = AsmCompileCore.CompileAndInsert(
                rom, SourcePath, CompileMethod, InsertMethod,
                addr, freeArea, HookRegister, StoreSymbol, CheckMissingLabel, undo);

            if (result.Success)
            {
                LastProductPath = result.ProductPath ?? "";
                // A lyn.event product (ConvertLyn, non-GoldRoad) is a script, not raw
                // bytes — it cannot back a BIN patch. Everything else (dump/keep-ELF, or
                // a GoldRoad .bin even under the ConvertLyn method) is a writable binary.
                bool isLynProduct = CompileMethod == AsmCompileCore.CompileMethod.ConvertLyn
                    && !AsmCompileCore.ShouldUseGoldRoad(SourcePath);
                LastInsertWasPatchable = !isLynProduct;
            }

            return result;
        }
    }
}
