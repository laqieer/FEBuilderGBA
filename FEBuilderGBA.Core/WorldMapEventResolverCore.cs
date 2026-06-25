using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Resolve the world-map-event script address for a given map id, branching
    /// by ROM version. READ-ONLY, never-throws.
    ///
    /// This is the cross-platform port of the WinForms version dispatch in
    /// <c>ToolExportEAEventForm.ExportWMAPEventEAButton_Click</c>
    /// (ToolExportEAEventForm.cs:106-135), which calls one of three
    /// <c>GetEventByMapID</c> implementations:
    /// <list type="bullet">
    /// <item>FE8 → <c>WorldMapEventPointerForm.GetEventByMapID(mapid, isBefore)</c></item>
    /// <item>FE7 → <c>WorldMapEventPointerFE7Form.GetEventByMapID(mapid)</c></item>
    /// <item>FE6 → <c>WorldMapEventPointerFE6Form.GetEventByMapID(mapid)</c></item>
    /// </list>
    /// The Avalonia "Export World Map Events" button previously read only the
    /// FE8 <c>worldmap_event_on_stageclear_pointer</c> for every version, which
    /// is <c>0x0</c> for FE6/FE7 — so those versions always reported
    /// "not available" (issue #1420).
    /// </summary>
    public static class WorldMapEventResolverCore
    {
        /// <summary>
        /// Resolve the world-map-event script address for <paramref name="mapid"/>.
        /// </summary>
        /// <param name="rom">The ROM to read (never <c>CoreState.ROM</c> implicitly).</param>
        /// <param name="mapid">Map id (chapter index).</param>
        /// <param name="isSelect">
        /// When <c>true</c>, resolve the "stage-select" (a.k.a. "before"/"selected")
        /// world-map event — only meaningful for FE8 (the second export button).
        /// FE6/FE7 have no stage-select world-map-event export (the WinForms
        /// second button returns early), so they always return
        /// <see cref="U.NOT_FOUND"/> when <paramref name="isSelect"/> is <c>true</c>.
        /// </param>
        /// <returns>
        /// The dereferenced event script offset, or <see cref="U.NOT_FOUND"/> on
        /// any failure / missing path (matches the WinForms early-returns).
        /// </returns>
        public static uint GetEventByMapID(ROM rom, uint mapid, bool isSelect = false)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            int version = rom.RomInfo.version;
            if (version == 8)
            {
                return GetEventByMapIDFE8(rom, mapid, isSelect);
            }
            else if (version == 7)
            {
                if (isSelect) return U.NOT_FOUND; // FE7 has no second (select) export
                return GetEventByMapIDFE7(rom, mapid);
            }
            else
            {//6
                if (isSelect) return U.NOT_FOUND; // FE6 has no second (select) export
                return GetEventByMapIDFE6(rom, mapid);
            }
        }

        /// <summary>
        /// Port of <c>WorldMapEventPointerForm.GetEventByMapID(mapid, isBefore)</c>.
        /// FE8 indexes a size-4 pointer table:
        /// <list type="bullet">
        /// <item><c>isSelect=false</c> ("clear"): <c>wmapid = mapid</c>,
        ///   table = <c>worldmap_event_on_stageclear_pointer</c>.</item>
        /// <item><c>isSelect=true</c> ("select"/before): <c>wmapid =
        ///   GetWorldMapEventIDWhereMapID(mapid)</c>, table =
        ///   <c>worldmap_event_on_stageselect_pointer</c>.</item>
        /// </list>
        /// </summary>
        static uint GetEventByMapIDFE8(ROM rom, uint mapid, bool isSelect)
        {
            uint wmapid;
            uint tablePointer;
            if (isSelect)
            {
                wmapid = GetWorldMapEventIDWhereMapID(rom, mapid);
                tablePointer = rom.RomInfo.worldmap_event_on_stageselect_pointer;
            }
            else
            {
                wmapid = mapid;
                tablePointer = rom.RomInfo.worldmap_event_on_stageclear_pointer;
            }

            if (wmapid == 0)
            {//存在しない (does not exist)
                return U.NOT_FOUND;
            }

            return ResolveIndexedTable(rom, tablePointer, wmapid);
        }

        /// <summary>
        /// Port of <c>WorldMapEventPointerFE7Form.GetEventByMapID(mapid)</c>.
        /// <c>wmapid = GetWorldMapEventIDWhereMapID(mapid)</c>, table =
        /// <c>worldmap_event_on_stageselect_pointer</c> (populated for FE7;
        /// the stageclear pointer is 0x0).
        /// </summary>
        static uint GetEventByMapIDFE7(ROM rom, uint mapid)
        {
            uint wmapid = GetWorldMapEventIDWhereMapID(rom, mapid);
            if (wmapid == 0)
            {//存在しない
                return U.NOT_FOUND;
            }
            return ResolveIndexedTable(rom, rom.RomInfo.worldmap_event_on_stageselect_pointer, wmapid);
        }

        /// <summary>
        /// Port of <c>WorldMapEventPointerFE6Form.GetEventByMapID(mapid)</c>.
        /// FE6 stores a PLIST per map: <c>wmapid =
        /// GetWorldMapEventIDWhereMapID(mapid)</c> then resolve through the
        /// FE6-only WORLDMAP PLIST table.
        /// </summary>
        static uint GetEventByMapIDFE6(ROM rom, uint mapid)
        {
            uint wmapid = GetWorldMapEventIDWhereMapID(rom, mapid);
            if (wmapid == 0)
            {//存在しない
                return U.NOT_FOUND;
            }
            //FE6はPLISTが格納されている. (FE6 stores a PLIST)
            uint addr = MapChangeCore.PlistToOffsetAddr(rom,
                MapChangeCore.PlistType.WORLDMAP_FE6ONLY, wmapid, out _);
            return addr; // already NOT_FOUND on failure
        }

        /// <summary>
        /// Resolve a size-4 indexed world-map-event pointer table. Mirrors
        /// WinForms <c>InputFormRef.IDToAddr(id)</c> (BaseAddress = p32(tablePointer),
        /// BlockSize = 4) followed by <c>p32</c> of the slot.
        /// </summary>
        static uint ResolveIndexedTable(ROM rom, uint tablePointer, uint wmapid)
        {
            if (tablePointer == 0 || !U.isSafetyOffset(tablePointer + 3, rom))
            {
                return U.NOT_FOUND;
            }
            uint baseAddr = rom.p32(tablePointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return U.NOT_FOUND;
            }
            uint slot = baseAddr + wmapid * 4u;
            if (slot + 4u > (uint)rom.Data.Length)
            {
                return U.NOT_FOUND;
            }
            uint eventAddr = rom.p32(slot);
            if (!U.isSafetyOffset(eventAddr, rom))
            {
                return U.NOT_FOUND;
            }
            return eventAddr;
        }

        /// <summary>
        /// Port of <c>MapSettingForm.GetWorldMapEventIDWhereMapID(mapid)</c>:
        /// read the world-map-event PLIST byte from the per-map setting struct at
        /// offset <c>map_setting_worldmap_plist_pos</c>. Returns the plist/index
        /// byte, or <c>0</c> when the map address is invalid (treated as "no
        /// world-map event" by the callers, matching WF where the byte is 0).
        /// </summary>
        static uint GetWorldMapEventIDWhereMapID(ROM rom, uint mapid)
        {
            uint mapAddr = MapSettingCore.GetMapAddr(rom, mapid);
            if (mapAddr == U.NOT_FOUND)
            {
                return 0;
            }
            uint plistPos = rom.RomInfo.map_setting_worldmap_plist_pos;
            if (!U.isSafetyOffset(mapAddr + plistPos, rom))
            {
                return 0;
            }
            return rom.u8(mapAddr + plistPos);
        }
    }
}
