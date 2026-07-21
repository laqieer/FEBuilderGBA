using System;
using System.Collections.Generic;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform map enumeration logic extracted from WinForms MapSettingForm.
    /// Reads map settings directly from ROM pointer tables.
    /// </summary>
    public static class MapSettingCore
    {
        /// <summary>
        /// Determines if the FE7 map setting struct uses the FE7U (152-byte) layout.
        /// Used to dispatch between FE7JP and FE7U editors.
        /// </summary>
        public static bool IsFE7ULayout(uint mapSettingDataSize) => mapSettingDataSize >= 152;

        /// <summary>
        /// Enumerate all maps from <c>CoreState.ROM</c>'s map setting pointer table.
        /// Returns list of (address, display-name) pairs.
        /// </summary>
        public static List<AddrResult> MakeMapIDList() => MakeMapIDList(CoreState.ROM);

        /// <summary>
        /// Enumerate all maps from the given ROM's map setting pointer table.
        /// Structural row enumeration uses <paramref name="rom"/>, while display-name
        /// decoding uses the current text runtime. Snapshot-isolated callers that only
        /// need addresses/tags should use <see cref="EnumerateMapAddresses"/>.
        /// </summary>
        public static List<AddrResult> MakeMapIDList(ROM rom)
        {
            var result = new List<AddrResult>();
            foreach (AddrResult entry in EnumerateMapAddresses(rom, CancellationToken.None))
            {
                string name = U.ToHexString(entry.tag) + " " + GetMapName(rom, entry.addr);
                result.Add(new AddrResult(entry.addr, name, entry.tag));
            }
            return result;
        }

        /// <summary>
        /// Enumerate map-setting addresses and zero-based row tags without formatting
        /// display names. Every ROM read is made against <paramref name="rom"/>, and
        /// cancellation is observed before setup, before each row, and while validating
        /// a potentially long text-pointer table.
        /// </summary>
        internal static IEnumerable<AddrResult> EnumerateMapAddresses(
            ROM rom,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rom == null || rom.RomInfo == null)
                yield break;

            uint basePointer = rom.RomInfo.map_setting_pointer;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (basePointer == 0 || dataSize == 0)
                yield break;
            if (!U.isSafetyOffset(basePointer, rom)
                || (ulong)basePointer + 4 > (ulong)rom.Data.Length)
                yield break;

            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom))
                yield break;

            for (uint i = 0; ; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ulong addr64 = (ulong)baseAddr + ((ulong)i * dataSize);
                if (addr64 > uint.MaxValue)
                    yield break;

                uint addr = (uint)addr64;
                if (!U.isSafetyOffset(addr, rom)
                    || addr64 + dataSize > (ulong)rom.Data.Length)
                    yield break;
                if (!IsMapSettingValid(rom, addr, cancellationToken))
                    yield break;

                yield return new AddrResult(addr, "", i);
                if (i == uint.MaxValue)
                    yield break;
            }
        }

        /// <summary>
        /// Map a map-setting DATA address back to its map id (list index).
        /// Inverse of <see cref="MakeMapIDList(ROM)"/>'s
        /// <c>baseAddr + i*dataSize</c>. PURE / read-only.
        ///
        /// Returns <see cref="U.NOT_FOUND"/> when the rom/RomInfo is null, the
        /// map-setting pointer slot or datasize is unusable, the table base is
        /// unsafe, <paramref name="addr"/> is below the table base, not aligned
        /// to a row boundary, has a row that runs past EOF, or is an aligned
        /// address that does NOT correspond to an enumerated map entry (i.e. it
        /// fails the same terminator/validity heuristic the list builder uses —
        /// this rejects a plausible-but-bogus id for an aligned address past the
        /// table terminator). Pointer-form input (0x080xxxxx) is normalized to a
        /// ROM offset before the lookup.
        /// </summary>
        public static uint GetMapIdFromAddr(ROM rom, uint addr)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (addr == U.NOT_FOUND) return U.NOT_FOUND;

            // Accept a GBA pointer (0x080xxxxx) as well as a raw ROM offset.
            if (U.isPointer(addr)) addr = U.toOffset(addr);

            uint basePointer = rom.RomInfo.map_setting_pointer;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (basePointer == 0 || dataSize == 0) return U.NOT_FOUND;

            // Guard the pointer SLOT before dereferencing it via p32.
            // isSafetyOffset only checks basePointer < Data.Length; p32 reads 4
            // bytes, so also require the full 4-byte slot to be in ROM (overflow
            // -safe) to keep this method truly non-throwing as documented.
            if (!U.isSafetyOffset(basePointer, rom)) return U.NOT_FOUND;
            if ((ulong)basePointer + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;

            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            if (addr < baseAddr) return U.NOT_FOUND;
            uint delta = addr - baseAddr;
            if (delta % dataSize != 0) return U.NOT_FOUND;

            // Overflow-safe full-row EOF bound — the entire record must be in ROM.
            if ((ulong)addr + dataSize > (ulong)rom.Data.Length) return U.NOT_FOUND;

            // Gate against the ENUMERATED rows, not just IsMapSettingValid on the
            // candidate row in isolation: MakeMapIDList stops at the first invalid
            // (terminator) row, so a valid-looking row AFTER an earlier terminator
            // is NOT a real map entry and must resolve to NOT_FOUND. Require the
            // candidate id to be enumerated AND map back to the exact same
            // address (Copilot CLI review on #1086).
            uint id = delta / dataSize;
            foreach (AddrResult entry in EnumerateMapAddresses(rom, CancellationToken.None))
            {
                if (entry.tag == id)
                    return entry.addr == addr ? id : U.NOT_FOUND;
                if (entry.tag > id)
                    break;
            }
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Get the number of valid maps in <c>CoreState.ROM</c>.
        /// </summary>
        public static int GetMapCount()
        {
            int count = 0;
            foreach (AddrResult _ in EnumerateMapAddresses(CoreState.ROM, CancellationToken.None))
                count++;
            return count;
        }

        /// <summary>
        /// Get the ROM address for a specific map ID in <c>CoreState.ROM</c>.
        /// For ROM-explicit callers use <see cref="GetMapAddr(ROM, uint)"/>.
        /// </summary>
        public static uint GetMapAddr(uint mapId) => GetMapAddr(CoreState.ROM, mapId);

        /// <summary>
        /// Get the ROM address for a specific map ID in the given ROM.
        /// Does NOT read <c>CoreState.ROM</c>.
        /// </summary>
        public static uint GetMapAddr(ROM rom, uint mapId)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            uint baseAddr = rom.p32(rom.RomInfo.map_setting_pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            uint addr = (uint)(baseAddr + (mapId * rom.RomInfo.map_setting_datasize));
            if (!U.isSafetyOffset(addr, rom)) return U.NOT_FOUND;

            return addr;
        }

        /// <summary>
        /// Check if the map setting at addr is valid (not past the end of map data).
        /// Returns true if the map entry is valid, false if it marks end-of-data.
        /// Logic extracted from WinForms MapSettingForm.IsMapSettingEnd.
        /// </summary>
        /// <summary>
        /// Check whether the map setting entry at <paramref name="addr"/> is
        /// a valid entry (i.e. real ROM data, not the table terminator or
        /// trailing garbage). Exposed for cross-reference scanning
        /// (<c>TextRefTableRegistry</c>) which needs the same terminator
        /// heuristic as the editor's list builder.
        /// </summary>
        internal static bool IsMapSettingValid(ROM rom, uint addr) =>
            IsMapSettingValid(rom, addr, CancellationToken.None);

        static bool IsMapSettingValid(
            ROM rom,
            uint addr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WinForms treats a pointer in the first dword as a valid map entry.
            uint a = rom.u32(addr + 0);
            if (U.isPointer(a))
                return true;

            // Weather check
            uint weather = rom.u8(addr + 12);
            if (weather >= 0xE)
                return false;

            // PLIST validation
            uint plist1 = rom.u32(addr + 4);
            if (plist1 == 0 || plist1 == 0xFFFFFFFF)
            {
                uint plist2 = rom.u32(addr + 8);
                if (plist2 == 0 || plist2 == 0xFFFFFFFF)
                    return false;
            }

            // For FE7/FE8-style ROMs with larger data size, do text ID bounds check
            if (rom.RomInfo.map_setting_datasize >= 148)
            {
                uint textmax = GetTextDataCount(rom, cancellationToken);
                if (textmax > 0)
                {
                    // Map name text IDs are at the same offset for FE7/FE7U/FE8
                    uint map1 = rom.u16(addr + 0x70); // offset 112
                    if (map1 >= textmax) return false;

                    uint map2 = rom.u16(addr + 0x72); // offset 114
                    if (map2 >= textmax) return false;

                    // Clear condition text offsets differ by version:
                    // FE7U (152-byte struct): 0x8C/0x8E (offsets 140/142)
                    // FE7JP/FE8 (148-byte struct): 0x88/0x8A (offsets 136/138)
                    uint clearCondOff1, clearCondOff2;
                    if (rom.RomInfo.map_setting_datasize >= 152)
                    {
                        // FE7U: 4 extra bytes shift clear conditions
                        clearCondOff1 = 0x8C; // 140
                        clearCondOff2 = 0x8E; // 142
                    }
                    else
                    {
                        // FE7JP / FE8
                        clearCondOff1 = 0x88; // 136
                        clearCondOff2 = 0x8A; // 138
                    }

                    uint clearcond1 = rom.u16(addr + clearCondOff1);
                    if (clearcond1 >= textmax) return false;

                    uint clearcond2 = rom.u16(addr + clearCondOff2);
                    if (clearcond2 >= textmax) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the text data count from ROM (simplified - avoids TextForm dependency).
        /// </summary>
        static uint GetTextDataCount(ROM rom, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rom.RomInfo.text_pointer == 0) return 0;
            if (!U.isSafetyOffset(rom.RomInfo.text_pointer, rom)
                || (ulong)rom.RomInfo.text_pointer + 4 > (ulong)rom.Data.Length)
                return 0;

            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBase, rom)) return 0;

            // Walk the text pointer table to find count
            // Simplified: use a reasonable upper bound
            for (uint i = 0; i < 0x2000; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint entryAddr = (uint)(textBase + i * 4);
                if (entryAddr + 4 > (uint)rom.Data.Length) return i;

                uint ptr = rom.u32(entryAddr);
                if (ptr == 0) return i;
                if (!U.isPointer(ptr) && ptr != 0) return i;
            }
            return 0x2000;
        }

        /// <summary>
        /// Resolve a map/chapter ID to a human-readable name (e.g. "Ch1 Prologue")
        /// using <c>CoreState.ROM</c>. Mirrors WinForms
        /// <c>MapSettingForm.GetMapName(id)</c>. Returns "" when the ID is out of
        /// range or the ROM is unavailable. Callers that need the WinForms "ANY"
        /// sentinel rendering (FE7/8 0xFF, FE6 over-count) should apply that guard
        /// themselves before calling this.
        /// </summary>
        public static string GetMapNameById(uint mapId) => GetMapNameById(CoreState.ROM, mapId);

        /// <summary>
        /// Resolve a map/chapter ID to a human-readable name using the given ROM.
        /// Does NOT read <c>CoreState.ROM</c>. Returns "" for an out-of-range ID,
        /// a table-terminator/garbage entry, an entry whose record extends past
        /// EOF, or an unavailable ROM.
        /// </summary>
        public static string GetMapNameById(ROM rom, uint mapId)
        {
            if (rom == null || rom.RomInfo == null) return "";

            uint addr = GetMapAddr(rom, mapId);
            if (addr == U.NOT_FOUND || !U.isSafetyOffset(addr, rom)) return "";

            // GetMapAddr only bounds-checks the START of the record, so an
            // out-of-range mapId can land on trailing data (a garbage chapter
            // name) or a record that runs past EOF (GetMapName's u8/u16 reads
            // would then throw). Require the WHOLE record to be in-bounds AND
            // pass the same terminator/safety heuristic the map-settings list
            // builder uses, so the doc's "returns '' for out-of-range" holds and
            // GetMapName cannot throw (no bare catch needed).
            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) return "";
            if ((ulong)addr + dataSize > (ulong)rom.Data.Length) return "";
            if (!IsMapSettingValid(rom, addr)) return "";

            return GetMapName(rom, addr);
        }

        /// <summary>
        /// Public ROM+address map-name formatter — mirrors WinForms
        /// <c>MapSettingForm.GetMapNameWhereAddr(addr)</c>. Used by the
        /// map-PLIST label resolver (#952) which already holds the map's
        /// setting address and needs only the chapter-prefixed name.
        /// Returns "" when <paramref name="rom"/> is unavailable or
        /// <paramref name="addr"/> is unsafe.
        /// </summary>
        public static string GetMapNameWhereAddr(ROM rom, uint addr)
        {
            if (rom == null || rom.RomInfo == null) return "";
            if (!U.isSafetyOffset(addr, rom)) return "";
            return GetMapName(rom, addr);
        }

        /// <summary>
        /// Upper sanity bound on the number of references
        /// <see cref="ExpandMapSettingTable"/> expects to repoint to the
        /// map-setting base. The base is referenced from a small fixed set of
        /// engine sites (the canonical pointer slot + a handful of LDR loads);
        /// a hit count above this is almost certainly a false-positive flood
        /// from a coincidental raw u32 == base, so the audit guard rejects the
        /// expand WITHOUT mutating (issue #1085 plan-review finding #4). The
        /// bound is generous (real ROMs repoint well under 10) so a legitimately
        /// heavily-referenced ROM still passes.
        /// </summary>
        public const int MaxPlausibleRepointSlots = 64;

        /// <summary>
        /// Expand the map-setting (chapter) table by <paramref name="addCount"/>
        /// rows using FIRST-fill (so the new rows are valid and ENUMERATE — a
        /// zero-filled row is invalid and <see cref="MakeMapIDList(ROM)"/> stops
        /// at the first invalid row) and complete reference repointing (raw
        /// 32-bit + ARM-Thumb LDR literal-pool, whole-ROM) so no engine site is
        /// left pointing into the wiped old region. Mirrors WinForms
        /// <c>InputFormRef.ExpandsArea(ExpandsFillOption.FIRST, ...)</c> +
        /// <c>MoveToFreeSapceForm.SearchPointer</c> for this engine table
        /// (issue #1085). This is the orchestrator the Avalonia Map Settings
        /// (FE6) editor's "リストの拡張" (Expand List) button calls.
        ///
        /// <para><b>Atomic:</b> the whole operation (move + FIRST-fill + all-ref
        /// repoint + old-region wipe) runs under the caller's ambient undo
        /// scope, AND a defensive <c>rom.Data.Clone()</c> snapshot is kept. On
        /// ANY fault — a failed <see cref="DataExpansionCore.ExpandTableTo(ROM, uint, uint, uint, uint, DataExpansionCore.ExpandOptions)"/>,
        /// a failed audit guard, or an exception — the ROM is restored
        /// byte-identical (bytes AND length) and an error string is set with
        /// ZERO net change (#885/#923 pattern).</para>
        ///
        /// <para><b>Audit guard (#1085 finding #4):</b> the move + all-reference
        /// repoint runs first, then the guard inspects the recorded
        /// <see cref="DataExpansionCore.ExpandResult.RepointedSlots"/>; the
        /// result must (a) repoint a non-zero number of slots (a zero count
        /// means the canonical pointer was somehow not even found — abort),
        /// (b) include the canonical <c>map_setting_pointer</c> slot, and (c) not
        /// exceed <see cref="MaxPlausibleRepointSlots"/> (a flood ⇒ likely
        /// false-positive — abort). Because the byte-identical snapshot is taken
        /// BEFORE any mutation, a failed guard restores the snapshot for ZERO
        /// net change — so the validation is post-expand but the outcome is
        /// equivalent to a pre-mutation veto (no partial commit ever escapes).</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="addCount">Number of rows to add (must be &gt;= 1).</param>
        /// <param name="undo">The caller's active undo transaction (the same
        /// <see cref="Undo.UndoData"/> passed to the surrounding
        /// <c>ROM.BeginUndoScope</c> / <c>UndoService.Begin</c>). The actual ROM
        /// writes record into the AMBIENT scope, not this object directly; this
        /// parameter is used by the orchestrator to keep that ambient scope
        /// consistent with the snapshot restore — on ANY fault (a failed expand,
        /// a failed audit guard, or an exception) it CLEARS
        /// <c>undo.list</c> after restoring the byte-identical snapshot, so a
        /// subsequent caller-side <c>UndoService.Rollback()</c> cannot replay
        /// the now-out-of-date ranges against the already-restored ROM. May be
        /// <c>null</c> (e.g. in tests that drive the ambient scope directly), in
        /// which case the consistency cleanup is skipped.</param>
        /// <param name="error">Set to a human-readable message on failure;
        /// empty on success.</param>
        /// <returns>The <see cref="DataExpansionCore.ExpandResult"/> from the
        /// underlying expand (with <c>Success == false</c> + <paramref name="error"/>
        /// set on any guard/atomicity failure).</returns>
        public static DataExpansionCore.ExpandResult ExpandMapSettingTable(ROM rom, uint addCount, Undo.UndoData undo, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            {
                error = R._("ROM not loaded.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            if (addCount == 0)
            {
                error = R._("Add count must be at least 1.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            uint pointerAddr = rom.RomInfo.map_setting_pointer;
            uint entrySize = rom.RomInfo.map_setting_datasize;
            if (pointerAddr == 0 || entrySize == 0)
            {
                error = R._("Map setting table is not available for this ROM.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // currentCount = the editor's enumerated visible row count.
            uint currentCount = (uint)MakeMapIDList(rom).Count;
            if (currentCount == 0)
            {
                // FIRST-fill needs a source row 0 to copy; with no enumerated
                // rows there is nothing to seed the new rows from.
                error = R._("Cannot expand: the map setting list is empty (no row 0 to copy).");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }

            // Overflow-safe target count.
            if (addCount > uint.MaxValue - currentCount)
            {
                error = R._("Add count overflows the table size.");
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
            uint newCount = currentCount + addCount;

            uint oldBase = rom.p32(pointerAddr);

            // Defensive snapshot for the byte-identical (length-aware) restore
            // on ANY fault — guarantees a FAILED expand mutates ZERO bytes even
            // beyond what the ambient undo scope tracks.
            byte[] snap = (byte[])rom.Data.Clone();

            try
            {
                var result = DataExpansionCore.ExpandTableTo(
                    rom, pointerAddr, entrySize, currentCount, newCount,
                    new DataExpansionCore.ExpandOptions
                    {
                        Fill = DataExpansionCore.ExpandFill.First,
                        Repoint = DataExpansionCore.ExpandRepoint.RawAndLdrAll,
                        FullZeroTerminatorRow = false,
                    });

                if (!result.Success)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = result.Error ?? R._("Map setting table expansion failed.");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // --- Audit guard (#1085 finding #4) ---------------------------
                var slots = result.RepointedSlots ?? System.Array.Empty<uint>();

                // (a) zero repointed slots ⇒ the canonical pointer was not even
                // found — something is wrong; abort with ZERO net change.
                if (slots.Count == 0)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Map setting expand aborted: no references were repointed (expected at least the canonical pointer slot).");
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (b) the canonical map_setting_pointer slot MUST be among them.
                bool canonicalCovered = false;
                foreach (uint s in slots)
                {
                    if (s == pointerAddr) { canonicalCovered = true; break; }
                }
                if (!canonicalCovered)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Map setting expand aborted: the canonical pointer slot (0x{0:X}) was not among the repointed references.", pointerAddr);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                // (c) implausibly large ⇒ likely a false-positive flood from a
                // coincidental raw u32 == base — abort rather than corrupt.
                if (slots.Count > MaxPlausibleRepointSlots)
                {
                    RestoreSnapshot(rom, snap, undo);
                    error = R._("Map setting expand aborted: {0} references would be repointed, exceeding the plausible maximum ({1}) — likely a false-positive match.", slots.Count, MaxPlausibleRepointSlots);
                    return new DataExpansionCore.ExpandResult { Success = false, Error = error };
                }

                Log.Notify(string.Format(
                    "MapSetting list expand: base 0x{0:X} -> 0x{1:X}, {2} -> {3} rows, {4} reference(s) repointed.",
                    U.toOffset(oldBase), result.NewBaseAddress, currentCount, newCount, slots.Count));
                return result;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap, undo);
                error = R._("Map setting table expansion failed: {0}", ex.Message);
                return new DataExpansionCore.ExpandResult { Success = false, Error = error };
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// Mirrors <c>WaitIconImportCore.RestoreSnapshot</c> (#885/#923).
        ///
        /// <para>After restoring the bytes, CLEARS the caller's
        /// <paramref name="undo"/> position list (when non-null) so a subsequent
        /// caller-side <c>UndoService.Rollback()</c> cannot replay the now
        /// out-of-date ranges the ambient scope recorded during the
        /// partially-applied expand against the already-restored ROM (Copilot
        /// review on PR #1096 inline #3). The ROM is already byte-identical here,
        /// so those recorded ranges are stale; replaying them would corrupt it.</para>
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap, Undo.UndoData undo)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
            // Discard the stale recorded ranges — the ROM is back to the
            // pre-expand bytes, so a later Rollback of these ranges would
            // re-apply old data over the restored ROM.
            undo?.list?.Clear();
        }

        /// <summary>
        /// Get a human-readable name for a map at the given address.
        /// </summary>
        static string GetMapName(ROM rom, uint addr)
        {
            if (rom.RomInfo.version == 6)
            {
                // FE6: name text at offset 56
                uint id = rom.u16(addr + 56);
                return StripControlChars(FETextDecode.Direct(id));
            }

            // FE7/FE8: chapter prefix + name text at offset 112
            // Chapter number offset: FE7U (152-byte) at 132, FE7JP/FE8 (148-byte) at 128
            string mapCp = "";
            uint chapterOffset = rom.RomInfo.map_setting_datasize >= 152 ? 132u : 128u;
            uint chaptere = rom.u8(addr + chapterOffset);
            if (chaptere > 0)
            {
                if (U.isEven(chaptere))
                    mapCp = "Ch" + (chaptere / 2).ToString();
                else
                    mapCp = "Ch" + (chaptere / 2).ToString() + "x";
            }

            uint textId = rom.u16(addr + 112);
            string textName = StripControlChars(FETextDecode.Direct(textId));
            return (mapCp + " " + textName).Trim();
        }

        /// <summary>
        /// Removes control characters (U+0000–U+001F, excluding tab U+0009) from a
        /// decoded ROM string. FE7/FE8 chapter names can embed U+001F (unit-separator)
        /// as an internal formatting marker; this character renders as a tofu box in
        /// Avalonia's event-condition chapter-name ComboBox on macOS (issue #1705).
        /// </summary>
        internal static string StripControlChars(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) { if (c >= 0x20 || c == '\t') sb.Append(c); }
            return sb.ToString();
        }
    }
}
