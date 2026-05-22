using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Central registry that builds the list of <see cref="TextRefTableDescriptor"/>
    /// for the loaded ROM. Mirrors the WinForms <c>U.MakeVarsIDArray</c> dispatch:
    /// it branches on <see cref="ROMFEINFO.version"/> (6/7/8) and
    /// <see cref="ROMFEINFO.is_multibyte"/>, returning the per-version table set
    /// that the Avalonia Text Editor's "References" panel should scan.
    ///
    /// Each descriptor copies the WinForms <c>UseValsID.AppendTextID(...)</c> offset
    /// list and the underlying <c>InputFormRef</c> entry-size 1:1 so behaviour
    /// matches. Where ROMFEINFO exposes a data-size field
    /// (<c>map_setting_datasize</c>, <c>sound_room_datasize</c>,
    /// <c>unit_datasize</c>, etc.) the registry reads that field instead of
    /// hard-coding.
    ///
    /// Each descriptor also supplies a terminator predicate that mirrors the
    /// per-form WinForms <c>InputFormRef</c> stop callback (e.g. "stop at
    /// u16 == 0xFFFF", "stop at u8 == 0", "stop at empty run"). Without these,
    /// scanning with a large <c>MaxCount</c> upper bound can produce false
    /// positives after the real table terminator on relocated/expanded ROMs.
    ///
    /// Tables NOT in this registry (out of scope for issue #349):
    ///   - EventCond, MenuDefinition, StatusRMenu — require recursive scans.
    ///   - PatchForm, SkillConfig* — patch-specific.
    ///   - SoundBossBGM, WorldMapBGM, EventForceSortie, ClassAlphaName,
    ///     SummonUnits, MonsterProbability, OPClassFont, OPPrologue,
    ///     SupportAttribute, StatusParam — no text IDs in the entries.
    ///   - WorldMapEventPointer (all versions), event_haiku_tutorial_{1,2} (FE7) —
    ///     event/recursive scans, deferred.
    ///
    /// Tables INCLUDED beyond the original issue #349 scope:
    ///   - TextDic (FE8) — three sub-tables (dic_main, dic_chaptor, dic_title)
    ///     added at Copilot CLI review's suggestion (fixed-table descriptors fit
    ///     this case cleanly).
    /// </summary>
    public static class TextRefTableRegistry
    {
        /// <summary>
        /// Reasonable upper bound for tables where ROMFEINFO doesn't declare an
        /// explicit max count. Same convention used by other Avalonia editors
        /// (e.g. ItemEditorViewModel.LoadItemList = 0x100). The descriptor
        /// terminator predicates do most of the heavy lifting; this is the
        /// hard cap.
        /// </summary>
        const uint DefaultMaxCount = 0x100u;

        /// <summary>
        /// Larger upper bound for tables that can legitimately have many entries
        /// (sound room, support talks, haiku, battle talks, ED screens) but
        /// terminate on a sentinel.
        /// </summary>
        const uint LargeMaxCount = 0x400u;

        /// <summary>
        /// Maximum number of map settings to scan. WinForms uses
        /// <c>MapSettingForm.GetDataCount()</c> which is bounded by the actual
        /// number of valid entries; here we use an upper bound (FE8 default is
        /// 0x29 chapters; allow 0x80 to cover expanded ROMs).
        /// </summary>
        const uint MapSettingMaxCount = 0x80u;

        // -------------------------------------------------------------
        // Terminator predicates — mirror the WinForms InputFormRef stop
        // callbacks for each form. Each predicate is (rom, entryAddr,
        // entryIndex) and returns true when the scanner should STOP at
        // this entry (i.e. this entry is the sentinel / past real data).
        //
        // WinForms forms commonly combine TWO stop conditions:
        //   (a) sentinel byte/word/dword match (e.g. u16 == 0xFFFF), AND
        //   (b) "i > 10 && ROM.IsEmpty(addr, blockSize * 10)" — i.e. once
        //       we're past the first 10 entries AND the next 10 blocks
        //       are empty (all 0x00 or all 0xFF), treat as end-of-table.
        // The helpers here implement both. Empty-run guards are
        // parameterized by per-descriptor blockSize.
        // -------------------------------------------------------------

        /// <summary>Stop when u16 at entry+0 equals 0xFFFF.</summary>
        static bool StopOnU16FFFF(ROM rom, uint entry, uint i)
        {
            if (entry + 2 > (uint)rom.Data.Length) return true;
            return rom.u16(entry) == 0xFFFF;
        }

        /// <summary>Stop when u16 at entry+0 equals 0x0000.</summary>
        static bool StopOnU16Zero(ROM rom, uint entry, uint i)
        {
            if (entry + 2 > (uint)rom.Data.Length) return true;
            return rom.u16(entry) == 0x0000;
        }

        /// <summary>Stop when u8 at entry+0 equals 0x00.</summary>
        static bool StopOnU8Zero(ROM rom, uint entry, uint i)
        {
            if (entry + 1 > (uint)rom.Data.Length) return true;
            return rom.u8(entry) == 0;
        }

        /// <summary>Stop when u32 at entry+0 equals 0xFFFFFFFF. Used by sound room (mirrors <c>SoundRoomForm.Init</c>).</summary>
        static bool StopOnU32FFFFFFFF(ROM rom, uint entry, uint i)
        {
            if (entry + 4 > (uint)rom.Data.Length) return true;
            return rom.u32(entry) == 0xFFFFFFFFu;
        }

        /// <summary>Stop when u32 at entry+0 equals zero. Used by ED screens.</summary>
        static bool StopOnU32Zero(ROM rom, uint entry, uint i)
        {
            if (entry + 4 > (uint)rom.Data.Length) return true;
            return rom.u32(entry) == 0;
        }

        /// <summary>Stop when u32 at entry+40 is not a valid pointer (FE7/8 status game option list terminator).</summary>
        static bool StopOnStatusOptionEnd(ROM rom, uint entry, uint i)
        {
            if (entry + 44 > (uint)rom.Data.Length) return true;
            return !U.isPointer(rom.u32(entry + 40));
        }

        /// <summary>
        /// Stop when u16 at entry+0 is 0xFFFF or 0x0000. Used by FE6/FE7 battle talk
        /// (main and 2-tables) where <c>EventBattleTalkFE{6,7}Form.Init</c> stops on
        /// either sentinel.
        /// </summary>
        static bool StopOnU16FFFFOrZero(ROM rom, uint entry, uint i)
        {
            if (entry + 2 > (uint)rom.Data.Length) return true;
            ushort v = (ushort)rom.u16(entry);
            return v == 0xFFFF || v == 0x0000;
        }

        /// <summary>
        /// Stop when u8 at entry+0 is 0x00 or 0xFF. Used by FE7 battle talk N1
        /// table (<c>EventBattleTalkFE7Form.N1_Init</c>: <c>unit == 0 || unit == 0xFF</c>).
        /// </summary>
        static bool StopOnU8ZeroOrFF(ROM rom, uint entry, uint i)
        {
            if (entry + 1 > (uint)rom.Data.Length) return true;
            byte v = (byte)rom.u8(entry);
            return v == 0 || v == 0xFF;
        }

        /// <summary>
        /// Stop when u32 at entry+0 is &gt;= 0xFF. Used by status-units-menu
        /// (<c>StatusUnitsMenuForm.Init</c>: <c>order &lt; 0xFF</c>; we
        /// stop on the negation: <c>order &gt;= 0xFF</c>).
        /// </summary>
        static bool StopOnU32AtLeastFF(ROM rom, uint entry, uint i)
        {
            if (entry + 4 > (uint)rom.Data.Length) return true;
            return rom.u32(entry) >= 0xFFu;
        }

        /// <summary>
        /// Stop when u32 at entry+0 is &lt; 1 OR &gt; 0xFF. Used by FE7
        /// EventFinalSerif (<c>EventFinalSerifFE7Form.Init</c>:
        /// <c>unit_id &lt;= 0xff &amp;&amp; unit_id &gt;= 0x1</c>; we stop on the
        /// negation).
        /// </summary>
        static bool StopOnU32OutsideUnitRange(ROM rom, uint entry, uint i)
        {
            if (entry + 4 > (uint)rom.Data.Length) return true;
            uint v = rom.u32(entry);
            return v < 1 || v > 0xFFu;
        }

        /// <summary>
        /// WinForms-pattern terminator: stops when (a) the per-entry sentinel
        /// predicate hits, OR (b) we're past index 10 AND the next 10 blocks
        /// (10 × blockSize bytes from entry) are all zero (an "empty run").
        ///
        /// Many WinForms <c>*Form.Init</c> callbacks follow this exact pattern:
        /// <code>
        ///   if (sentinelHit) return false;
        ///   if (i &gt; 10 &amp;&amp; ROM.IsEmpty(addr, blockSize * 10)) return false;
        ///   return true;
        /// </code>
        /// We package both halves into one terminator so the registry can use
        /// it identically. <paramref name="blockSize"/> is the descriptor's
        /// EntrySize (so IsEmpty walks the next 10 entries).
        /// </summary>
        static Func<ROM, uint, uint, bool> WithEmptyRunStop(
            Func<ROM, uint, uint, bool> sentinel,
            uint blockSize)
        {
            return (rom, entry, i) =>
            {
                if (sentinel(rom, entry, i)) return true;
                if (i > 10)
                {
                    // ROM.IsEmpty is range-safe — returns true if all bytes in
                    // [addr .. addr+count) are uniformly 0x00 OR uniformly
                    // 0xFF (it does two passes — both terminator patterns are
                    // common in GBA ROMs). We check the next 10 blocks.
                    uint runBytes = blockSize * 10;
                    if (entry + runBytes <= (uint)rom.Data.Length && rom.IsEmpty(entry, runBytes))
                        return true;
                }
                return false;
            };
        }

        /// <summary>
        /// Build the full descriptor list for the given ROM. Returns an empty
        /// list if the ROM or RomInfo is null. The list is freshly constructed
        /// each call — descriptors are cheap value objects.
        /// </summary>
        public static List<TextRefTableDescriptor> BuildForRom(ROM rom)
        {
            var list = new List<TextRefTableDescriptor>();
            if (rom?.RomInfo == null) return list;

            var info = rom.RomInfo;

            // ===========================================================
            // Universal (FE6/7/8): unit, class, item — the descriptors
            // previously inlined in TextViewerViewModel. Moved here so the
            // registry is the single source of truth.
            // ===========================================================
            uint unitMax = info.unit_maxcount != 0 ? info.unit_maxcount : DefaultMaxCount;
            list.Add(new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = info.unit_pointer,
                EntrySize = info.unit_datasize,
                MaxCount = unitMax,
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = id => NameResolver.GetUnitName(id),
            });
            list.Add(new TextRefTableDescriptor
            {
                Kind = "Class",
                PointerField = info.class_pointer,
                EntrySize = info.class_datasize,
                MaxCount = DefaultMaxCount,
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = id => NameResolver.GetClassName(id),
            });
            list.Add(new TextRefTableDescriptor
            {
                Kind = "Item",
                PointerField = info.item_pointer,
                EntrySize = info.item_datasize,
                MaxCount = DefaultMaxCount,
                TextIdOffsets = new uint[] { 0, 2, 4 },
                NameResolver = id => NameResolver.GetItemName(id),
            });

            // ===========================================================
            // Version-independent table — MapTerrainNameEng (English/US/EU
            // ROMs). WinForms registers this only when `!is_multibyte`.
            // MapTerrainNameEngForm.Init terminates on u16 == 0 (the text-id
            // sentinel) — without this, the scan walks the full MaxCount
            // (0x100) and can produce false positives past the real table
            // end on relocated/expanded ROMs.
            // ===========================================================
            if (!info.is_multibyte && info.map_terrain_name_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "MapTerrain",
                    PointerField = info.map_terrain_name_pointer,
                    EntrySize = 2,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 0 },
                    Terminator = StopOnU16Zero,
                    NameResolver = id => $"Terrain {id:X02}",
                });
            }

            // ===========================================================
            // Version-specific tables.
            // ===========================================================
            int version = info.version;
            if (version == 8)
            {
                AddFE8(list, info);
            }
            else if (version == 7)
            {
                AddFE7(list, info);
            }
            else if (version == 6)
            {
                AddFE6(list, info);
            }

            return list;
        }

        // -------------------------------------------------------------
        // FE8 — Sacred Stones (both JP and U)
        // -------------------------------------------------------------
        static void AddFE8(List<TextRefTableDescriptor> list, ROMFEINFO info)
        {
            // MapSetting — chapter name + clear-condition + intro/outro.
            // WinForms MapSettingForm.MakeVarsIDArray uses offsets
            // { 112, 114, 136, 138 } (4 offsets — see MapSettingForm.cs:864).
            // FE7's larger lists (8 offsets for FE7J, 10 for FE7U) belong to
            // the FE7-specific forms, not FE8.
            if (info.map_setting_pointer != 0 && info.map_setting_datasize != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "MapSetting",
                    PointerField = info.map_setting_pointer,
                    EntrySize = info.map_setting_datasize,
                    MaxCount = MapSettingMaxCount,
                    TextIdOffsets = new uint[] { 112, 114, 136, 138 },
                    NameResolver = id => $"Map {id:X02}",
                });
            }

            // SupportTalk (FE8) — entry size 16, offsets {4, 6, 8}.
            // WinForms SupportTalkForm.Init: stop on u16==0xFFFF OR
            // (i > 10 && IsEmpty(addr, 16*10)).
            if (info.support_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SupportTalk",
                    PointerField = info.support_talk_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 6, 8 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFF, 16),
                    NameResolver = id => $"Support {id:X02}",
                });
            }

            // EventHaiku (FE8) — size 12; offset {6} only (textid path).
            // WinForms EventHaikuForm.Init: stop on u16==0xFFFF OR
            // (i > 10 && IsEmpty(addr, 12*10)).
            if (info.event_haiku_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "Haiku",
                    PointerField = info.event_haiku_pointer,
                    EntrySize = 12,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 6 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFF, 12),
                    NameResolver = id => $"Haiku {id:X02}",
                });
            }

            // EventBattleTalk (FE8) — size 16, offset {8} (textid path).
            // WinForms EventBattleTalkForm.Init: stop on u16==0xFFFF OR
            // (i > 10 && IsEmpty(addr, 16*10)).
            if (info.event_ballte_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "BattleTalk",
                    PointerField = info.event_ballte_talk_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 8 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFF, 16),
                    NameResolver = id => $"BattleTalk {id:X02}",
                });
            }

            // SoundRoom (FE8) — size from ROMFEINFO; offset 12.
            // WinForms SoundRoomForm.Init: stop on u32==0xFFFFFFFF OR
            // (i > 10 && IsEmpty(addr, datasize*10)).
            if (info.sound_room_pointer != 0 && info.sound_room_datasize != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SoundRoom",
                    PointerField = info.sound_room_pointer,
                    EntrySize = info.sound_room_datasize,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 12 },
                    Terminator = WithEmptyRunStop(StopOnU32FFFFFFFF, info.sound_room_datasize),
                    NameResolver = id => $"Track {id:X02}",
                });
            }

            // WorldMapPoint — FE8 only; size 32; offset 28.
            // Terminator: u32 == 0 (WorldMapPointForm.Init checks
            // `Program.ROM.u32(addr) != 0`).
            if (info.worldmap_point_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "WorldMapPoint",
                    PointerField = info.worldmap_point_pointer,
                    EntrySize = 32,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 28 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"WMPoint {id:X02}",
                });
            }

            // ED screens — FE8 has ed_2 (epithet), ed_3a (epilogue A), ed_3b (epilogue B).
            // EDForm sizes: ed_1=4 (no text), ed_2=8, ed_3a/b=8.
            // Terminator: u32 == 0 (EDForm.N1_Init / N2_Init).
            if (info.ed_2_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Epithet",
                    PointerField = info.ed_2_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"Epithet {id:X02}",
                });
            }
            if (info.ed_3a_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Epilogue_A",
                    PointerField = info.ed_3a_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"EpilogueA {id:X02}",
                });
            }
            if (info.ed_3b_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Epilogue_B",
                    PointerField = info.ed_3b_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"EpilogueB {id:X02}",
                });
            }

            // OP class demo (FE8) — multibyte (FE8J) uses size 28 offset 4
            // (OPClassDemoForm.Init); non-multibyte (FE8U) uses size 20 offset 0
            // (OPClassDemoFE8UForm.Init).
            if (info.op_class_demo_pointer != 0)
            {
                if (info.is_multibyte)
                {
                    list.Add(new TextRefTableDescriptor
                    {
                        Kind = "OPClassDemo",
                        PointerField = info.op_class_demo_pointer,
                        EntrySize = 28,
                        MaxCount = DefaultMaxCount,
                        TextIdOffsets = new uint[] { 4 },
                        NameResolver = id => $"OPDemo {id:X02}",
                    });
                }
                else
                {
                    list.Add(new TextRefTableDescriptor
                    {
                        Kind = "OPClassDemo",
                        PointerField = info.op_class_demo_pointer,
                        EntrySize = 20,
                        MaxCount = DefaultMaxCount,
                        TextIdOffsets = new uint[] { 0 },
                        NameResolver = id => $"OPDemo {id:X02}",
                    });
                }
            }

            // StatusOption — game options menu (FE8 has this).
            // WinForms offsets: 0,4,6,12,14,20,22,28,30; entry size 44.
            // Terminator: StatusOptionForm.Init checks U.isPointer(u32 at +40).
            if (info.status_game_option_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "StatusOption",
                    PointerField = info.status_game_option_pointer,
                    EntrySize = 44,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 0, 4, 6, 12, 14, 20, 22, 28, 30 },
                    Terminator = StopOnStatusOptionEnd,
                    NameResolver = id => $"Option {id:X02}",
                });
            }

            // StatusUnitsMenu — squad/unit-list menu (FE8 has this).
            // WinForms offsets: 4,12; entry size 16.
            if (info.status_units_menu_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "UnitsMenu",
                    PointerField = info.status_units_menu_pointer,
                    EntrySize = 16,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 4, 12 },
                    // StatusUnitsMenuForm.Init stops on order >= 0xFF (it
                    // returns false when the "order" u32 at +0 is >= 0xFF).
                    Terminator = StopOnU32AtLeastFF,
                    NameResolver = id => $"UnitsMenu {id:X02}",
                });
            }

            // TextDic — dictionary entries (FE8 only). Three sub-tables:
            //   dic_main_pointer:    size 12, offsets { 2, 4 }, terminator
            //                        u16@+2 == 0 || u16@+4 == 0
            //   dic_chaptor_pointer: size 4,  offsets { 0 }, max 9 entries
            //   dic_title_pointer:   size 2,  offsets { 0 }, max 12 entries
            // (see TextDicForm.cs — Init / N1_Init / N2_Init).
            if (info.dic_main_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "Dic",
                    PointerField = info.dic_main_pointer,
                    EntrySize = 12,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 2, 4 },
                    Terminator = (rom, entry, i) =>
                    {
                        if (entry + 6 > (uint)rom.Data.Length) return true;
                        // TextDicForm.Init stops when either u16@+2 or u16@+4 == 0.
                        return rom.u16(entry + 2) == 0 || rom.u16(entry + 4) == 0;
                    },
                    NameResolver = id => $"Dic {id:X02}",
                });
            }
            if (info.dic_chaptor_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "DicChapter",
                    PointerField = info.dic_chaptor_pointer,
                    EntrySize = 4,
                    MaxCount = 9, // TextDicForm.N1_Init hard cap (i < 9)
                    TextIdOffsets = new uint[] { 0 },
                    NameResolver = id => $"DicChap {id:X02}",
                });
            }
            if (info.dic_title_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "DicTitle",
                    PointerField = info.dic_title_pointer,
                    EntrySize = 2,
                    MaxCount = 12, // TextDicForm.N2_Init hard cap (i < 12)
                    TextIdOffsets = new uint[] { 0 },
                    NameResolver = id => $"DicTitle {id:X02}",
                });
            }
        }

        // -------------------------------------------------------------
        // FE7 — Blazing Blade (both JP and U)
        // -------------------------------------------------------------
        static void AddFE7(List<TextRefTableDescriptor> list, ROMFEINFO info)
        {
            // MapSetting — offsets differ by JP (multibyte) vs U.
            // FE7J (MapSettingFE7Form, line 203): { 112,114,118,120,122,124,136,138 }
            //   — 8 offsets (size 148), is_multibyte=true.
            // FE7U (MapSettingFE7UForm, line 202): { 112,114,116,118,122,124,126,128,140,142 }
            //   — 10 offsets (size 152), is_multibyte=false.
            if (info.map_setting_pointer != 0 && info.map_setting_datasize != 0)
            {
                uint[] offsets = info.is_multibyte
                    ? new uint[] { 112, 114, 118, 120, 122, 124, 136, 138 }
                    : new uint[] { 112, 114, 116, 118, 122, 124, 126, 128, 140, 142 };
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "MapSetting",
                    PointerField = info.map_setting_pointer,
                    EntrySize = info.map_setting_datasize,
                    MaxCount = MapSettingMaxCount,
                    TextIdOffsets = offsets,
                    NameResolver = id => $"Map {id:X02}",
                });
            }

            // SupportTalk (FE7) — size 20, offsets 4,8,12 (SupportTalkFE7Form).
            // WinForms SupportTalkFE7Form.Init: stop on u16==0 OR
            // (i > 10 && IsEmpty(addr, 20*10)).
            if (info.support_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SupportTalk",
                    PointerField = info.support_talk_pointer,
                    EntrySize = 20,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 8, 12 },
                    Terminator = WithEmptyRunStop(StopOnU16Zero, 20),
                    NameResolver = id => $"Support {id:X02}",
                });
            }

            // EventHaiku (FE7) — size 16, offset {4} only (textid path).
            // EventHaikuFE7Form.Init: size 16, stop on u8==0 OR (i > 10 && IsEmpty(addr, 16*10)).
            // event_haiku_tutorial_{1,2} are event-recursion only — deferred.
            if (info.event_haiku_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "Haiku",
                    PointerField = info.event_haiku_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = WithEmptyRunStop(StopOnU8Zero, 16),
                    NameResolver = id => $"Haiku {id:X02}",
                });
            }

            // EventBattleTalk (FE7 main) — size 16, offset {4}
            // EventBattleTalkFE7Form.Init: stop on u16==0||0xFFFF OR
            // (i > 10 && IsEmpty(addr, 16*10)).
            if (info.event_ballte_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "BattleTalk",
                    PointerField = info.event_ballte_talk_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFFOrZero, 16),
                    NameResolver = id => $"BattleTalk {id:X02}",
                });
            }

            // EventBattleTalk2 (FE7) — N1 table: size 12, offset {4}
            // EventBattleTalkFE7Form.N1_Init: stop on u8==0||0xFF OR
            // (i > 10 && IsEmpty(addr, 12*10)).
            if (info.event_ballte_talk2_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "BattleTalk2",
                    PointerField = info.event_ballte_talk2_pointer,
                    EntrySize = 12,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = WithEmptyRunStop(StopOnU8ZeroOrFF, 12),
                    NameResolver = id => $"BattleTalk2 {id:X02}",
                });
            }

            // SoundRoom (FE7) — offset 12 (same as FE8).
            // SoundRoomForm.Init: stop on u32==0xFFFFFFFF OR (i > 10 && IsEmpty(addr, size*10)).
            if (info.sound_room_pointer != 0 && info.sound_room_datasize != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SoundRoom",
                    PointerField = info.sound_room_pointer,
                    EntrySize = info.sound_room_datasize,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 12 },
                    Terminator = WithEmptyRunStop(StopOnU32FFFFFFFF, info.sound_room_datasize),
                    NameResolver = id => $"Track {id:X02}",
                });
            }

            // ED screens (FE7):
            //   ed_2:   epithet,        size 8, offset {4}
            //   ed_3a:  Eliwood ending, size 8, offset {4}
            //   ed_3b:  Hector ending,  size 8, offset {4}
            //   ed_3c:  Lyn ending,     size 12, offsets {4,8} — DIRECT BASE
            //           (EDFE7Form.N3_Init uses ifr.ReInit(ed_3c_pointer)
            //           because the field stores the actual table address,
            //           not a pointer to a pointer).
            if (info.ed_2_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Epithet",
                    PointerField = info.ed_2_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"Epithet {id:X02}",
                });
            }
            if (info.ed_3a_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Eliwood",
                    PointerField = info.ed_3a_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"EliwoodED {id:X02}",
                });
            }
            if (info.ed_3b_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Hector",
                    PointerField = info.ed_3b_pointer,
                    EntrySize = 8,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"HectorED {id:X02}",
                });
            }
            if (info.ed_3c_pointer != 0)
            {
                // DirectBase, not PointerField — see ROMFE7*.cs comment
                // "ポインタ指定できない" (cannot be specified as pointer)
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED_Lyn",
                    DirectBase = info.ed_3c_pointer,
                    EntrySize = 12,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 8 },
                    Terminator = StopOnU32Zero,
                    NameResolver = id => $"LynED {id:X02}",
                });
            }

            // OP class demo (FE7) — multibyte (FE7J): size 32, non-multibyte
            // (FE7U): size 28; both use offset {4}.
            // OPClassDemoFE7Form (FE7J) = 32; OPClassDemoFE7UForm (FE7U) = 28.
            if (info.op_class_demo_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "OPClassDemo",
                    PointerField = info.op_class_demo_pointer,
                    EntrySize = info.is_multibyte ? 32u : 28u,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    NameResolver = id => $"OPDemo {id:X02}",
                });
            }

            // StatusOption — FE7 also has game options
            if (info.status_game_option_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "StatusOption",
                    PointerField = info.status_game_option_pointer,
                    EntrySize = 44,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 0, 4, 6, 12, 14, 20, 22, 28, 30 },
                    Terminator = StopOnStatusOptionEnd,
                    NameResolver = id => $"Option {id:X02}",
                });
            }

            // StatusUnitsMenu — FE7 has this too
            if (info.status_units_menu_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "UnitsMenu",
                    PointerField = info.status_units_menu_pointer,
                    EntrySize = 16,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 4, 12 },
                    // StatusUnitsMenuForm.Init stops on order >= 0xFF (it
                    // returns false when the "order" u32 at +0 is >= 0xFF).
                    Terminator = StopOnU32AtLeastFF,
                    NameResolver = id => $"UnitsMenu {id:X02}",
                });
            }

            // EventFinalSerif (FE7 only) — final chapter lines; size 8, offset 4.
            // EventFinalSerifFE7Form.Init stops when unit_id is outside 1..0xFF.
            if (info.event_final_serif_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "FinalSerif",
                    PointerField = info.event_final_serif_pointer,
                    EntrySize = 8,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = StopOnU32OutsideUnitRange,
                    NameResolver = id => $"FinalLine {id:X02}",
                });
            }

            // EDSenseki (FE7 only) — battle-record comments; size 16, offsets {4,8,12}.
            // EDSensekiCommentForm.MakeVarsIDArray uses { 4, 8, 12 } (not just 4,8).
            // EDSensekiCommentForm.Init stops when u16@+0 == 0.
            if (info.senseki_comment_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "Senseki",
                    PointerField = info.senseki_comment_pointer,
                    EntrySize = 16,
                    MaxCount = DefaultMaxCount,
                    TextIdOffsets = new uint[] { 4, 8, 12 },
                    Terminator = StopOnU16Zero,
                    NameResolver = id => $"Senseki {id:X02}",
                });
            }
        }

        // -------------------------------------------------------------
        // FE6 — Binding Blade (JP only)
        // -------------------------------------------------------------
        static void AddFE6(List<TextRefTableDescriptor> list, ROMFEINFO info)
        {
            // MapSetting (FE6) — offsets 48,50,52,60; size from ROMFEINFO (68 or 72)
            // (MapSettingFE6Form.MakeVarsIDArray).
            if (info.map_setting_pointer != 0 && info.map_setting_datasize != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "MapSetting",
                    PointerField = info.map_setting_pointer,
                    EntrySize = info.map_setting_datasize,
                    MaxCount = MapSettingMaxCount,
                    TextIdOffsets = new uint[] { 48, 50, 52, 60 },
                    NameResolver = id => $"Map {id:X02}",
                });
            }

            // SupportTalk (FE6) — size 16, offsets {4,8,12} (SupportTalkFE6Form).
            // SupportTalkFE6Form.Init: stop on u16==0 OR (i > 10 && IsEmpty(addr, 16*10)).
            if (info.support_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SupportTalk",
                    PointerField = info.support_talk_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 8, 12 },
                    Terminator = WithEmptyRunStop(StopOnU16Zero, 16),
                    NameResolver = id => $"Support {id:X02}",
                });
            }

            // EventHaiku (FE6) — size 16, offsets {4, 12} (EventHaikuFE6Form).
            // EventHaikuFE6Form.Init: stop on u8==0 OR (i > 10 && IsEmpty(addr, 16*10)).
            if (info.event_haiku_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "Haiku",
                    PointerField = info.event_haiku_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 12 },
                    Terminator = WithEmptyRunStop(StopOnU8Zero, 16),
                    NameResolver = id => $"Haiku {id:X02}",
                });
            }

            // EventBattleTalk (FE6 main) — size 12, offset {4}.
            // EventBattleTalkFE6Form.Init: stop on u16==0||0xFFFF OR (i > 10 && IsEmpty(addr, 12*10)).
            if (info.event_ballte_talk_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "BattleTalk",
                    PointerField = info.event_ballte_talk_pointer,
                    EntrySize = 12,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFFOrZero, 12),
                    NameResolver = id => $"BattleTalk {id:X02}",
                });
            }

            // EventBattleTalk2 (FE6) — second table: size 16, offset {4}.
            // EventBattleTalkFE6Form.N_Init: stop on u16==0||0xFFFF OR (i > 10 && IsEmpty(addr, 16*10)).
            if (info.event_ballte_talk2_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "BattleTalk2",
                    PointerField = info.event_ballte_talk2_pointer,
                    EntrySize = 16,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4 },
                    Terminator = WithEmptyRunStop(StopOnU16FFFFOrZero, 16),
                    NameResolver = id => $"BattleTalk2 {id:X02}",
                });
            }

            // SoundRoom (FE6) — offsets {4, 8} (different from FE7/8 which use {12}).
            // SoundRoomFE6Form.MakeVarsIDArray uses { 4, 8 }; Init: stop on
            // u32==0xFFFFFFFF OR (i > 10 && IsEmpty(addr, size*10)).
            if (info.sound_room_pointer != 0 && info.sound_room_datasize != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "SoundRoom",
                    PointerField = info.sound_room_pointer,
                    EntrySize = info.sound_room_datasize,
                    MaxCount = LargeMaxCount,
                    TextIdOffsets = new uint[] { 4, 8 },
                    Terminator = WithEmptyRunStop(StopOnU32FFFFFFFF, info.sound_room_datasize),
                    NameResolver = id => $"Track {id:X02}",
                });
            }

            // FE6 ED — only ed_3a is scanned; size 8, offsets {0,2,4,6}.
            // EDFE6Form.MakeVarsIDArray uses N2_Init pointing to ed_3a_pointer.
            if (info.ed_3a_pointer != 0)
            {
                list.Add(new TextRefTableDescriptor
                {
                    Kind = "ED",
                    PointerField = info.ed_3a_pointer,
                    EntrySize = 8,
                    MaxCount = 0x42u, // EDFE6Form.N2_Init hard-codes max at 0x42
                    TextIdOffsets = new uint[] { 0, 2, 4, 6 },
                    NameResolver = id => $"ED {id:X02}",
                });
            }
        }
    }
}
