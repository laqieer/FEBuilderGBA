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
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

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
            // #1014: the Open Source File / Folder buttons gate visibility via
            // IsVisible="{Binding IsSourceFileAvailable}", so the view needs a
            // DataContext pointing at the VM (mirrors WorldMapImageView). The
            // existing NUD/track controls are still pushed/read manually — only
            // the two source-file buttons use the binding.
            DataContext = _vm;
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
                // #649: editable inputs unified via EditorTopBarWithInputs.
                TopBar.ReadStartAddress = _vm.ReadStartAddress;
                TopBar.ReadCount = (int)_vm.ReadCount;
                BlockSizeBox.Text = "8";
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.LoadList failed: {0}", ex.Message);
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
                // #1014: refresh the source-file affordance for the selected
                // song (WF per-song "Song_" + hex(songId) ResourceCache key) so
                // the Open Source File / Folder buttons show/hide for the
                // recorded source. When nothing is selected (SelectedSongIndex
                // == -1) clear the affordance — do NOT cast -1 to 0xFFFFFFFF,
                // which would query the bogus "Song_FFFFFFFF" key. #1058 review.
                if (_vm.SelectedSongIndex < 0)
                {
                    _vm.SourceFilePath = string.Empty;
                    _vm.IsSourceFileAvailable = false;
                }
                else
                {
                    _vm.RefreshSourceFile((uint)_vm.SelectedSongIndex);
                }
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.OnSelected failed: {0}", ex.Message);
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
                    lb.IsEnabled = true;
                    lb.Opacity = 1.0;
                }
                else
                {
                    lb.ItemsSource = Array.Empty<string>();
                    lbl.IsEnabled = false;
                    lbl.Opacity = 0.4;
                    // Also disable + dim the ListBox itself so empty columns
                    // are not focusable (Copilot bot review #2 / PR #558).
                    lb.IsEnabled = false;
                    lb.Opacity = 0.4;
                }
            }

            TrackSummaryLabel.Text = $"{_vm.Tracks.Count} track(s)";
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            // Legacy handler retained for any code-behind invocations; new
            // EditorTopBarWithInputs routes to OnTopBarReloadRequested.
            OnTopBarReloadRequested(sender, e);
        }

        // #649: routed event from the unified EditorTopBarWithInputs Reload
        // button. Push the bar's editable values into the VM then re-load.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ReadStartAddress = TopBar.ReadStartAddress;
                _vm.ReadCount = (uint)TopBar.ReadCount;
                LoadList();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.OnTopBarReloadRequested failed: {0}", ex.Message);
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
                Log.ErrorF("SongTrackView.Write_Click failed: {0}", ex.Message);
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
                Log.ErrorF("SongTrackView.SongExchange_Click failed: {0}", ex.Message);
            }
        }

        void AllTracks_Click(object? sender, PointerPressedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            try
            {
                // In WinForms, `SongTrackAllChangeTrackForm.Init(P4, Tracks)`
                // takes the instrument-set pointer and the full Tracks list.
                // Navigate carries a single uint, so #1015 passes the SONG
                // HEADER address (CurrentAddr) instead of the instrument-set
                // pointer: the Bulk Track Change editor re-derives the full
                // track list — and the distinct 0xBD voices used across them —
                // straight from the header (TrackCount @+0, InstrumentAddr
                // @+4), so no separate Tracks payload is needed.
                WindowManager.Instance.Navigate<SongTrackAllChangeTrackView>(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.AllTracks_Click failed: {0}", ex.Message);
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
                // In WinForms, `SongTrackChangeTrackForm.Init(..., this.Tracks[no])`
                // operates on the track's *data* address (`SongUtil.Track.basepointer`).
                // The Avalonia ChangeTrack target editor will need that same
                // scope once it becomes functional, so we pass DataOffset
                // (the resolved track-data ROM offset) — NOT PointerOffset
                // (the address of the 4-byte pointer slot in the song header)
                // which is meaningful only for the host editor. Guard against
                // invalid pointers / DataOffset == 0 (Copilot bot review #3 /
                // PR #558).
                var track = _vm.Tracks[trackOneBased - 1];
                if (!track.IsValid || track.DataOffset == 0)
                {
                    Log.Error($"SongTrackView.TrackLabel_Click: track {trackOneBased} has invalid DataOffset");
                    return;
                }
                WindowManager.Instance.Navigate<SongTrackChangeTrackView>(track.DataOffset);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.TrackLabel_Click failed: {0}", ex.Message);
            }
        }

        void LinkInternet_Click(object? sender, PointerPressedEventArgs e)
        {
            // Opens the "Find new resources on the Internet" wiki page in the
            // browser, mirroring WF MainFormUtil.GotoMoreData() and the other
            // Avalonia editors (ImageMagicFEditorView / ImageMagicCSACreatorView).
            // The fork wiki MoreData page lists the community music/graphics
            // resources; the dead upstream wiki URL is gone (#1381).
            try
            {
                _ = sender; _ = e;
                const string url = "https://github.com/laqieer/FEBuilderGBA/wiki/MoreData";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                // Core Log.Error is params string[] (joined with spaces, NOT
                // composite formatting) — pass the message and the full
                // exception (stack + inner) as separate args, never a "{0}"
                // placeholder, and avoid an extra concatenated allocation.
                Log.Error("SongTrackView.LinkInternet_Click failed:", ex.ToString());
            }
        }

        // -----------------------------------------------------------------
        // Export (dispatch-by-extension). MIDI export + .instrument (#1609)
        // instrument-set export are wired; .s (MPlay assembly source produced
        // by SongUtil.ExportSFile, e.g. includes MPlayDef.s) and .sf2
        // (SoundFont via external GBAMusRiper) stay out of scope. The single
        // "Export Music File" button routes by the chosen extension, mirroring
        // WinForms SongTrackForm.ExportButton_Click (.MID/.MIDI vs .INSTRUMENT).
        // -----------------------------------------------------------------

        async void ExportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            try
            {
                var midiType = new FilePickerFileType(R._("MIDI Files")) { Patterns = new[] { "*.mid", "*.midi" } };
                // #1609: offer the instrument-set (.instrument) export inline,
                // matching the WinForms Song Track Export filter. The Core seam
                // (SongInstrumentSetCore.ExportAll) is already wired by the Song
                // Instrument editor — no new Core work.
                var instType = new FilePickerFileType(R._("Instrument Set Files")) { Patterns = new[] { "*.instrument" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export Music File"),
                    SuggestedFileName = $"song_0x{_vm.CurrentAddr:X06}.mid",
                    FileTypeChoices = new[] { midiType, instType, allType },
                });

                if (file == null) return;

                // Dispatch by the chosen extension (mirrors WF
                // SongTrackForm.cs:436-471: .MID/.MIDI vs .INSTRUMENT).
                string ext = U.GetFilenameExt(file.Name ?? "");
                if (ext == ".INSTRUMENT")
                {
                    // #1639: .instrument export writes SIBLING files next to the
                    // chosen path (relative index), so it needs a real local
                    // directory. On Android SAF there is no local path → disable
                    // with a clear message instead of silently failing.
                    string? local = file.TryGetLocalPath();
                    if (string.IsNullOrEmpty(local))
                    {
                        CoreState.Services.ShowError(R._("Instrument-set export writes multiple files and requires desktop file-system access; it is not available on this device."));
                        return;
                    }
                    string? instError = _vm.ExportInstrumentSet(local);
                    if (instError != null)
                        CoreState.Services.ShowError(instError);
                    else
                        CoreState.Services.ShowInfo($"Instrument set exported to {local}");
                }
                else
                {
                    // .mid/.midi is a single file → route through the SAF bridge
                    // (temp + write-back on Android).
                    string? error = null;
                    string? written = await FileDialogHelper.WriteViaAsync(file, p => { error = _vm.ExportMidi(p); });
                    if (written == null) return;
                    if (error != null)
                        CoreState.Services.ShowError(error);
                    else
                        CoreState.Services.ShowInfo($"MIDI exported to {written}");
                }
            }
            catch (Exception ex)
            {
                // Core Log.Error is params string[] (joined with spaces, NOT
                // composite formatting) — pass the message and the full exception
                // (stack + inner) as separate args, never a "{0}" placeholder
                // (matches LinkInternet_Click above).
                Log.Error("SongTrackView.ExportMidi_Click failed:", ex.ToString());
            }
        }

        // Import dispatcher (#1001 PR1). The single "Import Music File" button now
        // routes by file extension: .mid/.midi -> the existing MIDI import; .wav
        // -> the whole-song WAV import (build a one-track song that plays the
        // sample). .s/.instrument stay routed to a "coming in PR2" message — the
        // .instrument reuse + the .s/SelectInstrument assembler land in PR2.
        async void ImportMidi_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            try
            {
                // #1001 PR2: the dispatcher now also reaches .s (SondFont
                // assembler) and .instrument (instrument-set index), so list them
                // in the combined "Music Files" filter plus dedicated entries.
                var musicType = new FilePickerFileType(R._("Music Files")) { Patterns = new[] { "*.mid", "*.midi", "*.wav", "*.s", "*.instrument" } };
                var midiType = new FilePickerFileType(R._("MIDI Files")) { Patterns = new[] { "*.mid", "*.midi" } };
                var wavType = new FilePickerFileType(R._("Wave Files")) { Patterns = new[] { "*.wav" } };
                var instType = new FilePickerFileType(R._("Instrument Set Files")) { Patterns = new[] { "*.instrument" } };
                var sType = new FilePickerFileType(R._("SondFont Source Files")) { Patterns = new[] { "*.s" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import Music File"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { musicType, midiType, wavType, instType, sType, allType },
                });

                if (files.Count == 0) return;

                // #1639: .mid/.midi and .wav imports are SINGLE files (bridge to a
                // temp on Android). .instrument and .s resolve SIBLING files
                // relative to the chosen path, so they need a real local
                // directory — disable those on SAF with a clear message.
                string ext = U.GetFilenameExt(files[0].Name ?? "");
                string? path;
                if (ext == ".INSTRUMENT" || ext == ".S")
                {
                    path = files[0].TryGetLocalPath();
                    if (string.IsNullOrEmpty(path))
                    {
                        CoreState.Services.ShowError(R._("Importing this format reads multiple files and requires desktop file-system access; it is not available on this device."));
                        return;
                    }
                }
                else
                {
                    path = await FileDialogHelper.ResolveReadPathAsync(files[0]);
                    if (string.IsNullOrEmpty(path)) return;
                }

                // ONE import path: both the file-picker Import and the
                // FE-Repo-Music button route the chosen path through the same
                // extension dispatcher (#1383).
                await ImportMusicPath(path);
            }
            catch (Exception ex)
            {
                Log.Error("ImportMidi_Click failed:", ex.ToString());
            }
        }

        // -----------------------------------------------------------------
        // #1383: the FE-Repo-Music button opens the music-mode FE-Repo browser
        // and feeds the selected music file through the SAME dispatcher as the
        // "Import Music File" button (ImportMusicPath) — no second import path.
        // The button is only visible when the music submodule is checked out
        // (FERepoPickHelper.IsMusicSupported, bound to VM.IsFERepoMusicAvailable).
        // -----------------------------------------------------------------
        async void FERepoMusic_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            try
            {
                string? path = await FERepoPickHelper.PickMusic(this);
                if (string.IsNullOrEmpty(path)) return;
                await ImportMusicPath(path);
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.FERepoMusic_Click failed:", ex.ToString());
            }
        }

        /// <summary>
        /// The single music-import dispatcher (#1001 / #1383). Routes a music
        /// file by extension to the appropriate import sub-flow for the
        /// currently selected song. Shared by the file-picker Import button and
        /// the FE-Repo-Music button.
        /// </summary>
        async System.Threading.Tasks.Task ImportMusicPath(string path)
        {
            if (!_vm.IsLoaded) return;
            try
            {
                if (string.IsNullOrEmpty(path)) return;

                // Dispatch by extension (case-insensitive).
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".wav")
                {
                    await ImportWaveAsSong(path);
                    return;
                }
                if (ext == ".instrument")
                {
                    // #1001 PR2: instrument-set (.instrument index) import — reuse
                    // the merged SongInstrumentSetCore.ImportAll seam + repoint the
                    // song header's voicegroup pointer (+4) at the imported set.
                    await ImportInstrumentSet(path);
                    return;
                }
                if (ext == ".s")
                {
                    // #1001 PR2: .s / SondFont assembler import. The user first
                    // picks the instrument set (pick-and-return), then the source
                    // is assembled + the song-table entry/header repointed.
                    await ImportSondFontSource(path);
                    return;
                }
                // Default: MIDI import (.mid/.midi or any other extension).

                // #1002 Slice 2: pick the instrument set BEFORE importing so the user
                // can choose which voicegroup the converted song uses — mirrors the .s
                // pick-and-return path (ImportSondFontSource). The MIDI writer writes
                // instrumentAddr VERBATIM into the new song header +4 (a GBA POINTER
                // slot), so we must store a POINTER (unlike the .s path where ImportS
                // applies toPointer internally and therefore receives an OFFSET).
                ROM rom = CoreState.ROM;
                if (rom == null) return;

                // 1. Resolve + validate the song-table slot up front.
                uint slot = _vm.GetSelectedSongTableEntryAddr();
                if (slot == U.NOT_FOUND || !U.isSafetyOffset(slot, rom))
                {
                    CoreState.Services.ShowError(R._(
                        "Cannot resolve the song-table entry for the selected song."));
                    return;
                }
                // 2. Dereference + validate the header (+4 = instrument-pointer slot).
                uint songHeaderOffset = rom.p32(slot);
                if (!U.isSafetyOffset(songHeaderOffset, rom)
                    || !U.isSafetyOffset(songHeaderOffset + 4, rom))
                {
                    CoreState.Services.ShowError(R._("The song table has no song header."));
                    return;
                }
                // 3. Seed the picker with the current voicegroup (WF f.Init(P4) parity).
                uint currentVoicegroup = rom.p32(songHeaderOffset + 4);

                // 4. Open the instrument picker as a modal pick-and-return.
                PickResult? pick;
                try
                {
                    pick = await WindowManager.Instance.PickFromEditor<SongTrackImportSelectInstrumentView>(
                        currentVoicegroup, this);
                }
                catch (Exception exPick)
                {
                    Log.ErrorF("SongTrackView.ImportMidi_Click pick failed: {0}", exPick.Message);
                    CoreState.Services.ShowError(R._("MIDI import failed: {0}", exPick.Message));
                    return;
                }
                if (pick == null) return; // user cancelled the picker.

                // 5. Normalize to offset and validate — the instrument list mixes an
                //    OFFSET-valued "Current" seed with toPointer'd discovered rows.
                uint instrumentOffset = U.toOffset(pick.Address);
                if (!U.isSafetyOffset(instrumentOffset, rom))
                {
                    CoreState.Services.ShowError(R._("The selected instrument set address is invalid."));
                    return;
                }
                // 6. Store as POINTER: SongMidiCore.AssembleGBASong writes this value
                //    VERBATIM into the new song header +4 (a GBA pointer slot).
                _vm.InstrumentAddr = U.toPointer(instrumentOffset);

                // Parse and show MIDI metadata preview first so the user can
                // confirm the file before it overwrites the song.
                string preview = _vm.PreviewMidi(path);
                if (preview.StartsWith("Error:"))
                {
                    CoreState.Services.ShowError(preview);
                    return;
                }

                bool confirm = CoreState.Services.ShowQuestion(
                    preview + "\n\n---\n" +
                    "Import this MIDI into the selected song? This appends the " +
                    "converted song data to ROM free space and repoints the " +
                    "song-table entry. The operation is a single undo step.");
                if (!confirm) return;

                // Real write-back under one undo record. ImportMidiFile writes
                // through ambient-undo-aware ROM APIs (write_range + write_u32)
                // and never resizes rom.Data, so a Rollback restores the ROM
                // byte-identical.
                _undoService.Begin("Import MIDI");
                try
                {
                    string? error = _vm.ImportMidi(path, out string summary);
                    if (error != null)
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(error);
                        return;
                    }

                    _undoService.Commit();
                    _vm.MarkClean();
                    // #1014: record the imported MIDI source path under the WF
                    // per-song "Song_" + hex(songId) ResourceCache key so the
                    // Open Source File / Folder buttons become available. WF
                    // records ONLY on a successful import (here: after Commit).
                    // Guard against an unselected song (SelectedSongIndex == -1)
                    // so we never persist under the bogus "Song_FFFFFFFF" key. #1058 review.
                    if (_vm.SelectedSongIndex >= 0)
                        _vm.RecordSourceFile((uint)_vm.SelectedSongIndex, path);
                    UpdateUI();
                    CoreState.Services.ShowInfo(summary);
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error($"ImportMidi_Click write-back failed: {ex.Message}");
                    CoreState.Services.ShowError($"MIDI import failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("SongTrackView.ImportMusicPath failed:", ex.ToString());
            }
        }

        // -----------------------------------------------------------------
        // #1001 PR1: whole-song WAV import. Build an ENTIRE one-track song that
        // plays the imported RIFF WAV sample (sample + 2-row voicegroup +
        // one-track playback stream + song header) and repoint the selected
        // song-table entry at it, in one validate-before-mutate transaction with
        // a byte-identical fault restore (SongTrackWaveImportCore). The song-table
        // slot is the SAME slot the MIDI import repoints
        // (GetSelectedSongTableEntryAddr).
        //
        // useLoop defaults to FALSE for WF parity: WF's import dialog initializes
        // LoopComboBox to index 0 = ループしない (no loop), so the default song
        // ends at FINE with no GOTO (Copilot plan review pt 3).
        // -----------------------------------------------------------------
        async System.Threading.Tasks.Task ImportWaveAsSong(string path)
        {
            // WF parity: SongID 0 is write-protected (silence song).
            if (_vm.SelectedSongIndex == 0)
            {
                CoreState.Services.ShowError(R._("Song ID 0 is write-protected (silence song)."));
                return;
            }

            uint slot = _vm.GetSelectedSongTableEntryAddr();
            if (slot == U.NOT_FOUND)
            {
                CoreState.Services.ShowError(R._(
                    "Cannot resolve the song-table entry for the selected song."));
                return;
            }

            bool confirm = CoreState.Services.ShowQuestion(R._(
                "Import this WAV as a new one-track song? This appends a sample, a " +
                "voicegroup, a playback track and a song header to ROM free space " +
                "and repoints the song-table entry. The operation is a single undo step."));
            if (!confirm) return;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                // File read can throw (permissions / missing) BEFORE any undo
                // scope is open — surface the error, no Rollback needed.
                Log.ErrorF("SongTrackView.ImportWaveAsSong read failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Wave import failed: {0}", ex.Message));
                return;
            }

            _undoService.Begin("Import Wave as Song");
            try
            {
                // useLoop:false for WF parity (no-loop is the WF default).
                uint headerPtr = SongTrackWaveImportCore.ImportWaveAsSong(
                    CoreState.ROM, slot, bytes, useLoop: false, out string err);
                if (headerPtr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(err ?? R._("Wave import failed."));
                    return;
                }

                _undoService.Commit();
                _vm.MarkClean();

                // Repoint the editor at the freshly-built song header so the UI
                // shows the new track immediately.
                if (U.isPointer(headerPtr))
                    _vm.LoadEntry(U.toOffset(headerPtr));

                // Record the imported WAV source path under the WF per-song key so
                // the Open Source File / Folder buttons become available (only on a
                // successful import; guard the unselected-song case).
                if (_vm.SelectedSongIndex >= 0)
                    _vm.RecordSourceFile((uint)_vm.SelectedSongIndex, path);
                UpdateUI();
                CoreState.Services.ShowInfo(R._(
                    "Wave imported as a new song at 0x{0:X08}.", U.toOffset(headerPtr)));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTrackView.ImportWaveAsSong failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Wave import failed: {0}", ex.Message));
            }
        }

        // -----------------------------------------------------------------
        // #1001 PR2: .instrument instrument-set import. Reuse the merged
        // SongInstrumentSetCore.ImportAll seam (recursive, single transaction,
        // validate-before-mutate, byte-identical fault restore) and repoint the
        // selected song header's voicegroup pointer (+4) at the imported set —
        // mirrors WF SongUtil.ImportInstrument. The song-table slot/header are
        // validated BEFORE the import appends anything (Copilot plan finding 3).
        // -----------------------------------------------------------------
        async System.Threading.Tasks.Task ImportInstrumentSet(string path)
        {
            // WF parity: SongID 0 is write-protected (silence song).
            if (_vm.SelectedSongIndex == 0)
            {
                CoreState.Services.ShowError(R._("Song ID 0 is write-protected (silence song)."));
                return;
            }

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Validate the song-table slot + dereferenced header (+4 voicegroup
            // pointer slot) BEFORE the import appends a single byte — a null/invalid
            // header must abort with ZERO mutation (Copilot plan finding 3).
            uint slot = _vm.GetSelectedSongTableEntryAddr();
            if (slot == U.NOT_FOUND || !U.isSafetyOffset(slot, rom))
            {
                CoreState.Services.ShowError(R._(
                    "Cannot resolve the song-table entry for the selected song."));
                return;
            }
            uint songHeaderOffset = rom.p32(slot);
            if (!U.isSafetyOffset(songHeaderOffset, rom)
                || !U.isSafetyOffset(songHeaderOffset + 4, rom))
            {
                CoreState.Services.ShowError(R._("The song table has no song header."));
                return;
            }

            bool confirm = CoreState.Services.ShowQuestion(R._(
                "Import this instrument set and point the selected song at it? This " +
                "appends the voicegroup to ROM free space and repoints the song " +
                "header's instrument pointer. The operation is a single undo step."));
            if (!confirm) return;

            string dir = Path.GetDirectoryName(path) ?? ".";
            string indexName = Path.GetFileName(path);
            // The Core consumes RELATIVE filename tokens; resolve each against the
            // chosen index directory via the path-traversal-safe resolver (rejects
            // absolute / ".."-escaping tokens — mirrors SongInstrumentView).
            Func<string, string[]?> readLines = name =>
            {
                string? p = ResolveInside(dir, name);
                return p != null && File.Exists(p) ? File.ReadAllLines(p) : null;
            };
            Func<string, byte[]?> readFile = name =>
            {
                string? p = ResolveInside(dir, name);
                return p != null && File.Exists(p) ? File.ReadAllBytes(p) : null;
            };

            _undoService.Begin("Import Instrument Set");
            try
            {
                // Route the Core appender through the real freespace allocator under
                // the ambient undo scope just opened.
                Func<byte[], uint> appender = buf => AppendBinaryDataHeadless(rom, buf);
                uint importedBase = SongInstrumentSetCore.ImportAll(
                    rom, indexName, readLines!, readFile!, appender, out string err);
                if (importedBase == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(err ?? R._("Instrument set import failed."));
                    return;
                }

                // Repoint the selected song header's voicegroup pointer (+4) at the
                // imported set. write_p32 takes the slot OFFSET + target OFFSET.
                rom.write_p32(songHeaderOffset + 4, importedBase);

                _undoService.Commit();
                _vm.MarkClean();
                if (_vm.SelectedSongIndex >= 0)
                    _vm.RecordSourceFile((uint)_vm.SelectedSongIndex, path);
                // Reload so the InstrumentAddr field reflects the new voicegroup.
                _vm.LoadEntry(songHeaderOffset);
                UpdateUI();
                CoreState.Services.ShowInfo(R._(
                    "Instrument set imported at 0x{0:X08}.", importedBase));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTrackView.ImportInstrumentSet failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Instrument set import failed: {0}", ex.Message));
            }
        }

        // -----------------------------------------------------------------
        // #1001 PR2: .s / SondFont assembler import. The user FIRST picks the
        // instrument set via the SongTrackImportSelectInstrument browser as a
        // pick-and-return (#1002 — the picker now feeds the import pipeline); the
        // picked address (normalized to an OFFSET + validated — Copilot plan
        // finding 1) becomes selectedInstrumentAddr. Then SongTrackSImportCore
        // assembles the .s and repoints the song-table entry/header in one
        // validate-before-mutate transaction with a byte-identical fault restore.
        // -----------------------------------------------------------------
        async System.Threading.Tasks.Task ImportSondFontSource(string path)
        {
            // WF parity: SongID 0 is write-protected (silence song).
            if (_vm.SelectedSongIndex == 0)
            {
                CoreState.Services.ShowError(R._("Song ID 0 is write-protected (silence song)."));
                return;
            }

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Validate the song-table slot + dereferenced header BEFORE picking /
            // mutating (so we never open the picker for an unimportable song).
            uint slot = _vm.GetSelectedSongTableEntryAddr();
            if (slot == U.NOT_FOUND || !U.isSafetyOffset(slot, rom))
            {
                CoreState.Services.ShowError(R._(
                    "Cannot resolve the song-table entry for the selected song."));
                return;
            }
            uint songHeaderOffset = rom.p32(slot);
            if (!U.isSafetyOffset(songHeaderOffset, rom)
                || !U.isSafetyOffset(songHeaderOffset + 4, rom))
            {
                CoreState.Services.ShowError(R._("The song table has no song header."));
                return;
            }

            // Seed the picker with the song's CURRENT voicegroup (WF f.Init(P4)) so
            // the "Current" row is meaningful (Copilot plan finding 2). p32 returns
            // an offset; 0/invalid simply lands on the first discovered set.
            uint currentVoicegroup = rom.p32(songHeaderOffset + 4);

            PickResult? pick;
            try
            {
                pick = await WindowManager.Instance.PickFromEditor<SongTrackImportSelectInstrumentView>(
                    currentVoicegroup, this);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.ImportSondFontSource pick failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._(".s import failed: {0}", ex.Message));
                return;
            }
            if (pick == null) return; // user cancelled the picker.

            // Normalize the picked address to an OFFSET and validate it (the
            // instrument list mixes the offset "Current" seed with toPointer'd
            // discovered rows — Copilot plan finding 1).
            uint instrumentOffset = U.toOffset(pick.Address);
            if (!U.isSafetyOffset(instrumentOffset, rom))
            {
                CoreState.Services.ShowError(R._("The selected instrument set address is invalid."));
                return;
            }

            bool confirm = CoreState.Services.ShowQuestion(R._(
                "Assemble this .s source into the selected song using the chosen " +
                "instrument set? This appends the assembled song data to ROM free " +
                "space and repoints the song-table entry. The operation is a single undo step."));
            if (!confirm) return;

            _undoService.Begin("Import .s");
            try
            {
                uint headerBase = SongTrackSImportCore.ImportS(
                    rom, path, slot, instrumentOffset,
                    File.ReadAllLines, buf => AppendBinaryDataHeadless(rom, buf),
                    out string err);
                if (headerBase == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(err ?? R._(".s import failed."));
                    return;
                }

                _undoService.Commit();
                _vm.MarkClean();
                if (_vm.SelectedSongIndex >= 0)
                    _vm.RecordSourceFile((uint)_vm.SelectedSongIndex, path);
                // Reload from the freshly-built song header.
                _vm.LoadEntry(headerBase);
                UpdateUI();
                CoreState.Services.ShowInfo(R._(
                    ".s assembled into the song at 0x{0:X08}.", headerBase));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTrackView.ImportSondFontSource failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._(".s import failed: {0}", ex.Message));
            }
        }

        // Headless equivalent of InputFormRef.AppendBinaryData: routes through the
        // registered CoreState.AppendBinaryData delegate under the active ambient
        // undo scope (mirrors SongInstrumentViewModel.AppendBinaryDataHeadless).
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer)
        {
            var allocator = CoreState.AppendBinaryData;
            if (allocator == null) return U.NOT_FOUND;
            var ambient = ROM.GetAmbientUndoData();
            if (ambient == null) return U.NOT_FOUND;
            return allocator(buffer, ambient);
        }

        // Resolve a relative side/nested-index token against the chosen import
        // directory, REJECTING (returns null) an absolute path or a ".."-escaping
        // token so a hand-edited TSV can never read outside the selected directory
        // (mirrors SongInstrumentView.ResolveInside — path traversal).
        internal static string? ResolveInside(string dir, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Path.IsPathRooted(name)) return null;
            string baseFull = Path.GetFullPath(dir);
            string candidate = Path.GetFullPath(Path.Combine(baseFull, name));
            var cmp = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string prefix = baseFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? baseFull
                : baseFull + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, cmp)
                && !string.Equals(candidate, baseFull, cmp))
                return null;
            return candidate;
        }

        // -----------------------------------------------------------------
        // #1014: Open Source File / Open Source Folder. Mirrors
        // WorldMapImageView.OpenSource_Click / SelectSource_Click. The recorded
        // path lives in CoreState.ResourceCache under the WF PER-SONG
        // "Song_" + hex(songId) key (see SongTrackViewModel). Buttons are gated
        // visible only when IsSourceFileAvailable.
        // -----------------------------------------------------------------

        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                if (!File.Exists(_vm.SourceFilePath))
                {
                    // The recorded path no longer exists — clear availability so
                    // the buttons hide (IsVisible binding) and report the cause.
                    _vm.IsSourceFileAvailable = false;
                    CoreState.Services?.ShowError($"Source file not found: {_vm.SourceFilePath}");
                    return;
                }
                var psi = new ProcessStartInfo(_vm.SourceFilePath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.OpenSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source file: {ex.Message}");
            }
        }

        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                if (!File.Exists(_vm.SourceFilePath))
                {
                    _vm.IsSourceFileAvailable = false;
                    CoreState.Services?.ShowError($"Source file not found: {_vm.SourceFilePath}");
                    return;
                }
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("explorer.exe",
                        $"/select,\"{_vm.SourceFilePath}\"")
                        { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    string? folder = Path.GetDirectoryName(_vm.SourceFilePath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var psi = new ProcessStartInfo(folder) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackView.SelectSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source folder: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        /// <summary>
        /// #1383: import a music file into the song at <paramref name="songHeaderAddr"/>
        /// (the song-header OFFSET, matching each list item's address) through the
        /// ONE shared dispatcher (ImportMusicPath). Used by the Avalonia Song
        /// Exchange tool's FE-Repo-Music button, which navigates here for the
        /// selected destination song and then hands off the chosen path — so both
        /// Song editors funnel through the exact same import code path, with no
        /// duplicate importer.
        ///
        /// Robustness (#1399 review): the editor's list is populated on Opened,
        /// which may not have run yet when this is called right after Navigate.
        /// So we ensure the list is loaded, actively select the requested song by
        /// its header address, and FAIL CLOSED (no import) if that song cannot be
        /// selected — never import into the wrong/default song.
        /// </summary>
        public async System.Threading.Tasks.Task ImportMusicFromExternal(uint songHeaderAddr, string path)
        {
            // Ensure the list has been populated (Opened -> LoadList may not have
            // fired yet for a freshly-navigated window).
            if (!_vm.IsLoaded && EntryList.GetItems().Count == 0)
            {
                LoadList();
                // Give the dispatcher a turn so the ListBox realizes the items
                // before we try to select one.
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            }

            // Actively select the requested destination song by its header
            // address; bail out (no import) if it is not present.
            if (!EntryList.SelectAddress(songHeaderAddr))
            {
                CoreState.Services.ShowError(R._(
                    "Could not select the destination song for import."));
                return;
            }
            if (!_vm.IsLoaded)
            {
                CoreState.Services.ShowError(R._(
                    "The destination song could not be loaded for import."));
                return;
            }

            await ImportMusicPath(path);
        }
    }
}
