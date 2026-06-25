using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptView : TranslatedWindow, IEditorView
    {
        readonly EventScriptViewModel _vm = new();

        public string ViewTitle => "Event Script Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public EventScriptView()
        {
            InitializeComponent();
            CommandsList.ItemsSource = _vm.Commands;
            CatalogCombo.ItemsSource = _vm.AvailableCommands;
        }

        void Disassemble_Click(object? sender, RoutedEventArgs e)
        {
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
                // Log.Error joins its params with spaces (no composite formatting), so
                // build the message string ourselves (Copilot PR review inline #3).
                Log.Error("EventScriptView.WriteAll failed: " + ex.Message);
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
        /// Tell the editor which kind of event script it is editing so the
        /// termination scan + Write-All terminator selection are correct
        /// (world-map / chapter top-level vs a normal chapter event). Callers that
        /// open this view on a world-map or top-level event pointer must call this
        /// BEFORE <see cref="NavigateTo"/> (Copilot PR review #1510 finding #2).
        /// </summary>
        public void SetEventKind(bool isWorldMapEvent, bool isTopLevelEvent)
        {
            _vm.IsWorldMapEvent = isWorldMapEvent;
            _vm.IsTopLevelEvent = isTopLevelEvent;
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
    }
}
