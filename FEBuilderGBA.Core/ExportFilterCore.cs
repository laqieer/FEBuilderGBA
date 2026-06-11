using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// #1028 Slice B — Text Editor Export Filter (category selector). Cross-platform,
    /// strictly READ-ONLY. Faithful port of the WinForms
    /// <c>ToolTranslateROM.InitExportFilter(filter)</c> dispatch + the per-form
    /// <c>{Form}.MakeVarsIDArray</c> methods it calls.
    ///
    /// WF builds a <c>Dictionary&lt;int,bool&gt;</c> of "kept" text ids (filter == 0
    /// or invalid -> null = ALL). The TSV export then keeps only text entries whose
    /// index <c>i ∈ ExportFilterArray</c>. We mirror that exactly:
    /// <see cref="BuildFilteredTextIds"/> returns <c>null</c> for ALL (filter 0 /
    /// invalid), otherwise the same id set WF collects.
    ///
    /// The 11 categories (matching WF indices):
    ///   0  All                (null)
    ///   1  Unit
    ///   2  Class
    ///   3  Item
    ///   4  SoundRoom          (FE6 uses SoundRoomFE6Form)
    ///   5  SupportTalk
    ///   6  Skill              (SkillSystemTextScanner)
    ///   7  BattleTalk         (+ FE8 event-pointer-when-0, FE6/7 N tables)
    ///   8  Haiku              (+ FE8 event-pointer-when-0, FE7 tutorial tables)
    ///   9  ED                 (version-specific WF set)
    ///   10 EventCond          (EventScriptReferenceScanner.CollectEventCondTextIds)
    ///
    /// Guards: every read goes through <see cref="TextReferenceFinder"/> (bounds-safe)
    /// or explicit U.isSafetyOffset checks; a version-absent category yields an empty
    /// set (documented), never throws.
    /// </summary>
    public static class ExportFilterCore
    {
        /// <summary>WF filter-category labels (R-keys). Indices 0..10.</summary>
        public static readonly string[] FilterLabelKeys = new[]
        {
            "All",
            "Unit",
            "Class",
            "Item",
            "Sound Room",
            "Support Talk",
            "Skill",
            "Battle Talk",
            "Death Quote",
            "Ending",
            "Chapter Text",
        };

        const uint LargeMaxCount = 0x400u;

        /// <summary>
        /// Build the set of text ids the given filter category keeps. Returns
        /// <c>null</c> for "All" (filter 0) OR an invalid index — matching WF
        /// (InitExportFilter sets ExportFilterArray = null in its else branch).
        /// Never throws; a version-absent category returns an empty set.
        /// </summary>
        public static HashSet<uint> BuildFilteredTextIds(ROM rom, int filterIndex)
        {
            if (rom?.RomInfo == null || rom.Data == null) return null;
            if (filterIndex <= 0 || filterIndex > 10) return null; // 0 / invalid => All

            var ids = new HashSet<uint>();
            int version = rom.RomInfo.version;
            var info = rom.RomInfo;

            switch (filterIndex)
            {
                case 1: // Unit — UnitForm.MakeVarsIDArray {0,2}
                    Collect(rom, ids, FixedTable(info.unit_pointer,
                        info.unit_datasize, MaxOr(info.unit_maxcount, 0x100), new uint[] { 0, 2 }));
                    break;

                case 2: // Class — ClassForm.MakeVarsIDArray {0,2}
                    Collect(rom, ids, FixedTable(info.class_pointer,
                        info.class_datasize, 0x100, new uint[] { 0, 2 }));
                    break;

                case 3: // Item — ItemForm.MakeVarsIDArray {0,2,4}
                    Collect(rom, ids, FixedTable(info.item_pointer,
                        info.item_datasize, 0x100, new uint[] { 0, 2, 4 }));
                    break;

                case 4: // SoundRoom — SoundRoomForm {12}+song{0}; FE6 SoundRoomFE6Form {4,8}+song{0}
                    CollectSoundRoom(rom, ids);
                    break;

                case 5: // SupportTalk — version-specific offsets
                    CollectSupportTalk(rom, ids);
                    break;

                case 6: // Skill — SkillSystemTextScanner
                    foreach (uint id in SkillSystemTextScanner.CollectSkillTextIds(rom)) ids.Add(id);
                    break;

                case 7: // BattleTalk
                    CollectBattleTalk(rom, ids);
                    break;

                case 8: // Haiku (death quotes)
                    CollectHaiku(rom, ids);
                    break;

                case 9: // ED
                    CollectED(rom, ids);
                    break;

                case 10: // EventCond (chapter text)
                    EventScriptReferenceScanner.CollectEventCondTextIds(rom, ids);
                    break;
            }

            return ids;
        }

        // ---- shared helpers ------------------------------------------------

        static uint MaxOr(uint v, uint fallback) => v != 0 ? v : fallback;

        static TextRefTableDescriptor FixedTable(uint pointerField, uint entrySize,
            uint maxCount, uint[] offsets)
        {
            return new TextRefTableDescriptor
            {
                Kind = "Filter",
                PointerField = pointerField,
                EntrySize = entrySize,
                MaxCount = maxCount,
                TextIdOffsets = offsets,
            };
        }

        static void Collect(ROM rom, HashSet<uint> ids, TextRefTableDescriptor desc)
        {
            if (desc == null || desc.PointerField == 0 || desc.EntrySize == 0) return;
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, new[] { desc }))
                ids.Add(id);
        }

        static void AddTextId(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        // ---- category 4: SoundRoom ----------------------------------------
        // SoundRoomForm.MakeVarsIDArray: AppendTextID {12} + AppendSongID {0}.
        // SoundRoomFE6Form.MakeVarsIDArray: AppendTextID {4,8} + AppendSongID {0}.
        // WF dumps BOTH text and song ids into ExportFilterArray by raw value, so
        // we include the song-id offset (offset 0) too. The terminator/entry-size
        // come from the registry's SoundRoom descriptor.
        static void CollectSoundRoom(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            if (info.sound_room_pointer == 0 || info.sound_room_datasize == 0) return;
            uint[] offsets = info.version == 6
                ? new uint[] { 4, 8, 0 }   // text {4,8} + song {0}
                : new uint[] { 12, 0 };    // text {12} + song {0}
            Collect(rom, ids, FixedTable(info.sound_room_pointer,
                info.sound_room_datasize, LargeMaxCount, offsets));
        }

        // ---- category 5: SupportTalk --------------------------------------
        // FE6 {4,8,12} size 16; FE7 {4,8,12} size 20; FE8 {4,6,8} size 16.
        static void CollectSupportTalk(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            if (info.support_talk_pointer == 0) return;
            uint entrySize;
            uint[] offsets;
            if (info.version == 6) { entrySize = 16; offsets = new uint[] { 4, 8, 12 }; }
            else if (info.version == 7) { entrySize = 20; offsets = new uint[] { 4, 8, 12 }; }
            else { entrySize = 16; offsets = new uint[] { 4, 6, 8 }; }
            Collect(rom, ids, FixedTable(info.support_talk_pointer, entrySize, LargeMaxCount, offsets));
        }

        // ---- category 7: BattleTalk ---------------------------------------
        // FE8 EventBattleTalkForm: per-entry size 16; text id at offset 8; if
        //   textid <= 0, FOLLOW the event pointer at offset 12 (scan its script).
        // FE7 EventBattleTalkFE7Form: main {4} size 16 + N1 (BattleTalk2) {4} size 12.
        // FE6 EventBattleTalkFE6Form: main {4} size 12 + N (BattleTalk2) {4} size 16.
        static void CollectBattleTalk(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            if (info.version == 8)
            {
                CollectBattleTalkFE8(rom, ids);
            }
            else if (info.version == 7)
            {
                Collect(rom, ids, FixedTable(info.event_ballte_talk_pointer, 16, LargeMaxCount, new uint[] { 4 }));
                Collect(rom, ids, FixedTable(info.event_ballte_talk2_pointer, 12, LargeMaxCount, new uint[] { 4 }));
            }
            else // FE6
            {
                Collect(rom, ids, FixedTable(info.event_ballte_talk_pointer, 12, LargeMaxCount, new uint[] { 4 }));
                Collect(rom, ids, FixedTable(info.event_ballte_talk2_pointer, 16, LargeMaxCount, new uint[] { 4 }));
            }
        }

        static void CollectBattleTalkFE8(ROM rom, HashSet<uint> ids)
        {
            uint pf = rom.RomInfo.event_ballte_talk_pointer;
            if (pf == 0 || !U.isSafetyOffset(pf + 3, rom)) return;
            uint baseAddr = rom.p32(pf);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            var tracelist = new List<uint>();
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 16)
            {
                if (!U.isSafetyOffset(p + 15, rom)) break;
                // SupportTalkForm-style empty-run + sentinel guard: WF Init stops
                // on u16==0xFFFF OR (i>10 && IsEmpty(addr, 16*10)).
                ushort sentinel = (ushort)rom.u16(p);
                if (sentinel == 0xFFFF) break;
                if (i > 10 && p + 160 <= (uint)rom.Data.Length && rom.IsEmpty(p, 160)) break;

                uint textid = rom.u16(p + 8);
                if (textid == 0)
                {
                    uint eventAddr = rom.p32(p + 12);
                    if (U.isSafetyOffset(eventAddr, rom))
                        EventScriptReferenceScanner.ScanScriptForTextIds(
                            rom, CoreState.EventScript, eventAddr, tracelist, ids);
                }
                else
                {
                    AddTextId(ids, textid);
                }
            }
        }

        // ---- category 8: Haiku --------------------------------------------
        // FE8 EventHaikuForm: size 12; text id at offset 6; if textid <= 0, FOLLOW
        //   event pointer at offset 8.
        // FE7 EventHaikuFE7Form: main {4} size 16 + event ptr at +8 (scan), plus
        //   tutorial tables 1 & 2 (size 12, event ptr at +4, scan).
        // FE6 EventHaikuFE6Form: {4,12} size 16.
        static void CollectHaiku(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            if (info.version == 8)
            {
                CollectHaikuFE8(rom, ids);
            }
            else if (info.version == 7)
            {
                CollectHaikuFE7(rom, ids);
            }
            else // FE6
            {
                Collect(rom, ids, FixedTable(info.event_haiku_pointer, 16, LargeMaxCount, new uint[] { 4, 12 }));
            }
        }

        static void CollectHaikuFE8(ROM rom, HashSet<uint> ids)
        {
            uint pf = rom.RomInfo.event_haiku_pointer;
            if (pf == 0 || !U.isSafetyOffset(pf + 3, rom)) return;
            uint baseAddr = rom.p32(pf);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            var tracelist = new List<uint>();
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 12)
            {
                if (!U.isSafetyOffset(p + 11, rom)) break;
                // WF EventHaikuForm.Init: stop on u16==0xFFFF OR (i>10 && IsEmpty(addr,12*10)).
                ushort sentinel = (ushort)rom.u16(p);
                if (sentinel == 0xFFFF) break;
                if (i > 10 && p + 120 <= (uint)rom.Data.Length && rom.IsEmpty(p, 120)) break;

                uint textid = rom.u16(p + 6);
                if (textid == 0)
                {
                    uint eventAddr = rom.p32(p + 8);
                    if (U.isSafetyOffset(eventAddr, rom))
                        EventScriptReferenceScanner.ScanScriptForTextIds(
                            rom, CoreState.EventScript, eventAddr, tracelist, ids);
                }
                else
                {
                    AddTextId(ids, textid);
                }
            }
        }

        static void CollectHaikuFE7(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            var tracelist = new List<uint>();

            // Main table: size 16; direct text id at offset 4 + event pointer at +8.
            uint pf = info.event_haiku_pointer;
            if (pf != 0 && U.isSafetyOffset(pf + 3, rom))
            {
                uint baseAddr = rom.p32(pf);
                if (U.isSafetyOffset(baseAddr, rom))
                {
                    uint p = baseAddr;
                    for (int i = 0; i < 0x400; i++, p += 16)
                    {
                        if (!U.isSafetyOffset(p + 15, rom)) break;
                        if (rom.u8(p) == 0) break; // WF Init: stop on u8==0
                        if (i > 10 && p + 160 <= (uint)rom.Data.Length && rom.IsEmpty(p, 160)) break;
                        AddTextId(ids, rom.u16(p + 4));
                        uint eventAddr = rom.p32(p + 8);
                        if (U.isSafetyOffset(eventAddr, rom))
                            EventScriptReferenceScanner.ScanScriptForTextIds(
                                rom, CoreState.EventScript, eventAddr, tracelist, ids);
                    }
                }
            }

            // Tutorial tables 1 & 2: size 12, NO direct text offsets, event pointer
            // at +4 (scan). N1_Init.ReInitPointer(event_haiku_tutorial_{1,2}_pointer).
            ScanHaikuTutorial(rom, info.event_haiku_tutorial_1_pointer, ids, tracelist);
            ScanHaikuTutorial(rom, info.event_haiku_tutorial_2_pointer, ids, tracelist);
        }

        static void ScanHaikuTutorial(ROM rom, uint pointerField, HashSet<uint> ids, List<uint> tracelist)
        {
            if (pointerField == 0 || !U.isSafetyOffset(pointerField + 3, rom)) return;
            uint baseAddr = rom.p32(pointerField);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 12)
            {
                if (!U.isSafetyOffset(p + 11, rom)) break;
                if (rom.u8(p) == 0) break; // N1_Init: stop on u8==0
                uint eventAddr = rom.p32(p + 4);
                if (U.isSafetyOffset(eventAddr, rom))
                    EventScriptReferenceScanner.ScanScriptForTextIds(
                        rom, CoreState.EventScript, eventAddr, tracelist, ids);
            }
        }

        // ---- category 9: ED -----------------------------------------------
        // FE8 EDForm: ed_2 {4} + ed_3a {4} + ed_3b {4} (NOT ed_1, no text).
        // FE7 EDFE7Form: ed_3c {4,8} + ed_2 {4} + ed_3a {4} + ed_3b {4}.
        // FE6 EDSensekiCommentForm (senseki {4,8,12}) + EDFE6Form (ed_3a {0,2,4,6}).
        static void CollectED(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            if (info.version == 8)
            {
                Collect(rom, ids, EdTable(info.ed_2_pointer, 8, new uint[] { 4 }));
                Collect(rom, ids, EdTable(info.ed_3a_pointer, 8, new uint[] { 4 }));
                Collect(rom, ids, EdTable(info.ed_3b_pointer, 8, new uint[] { 4 }));
            }
            else if (info.version == 7)
            {
                // ed_3c is a DIRECT base (not a pointer field) — EDFE7Form.N3_Init
                // uses ReInit(ed_3c_pointer).
                CollectEdDirect(rom, ids, info.ed_3c_pointer, 12, new uint[] { 4, 8 });
                Collect(rom, ids, EdTable(info.ed_2_pointer, 8, new uint[] { 4 }));
                Collect(rom, ids, EdTable(info.ed_3a_pointer, 8, new uint[] { 4 }));
                Collect(rom, ids, EdTable(info.ed_3b_pointer, 8, new uint[] { 4 }));
            }
            else // FE6
            {
                // EDSensekiCommentForm: senseki_comment_pointer, size 16, offsets
                // {4,8,12}, stop on u16(addr)==0.
                CollectSenseki(rom, ids);
                // EDFE6Form.N2_Init: ed_3a, size 8, offsets {0,2,4,6}, count i<0x42.
                Collect(rom, ids, new TextRefTableDescriptor
                {
                    Kind = "ED", PointerField = info.ed_3a_pointer, EntrySize = 8,
                    MaxCount = 0x42u, TextIdOffsets = new uint[] { 0, 2, 4, 6 },
                });
            }
        }

        // EDForm N1/N2/N3 tables terminate on u32(addr)==0 (the title text id u32).
        static TextRefTableDescriptor EdTable(uint pointerField, uint entrySize, uint[] offsets)
        {
            return new TextRefTableDescriptor
            {
                Kind = "ED",
                PointerField = pointerField,
                EntrySize = entrySize,
                MaxCount = LargeMaxCount,
                TextIdOffsets = offsets,
                Terminator = (r, entry, i) =>
                {
                    if (entry + 4 > (uint)r.Data.Length) return true;
                    return r.u32(entry) == 0;
                },
            };
        }

        static void CollectEdDirect(ROM rom, HashSet<uint> ids, uint directBase, uint entrySize, uint[] offsets)
        {
            if (directBase == 0) return;
            var desc = new TextRefTableDescriptor
            {
                Kind = "ED",
                DirectBase = directBase,
                EntrySize = entrySize,
                MaxCount = LargeMaxCount,
                TextIdOffsets = offsets,
                Terminator = (r, entry, i) =>
                {
                    if (entry + 4 > (uint)r.Data.Length) return true;
                    return r.u32(entry) == 0;
                },
            };
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, new[] { desc }))
                ids.Add(id);
        }

        static void CollectSenseki(ROM rom, HashSet<uint> ids)
        {
            uint pf = rom.RomInfo.senseki_comment_pointer;
            if (pf == 0) return;
            var desc = new TextRefTableDescriptor
            {
                Kind = "Senseki",
                PointerField = pf,
                EntrySize = 16,
                MaxCount = 0x100u,
                TextIdOffsets = new uint[] { 4, 8, 12 },
                // EDSensekiCommentForm.Init: stop on u16(addr)==0.
                Terminator = (r, entry, i) =>
                {
                    if (entry + 2 > (uint)r.Data.Length) return true;
                    return r.u16(entry) == 0;
                },
            };
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, new[] { desc }))
                ids.Add(id);
        }
    }
}
