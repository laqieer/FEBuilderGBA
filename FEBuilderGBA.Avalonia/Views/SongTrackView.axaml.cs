// SPDX-License-Identifier: GPL-3.0-or-later
// SongTrackView — Avalonia parity rebuild for #412. Mirrors `SongTrackForm`
// layout (panel1 read-config + AddressPanel master-write + panel5 detail +
// 16 per-track columns) and wires three real cross-editor jumps via
// `WindowManager.Navigate` (SongExchange / SongTrackAllChangeTrack /
// SongTrackChangeTrack). The three import-dispatch flows (Midi / Wave /
// SelectInstrument) stay deferred and explicitly absent from both the
// manifest and the click-handler wiring — see
// `SongTrackViewModel.NavigationTargets.cs`.
using System;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SongTrackViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Song Track Editor";
        public bool IsLoaded => _vm.IsLoaded;

        // The 16 track ListBox controls — populated in OnSelected from
        // `_vm.Tracks`. Indexed by 0..15 for direct mapping to track index.
        ListBox?[] _trackListBoxes = new ListBox?[16];
        TextBlock?[] _trackLabels = new TextBlock?[16];

        public SongTrackView()
        {
            InitializeComponent();
            ResolveTrackControls();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void ResolveTrackControls()
        {
            // Resolve the 16 static TrackN / TrackLabelN controls so we don't
            // FindControl on every selection change.
            _trackListBoxes[0] = this.FindControl<ListBox>("Track1");
            _trackListBoxes[1] = this.FindControl<ListBox>("Track2");
            _trackListBoxes[2] = this.FindControl<ListBox>("Track3");
            _trackListBoxes[3] = this.FindControl<ListBox>("Track4");
            _trackListBoxes[4] = this.FindControl<ListBox>("Track5");
            _trackListBoxes[5] = this.FindControl<ListBox>("Track6");
            _trackListBoxes[6] = this.FindControl<ListBox>("Track7");
            _trackListBoxes[7] = this.FindControl<ListBox>("Track8");
            _trackListBoxes[8] = this.FindControl<ListBox>("Track9");
            _trackListBoxes[9] = this.FindControl<ListBox>("Track10");
            _trackListBoxes[10] = this.FindControl<ListBox>("Track11");
            _trackListBoxes[11] = this.FindControl<ListBox>("Track12");
            _trackListBoxes[12] = this.FindControl<ListBox>("Track13");
            _trackListBoxes[13] = this.FindControl<ListBox>("Track14");
            _trackListBoxes[14] = this.FindControl<ListBox>("Track15");
            _trackListBoxes[15] = this.FindControl<ListBox>("Track16");

            _trackLabels[0] = this.FindControl<TextBlock>("TrackLabel1");
            _trackLabels[1] = this.FindControl<TextBlock>("TrackLabel2");
            _trackLabels[2] = this.FindControl<TextBlock>("TrackLabel3");
            _trackLabels[3] = this.FindControl<TextBlock>("TrackLabel4");
            _trackLabels[4] = this.FindControl<TextBlock>("TrackLabel5");
            _trackLabels[5] = this.FindControl<TextBlock>("TrackLabel6");
            _trackLabels[6] = this.FindControl<TextBlock>("TrackLabel7");
            _trackLabels[7] = this.FindControl<TextBlock>("TrackLabel8");
            _trackLabels[8] = this.FindControl<TextBlock>("TrackLabel9");
            _trackLabels[9] = this.FindControl<TextBlock>("TrackLabel10");
            _trackLabels[10] = this.FindControl<TextBlock>("TrackLabel11");
            _trackLabels[11] = this.FindControl<TextBlock>("TrackLabel12");
            _trackLabels[12] = this.FindControl<TextBlock>("TrackLabel13");
            _trackLabels[13] = this.FindControl<TextBlock>("TrackLabel14");
            _trackLabels[14] = this.FindControl<TextBlock>("TrackLabel15");
            _trackLabels[15] = this.FindControl<TextBlock>("TrackLabel16");
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadFullList();
                EntryList.SetItems(items);
                // Surface auto-detected read-config defaults into the UI.
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
                BlockSizeBox.Text = "8";
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
                // The selected AddrResult's `tag` field holds the songId (set
                // by SongTrackViewModel.LoadFullList()). -1 when nothing
                // selected so SongID-0 write-protect can still distinguish
                // "no song" from "song 0".
                _vm.SelectedSongIndex = EntryList.SelectedItem is AddrResult sel
                    ? (int)sel.tag : -1;
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
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TrackCountBox.Value = _vm.TrackCount;
            NumBlksBox.Value = _vm.NumBlks;
            PriorityBox.Value = _vm.Priority;
            ReverbBox.Value = _vm.Reverb;
            InstrumentAddrBox.Value = _vm.InstrumentAddr;

            // Populate the 16 fixed track columns. Active tracks show the
            // command-byte preview from _vm.Tracks; inactive columns clear
            // their items and disable per-track jump click.
            for (int i = 0; i < 16; i++)
            {
                var lb = _trackListBoxes[i];
                var lbl = _trackLabels[i];
                if (lb == null || lbl == null) continue;

                if (i < _vm.Tracks.Count)
                {
                    var t = _vm.Tracks[i];
                    lb.ItemsSource = new[] { t.Status, $"Ptr@0x{t.PointerOffset:X06}" };
                    lbl.IsEnabled = true;
                    lbl.Opacity = 1.0;
                }
                else
                {
                    lb.ItemsSource = Array.Empty<string>();
                    lbl.IsEnabled = false;
                    lbl.Opacity = 0.4;
                }
            }

            TrackSummaryLabel.Text = $"{_vm.Tracks.Count} track(s)";
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ReadStartAddress = (uint)(ReadStartAddressBox.Value ?? 0);
                _vm.ReadCount = (uint)(ReadCountBox.Value ?? 0);
                LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.ReloadList_Click failed: {0}", ex.Message);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            // WF parity: SongID 0 is write-protected (UseWriteProtectionID00).
            if (_vm.SelectedSongIndex == 0)
            {
                CoreState.Services.ShowError("Song ID 0 is write-protected (silence song).");
                return;
            }

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

        // -----------------------------------------------------------------
        // Cross-editor jumps. Mirrors the WF jump callsites that this PR
        // wires in Avalonia. The 3 import flows are NOT wired here — the
        // navigation manifest stays in lockstep with real behavior.
        // -----------------------------------------------------------------

        void SongExchange_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.SelectedSongIndex < 0) return;
            try
            {
                WindowManager.Instance.Navigate<SongExchangeView>((uint)_vm.SelectedSongIndex);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.SongExchange_Click failed: {0}", ex.Message);
            }
        }

        void AllTracks_Click(object? sender, PointerPressedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            try
            {
                WindowManager.Instance.Navigate<SongTrackAllChangeTrackView>(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.AllTracks_Click failed: {0}", ex.Message);
            }
        }

        void TrackLabel_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TextBlock tb) return;
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            // Parse the 1-based track index from Tag. Empty columns disable
            // their label via UpdateUI() — IsEnabled gate guards here too.
            if (!tb.IsEnabled) return;
            if (tb.Tag is not string tagStr) return;
            if (!int.TryParse(tagStr, out int trackOneBased)) return;
            if (trackOneBased < 1 || trackOneBased > _vm.Tracks.Count) return;

            try
            {
                // The track's pointer offset is the address that SongTrackChangeTrack
                // edits. Pass it as the navigation address — the target view
                // resolves its own scope via that address.
                var track = _vm.Tracks[trackOneBased - 1];
                WindowManager.Instance.Navigate<SongTrackChangeTrackView>(track.PointerOffset);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.TrackLabel_Click failed: {0}", ex.Message);
            }
        }

        void LinkInternet_Click(object? sender, PointerPressedEventArgs e)
        {
            // WF: MainFormUtil.GotoMoreData() — shell-out to the FEBuilderGBA
            // resource wiki. Avalonia parity is informational for now (the
            // shell launcher is WinForms-coupled); the label stays clickable
            // to surface the WF affordance to anyone driving via Automation.
            try
            {
                _ = sender; _ = e;
                CoreState.Services.ShowInfo("See https://github.com/FEBuilderGBA/FEBuilderGBA/wiki for online music resources.");
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.LinkInternet_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Export / Import (Midi only — preview). Wave / Instrument / Source
        // stay disabled until the Core extraction lands; their AXAML
        // declares the disabled state with explanatory tooltip.
        // -----------------------------------------------------------------

        async void ExportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            try
            {
                var midiType = new FilePickerFileType(R._("MIDI Files")) { Patterns = new[] { "*.mid", "*.midi" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export MIDI"),
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
                var midiType = new FilePickerFileType(R._("MIDI Files")) { Patterns = new[] { "*.mid", "*.midi" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import MIDI"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { midiType, allType },
                });

                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                // Parse and show MIDI metadata preview
                string preview = _vm.PreviewMidi(path);
                if (preview.StartsWith("Error:"))
                {
                    CoreState.Services.ShowError(preview);
                    return;
                }

                // Show metadata with write-back warning
                string message = preview + "\n\n" +
                    "---\n" +
                    "MIDI write-back to ROM is not yet fully implemented.\n" +
                    "Full MIDI-to-GBA conversion will be available in a future update.";
                CoreState.Services.ShowInfo(message);
            }
            catch (Exception ex)
            {
                Log.Error("ImportMidi_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
