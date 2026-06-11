// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Text Editor "used text id" union (faithful U.MakeVarsIDArray port).
//
// WinForms TextForm.SearcFreeArea_Click / TextForm.UpdateRef both build the union of
// every referenced text id via AsmMapFileAsmCache.GetVarsIDArray() (a cached
// U.MakeVarsIDArray, U.cs:2661) and then:
//   - free-area:  UseValsID.ConvertMaps(union)  — masks by RAW id REGARDLESS of
//                 TargetType (so SONG/BG ids mask too) + UseTextIDCache.AppendList.
//   - cross-ref:  filter union to TargetType == TEXTID, ID == id (+ cache MakeUseTextID).
//
// This Core seam mirrors U.MakeVarsIDArray's per-version dispatch and reuses the
// already-ported collectors (TextRefTableRegistry / TextReferenceFinder /
// ExportFilterCore / SkillSystemTextScanner / EventScriptReferenceScanner /
// AsmMapTextSymbolReader / PatchTextRefScannerCore), adding the menu / status-rmenu /
// worldmap-event / FE8N-Ver3-skill collectors that had no Core home yet.
//
// Result is typed: two sets — TextIds (TargetType==TEXTID) and SongIds (SONG) — so the
// free-area mask uses (TextIds ∪ SongIds ∪ cache) while cross-ref uses TextIds only.
// (No BG collector contributes here: U.MakeVarsIDArray's TEXT-editor union has no BG
// AppendBGID call for the relevant forms — patch MULTICG/BGICON BG ids are appended by
// PatchForm but are intentionally NOT folded into this text/song union, matching the
// fact that the free-area text scan over-approximates only via TEXT+SONG numeric ids.)
//
// READ-ONLY: never mutates the ROM. Every collector guards its reads; a foreign /
// half-initialised ROM degrades gracefully (the event-script collectors return their
// partial contribution, gated like EventScriptReferenceScanner).

using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Builds the Text Editor "used text id" union (faithful
    /// <c>U.MakeVarsIDArray</c> port). See file header.
    /// </summary>
    public static class MakeVarsIDArrayCore
    {
        /// <summary>Typed result: TEXTID set + SONG set (BG omitted — see header).</summary>
        public sealed class UsedRefs
        {
            /// <summary>Text ids (UseValsID TargetType == TEXTID).</summary>
            public HashSet<uint> TextIds { get; } = new HashSet<uint>();
            /// <summary>Song ids (UseValsID TargetType == SONG) — folded into the
            /// free-area numeric over-approximation only.</summary>
            public HashSet<uint> SongIds { get; } = new HashSet<uint>();
        }

        /// <summary>
        /// Build every referenced TEXT id + SONG id for the loaded ROM, mirroring
        /// WinForms <c>U.MakeVarsIDArray</c>'s per-version dispatch. Strictly
        /// READ-ONLY; never throws (each collector is guarded).
        /// </summary>
        public static UsedRefs BuildAllUsedRefs(ROM rom)
        {
            var r = new UsedRefs();
            if (rom?.RomInfo == null || rom.Data == null) return r;

            var info = rom.RomInfo;
            int version = info.version;
            var text = r.TextIds;
            var song = r.SongIds;

            // ---- Universal: Unit / Class / Item (registry fixed tables) ----
            CollectFixed(rom, text, info.unit_pointer, info.unit_datasize,
                MaxOr(info.unit_maxcount, 0x100), new uint[] { 0, 2 });
            CollectFixed(rom, text, info.class_pointer, info.class_datasize,
                0x100, new uint[] { 0, 2 });
            CollectFixed(rom, text, info.item_pointer, info.item_datasize,
                0x100, new uint[] { 0, 2, 4 });

            // ---- EventCond (every map's event scripts) ----
            EventScriptReferenceScanner.CollectEventCondTextIds(rom, text);

            // ---- !is_multibyte only: StatusParam (WF no-op*) + MapTerrainNameEng ----
            // *StatusParamForm.MakeVarsIDArray uses Init(null) whose basepointer is 0
            //  -> BaseAddress 0 -> DataCount 0 -> contributes NOTHING in WinForms. We
            //  faithfully reproduce that (no StatusParam collection).
            if (!info.is_multibyte)
            {
                if (info.map_terrain_name_pointer != 0)
                {
                    CollectFixedTerminated(rom, text, info.map_terrain_name_pointer, 2, 0x100,
                        new uint[] { 0 }, StopU16Zero);
                }
            }

            // ---- All versions: MenuDefinition (6 roots) + StatusRMenu (6 roots) +
            //      SoundBossBGM (SONG) ----
            CollectMenuDefinitions(rom, text);
            CollectStatusRMenu(rom, text);
            CollectSoundBossBGM(rom, song);

            // ---- Version-specific dispatch ----
            if (version == 8)
            {
                CollectFE8(rom, info, text, song);
            }
            else if (version == 7)
            {
                CollectFE7(rom, info, text, song);
            }
            else // 6
            {
                CollectFE6(rom, info, text, song);
            }

            // ---- PatchForm.MakeVarsIDArray (ADDR + STRUCT TEXT/SONG/EVENT) ----
            PatchTextRefScannerCore.CollectUsedRefs(rom, text, song);

            // ---- asmmap.MakeVarsIDArray (TEXTBATCH/SHORT + EVENT) ----
            AsmMapTextSymbolReader.CollectUsedTextIds(rom, text);

            return r;
        }

        /// <summary>
        /// The free-area "referenced" id set: every TEXT id ∪ every SONG id ∪ the
        /// cache's user/system/FE8-reserved ids. Mirrors WinForms
        /// <c>UseValsID.ConvertMaps(GetVarsIDArray())</c> (raw-id mask, ignoring
        /// TargetType) + <c>UseTextIDCache.AppendList</c>. Id 0 is always present
        /// (WF seeds <c>textmap[0] = true</c>).
        /// </summary>
        public static HashSet<uint> BuildFreeAreaUsedSet(ROM rom, ITextIDCache cache)
        {
            var refs = BuildAllUsedRefs(rom);
            var used = new HashSet<uint>();
            used.Add(0); // WF: textmap[0] = true
            foreach (uint id in refs.TextIds) used.Add(id);
            foreach (uint id in refs.SongIds) used.Add(id);
            if (cache != null)
            {
                try
                {
                    foreach (uint id in cache.EnumerateUsedTextIds(rom)) used.Add(id);
                }
                catch { /* cache enumeration must never break the scan */ }
            }
            return used;
        }

        // ===================================================================
        // FE8 dispatch (mirrors U.MakeVarsIDArray version==8 branch)
        // ===================================================================
        static void CollectFE8(ROM rom, ROMFEINFO info, HashSet<uint> text, HashSet<uint> song)
        {
            // WorldMapEventPointer (stage-clear + stage-select arrays + 3 fixed events)
            CollectWorldMapEventFE8(rom, info, text);

            // StatusOption + StatusUnitsMenu + MapSetting (registry fixed tables)
            CollectFromRegistry(rom, text, "StatusOption", "UnitsMenu", "MapSetting",
                "WorldMapPoint", "Dic", "DicChapter", "DicTitle", "OPClassDemo");

            // SupportTalk / ED / Haiku / BattleTalk / SoundRoom — use the faithful
            // ExportFilterCore collectors (they follow event pointers when textid==0
            // for FE8 Haiku/BattleTalk, which the registry fixed tables don't).
            ExportFilterCollect(rom, text, /*SupportTalk*/5, /*BattleTalk*/7,
                /*Haiku*/8, /*ED*/9, /*SoundRoom*/4);

            // WorldMapBGM (SONG)
            CollectFixedTerminated(rom, song, info.worldmap_bgm_pointer, 4, 0x100,
                new uint[] { 0, 2 }, /*no terminator: WF Init has none for song scan*/ null);

            // Skills
            foreach (uint id in SkillSystemTextScanner.CollectSkillTextIds(rom)) AddTextId(text, id);
            // FE8N Ver3 skill table (export filter skips it; the WF free-area union
            // includes offset 0 via SkillConfigFE8NVer3SkillForm.MakeVarsIDArray).
            CollectFE8NVer3Skill(rom, text);
        }

        // ===================================================================
        // FE7 dispatch (mirrors U.MakeVarsIDArray version==7 branch)
        // ===================================================================
        static void CollectFE7(ROM rom, ROMFEINFO info, HashSet<uint> text, HashSet<uint> song)
        {
            // MapSetting + OPClassDemo + StatusOption + StatusUnitsMenu +
            // EDSenseki + EventFinalSerif (registry fixed tables)
            CollectFromRegistry(rom, text, "MapSetting", "OPClassDemo", "StatusOption",
                "UnitsMenu", "Senseki", "FinalSerif");

            // SupportTalk / ED / Haiku / BattleTalk / SoundRoom (faithful collectors
            // — FE7 Haiku follows event pointers + tutorial tables).
            ExportFilterCollect(rom, text, 5, 7, 8, 9, 4);

            // WorldMapEventPointer FE7 (stage-select array + ending1/ending2 events)
            CollectWorldMapEventFE7(rom, info, text);
        }

        // ===================================================================
        // FE6 dispatch (mirrors U.MakeVarsIDArray else (==6) branch)
        // ===================================================================
        static void CollectFE6(ROM rom, ROMFEINFO info, HashSet<uint> text, HashSet<uint> song)
        {
            // MapSetting (registry)
            CollectFromRegistry(rom, text, "MapSetting");

            // SupportTalk / ED / Haiku / BattleTalk / SoundRoom (faithful collectors).
            ExportFilterCollect(rom, text, 5, 7, 8, 9, 4);

            // WorldMapEventPointer FE6 (PLIST-resolved per map)
            CollectWorldMapEventFE6(rom, info, text);
        }

        // ===================================================================
        // Helpers
        // ===================================================================
        static uint MaxOr(uint v, uint fallback) => v != 0 ? v : fallback;

        static void AddTextId(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        static void AddSongId(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        static bool StopU16Zero(ROM rom, uint entry, uint i)
        {
            if (entry + 2 > (uint)rom.Data.Length) return true;
            return rom.u16(entry) == 0;
        }

        // Collect text ids from a simple fixed pointer-field table (no terminator).
        static void CollectFixed(ROM rom, HashSet<uint> ids, uint pointerField,
            uint entrySize, uint maxCount, uint[] offsets)
        {
            if (pointerField == 0 || entrySize == 0) return;
            var desc = new TextRefTableDescriptor
            {
                Kind = "Var", PointerField = pointerField, EntrySize = entrySize,
                MaxCount = maxCount, TextIdOffsets = offsets,
            };
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, new[] { desc }))
                AddTextId(ids, id);
        }

        static void CollectFixedTerminated(ROM rom, HashSet<uint> ids, uint pointerField,
            uint entrySize, uint maxCount, uint[] offsets, System.Func<ROM, uint, uint, bool> term)
        {
            if (pointerField == 0 || entrySize == 0) return;
            var desc = new TextRefTableDescriptor
            {
                Kind = "Var", PointerField = pointerField, EntrySize = entrySize,
                MaxCount = maxCount, TextIdOffsets = offsets, Terminator = term,
            };
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, new[] { desc }))
                AddSongIdOrText(ids, id);
        }

        // CollectFixedTerminated is used for both song (worldmap/sound-boss) and
        // text (map-terrain) tables; the caller passes the right destination set so
        // the same range guard applies either way.
        static void AddSongIdOrText(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        // Pull specific registry descriptors (by Kind) and collect their text ids —
        // reuses TextRefTableRegistry's per-version terminators verbatim.
        static void CollectFromRegistry(ROM rom, HashSet<uint> ids, params string[] kinds)
        {
            var all = TextRefTableRegistry.BuildForRom(rom);
            var wanted = new HashSet<string>(kinds);
            var selected = new List<TextRefTableDescriptor>();
            foreach (var d in all)
            {
                if (wanted.Contains(d.Kind)) selected.Add(d);
            }
            if (selected.Count == 0) return;
            foreach (uint id in TextReferenceFinder.CollectReferencedTextIds(rom, selected))
                AddTextId(ids, id);
        }

        // Route the ExportFilterCore faithful collectors (SupportTalk/BattleTalk/
        // Haiku/ED/SoundRoom) — they include the event-pointer-when-0 FE8 logic and
        // the FE7 haiku tutorial tables that the registry fixed descriptors omit.
        static void ExportFilterCollect(ROM rom, HashSet<uint> ids,
            int support, int battleTalk, int haiku, int ed, int soundRoom)
        {
            foreach (int cat in new[] { support, battleTalk, haiku, ed, soundRoom })
            {
                var set = ExportFilterCore.BuildFilteredTextIds(rom, cat);
                if (set == null) continue;
                foreach (uint id in set) AddTextId(ids, id);
            }
        }

        // ---- MenuDefinition (6 roots) -------------------------------------
        // Port of MenuDefinitionForm.MakeVarsIDArray (6 pointer roots) +
        // MenuCommandForm.MakeVarsIDArray ({4,6}). Each root: ReInitPointer(root),
        // entry size 36, count predicate isPointer(u32(p+8)); per entry the command
        // sub-table at (p+8) is walked (size 36, predicate isPointer(u32(c+0xc)),
        // text ids {4,6}). Mirrors EventScriptReferenceScanner.ExpandMenuExtends but
        // walks the 6 fixed menu roots directly (not via event POINTER_MENUEXTENDS).
        static void CollectMenuDefinitions(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            uint[] roots =
            {
                info.menu_definiton_pointer,
                info.menu_promotion_pointer,
                info.menu_promotion_branch_pointer,
                info.menu_definiton_split_pointer,
                info.menu_definiton_worldmap_pointer,
                info.menu_definiton_worldmap_shop_pointer,
            };
            foreach (uint root in roots)
            {
                if (root == 0 || !U.isSafetyOffset(root + 3, rom)) continue;
                uint baseAddr = rom.p32(root);
                if (!U.isSafetyOffset(baseAddr, rom)) continue;
                const uint MENU_SIZE = 36;
                uint p = baseAddr;
                for (int i = 0; i < 0x100; i++, p += MENU_SIZE)
                {
                    if (!U.isSafetyOffset(p + 8 + 3, rom)) break;
                    if (!U.isPointer(rom.u32(p + 8))) break;
                    // MenuCommandForm.MakeVarsIDArray(list, 8 + p): ReInitPointer(8+p)
                    // -> base = p32(8+p); entry size 36, predicate isPointer(u32(c+0xc)),
                    // text ids {4,6}.
                    CollectMenuCommand(rom, 8 + p, ids);
                }
            }
        }

        static void CollectMenuCommand(ROM rom, uint pointer, HashSet<uint> ids)
        {
            if (!U.isSafetyOffset(pointer + 3, rom)) return;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            const uint MENU_SIZE = 36;
            uint p = baseAddr;
            for (int i = 0; i < 0x100; i++, p += MENU_SIZE)
            {
                if (!U.isSafetyOffset(p + 0xc + 3, rom)) break;
                if (!U.isPointer(rom.u32(p + 0xc))) break;
                if (U.isSafetyOffset(p + 4 + 1, rom)) AddTextId(ids, rom.u16(p + 4));
                if (U.isSafetyOffset(p + 6 + 1, rom)) AddTextId(ids, rom.u16(p + 6));
            }
        }

        // ---- StatusRMenu (recursive graph, 6 roots) -----------------------
        // Port of StatusRMenuForm.MakeVarsIDArray + MakeVarsIDArraySub. Each of 6
        // roots: p = p32(root); recurse from p following pointers at {p+0,p+4,p+8,
        // p+12}; text id at p+18; foundDic guards cycles (one shared dict across all
        // roots, exactly as WF).
        static void CollectStatusRMenu(ROM rom, HashSet<uint> ids)
        {
            var info = rom.RomInfo;
            uint[] roots =
            {
                info.status_rmenu_unit_pointer,
                info.status_rmenu_game_pointer,
                info.status_rmenu3_pointer,
                info.status_rmenu4_pointer,
                info.status_rmenu5_pointer,
                info.status_rmenu6_pointer,
            };
            var found = new HashSet<uint>();
            foreach (uint root in roots)
            {
                if (root == 0) continue;
                if (!U.isSafetyOffset(root + 3, rom)) continue;
                uint p = rom.p32(root);
                RMenuSub(rom, p, found, ids);
            }
        }

        static void RMenuSub(ROM rom, uint p, HashSet<uint> found, HashSet<uint> ids)
        {
            if (!U.isSafetyOffset(p + 12, rom)) return;
            if (!found.Contains(p))
            {
                if (U.isSafetyOffset(p + 18 + 1, rom)) AddTextId(ids, rom.u16(p + 18));
            }
            found.Add(p);

            for (uint off = 0; off <= 12; off += 4)
            {
                if (!U.isSafetyOffset(p + off + 3, rom)) continue;
                uint pp = rom.p32(p + off);
                if (U.isSafetyOffset(pp, rom) && !found.Contains(pp))
                {
                    RMenuSub(rom, pp, found, ids);
                }
            }
        }

        // ---- SoundBossBGM (SONG) ------------------------------------------
        // Port of SoundBossBGMForm.MakeVarsIDArray: AppendSongID {4}; Init: size 8,
        // pointer field sound_boss_bgm_pointer, stop on u16==0xFFFF or
        // (i>10 && IsEmpty(addr, 8*10)). AppendSongID reads u16 (default isByte=false).
        static void CollectSoundBossBGM(ROM rom, HashSet<uint> song)
        {
            var info = rom.RomInfo;
            uint pf = info.sound_boss_bgm_pointer;
            if (pf == 0 || !U.isSafetyOffset(pf + 3, rom)) return;
            uint baseAddr = rom.p32(pf);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 8)
            {
                if (!U.isSafetyOffset(p + 5, rom)) break;
                if (rom.u16(p) == 0xFFFF) break;
                if (i > 10 && p + 80 <= (uint)rom.Data.Length && rom.IsEmpty(p, 80)) break;
                AddSongId(song, rom.u16(p + 4));
            }
        }

        // ---- WorldMapEventPointer FE8 -------------------------------------
        static void CollectWorldMapEventFE8(ROM rom, ROMFEINFO info, HashSet<uint> ids)
        {
            CollectWorldMapEventArray(rom, info.worldmap_event_on_stageclear_pointer, ids);
            CollectWorldMapEventArray(rom, info.worldmap_event_on_stageselect_pointer, ids);
            CollectEventPointerField(rom, info.oping_event_pointer, ids);
            CollectEventPointerField(rom, info.ending1_event_pointer, ids);
            CollectEventPointerField(rom, info.ending2_event_pointer, ids);
        }

        // ---- WorldMapEventPointer FE7 -------------------------------------
        static void CollectWorldMapEventFE7(ROM rom, ROMFEINFO info, HashSet<uint> ids)
        {
            CollectWorldMapEventArray(rom, info.worldmap_event_on_stageselect_pointer, ids);
            CollectEventPointerField(rom, info.ending1_event_pointer, ids);
            CollectEventPointerField(rom, info.ending2_event_pointer, ids);
        }

        // ---- WorldMapEventPointer FE6 (PLIST per map) ---------------------
        static void CollectWorldMapEventFE6(ROM rom, ROMFEINFO info, HashSet<uint> ids)
        {
            if (!EventScanReady(rom)) return;
            var tracelist = new List<uint>();
            var maps = MapSettingCore.MakeMapIDList(rom);
            byte plistPos = (byte)info.map_setting_worldmap_plist_pos;
            foreach (var map in maps)
            {
                uint mapAddr = map.addr;
                if (!U.isSafetyOffset(mapAddr + plistPos + 1, rom)) continue;
                uint wmapid = rom.u8(mapAddr + plistPos);
                if (wmapid == 0) continue; // not present

                uint outPointer;
                uint eventAddr = MapChangeCore.PlistToOffsetAddr(rom,
                    MapChangeCore.PlistType.WORLDMAP_FE6ONLY, wmapid, out outPointer);
                if (eventAddr == U.NOT_FOUND || !U.isSafetyOffset(outPointer, rom)) continue;
                // WF passes the POINTER `p` (out param) to MakeVarsIDArrayByEventPointer,
                // which derefs p32(p) then scans. PlistToOffsetAddr returns the already
                // resolved event offset AND the pointer slot; deref the slot to match WF.
                if (outPointer + 4 > (uint)rom.Data.Length) continue;
                uint scanAddr = rom.p32(outPointer);
                if (!U.isSafetyOffset(scanAddr, rom)) continue;
                EventScriptReferenceScanner.ScanScriptForTextIds(
                    rom, CoreState.EventScript, scanAddr, tracelist, ids);
            }
        }

        // A size-4 pointer ARRAY where each entry is itself an event POINTER. The WF
        // InputFormRef count predicate forces slot 0 valid (i==0 -> true) then stops
        // at the first non-pointer slot. Each slot p -> MakeVarsIDArrayByEventPointer
        // (deref p32(p), then scan).
        static void CollectWorldMapEventArray(ROM rom, uint arrayPointer, HashSet<uint> ids)
        {
            if (!EventScanReady(rom)) return;
            if (arrayPointer == 0 || !U.isSafetyOffset(arrayPointer + 3, rom)) return;
            uint baseAddr = rom.p32(arrayPointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            var tracelist = new List<uint>();
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 4)
            {
                if (p + 4 > (uint)rom.Data.Length) break;
                if (i != 0 && !U.isPointer(rom.u32(p))) break;
                CollectEventPointerSlot(rom, p, tracelist, ids);
            }
        }

        // Deref a pointer FIELD then scan the event script at the dereferenced addr.
        static void CollectEventPointerField(ROM rom, uint pointerField, HashSet<uint> ids)
        {
            if (!EventScanReady(rom)) return;
            if (pointerField == 0 || !U.isSafetyOffset(pointerField + 3, rom)) return;
            var tracelist = new List<uint>();
            CollectEventPointerSlot(rom, pointerField, tracelist, ids);
        }

        static void CollectEventPointerSlot(ROM rom, uint pointerSlot, List<uint> tracelist, HashSet<uint> ids)
        {
            if (pointerSlot + 4 > (uint)rom.Data.Length) return;
            uint eventAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(eventAddr, rom)) return;
            EventScriptReferenceScanner.ScanScriptForTextIds(
                rom, CoreState.EventScript, eventAddr, tracelist, ids);
        }

        static bool EventScanReady(ROM rom)
        {
            return CoreState.EventScript != null
                && CoreState.ROM != null
                && ReferenceEquals(CoreState.ROM, rom)
                && CoreState.CommentCache != null;
        }

        // ---- FE8N Ver3 skill table (free-area only) -----------------------
        // Port of SkillConfigFE8NVer3SkillForm.MakeVarsIDArray. Only when the
        // installed skill system is FE8N_ver3. base = p32(0x892A8+4) (skl_table);
        // block size = u32(0x892A8+8) (icon list size, 1..100); count stop on
        // u8(addr)==0xFF; text id at offset 0.
        static void CollectFE8NVer3Skill(ROM rom, HashSet<uint> ids)
        {
            if (SkillSystemTextScanner.SearchSkillSystem(rom)
                != SkillSystemTextScanner.SkillSystemEnum.FE8N_ver3) return;

            // FindSkillFE8NVer3IconPointersLow prerequisites.
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return;
            if (!U.isSafetyOffset(0x892A8 + 4 + 3, rom)) return;
            uint sklTable = rom.u32(0x892A8 + 4);
            if (!U.isSafetyPointer(sklTable, rom)) return;
            if (!U.isSafetyOffset(0x892A8 + 8 + 3, rom)) return;
            uint iconListSize = rom.u32(0x892A8 + 8);
            if (iconListSize <= 0 || iconListSize > 100) return;

            // g_SkillBaseAddress = 0x892A8 + 4 is a pointer FIELD -> base = sklTable.
            uint baseAddr = U.toOffset(sklTable);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += iconListSize)
            {
                if (!U.isSafetyOffset(p + 1, rom)) break;
                if (rom.u8(p) == 0xFF) break; // terminator
                if (p + 2 > (uint)rom.Data.Length) break;
                AddTextId(ids, rom.u16(p)); // offset 0
            }
        }
    }
}
