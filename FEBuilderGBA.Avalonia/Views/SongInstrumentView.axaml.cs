// SPDX-License-Identifier: GPL-3.0-or-later
// SongInstrumentView — Avalonia parity rebuild for #387. Mirrors
// `SongInstrumentForm` layout (panel1 read-config + AddressPanel master-write
// + panel2 common-header + 14 instrument tabs + panel4 fingerprint footer).
// The single WF cross-editor jump callsite
// (`SongInstrumentImportWaveForm`) stays deferred and explicitly absent from
// both the manifest and the click-handler wiring — see
// `SongInstrumentViewModel.NavigationTargets.cs`.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongInstrumentView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SongInstrumentViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Instrument Editor";
        public bool IsLoaded => _vm.IsLoaded;

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
            Opened += (_, _) => LoadList();
            _viewReady = true;
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
                uint userBase = (uint)(ReadStartAddressBox?.Value ?? 0);
                uint userCount = (uint)(ReadCountBox?.Value ?? 0);
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
                    if (_vm.BaseAddr != 0 && ReadStartAddressBox != null)
                        ReadStartAddressBox.Value = _vm.BaseAddr;
                }
                BlockSizeBox.Text = "12";

                // Apply the Filter textbox (a parallel WF-style filter that
                // routes through AddressListControl.ApplySearchFilter).
                string? filter = FilterBox?.Text;
                if (!string.IsNullOrWhiteSpace(filter))
                    EntryList.ApplySearchFilter(filter);
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentView.LoadList failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentView.JumpToAddr failed: {0}", ex.Message);
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
                Log.Error("SongInstrumentView.OnSelected failed: {0}", ex.Message);
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
                Log.Error("SongInstrumentView.Write_Click failed: {0}", ex.Message);
            }
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
    }
}
