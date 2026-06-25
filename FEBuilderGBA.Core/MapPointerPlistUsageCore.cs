using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, READ-ONLY helpers for the "New PLIST" popup
    /// (Avalonia <c>MapPointerNewPLISTPopupView</c>, used by the EventCond
    /// Precise-event-condition-area allocation). Issue #1433.
    ///
    /// Ported byte-faithful from WinForms:
    /// <list type="bullet">
    ///   <item><c>MapSettingForm.GetMapIDsWherePlist</c> (lines 242-365)</item>
    ///   <item><c>MapPointerNewPLISTPopupForm.PlistToName</c> (lines 75-128)</item>
    ///   <item><c>MapPointerNewPLISTPopupForm.InitUI</c> Extend-state branch (lines 24-50)</item>
    ///   <item><c>MapPointerForm.IsExtendsPlist</c> (lines 442-456)</item>
    ///   <item><c>MapPointerForm.GetDataCount(EVENT)</c> (lines 463-469)</item>
    /// </list>
    /// Reuses existing Core seams (<see cref="MapPListResolverCore"/>,
    /// <see cref="MapSettingCore"/>, <see cref="MapChangeCore"/>) so the
    /// per-map PLIST byte reads stay EOF-guarded and single-sourced.
    ///
    /// All methods are pure, guard every pointer slot before
    /// <see cref="ROM.p32"/>, and never throw (a null / truncated ROM
    /// yields an empty / safe result).
    /// </summary>
    public static class MapPointerPlistUsageCore
    {
        /// <summary>
        /// Extend-button state for the popup InitUI (mirrors WF
        /// <c>MapPointerNewPLISTPopupForm.InitUI</c>).
        /// </summary>
        public enum ExtendState
        {
            /// <summary>WF: PLISTExtendsButton enabled, no "already extended"
            /// note. In Avalonia the split editor flow is not wired, so the
            /// button is presented as honestly unavailable rather than as a
            /// dead enabled control (#1433).</summary>
            Enabled,
            /// <summary>WF: <c>IsExtendsPlist()</c> true but not split — button
            /// disabled, "already extended" note shown, explanation shown.</summary>
            AlreadyExtended,
            /// <summary>WF: <c>IsPlistSplits()</c> true — button disabled,
            /// "already extended" note shown, plain explanation hidden.</summary>
            AlreadySplit,
        }

        /// <summary>Result of a PLIST usage lookup for the info display.</summary>
        public readonly struct UsageInfo
        {
            public UsageInfo(string message, bool isAlreadyUse)
            {
                Message = message;
                IsAlreadyUse = isAlreadyUse;
            }

            /// <summary>Localized message for the read-only link box.</summary>
            public string Message { get; }

            /// <summary>True when the PLIST is reserved (==0), out of range, or
            /// already referenced by a map — the OK button must confirm before
            /// overwriting.</summary>
            public bool IsAlreadyUse { get; }
        }

        /// <summary>
        /// Port of <c>MapPointerForm.GetDataCount(PLIST_TYPE.EVENT)</c>: the
        /// number of selectable event-PLIST entries. Reuses
        /// <see cref="MapChangeCore.GetPlistLimit"/> (256 when split, else the
        /// per-version vanilla default size). Returns 0 on a null ROM so the
        /// caller can fall back to a safe maximum.
        /// </summary>
        public static uint GetEventPlistCount(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            return MapChangeCore.GetPlistLimit(rom);
        }

        /// <summary>
        /// Port of <c>MapPointerForm.IsExtendsPlist()</c>: split ⇒ true; else
        /// the event-PLIST table has ≥ 255 entries ⇒ true. (WF reads the EVENT
        /// table <c>DataCount</c>; <see cref="GetEventPlistCount"/> is the Core
        /// equivalent of that count.)
        /// </summary>
        public static bool IsExtendsPlist(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            if (MapChangeCore.IsPlistSplit(rom)) return true;
            return GetEventPlistCount(rom) >= 255;
        }

        /// <summary>
        /// Resolve the Extend-button state for the popup. Mirrors WF
        /// <c>InitUI</c>: split ⇒ <see cref="ExtendState.AlreadySplit"/>;
        /// extended-but-not-split ⇒ <see cref="ExtendState.AlreadyExtended"/>;
        /// otherwise <see cref="ExtendState.Enabled"/>.
        /// </summary>
        public static ExtendState GetExtendState(ROM rom)
        {
            if (rom?.RomInfo == null) return ExtendState.Enabled;
            if (MapChangeCore.IsPlistSplit(rom)) return ExtendState.AlreadySplit;
            if (IsExtendsPlist(rom)) return ExtendState.AlreadyExtended;
            return ExtendState.Enabled;
        }

        /// <summary>
        /// Faithful port of <c>MapSettingForm.GetMapIDsWherePlist</c>.
        /// Returns the list of map IDs whose PLIST field of the given
        /// <paramref name="type"/> equals <paramref name="plist"/>.
        /// A <c>null</c> <paramref name="type"/> models the WF
        /// <c>PLIST_TYPE.UNKNOWN</c> "scan every PLIST field" mode.
        ///
        /// Reuses <see cref="MapPListResolverCore.GetMapPListsWhereAddr"/> for
        /// the per-map byte reads (already EOF-guarded), so this method only
        /// applies the WF field-selection logic. Never throws.
        /// </summary>
        public static List<uint> GetMapIDsWherePlist(ROM rom, MapChangeCore.PlistType? type, uint plist)
        {
            var result = new List<uint>();
            if (rom?.RomInfo == null) return result;
            // WF asserts plist >= 1; a 0 plist matches nothing meaningful.
            if (plist == 0) return result;

            List<AddrResult> maps = MapSettingCore.MakeMapIDList(rom);
            foreach (AddrResult m in maps)
            {
                MapPListResolverCore.PLists p = MapPListResolverCore.GetMapPListsWhereAddr(rom, m.addr);
                uint mapId = m.tag;

                if (type == null)
                {
                    // UNKNOWN — scan every PLIST field (WF lines 274-291).
                    if ((p.obj_plist & 0xff) == plist
                        || ((p.obj_plist >> 8) & 0xff) == plist
                        || p.palette_plist == plist
                        || p.config_plist == plist
                        || p.mappointer_plist == plist
                        || p.anime1_plist == plist
                        || p.anime2_plist == plist
                        || p.mapchange_plist == plist
                        || p.event_plist == plist
                        || p.worldmapevent_plist == plist
                        || p.palette2_plist == plist)
                    {
                        result.Add(mapId);
                    }
                    continue;
                }

                switch (type.Value)
                {
                    case MapChangeCore.PlistType.ANIMATION:
                        if (p.anime1_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.ANIMATION2:
                        if (p.anime2_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.CHANGE:
                        if (p.mapchange_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.CONFIG:
                        if (p.config_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.EVENT:
                        if (p.event_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.WORLDMAP_FE6ONLY:
                        if (p.worldmapevent_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.PALETTE:
                        // WF adds the map twice if both palette and second
                        // palette match; preserve that (count-based callers
                        // only care about >= 1).
                        if (p.palette_plist == plist) result.Add(mapId);
                        if (p.palette2_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.MAP:
                        if (p.mappointer_plist == plist) result.Add(mapId);
                        break;
                    case MapChangeCore.PlistType.OBJECT:
                        if ((p.obj_plist & 0xff) == plist
                            || ((p.obj_plist >> 8) & 0xff) == plist)
                        {
                            result.Add(mapId);
                        }
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Faithful port of <c>MapPointerNewPLISTPopupForm.PlistToName</c>.
        /// Computes the localized info-display message and the
        /// <c>IsAlreadyUse</c> flag for a typed PLIST. The
        /// <paramref name="searchType"/> is the popup's search type (EVENT for
        /// the EventCond Precise-allocate caller).
        ///
        /// Mirrors WF exactly:
        /// <list type="bullet">
        ///   <item>plist==0 ⇒ reserved message, IsAlreadyUse=true.</item>
        ///   <item>out of range (≥ event-plist count) ⇒ reserved message,
        ///   IsAlreadyUse=true (Avalonia OK-validation parity, #1433).</item>
        ///   <item>split ROM ⇒ scan the typed table (+ WF cross-checks for
        ///   PALETTE/OBJECT and ANIMATION/ANIMATION2).</item>
        ///   <item>non-split ROM ⇒ scan every field (UNKNOWN).</item>
        ///   <item>count ≥ 1 ⇒ "already used: {names}", IsAlreadyUse=true.</item>
        ///   <item>else ⇒ "not referenced (recommended)", IsAlreadyUse=false.</item>
        /// </list>
        /// Never throws.
        /// </summary>
        public static UsageInfo BuildPlistUsageInfo(ROM rom, MapChangeCore.PlistType searchType, uint plist)
        {
            if (rom?.RomInfo == null)
                return new UsageInfo(string.Empty, false);

            if (plist == 0)
            {
                // WF: "PLIST=0は、書き込み禁止です。" (PLIST=0 is write-protected).
                return new UsageInfo(R._("PLIST=0は、書き込み禁止です。"), true);
            }

            // Range parity (#1433): an out-of-range PLIST would later fail in
            // WriteEventPLIST; treat it as "in use / not recommended" here so
            // the user gets feedback before OK.
            uint count = GetEventPlistCount(rom);
            if (count != 0 && plist >= count)
            {
                return new UsageInfo(R._("PLIST=0は、書き込み禁止です。"), true);
            }

            List<uint> maps;
            if (MapChangeCore.IsPlistSplit(rom))
            {
                // Split — scan only the typed table, with WF's cross-checks.
                maps = GetMapIDsWherePlist(rom, searchType, plist);
                if (maps.Count < 1)
                {
                    if (searchType == MapChangeCore.PlistType.PALETTE)
                        maps = GetMapIDsWherePlist(rom, MapChangeCore.PlistType.OBJECT, plist);
                    else if (searchType == MapChangeCore.PlistType.OBJECT)
                        maps = GetMapIDsWherePlist(rom, MapChangeCore.PlistType.PALETTE, plist);
                    else if (searchType == MapChangeCore.PlistType.ANIMATION)
                        maps = GetMapIDsWherePlist(rom, MapChangeCore.PlistType.ANIMATION2, plist);
                    else if (searchType == MapChangeCore.PlistType.ANIMATION2)
                        maps = GetMapIDsWherePlist(rom, MapChangeCore.PlistType.ANIMATION, plist);
                }
            }
            else
            {
                // Not split — must scan every field (WF UNKNOWN).
                maps = GetMapIDsWherePlist(rom, null, plist);
            }

            if (maps.Count >= 1)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < maps.Count; i++)
                {
                    if (i != 0) sb.Append(',');
                    sb.Append(MapSettingCore.GetMapNameById(rom, maps[i]));
                }
                // WF: "既に利用されています。\r\n利用マップ:{0}"
                return new UsageInfo(R._("既に利用されています。\r\n利用マップ:{0}", sb.ToString()), true);
            }

            // WF: "マップ設定では参照されていません。\r\n(このPLISTの利用を推奨します)"
            return new UsageInfo(R._("マップ設定では参照されていません。\r\n(このPLISTの利用を推奨します)"), false);
        }

        /// <summary>
        /// The Yes/No overwrite-confirmation message shown by the popup's OK
        /// button when the selected PLIST is already in use. Mirrors WF
        /// <c>MapPointerNewPLISTPopupForm.OKButton_Click</c>'s
        /// <c>R.ShowNoYes(...)</c> text.
        /// </summary>
        public static string OverwriteConfirmMessage()
        {
            return R._("このPLISTはすでに利用されています。\r\n利用されているPLISTを上書きするのは危険です。\r\n本当に、このPLISTにデータを割り当てますか？\r\n");
        }
    }
}
