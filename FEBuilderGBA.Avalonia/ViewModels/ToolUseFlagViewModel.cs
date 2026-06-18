using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Read-only "Flags-Used-in-Chapter" tool ViewModel (issue #1192 — port of
    /// WinForms <c>ToolUseFlagForm</c>). For the selected chapter (map) it lists
    /// every event-flag reference from the in-scope subsystems via the
    /// cross-platform <see cref="UseFlagScanCore"/> aggregator:
    ///   event-condition records (Turn/Talk/Object/Always), the event scripts
    ///   those conditions reach (ArgType.FLAG), map-change records, and — at full
    ///   WinForms parity (#1253) — the per-version death-quote (Haiku) and
    ///   battle-conversation (BattleTalk) records scoped to the chapter.
    ///
    /// This is a tool / aggregator that READS ROM bytes for the scan but NEVER
    /// writes — so it deliberately exposes no data-verify report contract (a
    /// no-write aggregator VM is orphan-safe by the FEBuilderGBA.Tests contract;
    /// implementing or even naming that verify interface would trip the
    /// NoOrphanVMs gate).
    /// </summary>
    public class ToolUseFlagViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        int _flagUsageCount;
        string _summaryText = "";
        string _selectedFlagText = "";
        string _selectedFlagName = "";
        string _selectedTypeText = "";
        string _selectedInfoText = "";
        string _selectedAddrText = "";

        // The flag-usage records backing the entry list, in row order (one row
        // per record). Index-keyed so duplicate flag ids each resolve their own
        // detail (matching the ToolFELint pattern).
        List<UseFlagIDCore> _usages = new();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Number of flag-usage rows from the last scan.</summary>
        public int FlagUsageCount { get => _flagUsageCount; set => SetField(ref _flagUsageCount, value); }

        /// <summary>Localized one-line summary built IN THE VM (Avalonia XAML
        /// StringFormat is not translated).</summary>
        public string SummaryText { get => _summaryText; set => SetField(ref _summaryText, value); }

        public string SelectedFlagText { get => _selectedFlagText; set => SetField(ref _selectedFlagText, value); }
        public string SelectedFlagName { get => _selectedFlagName; set => SetField(ref _selectedFlagName, value); }
        public string SelectedTypeText { get => _selectedTypeText; set => SetField(ref _selectedTypeText, value); }
        public string SelectedInfoText { get => _selectedInfoText; set => SetField(ref _selectedInfoText, value); }
        public string SelectedAddrText { get => _selectedAddrText; set => SetField(ref _selectedAddrText, value); }

        /// <summary>Number of stored flag-usage rows.</summary>
        public int UsageListCount => _usages.Count;

        /// <summary>
        /// The chapter (map) list for the left-panel selector. Empty when no ROM
        /// is loaded.
        /// </summary>
        public List<AddrResult> LoadMapList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return MapSettingCore.MakeMapIDList(rom);
        }

        /// <summary>
        /// Scan the selected chapter and build the flag-usage entry list. Each row
        /// is one <see cref="UseFlagIDCore"/>. Detail/summary are reset first so a
        /// fresh scan never leaves stale detail. Guards a missing ROM (empty list).
        /// </summary>
        public List<AddrResult> LoadForChapter(uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return BuildList(new List<UseFlagIDCore>());

            return BuildList(UseFlagScanCore.Scan(rom, mapId) ?? new List<UseFlagIDCore>());
        }

        /// <summary>
        /// Test seam (InternalsVisibleTo): build the entry list from an injected
        /// usage set instead of scanning, so the row/detail behaviour is covered
        /// deterministically without a real chapter.
        /// </summary>
        internal List<AddrResult> LoadFromUsages(List<UseFlagIDCore> usages)
            => BuildList(usages ?? new List<UseFlagIDCore>());

        List<AddrResult> BuildList(List<UseFlagIDCore> usages)
        {
            _usages = usages;
            ClearDetail();

            var result = new List<AddrResult>();
            foreach (UseFlagIDCore u in _usages)
            {
                // Row label: "0x<flag> <name>  [<type>] <info>" — assembled here in
                // the VM (not via XAML StringFormat, which Avalonia does not
                // translate). The <type> is the raw FELint enum tag (e.g.
                // EVENT_COND_ALWAYS / MAPCHANGE), shown untranslated to match the
                // sibling ToolFELintViewModel.CategoryText — these tags read fine in
                // any locale and there is no Core type→localized-name map to use.
                string flagName = FlagNameOf(u.ID);
                string label = ToHexString(u.ID)
                    + (flagName.Length > 0 ? " " + flagName : "")
                    + "  [" + TypeText(u.DataType) + "]"
                    + (string.IsNullOrEmpty(u.Info) ? "" : " " + u.Info);
                result.Add(new AddrResult(u.Addr, label, u.Tag));
            }

            FlagUsageCount = _usages.Count;
            IsLoaded = false;
            RefreshSummary();
            return result;
        }

        /// <summary>
        /// INDEX-keyed detail load. <paramref name="index"/> is the row's original
        /// index, which equals the usage's index in the stored list. Out-of-range
        /// clears the detail panel.
        /// </summary>
        public void LoadEntryByIndex(int index)
        {
            if (index < 0 || index >= _usages.Count)
            {
                ClearDetail();
                return;
            }

            UseFlagIDCore u = _usages[index];
            uint addr = EffectiveAddr(u);
            CurrentAddr = addr;
            IsLoaded = true;
            SelectedFlagText = ToHexString(u.ID);
            SelectedFlagName = FlagNameOf(u.ID);
            SelectedTypeText = TypeText(u.DataType);
            SelectedInfoText = u.Info ?? "";
            SelectedAddrText = string.Format("0x{0:X08}", addr);
        }

        /// <summary>
        /// The byte offset the detail panel shows and the jump targets, by source
        /// type. For EVENTSCRIPT the row's <see cref="UseFlagIDCore.Addr"/> is the
        /// event-script TREE ROOT and <see cref="UseFlagIDCore.Tag"/> is the actual
        /// referencing COMMAND (mirroring WF GotoEvent → EventScriptForm.JumpTo,
        /// which positions the cursor at <c>tag</c>), so we surface Tag — that's
        /// the byte where the flag is referenced. For EVENT_COND_* / MAPCHANGE the
        /// Addr IS the record address (Tag is the slot index, not an address), so
        /// Addr is correct. Falls back to Addr if an EVENTSCRIPT Tag is unset.
        /// </summary>
        static uint EffectiveAddr(UseFlagIDCore u)
            => u.DataType == FELintCore.Type.EVENTSCRIPT && u.Tag != U.NOT_FOUND
                ? u.Tag
                : u.Addr;

        /// <summary>
        /// Address-keyed detail load (FIRST stored usage whose <c>.Addr</c>
        /// matches). Used by <c>IEditorView.NavigateTo</c>. Duplicate addresses
        /// resolve to the first match — the interactive UI uses
        /// <see cref="LoadEntryByIndex"/>, which is row-exact.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            for (int i = 0; i < _usages.Count; i++)
            {
                if (_usages[i].Addr == addr)
                {
                    LoadEntryByIndex(i);
                    return;
                }
            }
            ClearDetail();
        }

        /// <summary>
        /// True only when the row at <paramref name="index"/> has a real in-ROM
        /// byte offset (drives the double-click/Enter jump-to-HexEditor path).
        /// Uses <see cref="EffectiveAddr"/> so an EVENTSCRIPT row jumps to its
        /// referencing command (Tag), not the event-tree root.
        /// </summary>
        public bool TryGetJumpOffset(int index, out uint offset)
        {
            offset = 0;
            if (index < 0 || index >= _usages.Count) return false;

            ROM rom = CoreState.ROM;
            if (rom == null) return false;

            uint addr = EffectiveAddr(_usages[index]);
            if (!U.isSafetyOffset(addr, rom)) return false;

            offset = addr;
            return true;
        }

        void ClearDetail()
        {
            IsLoaded = false;
            SelectedFlagText = "";
            SelectedFlagName = "";
            SelectedTypeText = "";
            SelectedInfoText = "";
            SelectedAddrText = "";
        }

        void RefreshSummary()
        {
            SummaryText = R._("Flags used") + ": " + FlagUsageCount;
        }

        // Magnitude-padded hex with a "0x" prefix (mirrors WF U.ToHexString
        // usage in the flag list — short flags shown compactly).
        static string ToHexString(uint a) =>
            a <= 0xff ? "0x" + a.ToString("X02")
            : a <= 0xffff ? "0x" + a.ToString("X04")
            : a <= 0xffffff ? "0x" + a.ToString("X06")
            : "0x" + a.ToString("X08");

        // Resolve a flag's custom/base name via the shared flag-name cache
        // (same source as the Flag-Name editor). "" when no name / no cache.
        static string FlagNameOf(uint flag)
        {
            EtcCacheFLag cache = CoreState.FlagCache;
            if (cache == null) return "";
            return cache.TryGetValue(flag, out string name) ? (name ?? "") : "";
        }

        // Raw FELint enum tag (e.g. "EVENT_COND_ALWAYS", "MAPCHANGE"), NOT
        // localized — matches ToolFELintViewModel.CategoryText. The tags are
        // locale-neutral identifiers and there is no Core type→display-name map.
        static string TypeText(FELintCore.Type type) => type.ToString();
    }
}
