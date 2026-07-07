using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Procs Script editor. #1585: re-skinned from a placeholder shell to the same
    /// structural-editing surface as <see cref="EventScriptView"/> — insert (catalog
    /// or hex), delete, move up/down, import-from-text, and a Write-All that
    /// re-serializes the resized script and writes it back under one undo scope. It is
    /// backed by the shared cross-platform <c>EventScriptEditorCore</c> engine via
    /// <see cref="EventScriptViewModel"/> with <see cref="EventScript.EventScriptType.Procs"/>,
    /// so the Procs editor and the Event editor share one engine (CLAUDE.md
    /// "Script-type agnostic" seam).
    /// </summary>
    public partial class ProcsScriptView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EventScriptViewModel _vm = new()
        {
            ScriptType = EventScript.EventScriptType.Procs
        };

        public string ViewTitle => "Procs Script Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Procs Script Editor", 1180, 780, MinWidth: 1180, MinHeight: 780);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ProcsScriptView()
        {
            InitializeComponent();
            CommandsList.ItemsSource = _vm.Commands;
            CatalogCombo.ItemsSource = _vm.AvailableCommands;
        }

        void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
            // Procs scripts are never world-map/top-level events; a manual disassemble
            // is always a chapter-event (TERM) assumption. ClearStagedEventKind keeps
            // any stale pending kind from leaking the wrong terminator (#1510 parity).
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
                StatusLabel.Text = R._("Invalid address. Enter a hex value like 0x08001234 or 1234.");
            }
        }

        void CommandsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedCommandIndex = CommandsList.SelectedIndex;
        }

        // ── structural-edit handlers ───────────────────────────────────

        void Insert_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedCommandCatalogIndex = CatalogCombo.SelectedIndex;
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
                Log.Error("ProcsScriptView.WriteAll failed: " + ex.ToString());
                StatusLabel.Text = R._("Write failed") + ": " + ex.Message;
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

        /// <summary>Navigate to a specific address and disassemble (used by the
        /// JumpToProcsCursor RAM jump and any Open+address path).</summary>
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
    }
}
