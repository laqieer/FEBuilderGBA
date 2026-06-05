using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackImportMidiView : TranslatedWindow, IEditorView
    {
        readonly SongTrackImportMidiViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "MIDI Import";
        public bool IsLoaded => _vm.IsLoaded;

        public SongTrackImportMidiView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackImportMidiView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                UpdateImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackImportMidiView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            // Show the selected song header offset and the resolved table-entry
            // slot so the user can see exactly which song the MIDI will replace.
            if (_vm.IsLoaded)
                AddrLabel.Text = string.Format(
                    "Header 0x{0:X08}  (table slot 0x{1:X08})",
                    _vm.CurrentAddr, _vm.SongTableEntryAddr);
            else
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        /// <summary>Enable Import only when BOTH a destination song and a
        /// parsed MIDI file are present.</summary>
        void UpdateImportButtonState()
        {
            ImportButton.IsEnabled =
                _vm.IsLoaded && _vm.SongTableEntryAddr != 0 &&
                _vm.HasMidiInfo && !string.IsNullOrEmpty(_vm.MidiFilePath);
        }

        async void BrowseMidi_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var midiType = new FilePickerFileType(R._("MIDI Files")) { Patterns = new[] { "*.mid", "*.midi" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Select MIDI File"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { midiType, allType },
                });

                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                string? error = _vm.ParseMidiInfo(path);
                if (error != null)
                {
                    CoreState.Services.ShowError(error);
                    MidiFileLabel.Text = "Parse failed";
                    MidiInfoBorder.IsVisible = false;
                }
                else
                {
                    MidiFileLabel.Text = System.IO.Path.GetFileName(path);
                    MidiInfoLabel.Text = _vm.MidiInfoText;
                    MidiInfoBorder.IsVisible = true;
                }
                UpdateImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("BrowseMidi_Click failed: {0}", ex.Message);
            }
        }

        void ImportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.SongTableEntryAddr == 0)
            {
                CoreState.Services.ShowError("No destination song selected. Pick a song from the list on the left first.");
                return;
            }
            if (!_vm.HasMidiInfo || string.IsNullOrEmpty(_vm.MidiFilePath))
            {
                CoreState.Services.ShowError("No MIDI file selected. Use 'Browse MIDI File...' first.");
                return;
            }

            bool confirm = CoreState.Services.ShowQuestion(
                $"Import this MIDI into the selected song (header 0x{_vm.CurrentAddr:X08})?\n\n" +
                "This appends the converted song data to ROM free space and repoints " +
                "the song-table entry. The operation is a single undo step.");
            if (!confirm) return;

            // Real write-back under one undo record. ImportMidiFile writes
            // through ambient-undo-aware ROM APIs (write_range + write_u32) and
            // never resizes rom.Data, so a Rollback restores the ROM
            // byte-identical.
            _undoService.Begin("Import MIDI");
            try
            {
                string? error = _vm.ImportMidi(out string summary);
                if (error != null)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(error);
                    return;
                }

                _undoService.Commit();
                UpdateUI();
                CoreState.Services.ShowInfo(summary);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SongTrackImportMidiView.ImportMidi_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"MIDI import failed: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
