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

        public SongInstrumentView()
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
                BlockSizeBox.Text = "12";
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

        void UpdateUI()
        {
            SelectedAddressLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AddressBox.Value = _vm.CurrentAddr;
            HeaderByteBox.Value = _vm.HeaderByte;
            MoreInfoBox.Text = _vm.TypeName;

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
            _vm.B1 = (byte)(GetNumericByName($"{tabPrefix}_B1_Box") ?? 0);
            _vm.B2 = (byte)(GetNumericByName($"{tabPrefix}_B2_Box") ?? 0);
            _vm.B3 = (byte)(GetNumericByName($"{tabPrefix}_B3_Box") ?? 0);
            _vm.B4 = (byte)(GetNumericByName($"{tabPrefix}_B4_Box") ?? 0);
            _vm.B5 = (byte)(GetNumericByName($"{tabPrefix}_B5_Box") ?? 0);
            _vm.B6 = (byte)(GetNumericByName($"{tabPrefix}_B6_Box") ?? 0);
            _vm.B7 = (byte)(GetNumericByName($"{tabPrefix}_B7_Box") ?? 0);
            _vm.B8 = (byte)(GetNumericByName($"{tabPrefix}_B8_Box") ?? 0);
            _vm.B9 = (byte)(GetNumericByName($"{tabPrefix}_B9_Box") ?? 0);
            _vm.B10 = (byte)(GetNumericByName($"{tabPrefix}_B10_Box") ?? 0);
            _vm.B11 = (byte)(GetNumericByName($"{tabPrefix}_B11_Box") ?? 0);

            switch (tabPrefix)
            {
                case "N00": case "N08": case "N10": case "N18":
                case "N03": case "N0B":
                    _vm.WavePtr = (uint)(GetNumericByName($"{tabPrefix}_P4_Box") ?? 0);
                    break;
                case "N04": case "N0C":
                    _vm.Period = (byte)(GetNumericByName($"{tabPrefix}_P4_Box") ?? 0);
                    break;
                case "N40":
                    _vm.KeyMapPtr = (uint)(GetNumericByName($"{tabPrefix}_P4_Box") ?? 0);
                    _vm.SubInstrPtr = (uint)(GetNumericByName($"{tabPrefix}_P8_Box") ?? 0);
                    break;
                case "N80":
                    _vm.SubInstrPtr = (uint)(GetNumericByName($"{tabPrefix}_P4_Box") ?? 0);
                    break;
            }
        }

        public void NavigateTo(uint address)
        {
            if (address != 0) _vm.LoadEntry(address);
            EntryList.SelectAddress(address);
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
