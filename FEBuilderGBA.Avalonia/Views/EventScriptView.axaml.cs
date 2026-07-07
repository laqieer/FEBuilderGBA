using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventScriptViewModel _vm = new();

        public string ViewTitle => "Event Script Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Event Script Editor", 1180, 780, MinWidth: 1180, MinHeight: 780);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EventScriptView()
        {
            InitializeComponent();
            CommandsList.ItemsSource = _vm.Commands;
            // #1736: the picker shows the FILTERED catalog; each entry keeps its
            // original catalog index so insertion is unaffected by filtering.
            CatalogCombo.ItemsSource = _vm.FilteredCommands;
        }

        void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
            // A user-initiated (manual) disassemble is a chapter-event assumption — discard
            // any stale pending kind left over from an abandoned jump/NewAlloc flow so it
            // can't leak the wrong terminator into a normal script (#1510). Jump paths set
            // the kind then call NavigateTo directly (bypassing this handler), so they still
            // consume their staged kind.
            _vm.ClearStagedEventKind();
            _vm.AddressText = AddressBox.Text ?? "";
            RunDisassemble();
        }

        void RunDisassemble()
        {
            if (_vm.TryParseAddress(out uint address))
            {
                _vm.DisassembleAt(address);
                ScriptTextBox.Text = _vm.DisassembledText;
                StatusLabel.Text = _vm.StatusText;
            }
            else
            {
                StatusLabel.Text = "Invalid address. Enter a hex value like 0x08001234 or 1234.";
            }
        }

        void CommandsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedCommandIndex = CommandsList.SelectedIndex;
        }

        void CatalogFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            // #1736: live case-insensitive substring filter of the Insert-command catalog.
            _vm.CommandFilterText = CatalogFilterBox.Text ?? "";
        }

        // ── structural-edit handlers ───────────────────────────────────

        void Insert_Click(object? sender, RoutedEventArgs e)
        {
            // #1736: map the selected FILTERED entry back to its ORIGINAL catalog index
            // (the filtered list's position differs from the full catalog).
            _vm.SelectedCommandCatalogIndex =
                (CatalogCombo.SelectedItem as EventScriptViewModel.CommandCatalogEntry)?.Index ?? -1;
            _vm.InsertSelectedCatalogCommand();
            AfterEdit();
        }

        void InsertHex_Click(object? sender, RoutedEventArgs e)
        {
            _vm.InsertHexText = InsertHexBox.Text ?? "";
            _vm.InsertHexCommand();
            AfterEdit();
        }

        void Delete_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DeleteSelected();
            AfterEdit();
        }

        void MoveUp_Click(object? sender, RoutedEventArgs e)
        {
            _vm.MoveSelectedUp();
            AfterEdit();
        }

        void MoveDown_Click(object? sender, RoutedEventArgs e)
        {
            _vm.MoveSelectedDown();
            AfterEdit();
        }

        void ImportAppend_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImportText = ImportBox.Text ?? "";
            _vm.ImportFromText(clear: false);
            AfterEdit();
        }

        void ImportReplace_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImportText = ImportBox.Text ?? "";
            _vm.ImportFromText(clear: true);
            AfterEdit();
        }

        void WriteAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.WriteAll();
                // The base address may have changed (relocation); reflect it.
                AddressBox.Text = _vm.AddressText;
                AfterEdit();
            }
            catch (Exception ex)
            {
                // Log.Error joins its params with spaces (no composite formatting), so build
                // the message ourselves; log the FULL exception (stack + inner) for
                // diagnosis, not just ex.Message (Copilot PR review #1510). The status line
                // keeps the short message for the user.
                Log.Error("EventScriptView.WriteAll failed: " + ex.ToString());
                StatusLabel.Text = $"Write failed: {ex.Message}";
            }
        }

        /// <summary>Sync the read-only text view + status + selection after an edit.</summary>
        void AfterEdit()
        {
            ScriptTextBox.Text = _vm.DisassembledText;
            StatusLabel.Text = _vm.StatusText;
            if (_vm.SelectedCommandIndex >= 0 && _vm.SelectedCommandIndex < CommandsList.ItemCount)
                CommandsList.SelectedIndex = _vm.SelectedCommandIndex;
        }

        /// <summary>
        /// Tell the editor which kind of event script it is editing so the termination scan
        /// + Write-All terminator selection are correct (world-map / chapter top-level vs a
        /// normal chapter event). Callers that open this view on a world-map or top-level
        /// event pointer must call this BEFORE <see cref="NavigateTo"/>. The kind is applied
        /// ONE-SHOT to the next disassembly only, so reusing this cached editor for a normal
        /// script afterwards reverts to chapter-event semantics (Copilot PR review #1510).
        /// </summary>
        public void SetEventKind(bool isWorldMapEvent, bool isTopLevelEvent)
        {
            _vm.StageEventKind(isWorldMapEvent, isTopLevelEvent);
        }

        /// <summary>Navigate to a specific address and disassemble.</summary>
        public void NavigateTo(uint address)
        {
            _vm.AddressText = $"0x{address:X08}";
            AddressBox.Text = _vm.AddressText;
            RunDisassemble();
        }

        public void SelectFirstItem()
        {
            if (CommandsList.ItemCount > 0)
                CommandsList.SelectedIndex = 0;
        }

        /// <summary>True once a script has been disassembled into the editable list, so an
        /// in-editor template insert has somewhere to land (#1585 — the template browser
        /// gates "Send to Event Editor" on this).</summary>
        public bool HasLoadedScript => _vm.CommandCount > 0;

        /// <summary>
        /// Insert a generated template (a list of editable commands) at the current
        /// selection (#1585 in-editor template insert). Returns false when no script is
        /// loaded yet (the user must disassemble an event first — InsertTemplate refuses
        /// against an empty list) or when <paramref name="codes"/> is empty. On success
        /// the list + read-only view + selection are refreshed and the script is marked
        /// dirty (the user reviews then clicks Write All).
        /// </summary>
        public bool InsertCurrentTemplate(System.Collections.Generic.IList<EventScript.OneCode> codes)
        {
            bool ok = _vm.InsertTemplate(codes);
            AfterEdit();
            return ok;
        }

        /// <summary>
        /// Build the host context (map-id + label allocator) for this open editor so
        /// the Script Template browser can substitute the context-required templates'
        /// placeholders against THIS editor's loaded script (#1591). Returns null when
        /// no script is loaded — the browser then refuses the context-required
        /// templates (the gate holds; no partial bytes).
        /// </summary>
        public IEventEditorHostContext BuildHostContext() => _vm.BuildHostContext();
    }
}
