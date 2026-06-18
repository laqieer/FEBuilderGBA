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
    /// Uninstall is deferred to a follow-up issue; an applied insert can be
    /// reverted via Undo for now.
    /// </summary>
    public partial class EventAssemblerView : TranslatedWindow, IEditorView
    {
        readonly EventAssemblerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Event Assembler";
        public bool IsLoaded => true;

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

            Opened += (_, _) =>
            {
                // Surface a clear message up front if EA/ColorzCore is not configured.
                if (!_vm.IsEventAssemblerAvailable)
                    _vm.StatusMessage = _vm.NotFoundMessage;
            };
        }

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

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
                    string? path = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                        _vm.SourcePath = path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventAssemblerView.Browse failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            // Prompt for a file if none chosen yet (mirrors WF ImportButton_Click).
            if (!_vm.SourceExists)
            {
                Browse_Click(sender, e);
                return;
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

            try
            {
                _undoService.Begin("Event Assembler");
                var undo = _undoService.GetActiveUndoData();

                // The EA process can take several seconds — run off the UI thread.
                var result = await Task.Run(() => _vm.Import(undo!));

                if (result.Success)
                {
                    _undoService.Commit();
                    _vm.HasResult = true;
                    _vm.CanUndo = true;

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
                    _undoService.Rollback();
                    _vm.StatusMessage = R._("Compilation failed.") + "\r\n" + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                try { _undoService.Rollback(); }
                catch (Exception rbEx) { Log.Error("EventAssemblerView.Import rollback failed: " + rbEx.ToString()); }
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

        // Uninstall is DEFERRED to a follow-up issue. The faithful WinForms uninstall
        // (PatchForm.MakeInstantEAToPatch → UnInstallPatch) is coupled to the WinForms
        // patch subsystem, so it is out of scope for this slice; an applied insert can
        // be reverted via the Undo button for now.

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
