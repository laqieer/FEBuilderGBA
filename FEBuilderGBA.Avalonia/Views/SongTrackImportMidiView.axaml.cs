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
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackImportMidiView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
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
                    ImportButton.IsEnabled = false;
                }
                else
                {
                    MidiFileLabel.Text = System.IO.Path.GetFileName(path);
                    MidiInfoLabel.Text = _vm.MidiInfoText;
                    MidiInfoBorder.IsVisible = true;
                    ImportButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("BrowseMidi_Click failed: {0}", ex.Message);
            }
        }

        void ImportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasMidiInfo || string.IsNullOrEmpty(_vm.MidiFilePath))
            {
                CoreState.Services.ShowError("No MIDI file selected. Use 'Browse MIDI File...' first.");
                return;
            }

            CoreState.Services.ShowInfo(
                "MIDI write-back to ROM is not yet fully implemented.\n\n" +
                "The MIDI file has been parsed and its metadata is displayed above. " +
                "Full MIDI-to-GBA conversion and ROM write-back will be available in a future update.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
