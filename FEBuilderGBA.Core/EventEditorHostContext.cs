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

        // ----------------------------------------------------------------
        // EventCond-RECORD Alloc-Event side effects (#1592) — the WinForms
        // `InputFormRef.AllocEvent_EventTemplate` surface that mutates the
        // parent EventCond record fields (W2 / B8 / B9 / event-pointer),
        // which is a DIFFERENT surface from #1591's in-editor browser insert.
        //
        // These are PURE resolvers: they compute WHAT to write into the record;
        // the (platform) caller performs the actual undo-tracked ROM write via
        // the EventCond editor's WriteCondRecord path. No ROM mutation here.
        // ----------------------------------------------------------------

        /// <summary>
        /// The two numbered "CALL" buttons from the WinForms EventCond
        /// Alloc-Event template dialog (<c>EventTemplate{1..6}Form</c>):
        /// <list type="bullet">
        /// <item><c>CallEndEvent</c> — write the chapter END_EVENT pointer
        /// (<c>GetEndEvent(GetMapID(...))</c>) into the record's event-pointer
        /// field AND set the victory flag (W2 = 0x03).</item>
        /// <item><c>Call1</c> — write the literal value <c>1</c> into the
        /// event-pointer field (no flag).</item>
        /// </list>
        /// </summary>
        public enum AllocTemplateChoice
        {
            CallEndEvent,
            Call1,
        }

        /// <summary>
        /// The record-field side effects a template choice produces. The caller
        /// applies these to the parent EventCond record (event-pointer field +
        /// W2 victory flag + B8/B9 counter-reinforcement) under an undo scope.
        /// When <see cref="Resolvable"/> is false the caller MUST refuse and
        /// write nothing (no silent map-0 / garbage pointer — #1589/#1591 safety
        /// discipline carried into the record surface).
        /// </summary>
        public struct AllocEventRecordSideEffects
        {
            /// <summary>True ⇒ the caller may write <see cref="EventPtr"/> into
            /// the record's event-pointer field. False (with Resolvable=false)
            /// ⇒ refuse.</summary>
            public bool HasEventPtr;

            /// <summary>The value to write into the event-pointer field —
            /// either the literal <c>1</c> (Call1) or <c>U.toPointer(endAddr)</c>
            /// (CallEndEvent). Only meaningful when <see cref="HasEventPtr"/>.</summary>
            public uint EventPtr;

            /// <summary>True ⇒ set the record's W2 (victory) field to 0x03.
            /// When false the caller follows the WinForms "clear only if exactly
            /// 0x03" rule on the existing value.</summary>
            public bool SetFlag03;

            /// <summary>True ⇒ apply the counter-reinforcement record fields
            /// (B8 = 1, B9 = 255). Only produced by
            /// <see cref="CounterReinforcementSideEffects"/>.</summary>
            public bool CounterReinforcement;

            /// <summary>False ⇒ the choice could not be resolved (no selected/
            /// valid map, or no chapter END_EVENT pointer). The caller MUST
            /// refuse and mutate nothing.</summary>
            public bool Resolvable;
        }

        /// <summary>
        /// Resolve the record side effects for a numbered CALL button, ported
        /// verbatim from <c>InputFormRef.AllocEvent_EventTemplate</c> +
        /// <c>EventTemplate{1..6}Form.CALL_*_button_Click</c>:
        /// <list type="bullet">
        /// <item><c>Call1</c> ⇒ always resolvable; event-pointer field = literal
        /// <c>1</c>; no flag.</item>
        /// <item><c>CallEndEvent</c> ⇒ resolve <c>GetEndEvent(GetMapID())</c>; on
        /// success event-pointer = <c>U.toPointer(endAddr)</c> and W2 = 0x03; on
        /// failure (no selected/valid map, or no END_EVENT pointer) returns
        /// <c>Resolvable=false</c> — the caller refuses (no wrong pointer).</item>
        /// </list>
        /// The explicit invalid-map guard (Copilot #1592 review finding #4):
        /// refuse BEFORE <see cref="ResolveEndEvent"/> when
        /// <paramref name="mapid"/> is <see cref="U.NOT_FOUND"/>, so a wrapped /
        /// unselected map id can never produce a plausible-but-wrong address.
        /// </summary>
        public static AllocEventRecordSideEffects ResolveCallTemplate(ROM rom, uint mapid, AllocTemplateChoice choice)
        {
            var eff = new AllocEventRecordSideEffects();

            if (choice == AllocTemplateChoice.Call1)
            {
                // CALL_1: literal 1 written directly into the event-pointer
                // field (WinForms `src_object.Value = callEventAddr` when ==1).
                eff.HasEventPtr = true;
                eff.EventPtr = 1;
                eff.SetFlag03 = false;
                eff.Resolvable = true;
                return eff;
            }

            // CALL_EndEvent. Invalid-map guard first (finding #4): never call
            // ResolveEndEvent with NOT_FOUND — refuse outright.
            if (rom?.RomInfo == null || mapid == U.NOT_FOUND)
            {
                eff.Resolvable = false;
                return eff;
            }

            uint endAddr = ResolveEndEvent(rom, mapid);
            if (endAddr == U.NOT_FOUND)
            {
                // No chapter END_EVENT pointer — refuse (no silent garbage).
                eff.Resolvable = false;
                return eff;
            }

            eff.HasEventPtr = true;
            eff.EventPtr = U.toPointer(endAddr);
            eff.SetFlag03 = true; // CALL_EndEvent sets NeedFlag03 = true.
            eff.Resolvable = true;
            return eff;
        }

        /// <summary>
        /// The counter-reinforcement record side effects, ported from
        /// <c>EventTemplate3Form.EnemyReinforcementByCounterButton_Click</c>
        /// (sets <c>CounterReinforcementEvent = true</c>) + the
        /// <c>AllocEvent_EventTemplate</c> follow-up that writes <c>B8 = 1</c>
        /// and <c>B9 = 255</c>. Always resolvable (no map/end-event lookup) —
        /// the caller allocates the counter event block and applies these
        /// fields to the TURN record (where B8/B9 are TurnStart/TurnEnd).
        /// </summary>
        public static AllocEventRecordSideEffects CounterReinforcementSideEffects()
        {
            return new AllocEventRecordSideEffects
            {
                CounterReinforcement = true,
                Resolvable = true,
            };
        }

        /// <summary>
        /// True when the EventCond record category + condition-type combination
        /// is one of the WinForms NEWALLOC-EVENT surfaces — i.e. the record's
        /// event-pointer field at +4 is a genuine top-level CODE pointer (not a
        /// chest's packed item/durability/gold or a shop's item-list pointer)
        /// (Copilot #1592 review finding #3). Mirrors the
        /// <c>*_L_4_NEWALLOC_EVENT*</c> button surfaces in
        /// <c>EventCondForm.Designer.cs</c>:
        /// <list type="bullet">
        /// <item>TURN N02 (type 0x02 / FE7 NFE702)</item>
        /// <item>TALK N03 (0x03), N04 (0x04), FE6 N0D (0x0D)</item>
        /// <item>OBJECT N06 (0x06 Visit Village), N08 (0x08 Door) — NOT N05/N07
        /// chest or N0A shop</item>
        /// <item>ALWAYS N01 (0x01), N0B (0x0B), N0D (0x0D), N0E (0x0E)</item>
        /// </list>
        /// </summary>
        public static bool IsEventPointerSurface(MapEventUnitCore.CondType category, uint condType)
        {
            switch (category)
            {
                case MapEventUnitCore.CondType.Turn:
                    // TURN N02 has the EVENT3 template; treat any non-zero TURN
                    // type as an event-pointer surface (TURN records always carry
                    // a real event pointer at +4).
                    return condType == 0x02;
                case MapEventUnitCore.CondType.Talk:
                    return condType == 0x03 || condType == 0x04 || condType == 0x0D;
                case MapEventUnitCore.CondType.Object:
                    return condType == 0x06 || condType == 0x08;
                case MapEventUnitCore.CondType.Always:
                    return condType == 0x01 || condType == 0x0B
                        || condType == 0x0D || condType == 0x0E;
                default:
                    return false;
            }
        }

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
