using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Add-via-ASM/C tool (WF <c>ToolASMInsertForm</c>). Compiles a C/C++/ASM source
    /// through the devkitARM chain — or, for a legacy <c>@thumb</c> <c>.ASM</c> source,
    /// the GoldRoad assembler (auto-selected from the source content, no extra UI) —
    /// and inserts the result into the ROM via the GUI-free Core helper
    /// <see cref="AsmCompileCore"/>, with a compile method (dump binary / keep ELF /
    /// convert to lyn.event), an insert method (compile-only / write-at-address /
    /// hook-inject), a hook register, a debug-symbol store choice, a missing-label
    /// check and undo.
    ///
    /// Combo items are added in code via R._() so they pick up ja/zh translations
    /// (ViewTranslationHelper does not translate ComboBoxItem content).
    ///
    /// The "Make Patch" text generator is deferred to a follow-up issue
    /// (WinForms-coupled); an applied insert can be reverted via Undo.
    /// </summary>
    public partial class ToolASMInsertView : TranslatedWindow, IEditorView
    {
        readonly ToolASMInsertViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Add via ASM/C";
        public bool IsLoaded => true;

        public ToolASMInsertView()
        {
            InitializeComponent();
            DataContext = _vm;

            // Compile methods (mirror WF ELFComboBox; default index 0 = dump binary).
            CompileMethodCombo.Items.Add(R._("ELF -> raw binary (delete ELF)"));
            CompileMethodCombo.Items.Add(R._("ELF -> raw binary (keep ELF)"));
            CompileMethodCombo.Items.Add(R._("Convert to lyn.event (compile-only)"));

            // Insert methods (mirror WF Method combo; default index 0 = compile-only).
            InsertMethodCombo.Items.Add(R._("Make file only (do not write to the ROM)"));
            InsertMethodCombo.Items.Add(R._("Write at the address"));
            InsertMethodCombo.Items.Add(R._("Hook the address (jump to a free area)"));

            // Hook registers r0..r8 (mirror WF HookRegister; default index 3 = r3).
            for (int i = 0; i <= 8; i++)
                HookRegisterCombo.Items.Add("r" + i);

            // Debug-symbol store (mirror WF DebugSymbolComboBox; default index 3).
            DebugSymbolCombo.Items.Add(R._("None"));
            DebugSymbolCombo.Items.Add(R._("Save sym.txt"));
            DebugSymbolCombo.Items.Add(R._("Save as comment"));
            DebugSymbolCombo.Items.Add(R._("Save both"));

            CompileMethodCombo.SelectedIndex = _vm.CompileMethodIndex;
            InsertMethodCombo.SelectedIndex = _vm.InsertMethodIndex;
            HookRegisterCombo.SelectedIndex = _vm.HookRegisterIndex;
            DebugSymbolCombo.SelectedIndex = _vm.DebugSymbolIndex;

            UpdateInsertMethodVisibility();

            Opened += (_, _) =>
            {
                // Surface a clear message up front if devkitARM is not configured.
                if (!_vm.IsDevkitProAvailable)
                    _vm.StatusMessage = _vm.NotFoundMessage;
            };
        }

        /// <summary>
        /// Show/hide the address / free-area / hook-register rows per the insert
        /// method (mirrors WF <c>Method_SelectedIndexChanged</c>):
        ///   0 compile-only → none; 1 write-at-address → address only;
        ///   2 hook-inject → address (hook site) + free area + hook register.
        /// </summary>
        void UpdateInsertMethodVisibility()
        {
            int idx = InsertMethodCombo.SelectedIndex;
            bool isWrite = idx == 1;
            bool isHook = idx == 2;

            AddressRow.IsVisible = isWrite || isHook;
            FreeAreaRow.IsVisible = isHook;
            HookRegisterRow.IsVisible = isHook;

            AddressLabel.Text = isHook ? R._("Hook address:") : R._("Write to:");
        }

        void InsertMethod_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateInsertMethodVisibility();
        }

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            await BrowseForSourceAsync();
        }

        /// <summary>
        /// Show the source picker and store the chosen path on the VM. Returns true
        /// if the user picked a usable file. Awaitable so Run can pick-then-continue
        /// in the same invocation (mirrors WF RunButton/AllowDropFilename).
        /// </summary>
        async Task<bool> BrowseForSourceAsync()
        {
            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return false;

            try
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Please select a source file to import."),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(R._("source file"))
                            { Patterns = new[] { "*.c", "*.cpp", "*.s", "*.asm" } },
                        new FilePickerFileType("asm") { Patterns = new[] { "*.s", "*.asm" } },
                        new FilePickerFileType("c") { Patterns = new[] { "*.c", "*.cpp" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                {
                    string? path = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        _vm.SourcePath = path;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolASMInsertView.Browse failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
            return false;
        }

        async void Run_Click(object? sender, RoutedEventArgs e)
        {
            // Prompt for a file if none chosen yet, then CONTINUE the run in the same
            // action once a file is picked (mirrors WF AllowDropFilename auto-run).
            if (!_vm.SourceExists)
            {
                if (!await BrowseForSourceAsync() || !_vm.SourceExists)
                    return; // user cancelled / no usable file
            }
            // The assembler is auto-selected from the source content (devkitARM, or
            // GoldRoad for a @thumb .ASM) — surface the not-found message for whichever
            // one this specific source requires (mirrors WF MainFormUtil.Compile).
            if (!_vm.IsRequiredToolAvailable)
            {
                _vm.StatusMessage = _vm.RequiredToolNotFoundMessage;
                return;
            }

            RunButton.IsEnabled = false;
            _vm.StatusMessage = R._("Compiling...");

            // Use an EXPLICIT UndoData passed through to the Core helper rather than
            // the thread-local ambient ROM.BeginUndoScope: the compile+insert runs on
            // a background thread (Task.Run), and the ambient scope is thread-local to
            // the UI thread. The Core helper records diffs directly into this passed
            // UndoData, so undo capture stays correct and thread-consistent. We push it
            // (UI thread) only after a successful insert, via UndoService.CommitExternal
            // which also refreshes the dirty bit.
            var undo = (CoreState.Undo ??= new Undo()).NewUndoData("Add via ASM/C");

            try
            {
                // The devkitARM compile can take several seconds — run off the UI thread.
                var result = await Task.Run(() => _vm.Run(undo));

                if (result.Success)
                {
                    // Only a real ROM mutation is undoable; compile-only / lyn produces
                    // a file but writes nothing, so there is nothing to commit/undo.
                    bool mutated = undo.list.Count > 0;
                    if (mutated && _undoService.CommitExternal(undo))
                        _vm.CanUndo = true;

                    string msg = R._("Compilation successful.");
                    if (!string.IsNullOrEmpty(result.ProductPath))
                        msg += "\r\n" + R._("Product: {0}", result.ProductPath);
                    if (result.InsertedAddr != U.NOT_FOUND)
                        msg += "\r\n" + R._("Inserted at: {0}", U.To0xHexString(result.InsertedAddr));
                    if (!string.IsNullOrEmpty(result.Output.Trim()))
                        msg += "\r\n" + result.Output.Trim();
                    _vm.StatusMessage = msg;
                }
                else
                {
                    // Compile/insert failed → nothing was applied (fault-safe helper),
                    // so there is nothing to undo; just surface the error.
                    _vm.StatusMessage = R._("Compilation failed.") + "\r\n" + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolASMInsertView.Run failed: " + ex.ToString());
                _vm.StatusMessage = R._("Compilation failed.") + "\r\n" + ex.ToString();
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null) return;
                CoreState.Undo.RunUndo();
                _vm.CanUndo = false;
                _vm.StatusMessage = R._("The last operation has been undone.");
            }
            catch (Exception ex)
            {
                Log.Error("ToolASMInsertView.Undo failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
        }

        // This tool has no entry list (it is a compile form like EventAssemblerView).
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
