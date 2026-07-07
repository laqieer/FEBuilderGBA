// SPDX-License-Identifier: GPL-3.0-or-later
// SongInstrumentView — Avalonia parity rebuild for #387. Mirrors
// `SongInstrumentForm` layout (panel1 read-config + AddressPanel master-write
// + panel2 common-header + 14 instrument tabs + panel4 fingerprint footer).
// The single WF cross-editor jump callsite
// (`SongInstrumentImportWaveForm`) stays deferred and explicitly absent from
// both the manifest and the click-handler wiring — see
// `SongInstrumentViewModel.NavigationTargets.cs`.
using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongInstrumentView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SongInstrumentViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Instrument Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Instrument Editor", 1200, 780, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        // Header bytes that map to the 14 WF UNIONTAB_Nxx pages, in tab order.
        // Used to populate TypeCombo and to map combo selection -> HeaderByte.
        static readonly byte[] TypeComboHeaderBytes = {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x08, 0x09,
            0x0A, 0x0B, 0x0C, 0x10, 0x18, 0x40, 0x80,
        };

        bool _isUpdatingUi;
        // True once the constructor finishes wiring TypeCombo / HeaderByteBox.
        // Avalonia raises SelectionChanged for both UnionTab and TypeCombo
        // during XAML EndInit() — before our event-wired controls are
        // resolved — and that fires NREs in the handlers. Gate both handlers
        // on this flag so we only respond to real user actions.
        bool _viewReady;

        public SongInstrumentView()
        {
            InitializeComponent();
            PopulateTypeCombo();
            EntryList.SelectedAddressChanged += OnSelected;
            _viewReady = true;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void PopulateTypeCombo()
        {
            // Populate the Type combo with the 14 WF instrument-type labels
            // in canonical tab order. Selection drives HeaderByte +
            // UNIONTAB selection through TypeCombo_SelectionChanged. The
            // type-name strings are wrapped with R._() so the ja/zh
            // translations in config/translate/* localize the combo at
            // runtime (Copilot review PR #626 round 2 finding — combo
            // items aren't reached by ViewTranslationHelper).
            TypeCombo.ItemsSource = null;
            var items = new System.Collections.Generic.List<string>(TypeComboHeaderBytes.Length);
            foreach (var b in TypeComboHeaderBytes)
            {
                string typeName = SongInstrumentViewModel.GetInstrumentTypeName(b);
                items.Add($"0x{b:X02} {R._(typeName)}");
            }
            TypeCombo.ItemsSource = items;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                // Honor the read-config bar inputs when present (mirrors WF
                // panel1: First Address / Read Count). When ReadStartAddress
                // is non-zero the user-edited base overrides the auto-
                // resolved first-song voicegroup. When ReadCount is non-zero
                // it caps the scan window (LoadInstrumentList stops at the
                // smaller of the cap and the first invalid instrument).
                // Both fall back to defaults when unset (Copilot review
                // PR #626 round 2 finding #2 + round 3 finding (b)).
                // #649: editable inputs unified via EditorTopBarWithInputs.
                uint userBase = TopBar?.ReadStartAddress ?? 0u;
                uint userCount = (uint)(TopBar?.ReadCount ?? 0);
                if (userBase != 0)
                {
                    var explicitItems = _vm.LoadInstrumentList(userBase, userCount);
                    EntryList.SetItems(explicitItems);
                }
                else
                {
                    var items = _vm.LoadList();
                    // Apply the count cap to the auto-resolved list as well,
                    // so the read-config bar's ReadCount works whether the
                    // user supplied an explicit base or not. Cap > items
                    // count is a no-op. Clamp userCount to int.MaxValue
                    // so the int-vs-uint comparison stays sign-safe even
                    // for pathological inputs.
                    int userCountInt = userCount > int.MaxValue ? int.MaxValue : (int)userCount;
                    if (userCountInt > 0 && items.Count > userCountInt)
                        items = items.GetRange(0, userCountInt);
                    EntryList.SetItems(items);
                    // Surface auto-detected base back into the read-config bar
                    // so subsequent Reload clicks have a non-zero starting
                    // value the user can edit.
                    if (_vm.BaseAddr != 0 && TopBar != null)
                        TopBar.ReadStartAddress = _vm.BaseAddr;
                }
                BlockSizeBox.Text = "12";

                // Apply the Filter textbox (a parallel WF-style filter that
                // routes through AddressListControl.ApplySearchFilter).
                string? filter = FilterBox?.Text;
                if (!string.IsNullOrWhiteSpace(filter))
                    EntryList.ApplySearchFilter(filter);

                // Enable Expand List only when a song-context voicegroup with
                // a defined-prefix < 128 is loaded (#780).
                UpdateExpandButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongInstrumentView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        /// <summary>
        /// Reload the instrument list from a specific base address (e.g., from SongTrack).
        /// </summary>
        public void JumpToAddr(uint baseAddr)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadInstrumentList(baseAddr);
                EntryList.SetItems(items);
                if (items.Count > 0)
                    EntryList.SelectFirst();
                UpdateExpandButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongInstrumentView.JumpToAddr failed: {0}", ex.Message);
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
                Log.ErrorF("SongInstrumentView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        // #649: routed event from the unified EditorTopBarWithInputs Reload
        // button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        /// <summary>
        /// Enable the Expand List button only when a song-context voicegroup
        /// with a defined-prefix instrument count &lt; 128 is loaded (#780).
        /// Other states (no song context / already 128 / no ROM) keep it
        /// disabled — mirrors how the other editors gate Expand/NewAlloc on
        /// the current selection.
        /// </summary>
        void UpdateExpandButtonState()
        {
            if (ListExpandButton != null)
                ListExpandButton.IsEnabled = _vm.CanExpandVoicegroup;
        }

        /// <summary>
        /// Grow the loaded voicegroup to 128 instruments (#780). Mirrors
        /// <c>MapExitPointView.ExpandList_Click</c>: open an undo scope, call
        /// the VM, roll back + notify on refusal, otherwise commit, reload the
        /// instrument list so all 128 rows show, and report the new base.
        /// </summary>
        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanExpandVoicegroup) return;

            _undoService.Begin("SongInstrument ExpandVoicegroup");
            try
            {
                if (!_vm.ExpandVoicegroupTo128(_undoService.GetActiveUndoData()))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(
                        "Could not expand this voicegroup to 128 instruments " +
                        "(no song context, already full, or no free space).");
                    return;
                }
                uint newBase = _vm.BaseAddr;
                _undoService.Commit();
                _vm.MarkClean();

                // Re-resolve + re-read so the 128 rows show. The VM re-anchored
                // its song context (and BaseAddr) onto the relocated base. The
                // read-config bar's First Address still holds the OLD base
                // (surfaced during the initial LoadList), and LoadList prefers
                // that explicit value — so sync it to the new base first,
                // otherwise we would re-list the now-wiped old block.
                if (TopBar != null)
                    TopBar.ReadStartAddress = newBase;
                LoadList();
                UpdateExpandButtonState();

                CoreState.Services?.ShowInfo(
                    $"Expanded voicegroup to 128 instruments at 0x{newBase:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongInstrumentView.ListExpand_Click failed: {0}", ex.Message);
            }
        }

        void FilterBox_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
        {
            // Enter key applies the filter (mirrors WF SearchBox UX). Other
            // keys are no-ops so per-keystroke filter pressure doesn't
            // hammer the master list.
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                EntryList.ApplySearchFilter(FilterBox?.Text);
                e.Handled = true;
            }
        }

        void UpdateUI()
        {
            // Guard against re-entrant Combo SelectionChanged that would
            // otherwise overwrite the VM with the stale combo value while
            // we are still propagating the VM update down into the UI.
            _isUpdatingUi = true;
            try
            {
                SelectedAddressLabel.Text = $"0x{_vm.CurrentAddr:X08}";
                AddressBox.Value = _vm.CurrentAddr;
                HeaderByteBox.Value = _vm.HeaderByte;
                MoreInfoBox.Text = _vm.TypeName;

                // Populate FINGERPRINT footer so it actually changes per
                // selection (Copilot review PR #626 round 2 finding #5 — the
                // footer was declared but never populated, always blank).
                if (FingerprintBox != null)
                    FingerprintBox.Text = _vm.ComputeFingerprint();

                // Sync the Type combo with the loaded HeaderByte so it
                // always shows the same instrument-type label as the active
                // tab and the More Info text. -1 = unknown (off-list byte).
                int comboIndex = -1;
                for (int i = 0; i < TypeComboHeaderBytes.Length; i++)
                {
                    if (TypeComboHeaderBytes[i] == _vm.HeaderByte)
                    {
                        comboIndex = i;
                        break;
                    }
                }
                TypeCombo.SelectedIndex = comboIndex;

                // Select the exact tab by header byte (#387 plan review v2 concern #1).
                string expectedTabId = SongInstrumentViewModel.GetExpectedTabId(_vm.HeaderByte);
                SelectTabByAutomationId(expectedTabId);

                // Populate per-tab raw byte fields. Each tab has its own set of
                // Nxx_Bn / Nxx_Pn controls; we update all 14 tabs from the same
                // VM raw fields so switching tabs (user changes Type combo) does
                // not show stale data.
                string[] tabNames = { "N00", "N01", "N02", "N03", "N04", "N08", "N09",
                                      "N0A", "N0B", "N0C", "N10", "N18", "N40", "N80" };
                foreach (var tab in tabNames)
                    PopulateTabFields(tab);
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        void TypeCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (!_viewReady || _isUpdatingUi) return;
            int idx = TypeCombo.SelectedIndex;
            if (idx < 0 || idx >= TypeComboHeaderBytes.Length) return;

            // User picked an instrument type from the combo. Mirror WinForms
            // behavior: update HeaderByte, re-classify, refresh TypeName /
            // More Info, and switch the visible UNIONTAB. Per-byte values are
            // left as-is so the user can still type B1..B11 raw bytes before
            // pressing Write.
            byte newHeader = TypeComboHeaderBytes[idx];
            if (_vm.HeaderByte == newHeader) return;

            _isUpdatingUi = true;
            try
            {
                _vm.HeaderByte = newHeader;
                _vm.Category = SongInstrumentViewModel.ClassifyType(newHeader);
                _vm.TypeName = SongInstrumentViewModel.GetInstrumentTypeName(newHeader);

                HeaderByteBox.Value = newHeader;
                MoreInfoBox.Text = _vm.TypeName;

                string expectedTabId = SongInstrumentViewModel.GetExpectedTabId(newHeader);
                SelectTabByAutomationId(expectedTabId);
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        void UnionTab_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (!_viewReady || _isUpdatingUi) return;
            // User clicked a different tab directly. Mirror the type-combo
            // path so HeaderByte / TypeName / TypeCombo stay in lockstep.
            if (UnionTab.SelectedItem is not TabItem ti) return;
            var aid = global::Avalonia.Automation.AutomationProperties.GetAutomationId(ti);
            if (string.IsNullOrEmpty(aid)) return;

            // Resolve "SongInstrument_UNIONTAB_Nxx_Tab" -> byte 0xNN.
            byte? newHeader = null;
            for (int i = 0; i < TypeComboHeaderBytes.Length; i++)
            {
                string expected = SongInstrumentViewModel.GetExpectedTabId(TypeComboHeaderBytes[i]);
                if (aid == expected) { newHeader = TypeComboHeaderBytes[i]; break; }
            }
            if (newHeader == null || _vm.HeaderByte == newHeader.Value) return;

            _isUpdatingUi = true;
            try
            {
                _vm.HeaderByte = newHeader.Value;
                _vm.Category = SongInstrumentViewModel.ClassifyType(newHeader.Value);
                _vm.TypeName = SongInstrumentViewModel.GetInstrumentTypeName(newHeader.Value);

                HeaderByteBox.Value = newHeader.Value;
                MoreInfoBox.Text = _vm.TypeName;

                // Sync the TypeCombo to match the new tab so the two views
                // never diverge.
                int comboIndex = -1;
                for (int i = 0; i < TypeComboHeaderBytes.Length; i++)
                {
                    if (TypeComboHeaderBytes[i] == newHeader.Value)
                    {
                        comboIndex = i;
                        break;
                    }
                }
                TypeCombo.SelectedIndex = comboIndex;
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        void SelectTabByAutomationId(string targetTabId)
        {
            if (string.IsNullOrEmpty(targetTabId)) return;
            foreach (var item in UnionTab.Items)
            {
                if (item is TabItem ti)
                {
                    var aid = global::Avalonia.Automation.AutomationProperties.GetAutomationId(ti);
                    if (aid == targetTabId)
                    {
                        UnionTab.SelectedItem = ti;
                        return;
                    }
                }
            }
        }

        void PopulateTabFields(string tabPrefix)
        {
            // Find and set per-byte numeric inputs for this tab.
            // B1..B11 raw values come straight from _vm.B1..B11.
            SetNumericByName($"{tabPrefix}_B1_Box", _vm.B1);
            SetNumericByName($"{tabPrefix}_B2_Box", _vm.B2);
            SetNumericByName($"{tabPrefix}_B3_Box", _vm.B3);
            SetNumericByName($"{tabPrefix}_B4_Box", _vm.B4);
            SetNumericByName($"{tabPrefix}_B5_Box", _vm.B5);
            SetNumericByName($"{tabPrefix}_B6_Box", _vm.B6);
            SetNumericByName($"{tabPrefix}_B7_Box", _vm.B7);
            SetNumericByName($"{tabPrefix}_B8_Box", _vm.B8);
            SetNumericByName($"{tabPrefix}_B9_Box", _vm.B9);
            SetNumericByName($"{tabPrefix}_B10_Box", _vm.B10);
            SetNumericByName($"{tabPrefix}_B11_Box", _vm.B11);

            // P4 / P8 — instrument-type-specific u32 pointers.
            switch (tabPrefix)
            {
                case "N00": case "N08": case "N10": case "N18":
                case "N03": case "N0B":
                    SetNumericByName($"{tabPrefix}_P4_Box", _vm.WavePtr);
                    break;
                case "N04": case "N0C":
                    SetNumericByName($"{tabPrefix}_P4_Box", _vm.Period);
                    break;
                case "N40":
                    SetNumericByName($"{tabPrefix}_P4_Box", _vm.KeyMapPtr);
                    SetNumericByName($"{tabPrefix}_P8_Box", _vm.SubInstrPtr);
                    break;
                case "N80":
                    SetNumericByName($"{tabPrefix}_P4_Box", _vm.SubInstrPtr);
                    break;
            }
        }

        void SetNumericByName(string name, decimal value)
        {
            var ctrl = this.FindControl<NumericUpDown>(name);
            if (ctrl != null) ctrl.Value = value;
        }

        decimal? GetNumericByName(string name)
        {
            var ctrl = this.FindControl<NumericUpDown>(name);
            return ctrl?.Value;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit Instrument");
            try
            {
                _vm.HeaderByte = (byte)(HeaderByteBox.Value ?? 0);

                // Re-classify in case the user changed the header byte.
                var newCat = SongInstrumentViewModel.ClassifyType(_vm.HeaderByte);
                _vm.Category = newCat;
                _vm.TypeName = SongInstrumentViewModel.GetInstrumentTypeName(_vm.HeaderByte);

                // Pull values from the active tab's per-byte controls.
                string tabPrefix = GetActiveTabPrefix(_vm.HeaderByte);
                if (!string.IsNullOrEmpty(tabPrefix))
                    ReadTabFields(tabPrefix);

                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Instrument data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongInstrumentView.Write_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // DirectSound wave Export/Import (#1057 N00/N08; #1001 PR1 N10/N18). The
        // N00 (0x00), N08 (0x08 "DirectSound Fixed Freq"), N10 (0x10 "DirectSound
        // Reverse") and N18 (0x18 "DirectSound Fixed Freq Reverse") voices ALL
        // share the EXACT same on-ROM sample format (header type byte at +0,
        // sample pointer P4 at +4) — the "Reverse"/"Fixed Freq" variants only
        // change the instrument-entry PLAYBACK type, not the P4 sample header/body
        // (Copilot-confirmed). So all four reuse SongDirectSoundWavCore.ExportWave
        // / ImportWave verbatim. N03 (Wave Memory) stays DISABLED.
        //
        // The gates use the VM's LoadedHeaderByte (the on-ROM byte captured at
        // LoadEntry), NOT the mutable HeaderByte/Category, so a loaded 0x10/0x18
        // entry switched to the N08 tab in-memory cannot be repointed as if it
        // were a 0x08 voice (#1057 Copilot plan review pt 1). The shared
        // ExportWaveGated / ImportWaveGated helpers take the active tab's P4 box
        // name so each tab updates its OWN P4 box (N00->N00_P4_Box, ...,
        // N18->N18_P4_Box).
        // -----------------------------------------------------------------

        async void N00_Export_Click(object? sender, RoutedEventArgs e)
        {
            // Gate on the LOADED ROM byte (0x00), not the mutable category.
            await ExportWaveGated(_vm.IsLoadedDirectSound);
        }

        async void N00_Import_Click(object? sender, RoutedEventArgs e)
        {
            await ImportWaveGated(_vm.IsLoadedDirectSound, "N00_P4_Box");
        }

        async void N08_Export_Click(object? sender, RoutedEventArgs e)
        {
            // Gate on the LOADED ROM byte 0x08.
            await ExportWaveGated(_vm.IsLoadedDirectSoundFixedFreq);
        }

        async void N08_Import_Click(object? sender, RoutedEventArgs e)
        {
            // Import slot is THIS entry's own P4 (CurrentAddr + 4) and the success
            // update targets N08_P4_Box (not N00_P4_Box).
            await ImportWaveGated(_vm.IsLoadedDirectSoundFixedFreq, "N08_P4_Box");
        }

        async void N10_Export_Click(object? sender, RoutedEventArgs e)
        {
            // N10 (DirectSound Reverse, 0x10): same DirectSound sample layout as
            // N00/N08 (Copilot-confirmed). Gate on the LOADED ROM byte 0x10.
            await ExportWaveGated(_vm.IsLoadedDirectSoundReverse);
        }

        async void N10_Import_Click(object? sender, RoutedEventArgs e)
        {
            // Import slot is THIS entry's own P4 (CurrentAddr + 4); success updates
            // the N10 tab's OWN P4 box.
            await ImportWaveGated(_vm.IsLoadedDirectSoundReverse, "N10_P4_Box");
        }

        async void N18_Export_Click(object? sender, RoutedEventArgs e)
        {
            // N18 (DirectSound Fixed Freq Reverse, 0x18): same DirectSound sample
            // layout as N00/N08/N10. Gate on the LOADED ROM byte 0x18.
            await ExportWaveGated(_vm.IsLoadedDirectSoundFixedFreqReverse);
        }

        async void N18_Import_Click(object? sender, RoutedEventArgs e)
        {
            await ImportWaveGated(_vm.IsLoadedDirectSoundFixedFreqReverse, "N18_P4_Box");
        }

        // Shared DirectSound wave EXPORT used by N00 + N08. Reads the pointer value
        // _vm.WavePtr (= rom.u32(CurrentAddr+4)) and decodes it via the Core seam.
        async System.Threading.Tasks.Task ExportWaveGated(bool gate)
        {
            if (!_vm.IsLoaded) return;
            if (!gate)
            {
                CoreState.Services.ShowError(
                    R._("This instrument is not a DirectSound voice; there is no wave sample to export."));
                return;
            }

            try
            {
                byte[] wav = SongDirectSoundWavCore.ExportWave(CoreState.ROM, _vm.WavePtr);
                if (wav == null)
                {
                    CoreState.Services.ShowError(
                        R._("Could not decode the DirectSound sample. The wave pointer may be invalid."));
                    return;
                }

                string suggested = $"wave_0x{_vm.CurrentAddr:X06}.wav";
                // #1639: single-file WAV export → SAF bridge.
                string? written = await FileDialogHelper.SaveFileVia(
                    TopLevel.GetTopLevel(this) as Window, R._("Export Wave"), R._("Wave Files"), "*.wav", suggested, p => File.WriteAllBytes(p, wav));
                if (written == null) return;
                CoreState.Services.ShowInfo(R._("Wave exported to {0}", written));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongInstrumentView.ExportWaveGated failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Wave export failed: {0}", ex.Message));
            }
        }

        // Shared DirectSound wave IMPORT used by N00 + N08. Imports a WAV as a new
        // GBA sample (append + P4 repoint), under the view's undo scope, then
        // updates the active tab's P4 box (p4BoxName).
        async System.Threading.Tasks.Task ImportWaveGated(bool gate, string p4BoxName)
        {
            if (!_vm.IsLoaded) return;
            if (!gate)
            {
                CoreState.Services.ShowError(
                    R._("This instrument is not a DirectSound voice; a wave sample cannot be imported here."));
                return;
            }

            try
            {
                string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this), R._("Import Wave"), "*.wav");
                if (string.IsNullOrEmpty(path)) return;

                byte[] bytes = File.ReadAllBytes(path);

                // #1448: open the conversion dialog (sox resample / DPCM / SNR
                // preview), seeded with the chosen .wav, and await the ready GBA
                // DirectSound sample bytes. Cancel returns null => strict no-op
                // (no ROM mutation, no fallback import — #1448 review pt 2).
                var dlg = new SongInstrumentImportWaveView();
                dlg.Seed(bytes, System.IO.Path.GetFileName(path));
                byte[]? sample = await dlg.ShowDialog<byte[]?>(TopLevel.GetTopLevel(this) as Window);
                if (sample == null || sample.Length == 0) return; // cancelled / failed in-dialog

                _undoService.Begin("Import DirectSound Wave");
                try
                {
                    // P4 wave-pointer slot = THIS voice entry +4 (passed as OFFSET;
                    // ImportSampleBytes converts it to a GBA pointer via write_p32).
                    // The dialog already encoded the sample (raw 8-bit or DPCM), so
                    // we append the bytes verbatim — NOT re-running WavToByte (which
                    // would corrupt a DPCM sample).
                    uint newPtr = SongDirectSoundWavCore.ImportSampleBytes(
                        CoreState.ROM, _vm.CurrentAddr + 4, sample, out string err);
                    if (newPtr == U.NOT_FOUND)
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(err ?? R._("Wave import failed."));
                        return;
                    }

                    _vm.WavePtr = newPtr;
                    SetNumericByName(p4BoxName, _vm.WavePtr);
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo(R._("Wave imported. The sample pointer is now 0x{0:X08}.", newPtr));
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.ErrorF("SongInstrumentView.ImportWaveGated failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("Wave import failed: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                // Pre-Begin scope: the file dialog / File.ReadAllBytes can throw
                // (permissions, missing/locked file) BEFORE _undoService.Begin, so
                // no undo scope is open here and no Rollback is needed. Surface a
                // user-facing error instead of failing silently (Copilot review).
                Log.ErrorF("SongInstrumentView.ImportWaveGated: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Wave import failed: {0}", ex.Message));
            }
        }

        // -----------------------------------------------------------------
        // InstExport (#1057) — recursive READ-ONLY voicegroup export. Writes a TSV
        // index of the loaded voicegroup plus per-voice side files via the Core
        // seam SongInstrumentSetCore.ExportAll. Read-only — NO UndoService. The
        // Core emits RELATIVE filename tokens; the delegates resolve them against
        // the chosen index file's directory (#1057 Copilot plan review pt 3).
        // The recursive InstImport stays deferred to PR 2.
        // -----------------------------------------------------------------
        async void InstExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            if (CoreState.ROM == null) return;

            // The voicegroup base the editor is currently editing.
            uint vocaBase = _vm.BaseAddr;
            if (vocaBase == 0)
            {
                CoreState.Services.ShowError(
                    R._("No instrument set (voicegroup) is loaded to export."));
                return;
            }

            try
            {
                // #1639: instrument-set export writes SIBLING files next to the
                // chosen path, so require a real local path; a SAF pick (no local
                // path) cannot place siblings → message on Android, never silent.
                string? path = await FileDialogHelper.SaveFile(
                    TopLevel.GetTopLevel(this) as Window, R._("Export Instrument"), R._("Instrument Set"), "*.instrument",
                    $"voicegroup_0x{vocaBase:X06}.instrument");
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Instrument-set export writes multiple files and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                string dir = Path.GetDirectoryName(path) ?? ".";
                string baseName = Path.GetFileNameWithoutExtension(path);

                SongInstrumentSetCore.ExportAll(
                    CoreState.ROM, vocaBase, baseName,
                    // writeFile / writeLines resolve the relative Core token against
                    // the chosen index directory so all side + nested files land
                    // next to the .instrument index (never the process CWD, never an
                    // absolute path inside the TSV).
                    (name, bytes) => File.WriteAllBytes(Path.Combine(dir, name), bytes),
                    (name, lines) => File.WriteAllLines(Path.Combine(dir, name), lines));

                CoreState.Services.ShowInfo(R._("Instrument set exported to {0}", path));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongInstrumentView.InstExport_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Instrument set export failed: {0}", ex.Message));
            }
        }

        // -----------------------------------------------------------------
        // InstImport (#1057 PR2) — recursive ROM-MUTATING voicegroup import. The
        // inverse of InstExport: open a TSV index file, resolve its directory, then
        // read its side + nested files relative to that directory and append the
        // whole imported set to free space (single transaction, validate-before-
        // mutate, byte-identical fault restore — all in the Core seam
        // SongInstrumentSetCore.ImportAll), repointing the loaded voicegroup's song
        // reference(s) to the new base. Mirrors the N00/InstExport handlers'
        // try/catch + pre-Begin file-dialog-exception structure.
        // -----------------------------------------------------------------
        async void InstImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            if (CoreState.ROM == null) return;

            // The voicegroup base the editor is currently editing.
            uint vocaBase = _vm.BaseAddr;
            if (vocaBase == 0)
            {
                CoreState.Services.ShowError(
                    R._("No instrument set (voicegroup) is loaded to import into."));
                return;
            }

            try
            {
                // #1639: the .instrument index resolves nested/sibling files from
                // its OWN directory (dir below), so require a real local path; a
                // one-file SAF temp copy would break sibling resolution → message
                // on Android, never silent.
                string? path = await FileDialogHelper.OpenFile(
                    TopLevel.GetTopLevel(this) as Window, R._("Import Instrument"), "*.instrument", requireLocalPath: true);
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Importing an instrument set reads sibling files and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                string dir = Path.GetDirectoryName(path) ?? ".";
                string indexName = Path.GetFileName(path);

                // The Core emits/consumes RELATIVE filename tokens; resolve each
                // against the chosen index directory (never the process CWD, never
                // an absolute path inside the TSV). ResolveInside REJECTS an absolute
                // path or a ".."-escaping token (Copilot review — path traversal): a
                // rejected/missing token returns null so the Core reports it cleanly
                // during the validate phase (NO read outside the chosen directory).
                Func<string, string[]> readLines = name =>
                {
                    string? p = ResolveInside(dir, name);
                    return p != null && File.Exists(p) ? File.ReadAllLines(p) : null;
                };
                Func<string, byte[]> readFile = name =>
                {
                    string? p = ResolveInside(dir, name);
                    return p != null && File.Exists(p) ? File.ReadAllBytes(p) : null;
                };

                _undoService.Begin("Import Instrument Set");
                try
                {
                    if (!_vm.ImportLoadedVoicegroup(indexName, readLines, readFile,
                            out uint newBase, out string err))
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(
                            err ?? R._("Instrument set import failed."));
                        return;
                    }

                    _undoService.Commit();
                    _vm.MarkClean();

                    // Re-list from the new base so all imported rows show. The
                    // read-config bar's First Address still holds the OLD base, and
                    // LoadList prefers that explicit value, so sync it first.
                    if (TopBar != null)
                        TopBar.ReadStartAddress = newBase;
                    LoadList();
                    UpdateExpandButtonState();

                    CoreState.Services.ShowInfo(R._(
                        "Instrument set imported at 0x{0:X08}.", newBase));
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.ErrorF("SongInstrumentView.InstImport_Click failed: {0}", ex.Message);
                    CoreState.Services.ShowError(R._("Instrument set import failed: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                // Pre-Begin scope: the file dialog can throw BEFORE _undoService.Begin,
                // so no undo scope is open here and no Rollback is needed.
                Log.ErrorF("SongInstrumentView.InstImport_Click: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Instrument set import failed: {0}", ex.Message));
            }
        }

        // Resolve a relative side/nested-index token against the chosen import
        // directory, REJECTING (returns null) an absolute path or a ".."-escaping
        // token so a hand-edited / malicious TSV can never read a file outside the
        // selected directory (Copilot review — path traversal). The Core only ever
        // emits bare relative filenames, so a legitimate import is unaffected.
        // internal for the path-traversal unit test (InternalsVisibleTo).
        internal static string? ResolveInside(string dir, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Reject an absolute / rooted token outright (e.g. "C:\x", "/x", "\\server\x").
            if (Path.IsPathRooted(name)) return null;

            string baseFull = Path.GetFullPath(dir);
            string candidate = Path.GetFullPath(Path.Combine(baseFull, name));
            // The resolved path must stay inside the chosen directory (block "..").
            // Use case-INSENSITIVE comparison only on Windows (a case-insensitive
            // filesystem); on Linux/macOS use Ordinal so a case-difference can't be
            // exploited to slip a ".." escape past the prefix check (Copilot review).
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

        static string GetActiveTabPrefix(byte headerByte)
        {
            switch (headerByte)
            {
                case 0x00: return "N00";
                case 0x01: return "N01";
                case 0x02: return "N02";
                case 0x03: return "N03";
                case 0x04: return "N04";
                case 0x08: return "N08";
                case 0x09: return "N09";
                case 0x0A: return "N0A";
                case 0x0B: return "N0B";
                case 0x0C: return "N0C";
                case 0x10: return "N10";
                case 0x18: return "N18";
                case 0x40: return "N40";
                case 0x80: return "N80";
                default: return string.Empty;
            }
        }

        void ReadTabFields(string tabPrefix)
        {
            // Only update VM byte fields when the corresponding NumericUpDown
            // EXISTS on the active tab. Tabs intentionally omit some byte
            // controls (e.g. Noise N04/N0C does not surface B5..B7); without
            // this guard, missing controls would coalesce to 0 and Write()
            // would zero those bytes in ROM even when the user never edited
            // them. PR #626 Copilot review round 2 blocker.
            void TryReadByte(string name, System.Action<byte> assign)
            {
                decimal? v = GetNumericByName(name);
                if (v.HasValue) assign((byte)v.Value);
            }
            void TryReadU32(string name, System.Action<uint> assign)
            {
                decimal? v = GetNumericByName(name);
                if (v.HasValue) assign((uint)v.Value);
            }

            TryReadByte($"{tabPrefix}_B1_Box", v => _vm.B1 = v);
            TryReadByte($"{tabPrefix}_B2_Box", v => _vm.B2 = v);
            TryReadByte($"{tabPrefix}_B3_Box", v => _vm.B3 = v);
            TryReadByte($"{tabPrefix}_B4_Box", v => _vm.B4 = v);
            TryReadByte($"{tabPrefix}_B5_Box", v => _vm.B5 = v);
            TryReadByte($"{tabPrefix}_B6_Box", v => _vm.B6 = v);
            TryReadByte($"{tabPrefix}_B7_Box", v => _vm.B7 = v);
            TryReadByte($"{tabPrefix}_B8_Box", v => _vm.B8 = v);
            TryReadByte($"{tabPrefix}_B9_Box", v => _vm.B9 = v);
            TryReadByte($"{tabPrefix}_B10_Box", v => _vm.B10 = v);
            TryReadByte($"{tabPrefix}_B11_Box", v => _vm.B11 = v);

            switch (tabPrefix)
            {
                case "N00": case "N08": case "N10": case "N18":
                case "N03": case "N0B":
                    TryReadU32($"{tabPrefix}_P4_Box", v => _vm.WavePtr = v);
                    break;
                case "N04": case "N0C":
                    TryReadByte($"{tabPrefix}_P4_Box", v => _vm.Period = v);
                    break;
                case "N40":
                    TryReadU32($"{tabPrefix}_P4_Box", v => _vm.KeyMapPtr = v);
                    TryReadU32($"{tabPrefix}_P8_Box", v => _vm.SubInstrPtr = v);
                    break;
                case "N80":
                    TryReadU32($"{tabPrefix}_P4_Box", v => _vm.SubInstrPtr = v);
                    break;
            }
        }

        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            // SelectAddress fires SelectedAddressChanged -> OnSelected ->
            // _vm.LoadEntry(address) which already populates the editor.
            // Only fall back to a direct LoadEntry call when the address
            // is NOT in the master list (e.g. cross-editor jump from
            // outside the SongTrack instrument set) so SelectAddress is
            // a no-op. Avoids the redundant double-load Copilot flagged
            // in PR #626 round 2 finding #3.
            uint beforeAddr = EntryList.SelectedItem?.addr ?? 0;
            EntryList.SelectAddress(address);
            uint afterAddr = EntryList.SelectedItem?.addr ?? 0;
            if (beforeAddr == afterAddr && afterAddr != address)
            {
                // Address wasn't in the master list — direct-load so the
                // caller still ends up looking at the requested entry.
                _vm.LoadEntry(address);
                UpdateUI();
            }
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
