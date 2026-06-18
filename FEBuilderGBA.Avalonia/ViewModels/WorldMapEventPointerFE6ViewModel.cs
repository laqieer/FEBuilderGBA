using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// World Map Event Pointer (FE6) editor — port of WinForms
    /// <c>WorldMapEventPointerFE6Form</c>. FE6 is the most divergent of the three
    /// world-map-event variants: unlike the FE8 base
    /// (<see cref="WorldMapEventPointerViewModel"/>) and the FE7 sibling
    /// (<see cref="WorldMapEventPointerFE7ViewModel"/>) — both of which read a
    /// dedicated event-pointer table — FE6 has NO separate table. WF
    /// <c>N_Init</c> drives the list straight off the MAP SETTINGS table
    /// (<c>map_setting_pointer</c> / <c>map_setting_datasize</c>): every map whose
    /// world-map-event PLIST byte is non-zero produces one navigate-only row.
    ///
    /// <para>For a qualifying map the row's resolved address is the entry in the
    /// MAP-pointer table the PLIST indexes —
    /// <c>p32(map_map_pointer_pointer) + worldmapEventPlist * 4</c> — exactly as
    /// WF builds <c>AddrResult.addr</c>. Selecting a row jumps there; the FE6 form
    /// (like its Designer) has NO editable ROM-data fields and NO Write button, so
    /// this VM is strictly read-only — it writes no ROM bytes and so is not a
    /// data-verifiable editor (matching the WF form and the field-completeness
    /// contract).</para>
    /// </summary>
    public class WorldMapEventPointerFE6ViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Build the navigate-only list — port of WF <c>N_Init</c> + <c>MakeList</c>.
        /// Walks the enumerated MAP SETTINGS table (same base / datasize /
        /// terminator heuristic the editor's map list uses via
        /// <see cref="MapSettingCore.MakeMapIDList(ROM)"/>), keeps each map whose
        /// world-map-event PLIST byte is non-zero, and resolves its row address to
        /// the MAP-pointer-table slot the PLIST indexes.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // map_map_pointer_pointer holds the base of the MAP-arrangement PLIST
            // table the FE6 world-map-event PLIST indexes into. Guard the slot +
            // the dereferenced base so a malformed/empty RomInfo never throws.
            uint mapPtrPtr = rom.RomInfo.map_map_pointer_pointer;
            if (mapPtrPtr == 0 || !U.isSafetyOffset(mapPtrPtr, rom)) return new List<AddrResult>();
            uint mapBase = rom.p32(mapPtrPtr);
            if (!U.isSafetyOffset(mapBase, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            foreach (AddrResult ms in MapSettingCore.MakeMapIDList(rom))
            {
                // WF predicate: a map entry counts only when its first dword is a
                // pointer. MakeMapIDList already enforces the table terminator, so
                // this just rejects in-range non-pointer rows (matches N_Init's
                // isData check).
                if (!U.isPointer(rom.u32(ms.addr + 0))) continue;

                uint plist = MapPListResolverCore.GetWorldMapEventIDWhereAddr(rom, ms.addr);
                if (plist == 0 || plist == U.NOT_FOUND) continue;

                // WF row addr = p32(map_map_pointer_pointer) + plist * 4. Bounds-
                // guard the resolved slot so an out-of-range PLIST is skipped
                // rather than producing an unselectable / past-EOF row.
                uint rowAddr = mapBase + plist * 4;
                if (!U.isSafetyOffset(rowAddr, rom)) continue;

                // WF label: ToHexString(i) + GetMapNameWhereAddr(addr) — directly
                // concatenated (no separating space), preserved verbatim.
                string name = U.ToHexString(ms.tag) + MapSettingCore.GetMapNameWhereAddr(rom, ms.addr);
                result.Add(new AddrResult(rowAddr, name, ms.tag));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }

        /// <summary>Number of navigate-only rows (test hook, mirrors the FE7 VM).</summary>
        public int GetListCount() => LoadList().Count;
    }
}
