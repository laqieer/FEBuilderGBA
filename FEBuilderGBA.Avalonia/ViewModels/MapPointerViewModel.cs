using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _mapDataPointer;
        uint _selectedId;
        bool _canWrite;

        // Base address of the PLIST table currently shown in the list
        // (rom.p32 of the active filter's base pointer). Used to derive the
        // slot id of a loaded row so the VM is self-consistent even if a caller
        // does not pre-set SelectedId. 0 = no list loaded yet.
        uint _listBaseAddr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint MapDataPointer { get => _mapDataPointer; set => SetField(ref _mapDataPointer, value); }

        /// <summary>
        /// PLIST slot id (list index) of the currently-selected row. Slot 0 is
        /// the reserved NULL entry — every map-data resolver
        /// (<see cref="MapPListResolverCore"/> / <see cref="MapChangeCore"/>)
        /// treats a 0 plist as "unset", so writing a non-zero pointer there
        /// breaks the NULL sentinel. WinForms blocks this via
        /// <c>InputFormRef.UseWriteProtectionID00</c> (default option
        /// <c>func_write_00 = Deny</c>) plus <c>MapPointerForm.Write_Plsit</c>'s
        /// <c>plist == 0</c> reject; <see cref="WriteMapPointer"/> mirrors that
        /// hard block.
        /// </summary>
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>Get the list of PLIST type names for the filter combo.</summary>
        public List<string> GetPlistTypeNames()
        {
            var names = new List<string> { "MAP", "CONFIG", "OBJ/PAL", "CHANGE", "EVENT", "ANIMATION1", "ANIMATION2" };

            // WORLDMAP type is only available for FE6
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo != null && rom.RomInfo.version == 6)
                names.Add("WORLDMAP");

            return names;
        }

        /// <summary>
        /// Map the filter combo's <paramref name="typeIndex"/> to the shared
        /// <see cref="MapChangeCore.PlistType"/>. The combo order is
        /// MAP(0), CONFIG(1), OBJ/PAL(2), CHANGE(3), EVENT(4),
        /// ANIMATION1(5), ANIMATION2(6), WORLDMAP(7) — see
        /// <see cref="GetPlistTypeNames"/>.
        ///
        /// <para>NORMALIZATION vs WF: WF's split filter bundles
        /// ANIMATION1+ANIMATION2 into a single "ANIMATION" row (both share
        /// one table) and OBJECT+PALETTE into a single "OBJ/PAL" row. This
        /// Avalonia combo keeps ANIMATION1 / ANIMATION2 as two rows. Because
        /// both anime pointers resolve to the SAME table base in vanilla
        /// ROMs, the resolver's <see cref="MapPListResolverCore.ConvertBaseAddrToType"/>
        /// maps either base to <c>ANIMATION</c> first (WF precedence), so
        /// selecting "ANIMATION2" still resolves labels under the ANIMATION
        /// purpose — exactly matching WF's combined filter. The OBJ/PAL row
        /// maps to OBJECT (PAL/PAL2/OBJ all resolve under OBJECT in the
        /// split resolver, mirroring WF).</para>
        /// </summary>
        static MapChangeCore.PlistType TypeIndexToPlistType(int typeIndex)
        {
            return typeIndex switch
            {
                1 => MapChangeCore.PlistType.CONFIG,
                2 => MapChangeCore.PlistType.OBJECT,
                3 => MapChangeCore.PlistType.CHANGE,
                4 => MapChangeCore.PlistType.EVENT,
                5 => MapChangeCore.PlistType.ANIMATION,
                6 => MapChangeCore.PlistType.ANIMATION2,
                7 => MapChangeCore.PlistType.WORLDMAP_FE6ONLY,
                _ => MapChangeCore.PlistType.MAP,
            };
        }

        /// <summary>Get ROM pointer for the given PLIST type index.</summary>
        uint GetPlistPointer(int typeIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            // Resolve through the shared Core base-pointer seam so the
            // WORLDMAP base is the FE6 world-map EVENT table
            // (map_worldmapevent_pointer) — NOT worldmap_point_pointer. The
            // old worldmap_point_pointer was the real bug (#952): WF's
            // PLIST_TYPE.WORLDMAP_FE6ONLY uses map_worldmapevent_pointer.
            return MapChangeCore.GetPlistBasePointer(rom, TypeIndexToPlistType(typeIndex));
        }

        public List<AddrResult> LoadMapPointerList(int typeIndex = 0)
        {
            // Reset the remembered table base FIRST so an early return below
            // (no ROM, ptr==0, unsafe base) can't leave a STALE base from a
            // previous load — which would make LoadMapPointer derive a wrong
            // SelectedId and weaken the slot-0 write-protection guard
            // (Copilot PR #1478 review thread). 0 = "unknown" → derivation
            // fails safe to id 0 (reject).
            _listBaseAddr = 0;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = GetPlistPointer(typeIndex);
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Remember the active table base so LoadMapPointer can derive a
            // row's slot id (id == (addr - baseAddr) / 4) for the slot-0
            // write-protection guard (#1416).
            _listBaseAddr = baseAddr;

            // Use the canonical PLIST limit: split-PLIST ROMs force 256
            // (byte-indexed) — the vanilla map_map_pointer_list_default_size
            // (~0xEC on FE8) would TRUNCATE the list on split layouts. Mirrors
            // WF MapPointerForm.Init's limit calculation (#953 review).
            uint limit = MapChangeCore.GetPlistLimit(rom);
            if (limit == 0) limit = 256;

            // Build one local resolve cache for the whole list (each map's
            // PLists + name read once) and resolve every row to a map-name
            // label (e.g. "MAP Ch1" / "ANIME1 Prologue" / "NULL" /
            // "-EMPTY-") instead of a raw 0x… pointer (#952).
            var cache = MapPListResolverCore.BuildCache(rom);
            MapChangeCore.PlistType type = TypeIndexToPlistType(typeIndex);

            var result = new List<AddrResult>();
            for (uint i = 0; i < limit; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                string label = MapPListResolverCore.ResolveLabel(rom, type, i, cache);
                string name = $"{U.ToHexString(i)} {label}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapPointer(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            MapDataPointer = rom.u32(addr);

            // Derive the PLIST slot id of this row from the active table base so
            // the slot-0 write-protection guard works without the View having to
            // push the id explicitly (#1416). Each row is a 4-byte pointer, so
            // id == (addr - baseAddr) / 4. Falls back to 0 (reject) when the
            // base is unknown or the addr is below it — fail-safe.
            SelectedId = (_listBaseAddr != 0 && addr >= _listBaseAddr)
                ? (addr - _listBaseAddr) / 4
                : 0;

            CanWrite = true;
        }

        /// <summary>
        /// Write the edited pointer into the selected PLIST slot.
        /// Returns <c>null</c> on success, or a non-null error message when the
        /// write is rejected and the ROM is left UNCHANGED.
        ///
        /// <para>WF parity: PLIST slot 0 is the reserved NULL entry. WinForms
        /// makes a slot-0 write physically impossible by default
        /// (<c>UseWriteProtectionID00</c> + default <c>func_write_00 = Deny</c>,
        /// and <c>MapPointerForm.Write_Plsit</c>'s <c>plist == 0</c> reject).
        /// We mirror that hard block here — checked BEFORE any ROM access so the
        /// undo scope never sees a write (#1416).</para>
        /// </summary>
        public string? WriteMapPointer()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0)
                return "No map pointer slot is loaded.";

            // WF parity (UseWriteProtectionID00 / Write_Plsit): refuse to write
            // the reserved NULL slot 0. Guard FIRST — no ROM mutation occurs.
            if (SelectedId == 0)
                return R._("PLIST=0 is the reserved NULL entry and must stay 0. Writing here would corrupt the map-data NULL sentinel, so the write is rejected.");

            rom.write_u32(CurrentAddr, MapDataPointer);
            return null;
        }

        public int GetListCount() => LoadMapPointerList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["MapDataPointer"] = $"0x{MapDataPointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["MapDataPointer"] = "u32@0x00",
            };
        }
    }
}
