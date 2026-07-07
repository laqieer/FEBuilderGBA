using global::Avalonia;
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Add-via-Event-Assembler tool (WF <c>EventAssemblerForm</c>). Assembles and
    /// inserts an EA event script into the ROM via the GUI-free Core helper
    /// <c>EventAssemblerCompileCore</c> (shared with the CLI <c>--compile-event</c>),
    /// with free-area selection (Program/Data/None), AutoReCompile, a debug-symbol
    /// store choice and undo.
    ///
    /// Free-area / debug-symbol combo items are added in code via R._() so they
    /// pick up ja/zh translations (ViewTranslationHelper does not translate
    /// ComboBoxItem content).
    ///
    /// Uninstall (#1242) reverts an applied EA patch in place: it traces the loaded
    /// .event's written ranges via <c>EventAssemblerUninstallCore</c> and restores
    /// those bytes from a user-supplied clean-original ROM, under undo. (An applied
    /// insert can still also be reverted via the Undo button.)
    /// </summary>
    public partial class EventAssemblerView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventAssemblerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Event Assembler";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Event Assembler", 640, 520, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EventAssemblerView()
        {
            InitializeComponent();
            DataContext = _vm;

            // Free-area modes (mirror WF FreeAreaComboBox; ctor default index 0).
            FreeAreaCombo.Items.Add(R._("Type: Program (rebuild-safe upper area)"));
            FreeAreaCombo.Items.Add(R._("Type: Data (lower area)"));
            FreeAreaCombo.Items.Add(R._("Do not define a free area (use ORG in the EA)"));

            // Debug-symbol store (mirror WF DebugSymbolComboBox; ctor default index 3).
            DebugSymbolCombo.Items.Add(R._("None"));
            DebugSymbolCombo.Items.Add(R._("Save sym.txt"));
            DebugSymbolCombo.Items.Add(R._("Save as comment"));
            DebugSymbolCombo.Items.Add(R._("Save both"));

            FreeAreaCombo.SelectedIndex = _vm.FreeAreaIndex;
            DebugSymbolCombo.SelectedIndex = _vm.DebugSymbolIndex;

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                // Surface a clear message up front if EA/ColorzCore is not configured.
                if (!_vm.IsEventAssemblerAvailable)
                    _vm.StatusMessage = _vm.NotFoundMessage;
            }
        }

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            await BrowseForSourceAsync();
        }

        /// <summary>
        /// Show the .event/.txt picker and store the chosen path on the VM.
        /// Returns true if the user picked a usable file. Awaitable so Import can
        /// pick-then-continue in the same invocation (mirrors WF ImportButton_Click).
        /// </summary>
        async Task<bool> BrowseForSourceAsync()
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return false;

            try
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Please select an event file to import."),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(R._("event file")) { Patterns = new[] { "*.event", "*.txt" } },
                        new FilePickerFileType("event") { Patterns = new[] { "*.event" } },
                        new FilePickerFileType("text") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                {
                    // #1639: the event source is compiled by Event Assembler /
                    // ColorzCore, which needs a REAL filesystem path and resolves
                    // sibling #include files — it cannot run from a SAF stream. On
                    // Android (no local path) disable with a clear message instead
                    // of silently failing.
                    string? path = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        _vm.SourcePath = path;
                        return true;
                    }
                    _vm.StatusMessage = R._("Event Assembler needs desktop file-system access and is not available on this device.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventAssemblerView.Browse failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
            return false;
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            // Prompt for a file if none chosen yet, then CONTINUE the import in the
            // same action once a file is picked (mirrors WF ImportButton_Click).
            if (!_vm.SourceExists)
            {
                if (!await BrowseForSourceAsync() || !_vm.SourceExists)
                    return; // user cancelled / no usable file
            }
            if (!_vm.IsEventAssemblerAvailable)
            {
                _vm.StatusMessage = _vm.NotFoundMessage;
                return;
            }

            string prefix = "";
            if (_vm.AutoReCompile)
            {
                // The asm rebuild engine (devkitARM) is WinForms-only; note it and
                // continue with the EA assemble step (EA still resolves its own
                // .event/.lyn includes).
                prefix = R._("Auto re-compile of .s/.asm sources is not available in this build; running Event Assembler only.") + "\r\n";
            }

            ImportButton.IsEnabled = false;
            _vm.StatusMessage = prefix + R._("Compiling...");

            // Use an EXPLICIT UndoData passed through to the Core helper rather than
            // the thread-local ambient ROM.BeginUndoScope: the compile+insert runs on
            // a background thread (Task.Run), and the ambient scope is thread-local to
            // the UI thread — so it would NOT capture the background writes AND could
            // wrongly absorb an unrelated UI-thread ROM write. SwapNewROMData records
            // diffs directly into this passed UndoData, so undo capture stays correct
            // and thread-consistent. We push it (UI thread) only after a successful
            // insert, via UndoService.CommitExternal which also refreshes the dirty bit.
            var undo = (CoreState.Undo ??= new Undo()).NewUndoData("Event Assembler");

            try
            {
                // The EA process can take several seconds — run off the UI thread.
                var result = await Task.Run(() => _vm.Import(undo));

                if (result.Success)
                {
                    if (_undoService.CommitExternal(undo))
                        _vm.CanUndo = true;
                    _vm.HasResult = true;

                    string msg = R._("Compilation successful.");
                    if (result.InsertedAddr != U.NOT_FOUND)
                        msg += "\r\n" + R._("Inserted at: {0}", U.To0xHexString(result.InsertedAddr));
                    if (result.SymbolCount > 0)
                        msg += "\r\n" + R._("Symbols: {0} entries", result.SymbolCount.ToString());
                    if (!string.IsNullOrEmpty(result.Output.Trim()))
                        msg += "\r\n" + result.Output.Trim();
                    _vm.StatusMessage = msg;
                }
                else
                {
                    // Compile failed → nothing was applied (fault-safe helper), so
                    // there is nothing to undo; just surface the error.
                    _vm.StatusMessage = R._("Compilation failed.") + "\r\n" + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventAssemblerView.Import failed: " + ex.ToString());
                _vm.StatusMessage = R._("Compilation failed.") + "\r\n" + ex.ToString();
            }
            finally
            {
                ImportButton.IsEnabled = true;
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
                Log.Error("EventAssemblerView.Undo failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
        }

        async void Uninstall_Click(object? sender, RoutedEventArgs e)
        {
            // Need a loaded .event to trace, and a loaded ROM to revert in place.
            if (!_vm.SourceExists)
            {
                if (!await BrowseForSourceAsync() || !_vm.SourceExists)
                    return; // user cancelled / no usable file
            }
            if (CoreState.ROM == null)
            {
                _vm.StatusMessage = R._("No ROM is loaded.");
                return;
            }

            // Prompt for the CLEAN ORIGINAL ROM (the ROM as it was before the patch).
            // Faithful to WF UnInstallPatch, which asks the user for a ROM that does
            // NOT contain the patch and restores the traced ranges from it.
            string? cleanRomPath = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
            if (string.IsNullOrEmpty(cleanRomPath))
                return; // cancelled

            byte[] cleanRom;
            try
            {
                // Read off the UI thread — a full ROM from slow storage can be several
                // MB and would otherwise freeze the window before the revert task starts.
                cleanRom = await Task.Run(() => System.IO.File.ReadAllBytes(cleanRomPath));
            }
            catch (Exception ex)
            {
                Log.Error("EventAssemblerView.Uninstall read clean ROM failed: " + ex.ToString());
                _vm.StatusMessage = R._("Uninstall failed.") + "\r\n" + ex.ToString();
                return;
            }

            // Review-BEFORE-revert (closest to the WF binmap dialog): trace first and,
            // if any write-bearing block can't be traced, ask the user to confirm a
            // PARTIAL uninstall — those bytes will NOT be reverted and may stay patched.
            // The trace (EA parse + repeated GREP over the full ROM) can be slow for
            // large scripts/ROMs, so run it OFF the UI thread to keep the window
            // responsive. It is read-only here (only used to gate the confirm); the real
            // revert re-traces and writes inside the guarded Task.Run below.
            var preTrace = await Task.Run(() => EventAssemblerUninstallCore.TraceEAFile(_vm.SourcePath));
            if (preTrace.UntracedCount > 0)
            {
                int n = preTrace.UntracedCount;
                int shown = Math.Min(n, 5);
                string detail = "";
                for (int i = 0; i < shown; i++)
                    detail += "\r\n  - " + preTrace.Untraceable[i];
                if (n > shown)
                    detail += "\r\n  - " + R._("...and {0} more.", (n - shown).ToString());

                string prompt = R._("{0} block(s) in this EA cannot be traced (e.g. inline image / PROCS); those bytes will NOT be reverted and may remain patched.", n.ToString())
                    + detail + "\r\n\r\n"
                    + R._("Proceed with a partial uninstall?");
                var answer = await MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window, prompt,
                    R._("Partial uninstall"), MessageBoxMode.YesNo);
                if (answer != MessageBoxResult.Yes)
                {
                    _vm.StatusMessage = R._("Uninstall cancelled.");
                    return;
                }
            }

            UninstallButton.IsEnabled = false;
            ImportButton.IsEnabled = false;
            // Disable Undo during the revert — it may already be visible from a prior
            // Import, and a click mid-revert would race the in-progress undo writes and
            // corrupt the undo/history state. Capture its prior enabled state so the
            // finally restores EXACTLY that (not a blind true).
            bool undoWasEnabled = UndoButton.IsEnabled;
            UndoButton.IsEnabled = false;
            _vm.StatusMessage = R._("Uninstalling...");

            // Same explicit-UndoData discipline as Import: the trace+revert runs off
            // the UI thread (Task.Run), so we pass an explicit Undo.UndoData rather
            // than the thread-local ambient scope. write_u8(addr, o, undo) records each
            // restored byte into it; we push it (UI thread) via CommitExternal only on
            // success, and roll it back on failure so a partial revert never sticks.
            var undo = (CoreState.Undo ??= new Undo()).NewUndoData("Event Assembler Uninstall");

            try
            {
                var result = await Task.Run(() =>
                    EventAssemblerUninstallCore.Uninstall(_vm.SourcePath, cleanRom, undo));

                if (result.Success)
                {
                    if (_undoService.CommitExternal(undo))
                        _vm.CanUndo = true;
                    _vm.HasResult = true;
                    string msg = R._("Uninstall successful.") + "\r\n"
                        + R._("Reverted {0} range(s), {1} byte(s).",
                            result.RangeCount.ToString(), result.BytesReverted.ToString());

                    // NEVER claim a clean uninstall when the trace left residue. Warn the
                    // user with the block count + the first few descriptions so they can
                    // verify the ROM (mirrors how WF surfaces partial-uninstall risk).
                    if (!result.FullyTraced)
                    {
                        int n = result.UntraceableBlocks.Count;
                        msg += "\r\n\r\n" + R._("WARNING: {0} block(s) in this patch could not be traced and were NOT reverted — the uninstall may be incomplete. Please verify the ROM.", n.ToString());
                        int shown = Math.Min(n, 5);
                        for (int i = 0; i < shown; i++)
                            msg += "\r\n  - " + result.UntraceableBlocks[i];
                        if (n > shown)
                            msg += "\r\n  - " + R._("...and {0} more.", (n - shown).ToString());
                    }
                    _vm.StatusMessage = msg;
                }
                else
                {
                    // Trace/validation failed → nothing was applied; roll back the
                    // (empty) undo scope so it does not linger, and surface the error.
                    CoreState.Undo.Rollback(undo);
                    _vm.StatusMessage = R._("Uninstall failed.") + "\r\n" + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                // A mid-revert exception leaves a partial write — roll it back.
                try { CoreState.Undo?.Rollback(undo); }
                catch (Exception rollbackEx)
                {
                    Log.Error("EventAssemblerView.Uninstall rollback failed: " + rollbackEx.ToString());
                }
                Log.Error("EventAssemblerView.Uninstall failed: " + ex.ToString());
                _vm.StatusMessage = R._("Uninstall failed.") + "\r\n" + ex.ToString();
            }
            finally
            {
                UninstallButton.IsEnabled = true;
                ImportButton.IsEnabled = true;
                // Restore Undo to its PRIOR enabled state (success or failure) so a user
                // who had it usable before the uninstall is never left without a working
                // Undo button — and we don't enable it if it wasn't. A successful revert
                // sets CanUndo=true (which also drives IsVisible), so Undo stays usable.
                UndoButton.IsEnabled = undoWasEnabled;
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
