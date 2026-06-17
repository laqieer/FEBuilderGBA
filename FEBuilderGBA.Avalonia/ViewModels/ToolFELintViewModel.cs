using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Read-only Lint (FELint) viewer ViewModel (issue #1168 — port of WinForms
    /// <c>ToolFELintForm</c>). Reuses the cross-platform <see cref="FELintScanner"/>
    /// — the SAME scanner the CLI <c>--lint</c> command runs — to validate the loaded
    /// ROM (header/unit/class/item/map/text). No ROM mutation, no undo, no write path.
    ///
    /// Each <see cref="FELintCore.ErrorSt"/> maps to ONE entry row. Detail resolution is
    /// INDEX-keyed (the row's original index equals the error's index in the stored list)
    /// so duplicate-address rows — e.g. multiple <see cref="FELintCore.SYSTEM_MAP_ID"/>
    /// header/global errors — each display their own message instead of collapsing to the
    /// first match.
    /// </summary>
    public class ToolFELintViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        bool _hasErrors;
        int _errorCount;
        int _warningCount;
        string _summaryText = "";
        string _selectedSeverityText = "";
        string _selectedCategoryText = "";
        string _selectedAddrText = "";
        string _selectedMessage = "";

        /// <summary>The lint errors backing the entry list, in row order.</summary>
        List<FELintCore.ErrorSt> _errors = new();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>True when the scan found at least one real error/warning row.</summary>
        public bool HasErrors { get => _hasErrors; set => SetField(ref _hasErrors, value); }

        /// <summary>Number of ERROR-severity findings from the last scan.</summary>
        public int ErrorCount { get => _errorCount; set => SetField(ref _errorCount, value); }

        /// <summary>Number of WARNING-severity findings from the last scan.</summary>
        public int WarningCount { get => _warningCount; set => SetField(ref _warningCount, value); }

        /// <summary>
        /// Localized one-line summary built IN THE VM (NOT XAML StringFormat — Avalonia
        /// StringFormat is not translated).
        /// </summary>
        public string SummaryText { get => _summaryText; set => SetField(ref _summaryText, value); }

        public string SelectedSeverityText { get => _selectedSeverityText; set => SetField(ref _selectedSeverityText, value); }
        public string SelectedCategoryText { get => _selectedCategoryText; set => SetField(ref _selectedCategoryText, value); }
        public string SelectedAddrText { get => _selectedAddrText; set => SetField(ref _selectedAddrText, value); }
        public string SelectedMessage { get => _selectedMessage; set => SetField(ref _selectedMessage, value); }

        /// <summary>Number of stored lint rows (excludes the synthetic "no problems" row).</summary>
        public int ErrorListCount => _errors.Count;

        /// <summary>
        /// Run the cross-platform lint scan and build the entry list. Guards a missing ROM
        /// (returns empty). On a clean ROM returns a single info row and leaves
        /// <see cref="HasErrors"/> false.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                // No ROM = nothing scanned: a genuinely empty list (not the "no problems" row,
                // which means "scanned and clean"). State/detail still reset.
                return BuildList(new List<FELintCore.ErrorSt>(), showCleanRow: false);
            }

            return BuildList(new FELintScanner().Scan() ?? new List<FELintCore.ErrorSt>(),
                showCleanRow: true);
        }

        /// <summary>
        /// Test seam (used via <c>InternalsVisibleTo</c>): build the entry list from an
        /// injected error set instead of scanning the ROM, so detail-loading and
        /// duplicate-address behaviour are covered deterministically even when the loaded ROM
        /// is structurally clean (0 findings).
        /// </summary>
        internal List<AddrResult> LoadFromErrors(List<FELintCore.ErrorSt> errors)
            => BuildList(errors ?? new List<FELintCore.ErrorSt>(), showCleanRow: true);

        /// <summary>
        /// Store the findings, build one row per finding, and recompute the counts/summary.
        /// Always resets the detail panel — a fresh scan must not leave stale detail from the
        /// previous list (the address list does not raise a selection-changed event for an empty
        /// list). When <paramref name="showCleanRow"/> is true and there are no findings, a
        /// single informational "no problems" row is appended (the scanned-and-clean state).
        /// </summary>
        List<AddrResult> BuildList(List<FELintCore.ErrorSt> errors, bool showCleanRow)
        {
            _errors = errors;
            ClearDetail();

            int errCount = 0;
            int warnCount = 0;
            var result = new List<AddrResult>();
            foreach (FELintCore.ErrorSt err in _errors)
            {
                if (err.Severity == FELintCore.ErrorType.ERROR) errCount++;
                else warnCount++;

                string label = "[" + SeverityText(err.Severity) + "] "
                    + CategoryText(err.DataType) + ": " + err.Info;
                result.Add(new AddrResult(err.Addr, label, err.Tag));
            }

            ErrorCount = errCount;
            WarningCount = warnCount;
            HasErrors = _errors.Count > 0;
            RefreshSummary();

            if (showCleanRow && _errors.Count == 0)
            {
                // Scanned and clean — show one informational row. Index 0 maps to no stored
                // error, so LoadEntryByIndex clears the detail panel for it.
                result.Add(new AddrResult(0, R._("No problems found."), 0));
            }

            return result;
        }

        /// <summary>
        /// INDEX-keyed detail load. <paramref name="index"/> is the row's original
        /// (unfiltered) index, which equals the error's index in the stored list. Out-of-range
        /// (e.g. the synthetic "no problems" row) clears the detail panel.
        /// </summary>
        public void LoadEntryByIndex(int index)
        {
            if (index < 0 || index >= _errors.Count)
            {
                ClearDetail();
                return;
            }

            FELintCore.ErrorSt err = _errors[index];
            CurrentAddr = err.Addr;
            IsLoaded = true;
            SelectedSeverityText = SeverityText(err.Severity);
            SelectedCategoryText = CategoryText(err.DataType);
            SelectedAddrText = err.Addr == FELintCore.SYSTEM_MAP_ID
                ? R._("(system)")
                : string.Format("0x{0:X08}", err.Addr);
            SelectedMessage = err.Info ?? "";
        }

        /// <summary>
        /// Address-keyed detail load (FIRST stored error whose <c>.Addr</c> matches). Used by
        /// <c>IEditorView.NavigateTo</c>. NOTE: duplicate addresses (e.g. multiple
        /// <see cref="FELintCore.SYSTEM_MAP_ID"/> errors) resolve to the first match — the
        /// interactive UI uses <see cref="LoadEntryByIndex"/> instead, which is row-exact.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            for (int i = 0; i < _errors.Count; i++)
            {
                if (_errors[i].Addr == addr)
                {
                    LoadEntryByIndex(i);
                    return;
                }
            }
            ClearDetail();
        }

        /// <summary>
        /// True only when the row at <paramref name="index"/> is a real lint error with a
        /// jumpable ROM byte offset — i.e. not the <see cref="FELintCore.SYSTEM_MAP_ID"/>
        /// sentinel and inside the ROM data. The Hex Editor can display ANY in-ROM byte
        /// (it opens at offset 0 by default), so header-region findings such as the
        /// <c>0xB2</c> fixed-byte check are jumpable too — this deliberately does NOT use
        /// <see cref="U.isSafetyOffset(uint, ROM)"/>, which excludes the <c>0x0–0x1FF</c>
        /// header. Drives the double-click/Enter jump-to-HexEditor path.
        /// </summary>
        public bool TryGetJumpOffset(int index, out uint offset)
        {
            offset = 0;
            if (index < 0 || index >= _errors.Count) return false;

            ROM rom = CoreState.ROM;
            if (rom == null) return false;

            uint addr = _errors[index].Addr;
            if (addr == FELintCore.SYSTEM_MAP_ID) return false;
            if (addr >= (uint)rom.Data.Length) return false;

            offset = addr;
            return true;
        }

        void ClearDetail()
        {
            IsLoaded = false;
            SelectedSeverityText = "";
            SelectedCategoryText = "";
            SelectedAddrText = "";
            SelectedMessage = "";
        }

        void RefreshSummary()
        {
            SummaryText = R._("Errors") + ": " + ErrorCount
                + "  " + R._("Warnings") + ": " + WarningCount;
        }

        static string SeverityText(FELintCore.ErrorType severity)
            => severity == FELintCore.ErrorType.ERROR ? R._("ERROR") : R._("WARNING");

        static string CategoryText(FELintCore.Type type) => type.ToString();
    }
}
