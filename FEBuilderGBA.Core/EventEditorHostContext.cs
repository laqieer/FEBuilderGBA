// SPDX-License-Identifier: GPL-3.0-or-later
//
// EventEditorHostContext (#1591) — the cross-platform "Alloc-Event host context"
// that the Avalonia Event Script editor exposes and the Script Template browser
// consumes so the context-REQUIRED templates (XXXX / YYYY / XXXXXXXX) can
// substitute their placeholders with REAL map-label / pointer values when the
// user inserts into an OPEN editor.
//
// Ports the WinForms modal Alloc-Event flow pieces that the in-editor template
// insert needs:
//   - EventScriptInnerControl.FindMapID(addr)      -> the map-id provider
//   - EventCondForm.GetEndEvent/GetPlayerUnits/GetEnemyUnits(mapid)
//   - EventScriptInnerControl.IsUseLabelID + EventTemplateImpl.GetUnuseLabelID
//   - EventTemplateImpl.ToPointerToString / ToUShortToString
//
// Every primitive these depend on already lives in Core (MapEventUnitCore,
// EventScriptReferenceScanner, EventScriptUtil, U) so this stays GUI-free and
// never reaches into WinForms.
//
// SAFETY (Copilot plan review #1591 finding #1): GetMapID's WinForms default of
// 0 is a VALID chapter, so we expose TryGetMapID(out mapid) — a map-required
// template that cannot resolve its map must REFUSE (no wrong map-0 pointers),
// never silently substitute chapter 0. The caller (EventTemplateCore) gates on
// TryGetMapID for CALL_END_EVENT / PREPARATION.

using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// The host context an OPEN event editor provides to the template browser so
    /// context-required templates can substitute their placeholders. Implemented
    /// by <see cref="EventEditorHostContext"/> (production) and by test fakes.
    /// </summary>
    public interface IEventEditorHostContext
    {
        /// <summary>
        /// Resolve the map-id of the script currently loaded in the editor.
        /// Returns false (and leaves <paramref name="mapid"/> = 0) when the loaded
        /// address does not resolve to any chapter's event-script entry — in which
        /// case a map-REQUIRED template (CALL_END_EVENT / PREPARATION) must refuse
        /// rather than substitute chapter 0 (#1591 finding #1).
        /// </summary>
        bool TryGetMapID(out uint mapid);

        /// <summary>True when a command currently in the editor's list already uses
        /// the given conditional-label id (port of WinForms
        /// <c>EventScriptInnerControl.IsUseLabelID</c>).</summary>
        bool IsUseLabelID(uint labelID);
    }

    /// <summary>
    /// Production <see cref="IEventEditorHostContext"/> built from the open
    /// editor's loaded base address + its current editable command list.
    /// READ-ONLY: never mutates the ROM or the command list. Every read is
    /// guarded; the methods never throw.
    /// </summary>
    public sealed class EventEditorHostContext : IEventEditorHostContext
    {
        readonly ROM _rom;
        readonly uint _loadedOffset; // OFFSET (not GBA pointer) of the loaded script
        readonly IReadOnlyList<EventScript.OneCode> _commands;

        // Cache the resolved map-id so repeated TryGetMapID calls (one per
        // map-required template) don't re-walk every map each time.
        bool _mapResolved;
        bool _mapHas;
        uint _mapValue;

        /// <param name="rom">the active ROM (the only ROM consulted).</param>
        /// <param name="loadedOffset">the OFFSET of the script loaded in the editor
        /// (already <see cref="U.toOffset"/>'d).</param>
        /// <param name="commands">the editor's current editable command list
        /// (a live reference is fine — only read).</param>
        public EventEditorHostContext(ROM rom, uint loadedOffset, IReadOnlyList<EventScript.OneCode> commands)
        {
            _rom = rom;
            _loadedOffset = loadedOffset;
            _commands = commands ?? Array.Empty<EventScript.OneCode>();
        }

        public bool TryGetMapID(out uint mapid)
        {
            mapid = 0;
            if (_mapResolved)
            {
                mapid = _mapValue;
                return _mapHas;
            }
            _mapResolved = true;
            _mapHas = FindMapID(_rom, _loadedOffset, out _mapValue);
            mapid = _mapValue;
            return _mapHas;
        }

        public bool IsUseLabelID(uint labelID)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                EventScript.OneCode code = _commands[i];
                if (code?.Script == null) continue;
                uint cond_id = EventScriptUtil.GetScriptLabelID(code);
                if (cond_id == U.NOT_FOUND) continue;
                if (cond_id == labelID) return true;
            }
            return false;
        }

        // ----------------------------------------------------------------
        // Map-id provider (port of EventScriptInnerControl.FindMapID)
        // ----------------------------------------------------------------

        /// <summary>
        /// Port of <c>EventScriptInnerControl.FindMapID</c>: walk every map's
        /// event-script entry points (via the Core
        /// <see cref="EventScriptReferenceScanner.EnumerateEventEntries"/>, the
        /// faithful port of <c>MakeEventScriptPointer</c>) and return the map-id
        /// (the entry's <c>tag</c>) whose entry matches the loaded offset.
        /// <para>Unlike WinForms (which defaults to 0), this returns FALSE when no
        /// entry matches — so a map-required template can refuse instead of
        /// silently using chapter 0 (#1591 finding #1). The WinForms world-map
        /// fallback (<c>WorldMapEventPointerForm.MakeEventScriptPointer</c>) is NOT
        /// ported here; a world-map event therefore resolves to "no map" and the
        /// map-required templates refuse — the safe outcome (world-map events have
        /// no chapter end-event/preparation anyway).</para>
        /// </summary>
        public static bool FindMapID(ROM rom, uint loadedOffset, out uint mapid)
        {
            mapid = 0;
            if (rom?.RomInfo == null) return false;

            // EnumerateEventEntries is gated on EventScript/CommentCache wired AND
            // the active ROM; on a headless/early call it returns empty, so we just
            // get "no map" (refuse) — never a wrong substitution.
            List<AddrResult> entries;
            try
            {
                entries = EventScriptReferenceScanner.EnumerateEventEntries(rom);
            }
            catch
            {
                return false;
            }
            if (entries == null) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].addr == loadedOffset)
                {
                    mapid = entries[i].tag;
                    return true;
                }
            }
            return false;
        }

        // ----------------------------------------------------------------
        // Cond-slot pointer resolvers (port of EventCondForm.GetEndEvent /
        // GetPlayerUnits / GetEnemyUnits via MakePointerListBox(mapid, COND)[0]).
        // ----------------------------------------------------------------

        static uint ResolveFirstCondSlot(ROM rom, uint mapid, MapEventUnitCore.CondType wanted)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint mapcond_addr = MapEventUnitCore.GetEventAddrForMap(rom, mapid);
            if (mapcond_addr == U.NOT_FOUND || !U.isSafetyOffset(mapcond_addr, rom))
            {
                return U.NOT_FOUND;
            }

            var slots = MapEventUnitCore.GetCondSlots(rom);
            uint romLen = (uint)rom.Data.Length;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Type != wanted) continue;

                uint slotAddr = (uint)(mapcond_addr + 4 * i);
                if (slotAddr + 4 > romLen) break;

                uint addr = rom.p32(slotAddr);
                if (!U.isSafetyOffset(addr, rom)) continue;
                // Mirror MakePointerListBox's "addr + 8 >= length" reject so a
                // near-EOF pointer is not returned.
                if (addr + 8 >= romLen) continue;

                return addr; // first matching slot (WF arlist[0].addr)
            }
            return U.NOT_FOUND;
        }

        /// <summary>Port of <c>EventCondForm.GetEndEvent(mapid)</c> — first
        /// END_EVENT cond-slot pointer, or <see cref="U.NOT_FOUND"/>.</summary>
        public static uint ResolveEndEvent(ROM rom, uint mapid)
            => ResolveFirstCondSlot(rom, mapid, MapEventUnitCore.CondType.EndEvent);

        /// <summary>Port of <c>EventCondForm.GetPlayerUnits(mapid)</c>.</summary>
        public static uint ResolvePlayerUnits(ROM rom, uint mapid)
            => ResolveFirstCondSlot(rom, mapid, MapEventUnitCore.CondType.PlayerUnit);

        /// <summary>Port of <c>EventCondForm.GetEnemyUnits(mapid)</c>.</summary>
        public static uint ResolveEnemyUnits(ROM rom, uint mapid)
            => ResolveFirstCondSlot(rom, mapid, MapEventUnitCore.CondType.EnemyUnit);

        // ----------------------------------------------------------------
        // Label allocator + substitution-string formatters
        // (ports of EventTemplateImpl.GetUnuseLabelID / ToPointerToString /
        //  ToUShortToString).
        // ----------------------------------------------------------------

        // WF EventUnitForm.INVALIDATE_UNIT_POINTER (a "no unit specified" sentinel).
        public const uint INVALIDATE_UNIT_POINTER = 0xFFFFFF;

        /// <summary>
        /// Port of <c>EventTemplateImpl.GetUnuseLabelID</c>: the first conditional
        /// label id at or after <paramref name="startID"/> not already used by a
        /// command in the host's loaded list. Returns 0xFFFF if none free.
        /// </summary>
        public static uint GetUnuseLabelID(IEventEditorHostContext host, uint startID)
        {
            if (host == null) return 0xFFFF;
            for (uint id = startID; id < 0xFFFF; id++)
            {
                if (!host.IsUseLabelID(id)) return id;
            }
            return 0xFFFF;
        }

        /// <summary>Port of <c>EventTemplateImpl.ToPointerToString</c>: a 32-bit
        /// little-endian pointer as 8 hex chars (NOT_FOUND -> the invalidate
        /// sentinel), matching the config <c>XXXXXXXX</c> wire form.</summary>
        public static string ToPointerToString(uint addr)
        {
            if (addr == U.NOT_FOUND)
            {
                addr = INVALIDATE_UNIT_POINTER;
            }
            return U.ToHexString8(U.ChangeEndian32(U.toPointer(addr)));
        }

        /// <summary>Port of <c>EventTemplateImpl.ToUShortToString</c>: a 16-bit
        /// little-endian value as the first 4 hex chars (the config <c>XXXX</c>
        /// label wire form).</summary>
        public static string ToUShortToString(uint v)
        {
            return U.ToHexString8(U.ChangeEndian32(v)).Substring(0, 4);
        }
    }
}
