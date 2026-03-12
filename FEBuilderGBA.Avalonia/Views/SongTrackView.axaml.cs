using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackView : Window, IEditorView
    {
        readonly SongTrackViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Song Track Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public SongTrackView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TrackCountBox.Value = _vm.TrackCount;
            NumBlksBox.Value = _vm.NumBlks;
            PriorityBox.Value = _vm.Priority;
            ReverbBox.Value = _vm.Reverb;
            InstrumentAddrBox.Value = _vm.InstrumentAddr;

            // Populate track list
            TrackListBox.ItemsSource = _vm.Tracks;
            TrackSummaryLabel.Text = $"{_vm.Tracks.Count} track(s) found";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit Song Track");
            try
            {
                _vm.TrackCount = (uint)(TrackCountBox.Value ?? 0);
                _vm.NumBlks = (uint)(NumBlksBox.Value ?? 0);
                _vm.Priority = (uint)(PriorityBox.Value ?? 0);
                _vm.Reverb = (uint)(ReverbBox.Value ?? 0);
                _vm.InstrumentAddr = (uint)(InstrumentAddrBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Song track data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SongTrackView.Write_Click failed: {0}", ex.Message);
            }
        }

        async void ExportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            try
            {
                var midiType = new FilePickerFileType("MIDI Files") { Patterns = new[] { "*.mid", "*.midi" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export MIDI",
                    SuggestedFileName = $"song_0x{_vm.CurrentAddr:X06}.mid",
                    FileTypeChoices = new[] { midiType, allType },
                });

                string? path = file?.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                string? error = _vm.ExportMidi(path);
                if (error != null)
                    CoreState.Services.ShowError(error);
                else
                    CoreState.Services.ShowInfo($"MIDI exported to {path}");
            }
            catch (Exception ex)
            {
                Log.Error("ExportMidi_Click failed: {0}", ex.Message);
            }
        }

        async void ImportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            try
            {
                var midiType = new FilePickerFileType("MIDI Files") { Patterns = new[] { "*.mid", "*.midi" } };
                var allType = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import MIDI",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { midiType, allType },
                });

                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                string? error = _vm.ImportMidi(path);
                if (error != null)
                    CoreState.Services.ShowError(error);
                else
                {
                    CoreState.Services.ShowInfo("MIDI imported successfully.");
                    // Reload to reflect changes
                    _vm.LoadEntry(_vm.CurrentAddr);
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImportMidi_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
