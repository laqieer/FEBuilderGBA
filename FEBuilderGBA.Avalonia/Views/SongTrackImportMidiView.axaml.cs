using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

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
                Log.ErrorF("SongTrackImportMidiView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                // Carry the songId (AddrResult.tag) so a header shared by two
                // table entries resolves to the SELECTED slot.
                if (EntryList.SelectedItem is AddrResult sel)
                    _vm.LoadEntry(sel.addr, sel.tag);
                else
                    _vm.LoadEntry(addr);
                UpdateUI();
                UpdateImportButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackImportMidiView.OnSelected failed: {0}", ex.Message);
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

            // #1002 Slice 2: show the current instrument set pointer (seeded from
            // the destination header +4 on LoadEntry, or updated by the picker).
            if (InstrumentLabel != null)
                InstrumentLabel.Text = _vm.InstrumentAddr != 0
                    ? string.Format("0x{0:X08}", _vm.InstrumentAddr)
                    : R._("(use destination's current voicegroup)");
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

                // #1639: ParseMidiInfo/ImportMidi read by path, and the import
                // happens on a LATER button click. Bridge a SAF source (no local
                // path) to a temp file that survives until the deferred import
                // (the OS temp sweeper reclaims it). The display label uses the
                // original picked name, not the temp path.
                string displayName = files[0].Name ?? "midi";
                string? path = await FileDialogHelper.ResolveReadPathAsync(files[0]);
                if (string.IsNullOrEmpty(path)) return;

                string? error = _vm.ParseMidiInfo(path);
                if (error != null)
                {
                    CoreState.Services.ShowError(error);
                    MidiFileLabel.Text = R._("Parse failed");
                    MidiInfoBorder.IsVisible = false;
                }
                else
                {
                    MidiFileLabel.Text = displayName;
                    MidiInfoLabel.Text = _vm.MidiInfoText;
                    MidiInfoBorder.IsVisible = true;
                }
                UpdateImportButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("BrowseMidi_Click failed: {0}", ex.Message);
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
                Log.Error($"SongTrackImportMidiView.ImportMidi_Click failed: {ex.Message}");
                CoreState.Services.ShowError($"MIDI import failed: {ex.Message}");
            }
        }

        // #1002 Slice 2: instrument-set picker for the standalone MIDI import window.
        // Opens the SongTrackImportSelectInstrumentView in pick mode, seeds it with
        // the destination's current voicegroup, and stores the chosen address as a GBA
        // POINTER — SongMidiCore.AssembleGBASong writes instrumentAddr VERBATIM into
        // the new song header +4 (a GBA pointer slot), unlike the .s path where
        // ImportS applies toPointer internally and receives an OFFSET.
        async void SelectInstrument_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded)
            {
                CoreState.Services.ShowError(R._("Pick a destination song from the list first."));
                return;
            }

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Seed the picker with the destination's current voicegroup (already
            // stored in InstrumentAddr as a raw GBA pointer from LoadEntry).
            uint seed = _vm.InstrumentAddr;

            PickResult? pick;
            try
            {
                pick = await WindowManager.Instance.PickFromEditor<SongTrackImportSelectInstrumentView>(
                    seed, this);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackImportMidiView.SelectInstrument_Click pick failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Instrument selection failed: {0}", ex.Message));
                return;
            }
            if (pick == null) return; // user cancelled.

            // Normalize to offset + validate — the instrument list mixes an
            // OFFSET-valued "Current" seed with toPointer'd discovered rows.
            uint instrumentOffset = U.toOffset(pick.Address);
            if (!U.isSafetyOffset(instrumentOffset, rom))
            {
                CoreState.Services.ShowError(R._("The selected instrument set address is invalid."));
                return;
            }

            // Store as POINTER: the MIDI writer writes it verbatim into header +4.
            _vm.InstrumentAddr = U.toPointer(instrumentOffset);

            // Reflect the new choice in the label immediately.
            if (InstrumentLabel != null)
                InstrumentLabel.Text = string.Format("0x{0:X08}", _vm.InstrumentAddr);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
