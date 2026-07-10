using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform struct data export/import for ROM tables.
    /// Mirrors TranslateCore pattern: export to TSV, import from TSV, round-trip validation.
    /// </summary>
    public static class StructExportCore
    {
        /// <summary>
        /// Definition of a ROM data table (pointer, entry size, count strategy).
        /// </summary>
        public class TableDef
        {
            public string Name { get; set; }
            public string StructNameFE6 { get; set; }
            public string StructNameFE78 { get; set; }
            public string MetadataFileFE6 { get; set; }
            public string MetadataFileFE78 { get; set; }
            /// <summary>Get the base address for this table from the ROM.</summary>
            public Func<ROM, uint> GetBaseAddress { get; set; }
            /// <summary>Get the entry data size from the ROM.</summary>
            public Func<ROM, uint> GetDataSize { get; set; }
            /// <summary>Get the entry count for this table.</summary>
            public Func<ROM, uint> GetEntryCount { get; set; }
        }

        static readonly Dictionary<string, TableDef> _tables = new Dictionary<string, TableDef>(StringComparer.OrdinalIgnoreCase);

        static StructExportCore()
        {
            RegisterCoreTables();
            RegisterPortraitTables();
            RegisterSoundTables();
            RegisterSupportTables();
            RegisterEventTables();
            RegisterWorldMapTables();
            RegisterMiscTables();
            RegisterUniversalTables2();
            RegisterEndingTables();
            RegisterOPTables();
            RegisterFE8Tables();
        }

        static void RegisterCoreTables()
        {
            RegisterTable(new TableDef
            {
                Name = "units",
                StructNameFE6 = "Unit_FE6",
                StructNameFE78 = "Unit_FE78",
                MetadataFileFE6 = "struct_unit_fe6.txt",
                MetadataFileFE78 = "struct_unit_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.unit_pointer),
                GetDataSize = rom => rom.RomInfo.unit_datasize,
                GetEntryCount = rom => rom.RomInfo.unit_maxcount,
            });

            RegisterTable(new TableDef
            {
                Name = "classes",
                StructNameFE6 = "Class_FE6",
                StructNameFE78 = "Class_FE78",
                MetadataFileFE6 = "struct_class_fe6.txt",
                MetadataFileFE78 = "struct_class_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.class_pointer),
                GetDataSize = rom => rom.RomInfo.class_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.class_pointer);
                    uint dataSize = rom.RomInfo.class_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0xFF) return false;
                        return rom.u8(addr + 4) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "items",
                StructNameFE6 = "Item_FE6",
                StructNameFE78 = "Item_FE78",
                MetadataFileFE6 = "struct_item_fe6.txt",
                MetadataFileFE78 = "struct_item_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.item_pointer),
                GetDataSize = rom => rom.RomInfo.item_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.item_pointer);
                    uint dataSize = rom.RomInfo.item_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr + 12));
                    });
                },
            });
        }

        static void RegisterPortraitTables()
        {
            RegisterTable(new TableDef
            {
                Name = "portraits",
                StructNameFE6 = "Portrait_FE6",
                StructNameFE78 = "Portrait_FE78",
                MetadataFileFE6 = "struct_portrait_fe6.txt",
                MetadataFileFE78 = "struct_portrait_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.portrait_pointer),
                GetDataSize = rom => rom.RomInfo.portrait_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.portrait_pointer);
                    uint dataSize = rom.RomInfo.portrait_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0x3FF) return false;
                        // All three pointers at +0, +4, +8 must be valid pointers or NULL
                        return U.isPointerOrNULL(rom.u32(addr))
                            && U.isPointerOrNULL(rom.u32(addr + 4))
                            && U.isPointerOrNULL(rom.u32(addr + 8));
                    });
                },
            });
        }

        static void RegisterSoundTables()
        {
            RegisterTable(new TableDef
            {
                Name = "sound_room",
                StructNameFE6 = "SoundRoom_FE6",
                StructNameFE78 = "SoundRoom_FE78",
                MetadataFileFE6 = "struct_soundroom_fe6.txt",
                MetadataFileFE78 = "struct_soundroom_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.sound_room_pointer),
                GetDataSize = rom => rom.RomInfo.sound_room_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.sound_room_pointer);
                    uint dataSize = rom.RomInfo.sound_room_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u32(addr) != 0xFFFFFFFF;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "sound_boss_bgm",
                StructNameFE6 = "SoundBossBGM",
                StructNameFE78 = "SoundBossBGM",
                MetadataFileFE6 = "struct_sound_boss_bgm.txt",
                MetadataFileFE78 = "struct_sound_boss_bgm.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.sound_boss_bgm_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.sound_boss_bgm_pointer);
                },
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.sound_boss_bgm_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.sound_boss_bgm_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u16(addr) != 0xFFFF;
                    });
                },
            });
        }

        static void RegisterSupportTables()
        {
            RegisterTable(new TableDef
            {
                Name = "support_units",
                StructNameFE6 = "SupportUnit_FE6",
                StructNameFE78 = "SupportUnit_FE78",
                MetadataFileFE6 = "struct_support_unit_fe6.txt",
                MetadataFileFE78 = "struct_support_unit_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.support_unit_pointer),
                GetDataSize = rom => (uint)(rom.RomInfo.version == 6 ? 32 : 24),
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.support_unit_pointer);
                    uint dataSize = (uint)(rom.RomInfo.version == 6 ? 32 : 24);
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u16(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "support_talks",
                StructNameFE6 = "SupportTalk_FE6",
                StructNameFE78 = "SupportTalk_FE78",
                MetadataFileFE6 = "struct_support_talk_fe6.txt",
                MetadataFileFE78 = "struct_support_talk_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.support_talk_pointer),
                GetDataSize = rom => 16,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.support_talk_pointer);
                    return rom.getBlockDataCount(baseAddr, 16, (int i, uint addr) =>
                    {
                        if (i > 0xFFF) return false;
                        return rom.u16(addr) != 0xFFFF && (i <= 10 || rom.u16(addr) != 0);
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "support_attributes",
                StructNameFE6 = "SupportAttribute",
                StructNameFE78 = "SupportAttribute",
                MetadataFileFE6 = "struct_support_attribute.txt",
                MetadataFileFE78 = "struct_support_attribute.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.support_attribute_pointer),
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.support_attribute_pointer);
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0;
                    });
                },
            });
        }

        static void RegisterEventTables()
        {
            RegisterTable(new TableDef
            {
                Name = "event_haiku",
                StructNameFE6 = "EventHaiku_FE6",
                StructNameFE78 = "EventHaiku_FE78",
                MetadataFileFE6 = "struct_event_haiku_fe6.txt",
                MetadataFileFE78 = "struct_event_haiku_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.event_haiku_pointer),
                GetDataSize = rom => (uint)(rom.RomInfo.version == 6 ? 16 : 12),
                GetEntryCount = rom =>
                {
                    uint dataSize = (uint)(rom.RomInfo.version == 6 ? 16 : 12);
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.event_haiku_pointer);
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFFF) return false;
                        return rom.u16(addr) != 0xFFFF && (i <= 10 || rom.u16(addr) != 0);
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "event_battle_talk",
                StructNameFE6 = "EventBattleTalk_FE6",
                StructNameFE78 = "EventBattleTalk_FE78",
                MetadataFileFE6 = "struct_event_battle_talk_fe6.txt",
                MetadataFileFE78 = "struct_event_battle_talk_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.event_ballte_talk_pointer),
                GetDataSize = rom => (uint)(rom.RomInfo.version == 6 ? 12 : 16),
                GetEntryCount = rom =>
                {
                    uint dataSize = (uint)(rom.RomInfo.version == 6 ? 12 : 16);
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.event_ballte_talk_pointer);
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFFF) return false;
                        return rom.u16(addr) != 0xFFFF && (i <= 10 || rom.u16(addr) != 0);
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "event_force_sortie",
                StructNameFE6 = "EventForceSortie",
                StructNameFE78 = "EventForceSortie",
                MetadataFileFE6 = "struct_event_force_sortie.txt",
                MetadataFileFE78 = "struct_event_force_sortie.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.event_force_sortie_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.event_force_sortie_pointer);
                },
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.event_force_sortie_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.event_force_sortie_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u16(addr) != 0xFFFF;
                    });
                },
            });
        }

        static void RegisterWorldMapTables()
        {
            RegisterTable(new TableDef
            {
                Name = "worldmap_points",
                StructNameFE6 = "WorldMapPoint",
                StructNameFE78 = "WorldMapPoint",
                MetadataFileFE6 = "struct_worldmap_point.txt",
                MetadataFileFE78 = "struct_worldmap_point.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.worldmap_point_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.worldmap_point_pointer);
                },
                GetDataSize = rom => 32,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.worldmap_point_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.worldmap_point_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 32, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr + 12))
                            && U.isPointerOrNULL(rom.u32(addr + 16))
                            && U.isPointerOrNULL(rom.u32(addr + 20));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "worldmap_paths",
                StructNameFE6 = "WorldMapPath",
                StructNameFE78 = "WorldMapPath",
                MetadataFileFE6 = "struct_worldmap_path.txt",
                MetadataFileFE78 = "struct_worldmap_path.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.worldmap_road_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.worldmap_road_pointer);
                },
                GetDataSize = rom => 12,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.worldmap_road_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.worldmap_road_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 12, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "worldmap_bgm",
                StructNameFE6 = "WorldMapBGM",
                StructNameFE78 = "WorldMapBGM",
                MetadataFileFE6 = "struct_worldmap_bgm.txt",
                MetadataFileFE78 = "struct_worldmap_bgm.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.worldmap_bgm_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.worldmap_bgm_pointer);
                },
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.worldmap_bgm_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.worldmap_bgm_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        // Stop if we see (1,0) or (0,0)
                        uint w0 = rom.u16(addr);
                        uint w2 = rom.u16(addr + 2);
                        if (w0 <= 1 && w2 == 0) return false;
                        return true;
                    });
                },
            });
        }

        static void RegisterMiscTables()
        {
            RegisterTable(new TableDef
            {
                Name = "map_settings",
                StructNameFE6 = "MapSetting_FE6",
                StructNameFE78 = "MapSetting_FE78",
                MetadataFileFE6 = "struct_map_setting_fe6.txt",
                MetadataFileFE78 = "struct_map_setting_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.map_setting_pointer),
                GetDataSize = rom => rom.RomInfo.map_setting_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.map_setting_pointer);
                    uint dataSize = rom.RomInfo.map_setting_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "link_arena_deny",
                StructNameFE6 = "LinkArenaDeny",
                StructNameFE78 = "LinkArenaDeny",
                MetadataFileFE6 = "struct_link_arena_deny.txt",
                MetadataFileFE78 = "struct_link_arena_deny.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.link_arena_deny_unit_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.link_arena_deny_unit_pointer);
                },
                GetDataSize = rom => 2,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.link_arena_deny_unit_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.link_arena_deny_unit_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 2, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "cc_branch",
                StructNameFE6 = "CCBranch",
                StructNameFE78 = "CCBranch",
                MetadataFileFE6 = "struct_cc_branch.txt",
                MetadataFileFE78 = "struct_cc_branch.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ccbranch_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ccbranch_pointer);
                },
                GetDataSize = rom => 2,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ccbranch_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ccbranch_pointer);
                    if (baseAddr == 0) return 0;
                    // CC branch count matches class count
                    uint classBase = ResolvePointer(rom, rom.RomInfo.class_pointer);
                    uint classSize = rom.RomInfo.class_datasize;
                    return rom.getBlockDataCount(classBase, classSize, (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0xFF) return false;
                        return rom.u8(addr + 4) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "menu_definitions",
                StructNameFE6 = "MenuDefinition",
                StructNameFE78 = "MenuDefinition",
                MetadataFileFE6 = "struct_menu_definition.txt",
                MetadataFileFE78 = "struct_menu_definition.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.menu_definiton_pointer),
                GetDataSize = rom => 36,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.menu_definiton_pointer);
                    return rom.getBlockDataCount(baseAddr, 36, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr + 8));
                    });
                },
            });
        }

        static void RegisterUniversalTables2()
        {
            RegisterTable(new TableDef
            {
                Name = "item_weapon_triangle",
                StructNameFE6 = "ItemWeaponTriangle",
                StructNameFE78 = "ItemWeaponTriangle",
                MetadataFileFE6 = "struct_item_weapon_triangle.txt",
                MetadataFileFE78 = "struct_item_weapon_triangle.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.item_cornered_pointer),
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.item_cornered_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0xFF;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "map_exit_points",
                StructNameFE6 = "MapExitPoint",
                StructNameFE78 = "MapExitPoint",
                MetadataFileFE6 = "struct_map_exit_point.txt",
                MetadataFileFE78 = "struct_map_exit_point.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.map_exit_point_pointer),
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.map_exit_point_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (!U.isPointerOrNULL(rom.u32(addr))) return false;
                        return i < (int)rom.RomInfo.map_exit_point_npc_blockadd;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ai_map_settings",
                StructNameFE6 = "AIMapSetting",
                StructNameFE78 = "AIMapSetting",
                MetadataFileFE6 = "struct_ai_map_setting.txt",
                MetadataFileFE78 = "struct_ai_map_setting.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.ai_map_setting_pointer),
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ai_map_setting_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0xFF;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ai_perform_items",
                StructNameFE6 = "AIPerformItem",
                StructNameFE78 = "AIPerformItem",
                MetadataFileFE6 = "struct_ai_perform_item.txt",
                MetadataFileFE78 = "struct_ai_perform_item.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.ai_preform_item_pointer),
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ai_preform_item_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u16(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ai_perform_staff",
                StructNameFE6 = "AIPerformStaff",
                StructNameFE78 = "AIPerformStaff",
                MetadataFileFE6 = "struct_ai_perform_staff.txt",
                MetadataFileFE78 = "struct_ai_perform_staff.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.ai_preform_staff_pointer),
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ai_preform_staff_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u16(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ai_steal_items",
                StructNameFE6 = "AIStealItem",
                StructNameFE78 = "AIStealItem",
                MetadataFileFE6 = "struct_ai_steal_item.txt",
                MetadataFileFE78 = "struct_ai_steal_item.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.ai_steal_item_pointer),
                GetDataSize = rom => 2,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ai_steal_item_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 2, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0xFF;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ai_targets",
                StructNameFE6 = "AITarget",
                StructNameFE78 = "AITarget",
                MetadataFileFE6 = "struct_ai_target.txt",
                MetadataFileFE78 = "struct_ai_target.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.ai3_pointer),
                GetDataSize = rom => 20,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ai3_pointer);
                    if (baseAddr == 0) return 0;
                    // Fixed count of 8 entries
                    return 8;
                },
            });

            RegisterTable(new TableDef
            {
                Name = "generic_enemy_portraits",
                StructNameFE6 = "GenericEnemyPortrait",
                StructNameFE78 = "GenericEnemyPortrait",
                MetadataFileFE6 = "struct_generic_enemy_portrait.txt",
                MetadataFileFE78 = "struct_generic_enemy_portrait.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.generic_enemy_portrait_pointer),
                GetDataSize = rom => 4,
                GetEntryCount = rom => rom.RomInfo.generic_enemy_portrait_count,
            });

            RegisterTable(new TableDef
            {
                Name = "status_options",
                StructNameFE6 = "StatusOption",
                StructNameFE78 = "StatusOption",
                MetadataFileFE6 = "struct_status_option.txt",
                MetadataFileFE78 = "struct_status_option.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.status_game_option_pointer),
                GetDataSize = rom => 44,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.status_game_option_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 44, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointer(rom.u32(addr + 40));
                    });
                },
            });
        }

        static void RegisterEndingTables()
        {
            RegisterTable(new TableDef
            {
                Name = "ed_retreat",
                StructNameFE6 = "EDRetreat",
                StructNameFE78 = "EDRetreat",
                MetadataFileFE6 = "struct_ed_retreat.txt",
                MetadataFileFE78 = "struct_ed_retreat.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ed_1_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ed_1_pointer);
                },
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ed_1_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ed_1_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u32(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ed_epithet",
                StructNameFE6 = "EDEpithet",
                StructNameFE78 = "EDEpithet",
                MetadataFileFE6 = "struct_ed_epithet.txt",
                MetadataFileFE78 = "struct_ed_epithet.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ed_2_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ed_2_pointer);
                },
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ed_2_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ed_2_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ed_epilogue_a",
                StructNameFE6 = "EDEpilogue",
                StructNameFE78 = "EDEpilogue",
                MetadataFileFE6 = "struct_ed_epilogue.txt",
                MetadataFileFE78 = "struct_ed_epilogue.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ed_3a_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ed_3a_pointer);
                },
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ed_3a_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ed_3a_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u32(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ed_epilogue_b",
                StructNameFE6 = "EDEpilogue",
                StructNameFE78 = "EDEpilogue",
                MetadataFileFE6 = "struct_ed_epilogue.txt",
                MetadataFileFE78 = "struct_ed_epilogue.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ed_3b_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ed_3b_pointer);
                },
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ed_3b_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ed_3b_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u32(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "ed_epilogue_c",
                StructNameFE6 = "EDEpilogue",
                StructNameFE78 = "EDEpilogue",
                MetadataFileFE6 = "struct_ed_epilogue.txt",
                MetadataFileFE78 = "struct_ed_epilogue.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.ed_3c_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.ed_3c_pointer);
                },
                GetDataSize = rom => 8,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.ed_3c_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.ed_3c_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 8, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u32(addr) != 0;
                    });
                },
            });
        }

        static void RegisterOPTables()
        {
            RegisterTable(new TableDef
            {
                Name = "op_class_demo",
                StructNameFE6 = "OPClassDemo",
                StructNameFE78 = "OPClassDemo",
                MetadataFileFE6 = "struct_op_class_demo.txt",
                MetadataFileFE78 = "struct_op_class_demo.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.op_class_demo_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.op_class_demo_pointer);
                },
                GetDataSize = rom => 28,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.op_class_demo_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.op_class_demo_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 28, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointer(rom.u32(addr));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "op_class_font",
                StructNameFE6 = "OPClassFont",
                StructNameFE78 = "OPClassFont",
                MetadataFileFE6 = "struct_op_class_font.txt",
                MetadataFileFE78 = "struct_op_class_font.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.op_class_font_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.op_class_font_pointer);
                },
                GetDataSize = rom => 4,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.op_class_font_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.op_class_font_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 4, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointer(rom.u32(addr));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "op_prologue",
                StructNameFE6 = "OPPrologue",
                StructNameFE78 = "OPPrologue",
                MetadataFileFE6 = "struct_op_prologue.txt",
                MetadataFileFE78 = "struct_op_prologue.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.op_prologue_image_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.op_prologue_image_pointer);
                },
                GetDataSize = rom => 12,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.op_prologue_image_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.op_prologue_image_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 12, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointer(rom.u32(addr));
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "class_alpha_names",
                StructNameFE6 = "ClassAlphaName",
                StructNameFE78 = "ClassAlphaName",
                MetadataFileFE6 = "struct_class_alpha_name.txt",
                MetadataFileFE78 = "struct_class_alpha_name.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.class_alphaname_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.class_alphaname_pointer);
                },
                GetDataSize = rom => 20,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.class_alphaname_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.class_alphaname_pointer);
                    if (baseAddr == 0) return 0;
                    // Count matches class count
                    uint classBase = ResolvePointer(rom, rom.RomInfo.class_pointer);
                    uint classSize = rom.RomInfo.class_datasize;
                    return rom.getBlockDataCount(classBase, classSize, (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0xFF) return false;
                        return rom.u8(addr + 4) != 0;
                    });
                },
            });
        }

        static void RegisterFE8Tables()
        {
            RegisterTable(new TableDef
            {
                Name = "summon_units",
                StructNameFE6 = "SummonUnit",
                StructNameFE78 = "SummonUnit",
                MetadataFileFE6 = "struct_summon_unit.txt",
                MetadataFileFE78 = "struct_summon_unit.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.summon_unit_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.summon_unit_pointer);
                },
                GetDataSize = rom => 2,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.summon_unit_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.summon_unit_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 2, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "summons_demon_king",
                StructNameFE6 = "SummonsDemonKing",
                StructNameFE78 = "SummonsDemonKing",
                MetadataFileFE6 = "struct_summons_demon_king.txt",
                MetadataFileFE78 = "struct_summons_demon_king.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.summons_demon_king_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.summons_demon_king_pointer);
                },
                GetDataSize = rom => 20,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.summons_demon_king_pointer == 0) return 0;
                    if (rom.RomInfo.summons_demon_king_count_address == 0) return 0;
                    uint max = rom.u8(rom.RomInfo.summons_demon_king_count_address);
                    if (max >= 100) return 0;
                    return max + 1;
                },
            });

            RegisterTable(new TableDef
            {
                Name = "monster_probability",
                StructNameFE6 = "MonsterProbability",
                StructNameFE78 = "MonsterProbability",
                MetadataFileFE6 = "struct_monster_probability.txt",
                MetadataFileFE78 = "struct_monster_probability.txt",
                GetBaseAddress = rom =>
                {
                    if (rom.RomInfo.monster_probability_pointer == 0) return 0;
                    return ResolvePointer(rom, rom.RomInfo.monster_probability_pointer);
                },
                GetDataSize = rom => 12,
                GetEntryCount = rom =>
                {
                    if (rom.RomInfo.monster_probability_pointer == 0) return 0;
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.monster_probability_pointer);
                    if (baseAddr == 0) return 0;
                    return rom.getBlockDataCount(baseAddr, 12, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return rom.u8(addr) != 0xFF;
                    });
                },
            });
        }

        /// <summary>
        /// Resolve a ROM pointer: dereference the pointer at the given address.
        /// ROM info properties like unit_pointer store the address OF the pointer,
        /// not the data address itself. This mirrors InputFormRef behavior.
        /// </summary>
        static uint ResolvePointer(ROM rom, uint pointerAddr)
        {
            if (pointerAddr == 0 || pointerAddr == U.NOT_FOUND) return 0;
            uint offset = U.toOffset(pointerAddr);
            if (!U.isSafetyOffset(offset, rom)) return 0;
            return rom.p32(offset);
        }

        /// <summary>Register a table definition.</summary>
        public static void RegisterTable(TableDef def)
        {
            _tables[def.Name] = def;
        }

        /// <summary>Get all registered table names.</summary>
        public static IEnumerable<string> GetTableNames() => _tables.Keys;

        /// <summary>Get a table definition by name.</summary>
        public static TableDef GetTable(string name)
        {
            _tables.TryGetValue(name, out var def);
            return def;
        }

        /// <summary>
        /// Find the registered table whose data range contains the given address.
        /// <paramref name="addr"/> may be a ROM file offset or a GBA pointer
        /// (0x08000000+offset) — both are normalised via <see cref="U.toOffset"/>.
        /// A table matches when its data range [base, base + count*size) contains
        /// the offset (end-EXCLUSIVE). Tables that resolve to an empty/invalid
        /// range (base 0 / NOT_FOUND, size 0, or count 0) are skipped, and any
        /// per-table resolution exception is swallowed so one bad table does not
        /// abort the scan. When multiple tables overlap the address, the one with
        /// the narrowest data range (smallest count*size) wins. Returns null if no
        /// table contains the address.
        /// </summary>
        public static TableDef ResolveTableAt(ROM rom, uint addr)
        {
            if (rom?.RomInfo == null) return null;

            uint off = U.toOffset(addr);

            TableDef best = null;
            ulong bestSpan = ulong.MaxValue;

            foreach (var name in GetTableNames())
            {
                var table = GetTable(name);
                if (table == null) continue;

                try
                {
                    uint baseAddr = table.GetBaseAddress(rom);
                    uint size = table.GetDataSize(rom);
                    uint count = table.GetEntryCount(rom);

                    if (baseAddr == 0 || baseAddr == U.NOT_FOUND || size == 0 || count == 0)
                        continue;

                    ulong span = (ulong)count * (ulong)size;
                    ulong start = (ulong)baseAddr;
                    ulong end = start + span; // end-exclusive

                    if (start <= (ulong)off && (ulong)off < end)
                    {
                        if (span < bestSpan)
                        {
                            bestSpan = span;
                            best = table;
                        }
                    }
                }
                catch
                {
                    // Skip tables that throw during resolution (missing pointer,
                    // out-of-range read, etc.) — they simply don't match.
                }
            }

            return best;
        }

        /// <summary>
        /// Load the appropriate StructDef for a table based on ROM version.
        /// </summary>
        public static StructMetadata.StructDef LoadStructDef(ROM rom, TableDef table)
        {
            bool isFE6 = rom.RomInfo.version == 6;
            string fileName = isFE6 ? table.MetadataFileFE6 : table.MetadataFileFE78;
            string structName = isFE6 ? table.StructNameFE6 : table.StructNameFE78;

            string dir = Path.Combine(CoreState.BaseDirectory, "config", "data");
            string path = Path.Combine(dir, fileName);

            var meta = new StructMetadata();
            meta.LoadFromFile(path);
            return meta.GetStruct(structName);
        }

        /// <summary>
        /// Shared row-extraction seam (#1939): ONE base/count/stride/safety-stop
        /// traversal produces, for every readable row, both the typed field→hex-string
        /// dictionary (identical in shape/content to what <see cref="ExportTable"/> has
        /// always returned) AND the exact raw runtime-stride bytes for that same row —
        /// read from the same <paramref name="rom"/> at the same address, in the same
        /// loop iteration. <see cref="ExportTable"/> (TSV/CSV/EA/JSON's data source) and
        /// <see cref="FormatCData"/>/<see cref="ExportToCData"/> (the GNU11 C formatter's
        /// data source) both consume this single seam, so the typed view and the raw-byte
        /// view can never drift apart — there is no second, independently-maintained loop
        /// that could read a different address, stop at a different row, or decode a field
        /// differently than the raw bytes actually on the ROM.
        /// </summary>
        public static List<(uint index, Dictionary<string, string> fields, byte[] raw)> ExportTableRows(
            ROM rom, TableDef table, StructMetadata.StructDef structDef)
        {
            var result = new List<(uint, Dictionary<string, string>, byte[])>();
            if (rom?.RomInfo == null || table == null || structDef == null) return result;

            uint baseAddr = table.GetBaseAddress(rom);
            uint dataSize = table.GetDataSize(rom);
            uint count = table.GetEntryCount(rom);

            if (baseAddr == 0 || baseAddr == U.NOT_FOUND || count == 0) return result;

            for (uint i = 0; i < count; i++)
            {
                uint entryAddr = baseAddr + (i * dataSize);
                if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) break;

                var entry = new Dictionary<string, string>();

                // First column: index with decoded name
                string name = "";
                if (structDef.Fields.Count > 0 && structDef.Fields[0].Name == "NameTextID")
                {
                    uint textId = structDef.ReadField(rom, entryAddr, structDef.Fields[0]);
                    try { name = FETextDecode.Direct(textId) ?? ""; } catch { }
                }
                // Full-width formatting (not a byte cast): a table with 256+ rows must
                // not alias row 256 back to "0x00" — U.To0xHexString(uint) widens the
                // hex string (X02/X04/X06/X08) based on the actual magnitude of i.
                entry["_Index"] = U.To0xHexString(i) + " " + name;

                // All fields as hex values
                foreach (var field in structDef.Fields)
                {
                    uint val = structDef.ReadField(rom, entryAddr, field);
                    entry[field.Name] = FormatFieldValue(val, field);
                }

                // Exact raw runtime-stride bytes for this same row/address. The safety-stop
                // check above already guarantees entryAddr .. entryAddr+dataSize-1 is inside
                // rom.Data, so this always returns exactly dataSize bytes (never truncated).
                byte[] raw = U.getBinaryData(rom.Data, entryAddr, dataSize);

                result.Add((i, entry, raw));
            }

            return result;
        }

        /// <summary>
        /// Export a ROM table to a list of (index, fieldName→value) entries. Thin
        /// projection over <see cref="ExportTableRows"/> — kept as its own method (rather
        /// than inlined at call sites) so every existing TSV/CSV/EA/JSON caller keeps its
        /// original signature/behavior byte-for-byte (#1939).
        /// </summary>
        public static List<Dictionary<string, string>> ExportTable(ROM rom, TableDef table, StructMetadata.StructDef structDef)
        {
            var rows = ExportTableRows(rom, table, structDef);
            var result = new List<Dictionary<string, string>>(rows.Count);
            foreach (var row in rows) result.Add(row.fields);
            return result;
        }

        /// <summary>Format a field value as hex string appropriate for its type.</summary>
        static string FormatFieldValue(uint val, StructMetadata.FieldDef field)
        {
            return field.Type switch
            {
                StructMetadata.FieldType.Byte => "0x" + val.ToString("X02"),
                StructMetadata.FieldType.Word => "0x" + val.ToString("X04"),
                StructMetadata.FieldType.DWord => "0x" + val.ToString("X08"),
                StructMetadata.FieldType.Pointer => "0x" + val.ToString("X08"),
                _ => "0x" + val.ToString("X02"),
            };
        }

        /// <summary>
        /// Format table data as a TSV string. The header ("Index" + field names)
        /// is line 1; there is NO banner/prefix. <see cref="ExportToTSV"/> writes
        /// exactly this string to disk.
        /// </summary>
        public static string FormatTSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append("Index");
            foreach (var field in structDef.Fields)
            {
                sb.Append('\t');
                sb.Append(field.Name);
            }
            sb.AppendLine();

            // Data rows
            foreach (var entry in entries)
            {
                sb.Append(entry.TryGetValue("_Index", out var idx) ? idx : "");
                foreach (var field in structDef.Fields)
                {
                    sb.Append('\t');
                    sb.Append(entry.TryGetValue(field.Name, out var val) ? val : "");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format table data as an RFC 4180 compliant CSV string. The header
        /// ("Index" + field names) is line 1; there is NO banner/prefix.
        /// <see cref="ExportToCSV"/> writes exactly this string to disk.
        /// </summary>
        public static string FormatCSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append(CsvQuote("Index"));
            foreach (var field in structDef.Fields)
            {
                sb.Append(',');
                sb.Append(CsvQuote(field.Name));
            }
            sb.AppendLine();

            // Data rows
            foreach (var entry in entries)
            {
                sb.Append(CsvQuote(entry.TryGetValue("_Index", out var idx) ? idx : ""));
                foreach (var field in structDef.Fields)
                {
                    sb.Append(',');
                    sb.Append(CsvQuote(entry.TryGetValue(field.Name, out var val) ? val : ""));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format table data as EA (Event Assembler) #define text. The first line
        /// is the standard "// Event Assembler definitions for {struct}" header
        /// comment (not a stub banner). Each field becomes:
        /// #define {TableName}_{HexIndex}_{FieldName} 0xVALUE.
        /// <see cref="ExportToEA"/> writes exactly this string to disk.
        /// </summary>
        public static string FormatEA(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// Event Assembler definitions for {structDef.Name}");
            sb.AppendLine($"// Generated by FEBuilderGBA CLI");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                string index = entry.TryGetValue("_Index", out var idx) ? idx : "0x00";
                // Extract hex index from "0xNN Name" format
                string hexIdx = index.Split(' ')[0];

                foreach (var field in structDef.Fields)
                {
                    if (!entry.TryGetValue(field.Name, out var val)) continue;
                    sb.AppendLine($"#define {structDef.Name}_{hexIdx}_{field.Name} {val}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format table data as a JSON array of objects (#1937 — the LLM-backend
        /// / agent-consumable format). Public keys are exactly the TSV column
        /// headers used by <see cref="FormatTSV"/>: the internal <c>_Index</c> key
        /// is renamed to the public <c>Index</c> key (never leaked as-is), followed
        /// by one key per struct field in declaration order. Every value is
        /// serialized as a JSON *string* holding the same TSV-compatible hex/text
        /// representation (e.g. <c>"0x0A"</c>) — no numbers/booleans — so JSON
        /// round-trips through the same <c>U.atoi0x</c> parsing as TSV/CSV.
        /// <see cref="ExportToJSON"/> writes exactly this string to disk.
        /// </summary>
        public static string FormatJSON(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef)
        {
            var array = new JsonArray();
            foreach (var entry in entries)
            {
                var obj = new JsonObject
                {
                    ["Index"] = entry.TryGetValue("_Index", out var idx) ? idx : ""
                };
                foreach (var field in structDef.Fields)
                {
                    obj[field.Name] = entry.TryGetValue(field.Name, out var val) ? val : "";
                }
                array.Add(obj);
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            return array.ToJsonString(opts);
        }

        /// <summary>
        /// Parse a JSON document produced by (or compatible with) <see cref="FormatJSON"/>
        /// back into (index, fields) entries usable by <see cref="WriteTable"/>. Mirrors
        /// <see cref="ImportFromTSV"/> but for JSON input. Validates the *entire* document
        /// before returning anything, so a caller can rely on "no exception ⇒ every row is
        /// well-formed" and defer all ROM writes until after a successful parse:
        /// <list type="bullet">
        /// <item>the root element must be a JSON array;</item>
        /// <item>every row must be a JSON object;</item>
        /// <item>every property value must be a JSON string — numbers, booleans, nulls,
        /// arrays, and nested objects are all rejected with a specific, actionable message
        /// (row number + property name + the offending <see cref="JsonValueKind"/>);</item>
        /// <item>a row may not repeat the same property name twice — <c>JsonDocument</c>
        /// tolerates duplicate keys and silently keeps only the last one on enumeration,
        /// which would otherwise let a duplicated <c>Index</c> (or any field) silently
        /// win over an earlier, possibly-intended value;</item>
        /// <item>the public <c>Index</c> property is required and is strictly parsed back
        /// to the internal index used by <see cref="WriteTable"/> via
        /// <see cref="TryParseStrictIndex"/> — unlike TSV import's forgiving
        /// <c>ParseIndexFromFirstColumn</c> (which silently aliases unparsable garbage to
        /// row 0 through <c>U.atoi0x</c>'s truncating parse), a malformed/out-of-range/
        /// negative JSON <c>Index</c> is rejected outright rather than mutating the wrong
        /// row.</item>
        /// </list>
        /// Throws <see cref="JsonException"/> for malformed JSON syntax, or
        /// <see cref="FormatException"/> for a syntactically valid document that violates
        /// the shape above.
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ParseJSON(string json)
        {
            var result = new List<(int, Dictionary<string, string>)>();

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                throw new FormatException($"Invalid JSON import: root element must be an array, got {root.ValueKind}.");

            int rowNum = 0;
            foreach (JsonElement rowEl in root.EnumerateArray())
            {
                rowNum++;
                if (rowEl.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"Invalid JSON import: row {rowNum} must be an object, got {rowEl.ValueKind}.");

                string indexRaw = null;
                bool sawIndex = false;
                var seenProps = new HashSet<string>(StringComparer.Ordinal);
                var fields = new Dictionary<string, string>();
                foreach (JsonProperty prop in rowEl.EnumerateObject())
                {
                    if (!seenProps.Add(prop.Name))
                    {
                        throw new FormatException(
                            $"Invalid JSON import: row {rowNum} has a duplicate property '{prop.Name}'.");
                    }

                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        throw new FormatException(
                            $"Invalid JSON import: row {rowNum}, property '{prop.Name}' must be a JSON string, got {prop.Value.ValueKind}.");
                    }

                    string value = prop.Value.GetString();
                    if (prop.Name == "Index")
                    {
                        indexRaw = value;
                        sawIndex = true;
                    }
                    else
                    {
                        fields[prop.Name] = value;
                    }
                }

                if (!sawIndex)
                    throw new FormatException($"Invalid JSON import: row {rowNum} is missing the required 'Index' property.");

                if (!TryParseStrictIndex(indexRaw, out int entryIndex))
                    throw new FormatException($"Invalid JSON import: row {rowNum} has an unparsable 'Index' value '{indexRaw}'.");

                result.Add((entryIndex, fields));
            }

            return result;
        }

        /// <summary>
        /// Strictly parse a JSON <c>Index</c> value shaped like the TSV/CSV/JSON export's
        /// first column, <c>"&lt;token&gt;[ label]"</c>, where <c>&lt;token&gt;</c> is one of
        /// the three numeric forms <see cref="U.atoi0x"/> understands: <c>0x</c>-prefixed hex,
        /// <c>$</c>-prefixed hex, or plain decimal digits.
        /// <para/>
        /// Unlike <see cref="ParseIndexFromFirstColumn"/> (used by TSV import, which
        /// tolerates garbage by falling through to <c>U.atoi</c>'s truncating parse — e.g.
        /// <c>"banana"</c> silently becomes <c>0</c>), this requires the entire token to be
        /// a well-formed, in-range, non-negative number for its base. Overflow (more digits
        /// than fit in a <see cref="uint"/>), a missing/empty numeric portion, and any
        /// non-numeric token are all rejected rather than aliased to row 0. TSV import
        /// behavior is intentionally left unchanged by this stricter JSON-only parser.
        /// </summary>
        static bool TryParseStrictIndex(string indexRaw, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(indexRaw)) return false;

            string trimmed = indexRaw.Trim();
            int space = trimmed.IndexOf(' ');
            string token = space >= 0 ? trimmed.Substring(0, space) : trimmed;
            if (token.Length == 0) return false;

            string digits;
            bool isHex;
            if (token.Length >= 2 && token[0] == '0' && (token[1] == 'x' || token[1] == 'X'))
            {
                digits = token.Substring(2);
                isHex = true;
            }
            else if (token.Length >= 1 && token[0] == '$')
            {
                digits = token.Substring(1);
                isHex = true;
            }
            else
            {
                digits = token;
                isHex = false;
            }

            if (digits.Length == 0) return false;

            bool ok = isHex
                ? uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)
                : uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
            if (!ok || parsed > int.MaxValue) return false;

            index = (int)parsed;
            return true;
        }

        /// <summary>
        /// Semantic (struct/count-aware) preflight for the (index, fields) rows already
        /// shape-validated by <see cref="ParseJSON"/> (#1937 hardening). <see cref="ParseJSON"/>
        /// only validates JSON *kinds* (root is an array, rows are objects, values are
        /// strings, no duplicate property names) — it happily accepts a typo'd field name
        /// or a garbage numeric string like <c>"banana"</c> for a field, which
        /// <see cref="WriteTable"/> would then hand to the permissive <see cref="U.atoi0x"/>,
        /// silently coercing it to <c>0</c> and mutating the ROM. This method closes that
        /// gap and must run to completion (throwing on the first violation) before any
        /// <see cref="WriteTable"/>/<c>ROM.Save</c> call:
        /// <list type="bullet">
        /// <item>every non-<c>Index</c> property name must be a known field of
        /// <paramref name="structDef"/> — an unknown/typo'd name is rejected with the row
        /// number and the offending property name (a field simply absent from a row is
        /// still allowed, to support partial updates);</item>
        /// <item>every field value must strictly parse as a complete <c>0x</c>-hex,
        /// <c>$</c>-hex, or plain-decimal token — no trailing tokens, no bare prefixes, no
        /// negatives, no garbage — and must fit the field's <see cref="StructMetadata.FieldType"/>
        /// width (Byte/Word/DWord/Pointer); accepted values are rewritten in place to a
        /// canonical hexadecimal form (always a lowercase <c>0x</c> prefix, so
        /// <see cref="U.atoi0x"/> parses the full unsigned field range safely even if the JSON
        /// used decimal, <c>$</c>, or an uppercase <c>0X</c> prefix);</item>
        /// <item>no two rows in the document may resolve to the same <c>Index</c> — a
        /// duplicate row index is rejected rather than letting the second row's write
        /// silently clobber the first;</item>
        /// <item>every row's <c>Index</c> must be within <c>[0, entryCount)</c> for the
        /// resolved table — out-of-range indices are rejected here instead of relying on
        /// <see cref="WriteTable"/>'s silent per-row skip.</item>
        /// </list>
        /// Throws <see cref="FormatException"/> (mirroring <see cref="ParseJSON"/>'s own
        /// error type) with an actionable row/property message on the first violation
        /// found. <paramref name="entries"/>' field dictionaries are mutated in place with
        /// canonical values as each row passes; a caller can rely on "no exception ⇒ every
        /// row/field is well-formed and canonicalized".
        /// </summary>
        public static void ValidateJSONEntries(
            List<(int index, Dictionary<string, string> fields)> entries,
            StructMetadata.StructDef structDef,
            uint entryCount)
        {
            if (structDef == null) throw new ArgumentNullException(nameof(structDef));
            if (entries == null) return;

            var fieldsByName = new Dictionary<string, StructMetadata.FieldDef>(StringComparer.Ordinal);
            foreach (var field in structDef.Fields)
            {
                fieldsByName[field.Name] = field;
            }

            var seenIndices = new HashSet<int>();
            int rowNum = 0;
            foreach (var (index, fields) in entries)
            {
                rowNum++;

                if (!seenIndices.Add(index))
                {
                    throw new FormatException(
                        $"Invalid JSON import: row {rowNum} has a duplicate Index {index} — an earlier row in this document already targets that entry.");
                }

                if (entryCount == 0 || index < 0 || (uint)index >= entryCount)
                {
                    string range = entryCount == 0 ? "0 entries (table/base address not resolved)" : $"[0, {entryCount - 1}]";
                    throw new FormatException(
                        $"Invalid JSON import: row {rowNum} has Index {index}, outside the valid range {range} for struct '{structDef.Name}'.");
                }

                // Snapshot the keys before mutating values in the same dictionary.
                var propNames = new List<string>(fields.Keys);
                foreach (string propName in propNames)
                {
                    if (!fieldsByName.TryGetValue(propName, out StructMetadata.FieldDef field))
                    {
                        throw new FormatException(
                            $"Invalid JSON import: row {rowNum} has unknown property '{propName}' — struct '{structDef.Name}' has no such field.");
                    }

                    string raw = fields[propName];
                    if (!TryNormalizeFieldValue(raw, field.Type, out string canonical, out string error))
                    {
                        throw new FormatException(
                            $"Invalid JSON import: row {rowNum}, property '{propName}' has an invalid value '{raw}': {error}");
                    }

                    fields[propName] = canonical;
                }
            }
        }

        /// <summary>
        /// Strictly parse a single JSON field value shaped like <c>"0xNN"</c>/<c>"$NN"</c>/
        /// plain decimal (the exact shape <see cref="FormatFieldValue"/> emits, with no
        /// trailing label unlike the <c>Index</c> column) and range-check it against
        /// <paramref name="type"/>'s width. Every character after the optional prefix must
        /// be a valid digit for its base — this rejects trailing garbage (<c>"0x0A extra"</c>),
        /// negatives (<c>-</c> is never a valid digit), bare prefixes (<c>"0x"</c> alone), and
        /// overflow (too many digits, or a value exceeding the field's byte/word/dword/pointer
        /// width) — instead of the permissive <see cref="U.atoi0x"/> silently truncating/
        /// zeroing garbage. On success, <paramref name="canonical"/> is always re-emitted
        /// as hexadecimal with a lowercase <c>0x</c> prefix so a later
        /// <see cref="U.atoi0x"/> call parses the full unsigned 32-bit range correctly.
        /// This includes decimal input above <see cref="int.MaxValue"/>, which
        /// <see cref="U.atoi0x"/> cannot parse safely in decimal form.
        /// </summary>
        static bool TryNormalizeFieldValue(string raw, StructMetadata.FieldType type, out string canonical, out string error)
        {
            canonical = null;
            error = null;

            if (raw == null)
            {
                error = "value is missing";
                return false;
            }

            string trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                error = "value is empty";
                return false;
            }

            string digits;
            bool isHex;
            if (trimmed.Length >= 2 && trimmed[0] == '0' && (trimmed[1] == 'x' || trimmed[1] == 'X'))
            {
                digits = trimmed.Substring(2);
                isHex = true;
            }
            else if (trimmed.Length >= 1 && trimmed[0] == '$')
            {
                digits = trimmed.Substring(1);
                isHex = true;
            }
            else
            {
                digits = trimmed;
                isHex = false;
            }

            if (digits.Length == 0)
            {
                error = "missing numeric digits after prefix";
                return false;
            }

            foreach (char c in digits)
            {
                bool validDigit = isHex ? U.ishex(c) : U.isnum(c);
                if (!validDigit)
                {
                    error = $"contains a non-numeric/trailing character '{c}'";
                    return false;
                }
            }

            bool ok = isHex
                ? uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)
                : uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
            if (!ok)
            {
                error = "value does not fit a 32-bit unsigned integer (overflow)";
                return false;
            }

            uint max = FieldTypeMaxValue(type);
            if (parsed > max)
            {
                error = $"value 0x{parsed:X} exceeds the maximum for a {type} field (0x{max:X})";
                return false;
            }

            canonical = "0x" + parsed.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Maximum representable unsigned value for a struct field's width.</summary>
        static uint FieldTypeMaxValue(StructMetadata.FieldType type)
        {
            return type switch
            {
                StructMetadata.FieldType.Byte => 0xFFu,
                StructMetadata.FieldType.Word => 0xFFFFu,
                StructMetadata.FieldType.DWord => 0xFFFFFFFFu,
                StructMetadata.FieldType.Pointer => 0xFFFFFFFFu,
                _ => 0xFFu,
            };
        }

        /// <summary>
        /// Parse and fully validate a JSON import document against <paramref name="structDef"/>
        /// and <paramref name="entryCount"/> in one call: shape (<see cref="ParseJSON"/>) then
        /// semantics (<see cref="ValidateJSONEntries"/>). No ROM write may occur until this
        /// returns without throwing.
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ParseAndValidateJSON(
            string json, StructMetadata.StructDef structDef, uint entryCount)
        {
            var entries = ParseJSON(json);
            ValidateJSONEntries(entries, structDef, entryCount);
            return entries;
        }

        /// <summary>
        /// Import table data from a JSON file (see <see cref="ParseJSON"/> for the
        /// validated shape). Throws before returning if the document is malformed —
        /// callers must not mutate the ROM until this call succeeds.
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ImportFromJSON(string inputPath)
        {
            string content = File.ReadAllText(inputPath, Encoding.UTF8);
            return ParseJSON(content);
        }

        /// <summary>
        /// Import table data from a JSON file and run the full struct/count-aware semantic
        /// preflight (see <see cref="ValidateJSONEntries"/>) before returning — this is the
        /// overload CLI import (<c>--import-data --format=json</c>) uses so unknown fields,
        /// out-of-range/garbage numeric values, duplicate row indices, and out-of-range
        /// <c>Index</c> values are all rejected before any <see cref="WriteTable"/>/
        /// <c>ROM.Save</c> call. The shape-only <see cref="ImportFromJSON(string)"/> overload
        /// is preserved unchanged for callers that only need the shape contract (e.g.
        /// existing tests, or tooling without a resolved struct/entry count).
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ImportFromJSON(
            string inputPath, StructMetadata.StructDef structDef, uint entryCount)
        {
            string content = File.ReadAllText(inputPath, Encoding.UTF8);
            return ParseAndValidateJSON(content, structDef, entryCount);
        }

        /// <summary>
        /// Format the struct layout as a C-header (".h") string, porting the
        /// WinForms <c>DumpStructSelectDialogForm.MakeStructString</c> output.
        /// The first line is <c>struct {Name} {//{Name}</c>; each field emits one
        /// line keyed off <see cref="StructMetadata.FieldType"/>:
        /// Byte→<c>byte    _{Offset};//{Name}</c>, Word→<c>ushort   _{Offset};//{Name}</c>,
        /// DWord→<c>dword   _{Offset};//{Name}</c>, Pointer→<c>void*   _{Offset};//{Name}</c>
        /// (spacing matches WinForms exactly; <c>_{Offset}</c> uses the DECIMAL
        /// field offset like WinForms <c>nm.Id</c>). The footer is
        /// <c>}; sizeof({DataSize})</c>. <see cref="ExportToSTRUCT"/> writes
        /// exactly this string to disk.
        /// </summary>
        /// <remarks>
        /// Documented fidelity gap vs WinForms: WinForms distinguishes a
        /// signed-byte (<c>sbyte</c>) widget from <c>byte</c>, but the headless
        /// <see cref="StructMetadata.FieldType.Byte"/> carries no signedness, so
        /// all single bytes emit <c>byte</c>. WinForms hidden <c>h</c> widgets
        /// have no headless analog (already absent from the metadata).
        /// </remarks>
        public static string FormatSTRUCT(StructMetadata.StructDef structDef)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"struct {structDef.Name} {{//{structDef.Name}");

            foreach (var field in structDef.Fields)
            {
                string comment = ";//" + field.Name;
                switch (field.Type)
                {
                    case StructMetadata.FieldType.Byte:
                        sb.AppendLine("byte    _" + field.Offset + comment);
                        break;
                    case StructMetadata.FieldType.Word:
                        sb.AppendLine("ushort   _" + field.Offset + comment);
                        break;
                    case StructMetadata.FieldType.DWord:
                        sb.AppendLine("dword   _" + field.Offset + comment);
                        break;
                    case StructMetadata.FieldType.Pointer:
                        sb.AppendLine("void*   _" + field.Offset + comment);
                        break;
                }
            }

            sb.AppendLine("}; sizeof(" + structDef.DataSize + ")");
            return sb.ToString();
        }

        /// <summary>
        /// Format a No$gba memory map (".nmm") string, porting the WinForms
        /// <c>DumpStructSelectDialogForm.MakNMMString</c> header + per-field block.
        /// The header is the magic <c>1</c>, a <c>{Name} by FEBuilderGBA</c> title,
        /// the 0x-prefixed base address, the entry count, the block size, then two
        /// <c>NULL</c> lines and a blank line. Each field (in Fields order) emits
        /// its Name, decimal Offset, size (1/2/4), the type code <c>NEHU</c>, a
        /// <c>NULL</c> dropdown filename, and a blank line. <see cref="ExportToNMM"/>
        /// writes exactly this string to disk.
        /// </summary>
        /// <remarks>
        /// Documented fidelity gaps vs WinForms:
        /// (a) WinForms' per-field dropdown enumeration aux-files
        /// (<c>MakeNMMDropDownList</c>/<c>addFiles</c>) are widget-tree-driven and
        /// out of scope — all dropdown filenames emit <c>NULL</c>. The output stays
        /// a structurally valid No$gba memory map with correct field
        /// names/offsets/sizes/types; only the named value-dropdowns are absent.
        /// (b) The type-code display radix defaults to hex (<c>H</c>) — this is NOT
        /// a "WinForms default": WinForms derives H/D per-control from
        /// <c>NumericUpDown.Hexadecimal</c> (some UnitForm controls like B11/B25 are
        /// decimal), which the headless metadata does not carry. Signedness defaults
        /// to <c>U</c> (unsigned); the signed-byte variant has no headless analog.
        /// </remarks>
        public static string FormatNMM(ROM rom, TableDef table, StructMetadata.StructDef structDef)
        {
            var sb = new StringBuilder();
            sb.AppendLine("1");
            sb.AppendLine(structDef.Name + " by FEBuilderGBA");
            sb.AppendLine(U.To0xHexString(table.GetBaseAddress(rom)));
            sb.AppendLine(table.GetEntryCount(rom).ToString());
            sb.AppendLine(table.GetDataSize(rom).ToString());
            sb.AppendLine("NULL");
            sb.AppendLine("NULL");
            sb.AppendLine();

            foreach (var field in structDef.Fields)
            {
                sb.AppendLine(field.Name);
                sb.AppendLine(field.Offset.ToString()); // decimal index
                sb.AppendLine(field.Size.ToString());   // 1, 2, or 4
                // Type code: N + E(no dropdown) + H(hex radix default) + U(unsigned default).
                sb.AppendLine("NEHU");
                sb.AppendLine("NULL");                   // dropdown filename
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ====================================================================
        // GNU11 raw-byte-backed C struct/array formatter (#1939 Phase A). Produces a
        // self-contained devkitARM-GCC-compatible (arm-none-eabi-gcc, or host GCC) GNU11
        // translation unit per table: a one-byte-packed row struct, a 4-byte-aligned
        // array object, and matching count/type symbols. Every stride byte is emitted
        // exactly once — as a plain typed field, one arm of an anonymous packed union
        // covering a connected group of overlapping fields, a named metadata "gap"
        // member, or the runtime "trailing" member beyond the furthest declared field —
        // so the emitted struct's sizeof() always equals the resolved runtime stride
        // (asserted with _Static_assert) and no ROM byte is fabricated or dropped.
        // FormatTSV/FormatCSV/FormatEA/FormatJSON/FormatSTRUCT/FormatNMM above are
        // untouched by this addition (#1939 acceptance: byte-for-byte unchanged).
        // ====================================================================

        /// <summary>Kind of one resolved, non-overlapping GNU11 row-layout chunk (#1939).</summary>
        internal enum CLayoutChunkKind
        {
            /// <summary>A single metadata field with no overlap: emitted as a plain typed member.</summary>
            Field,
            /// <summary>2+ connected (contained or crossing) overlapping metadata fields:
            /// emitted as one anonymous packed union with promoted views and a raw arm.</summary>
            Overlap,
            /// <summary>An uncovered interior byte range before the furthest declared field end.</summary>
            Gap,
            /// <summary>The uncovered byte range from the furthest declared field end to the
            /// resolved runtime entry stride (e.g. a version whose runtime stride is larger
            /// than what the shared metadata file declares).</summary>
            Trailing,
        }

        /// <summary>
        /// One resolved, byte-exact slice of a GNU11 row layout. The ordered list returned
        /// by <see cref="BuildCLayout"/> partitions <c>[0, resolvedEntrySize)</c> with zero
        /// gaps and zero overlaps between chunks — every byte belongs to exactly one chunk.
        /// </summary>
        internal sealed class CLayoutChunk
        {
            public CLayoutChunkKind Kind;
            public uint Offset;
            public uint Length;
            /// <summary>Exactly 1 field for <see cref="CLayoutChunkKind.Field"/>, 2+ for
            /// <see cref="CLayoutChunkKind.Overlap"/>, empty for Gap/Trailing. Always sorted
            /// by ascending <see cref="StructMetadata.FieldDef.Offset"/>.</summary>
            public List<StructMetadata.FieldDef> Fields = new List<StructMetadata.FieldDef>();
        }

        /// <summary>
        /// Partition <paramref name="resolvedEntrySize"/> runtime-stride bytes into ordered,
        /// non-overlapping <see cref="CLayoutChunk"/>s from <paramref name="fields"/>'
        /// declared <c>[Offset, Offset+Size)</c> byte intervals using one ascending sweep:
        /// connected overlapping intervals — contained (one fully inside another, e.g. FE6
        /// <c>map_settings</c>' <c>BGM1</c> word aliasing its second byte as <c>Field15</c>)
        /// or crossing (partial overlap) — merge into a single <see cref="CLayoutChunkKind.Overlap"/>
        /// chunk; a lone, non-overlapping field's interval is a <see cref="CLayoutChunkKind.Field"/>
        /// chunk; any uncovered interior range before the furthest field end is a
        /// <see cref="CLayoutChunkKind.Gap"/> chunk; any uncovered range from the furthest
        /// field end up to <paramref name="resolvedEntrySize"/> is the single
        /// <see cref="CLayoutChunkKind.Trailing"/> chunk (empty when the runtime stride
        /// exactly matches the furthest declared field end).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="resolvedEntrySize"/> is smaller than the furthest declared field
        /// end — the resolved runtime stride can't even hold the declared metadata, so no
        /// valid GNU11 translation unit can be emitted (#1939 stride validation).
        /// </exception>
        internal static (List<CLayoutChunk> chunks, uint furthestFieldEnd) BuildCLayout(
            List<StructMetadata.FieldDef> fields, uint resolvedEntrySize)
        {
            var sorted = (fields ?? new List<StructMetadata.FieldDef>())
                .OrderBy(f => f.Offset)
                .ThenBy(f => f.Offset + (uint)f.Size)
                .ToList();

            uint furthestFieldEnd = 0;
            foreach (var f in sorted)
            {
                uint end = f.Offset + (uint)f.Size;
                if (end > furthestFieldEnd) furthestFieldEnd = end;
            }

            if (resolvedEntrySize < furthestFieldEnd)
            {
                throw new InvalidOperationException(
                    $"Resolved runtime entry stride 0x{resolvedEntrySize:X} is smaller than the " +
                    $"furthest declared metadata field end 0x{furthestFieldEnd:X}; refusing to emit " +
                    "a GNU11 struct that would truncate a declared field.");
            }

            var chunks = new List<CLayoutChunk>();
            uint cursor = 0;
            int idx = 0;
            while (idx < sorted.Count)
            {
                var f = sorted[idx];
                if (f.Offset > cursor)
                {
                    chunks.Add(new CLayoutChunk { Kind = CLayoutChunkKind.Gap, Offset = cursor, Length = f.Offset - cursor });
                    cursor = f.Offset;
                }

                uint groupStart = f.Offset;
                uint groupEnd = f.Offset + (uint)f.Size;
                var groupFields = new List<StructMetadata.FieldDef> { f };
                idx++;
                while (idx < sorted.Count && sorted[idx].Offset < groupEnd)
                {
                    uint end = sorted[idx].Offset + (uint)sorted[idx].Size;
                    if (end > groupEnd) groupEnd = end;
                    groupFields.Add(sorted[idx]);
                    idx++;
                }

                chunks.Add(new CLayoutChunk
                {
                    Kind = groupFields.Count == 1 ? CLayoutChunkKind.Field : CLayoutChunkKind.Overlap,
                    Offset = groupStart,
                    Length = groupEnd - groupStart,
                    Fields = groupFields,
                });
                cursor = groupEnd;
            }

            if (resolvedEntrySize > cursor)
            {
                chunks.Add(new CLayoutChunk { Kind = CLayoutChunkKind.Trailing, Offset = cursor, Length = resolvedEntrySize - cursor });
            }

            return (chunks, furthestFieldEnd);
        }

        /// <summary>Fixed-width GNU11 storage type for a struct field. Pointers stay raw
        /// <c>uint32_t</c> GBA addresses in this slice (#1939 — no symbol relocation yet).</summary>
        internal static string CTypeForField(StructMetadata.FieldType type)
        {
            return type switch
            {
                StructMetadata.FieldType.Byte => "uint8_t",
                StructMetadata.FieldType.Word => "uint16_t",
                StructMetadata.FieldType.DWord => "uint32_t",
                StructMetadata.FieldType.Pointer => "uint32_t",
                _ => "uint8_t",
            };
        }

        /// <summary>C11/GNU reserved words a sanitized identifier must not collide with.</summary>
        static readonly HashSet<string> CReservedIdentifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "auto","break","case","char","const","continue","default","do","double","else","enum",
            "extern","float","for","goto","if","inline","int","long","register","restrict","return",
            "short","signed","sizeof","static","struct","switch","typedef","union","unsigned","void",
            "volatile","while",
            "_Alignas","_Alignof","_Atomic","_Bool","_Complex","_Generic","_Imaginary","_Noreturn",
            "_Static_assert","_Thread_local",
            // GNU extension keywords that behave like reserved words in practice.
            "asm","typeof","__asm__","__asm","__typeof__","__typeof","__inline__","__inline",
            "__const__","__const","__volatile__","__volatile","__restrict__","__restrict",
            "__attribute__","__attribute","__extension__","__signed__","__signed","__real__","__imag__",
        };

        /// <summary>
        /// Deterministically sanitize an arbitrary string into a single valid C/GNU
        /// identifier: any character outside <c>[A-Za-z0-9_]</c> becomes <c>_</c>; a
        /// leading digit gets a <c>_</c> prefix; an empty result falls back to
        /// <c>_field</c>; and a result matching a C/GNU keyword (<see cref="CReservedIdentifiers"/>)
        /// gets a trailing <c>_</c> so it can never be emitted as a bare reserved word.
        /// Does not deduplicate — see <see cref="ClaimCIdentifier"/> for the separate,
        /// mandatory post-sanitization collision check.
        /// </summary>
        internal static string SanitizeCIdentifier(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "_field";

            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                sb.Append(ok ? c : '_');
            }

            string s = sb.ToString();
            if (s.Length == 0) s = "_field";
            if (s[0] >= '0' && s[0] <= '9') s = "_" + s;
            if (CReservedIdentifiers.Contains(s)) s = s + "_";
            return s;
        }

        /// <summary>
        /// Strictly validate a USER-SUPPLIED C data-symbol override (CLI <c>--c-symbol</c>,
        /// #1939 Phase B1): must be a well-formed C/GNU identifier — starts with a letter or
        /// underscore, every character is <c>[A-Za-z0-9_]</c> — and must NOT be a C/GNU
        /// reserved word (<see cref="CReservedIdentifiers"/>). Unlike
        /// <see cref="SanitizeCIdentifier"/> (used for ROM-metadata-derived names, which are
        /// silently repaired so a table/struct name always emits *something* valid), a
        /// user-supplied override is never silently rewritten — <paramref name="symbol"/> is
        /// used byte-for-byte verbatim when valid, and rejected outright with an actionable
        /// <paramref name="error"/> otherwise. Called by the CLI before any ROM load / output
        /// file creation, and re-checked by <see cref="FormatCData"/> itself so the guarantee
        /// holds for every caller, not just the CLI.
        /// </summary>
        public static bool TryValidateCSymbol(string symbol, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(symbol))
            {
                error = "symbol is empty";
                return false;
            }

            char first = symbol[0];
            bool firstOk = (first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_';
            if (!firstOk)
            {
                error = $"'{symbol}' must start with a letter or underscore, not '{first}'";
                return false;
            }

            foreach (char c in symbol)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                {
                    error = $"'{symbol}' contains an invalid character '{c}' (only letters, digits, and underscore are allowed)";
                    return false;
                }
            }

            if (CReservedIdentifiers.Contains(symbol))
            {
                error = $"'{symbol}' is a C/GNU reserved keyword and cannot be used as a symbol name";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Register <paramref name="name"/> as freshly emitted in the row struct's flat
        /// identifier scope (GNU anonymous unions/structs promote every arm's members up
        /// into the enclosing struct's namespace, so every field member, every union raw
        /// arm, and every gap/trailing member name must be globally unique within one row
        /// struct). Throws with both colliding contexts named if <paramref name="name"/>
        /// was already claimed — never silently renamed (#1939: "reject post-sanitization
        /// collisions").
        /// </summary>
        static void ClaimCIdentifier(Dictionary<string, string> used, string name, string context)
        {
            if (used.TryGetValue(name, out string existing))
            {
                throw new InvalidOperationException(
                    $"C identifier collision: '{name}' would be emitted for both {existing} and " +
                    $"{context}. Rename the conflicting field(s) in the struct metadata to avoid " +
                    "ambiguous GNU11 output.");
            }
            used[name] = context;
        }

        /// <summary>
        /// Neutralize a string for safe embedding inside a single-line <c>/* ... */</c> or
        /// <c>// ...</c> C comment: every character outside printable ASCII, or any of
        /// <c>*</c>/<c>/</c>/<c>\</c>, becomes <c>_</c>. This removes CR/LF and other control
        /// bytes (can't break out of a single line), removes every <c>*</c> and <c>/</c>
        /// (can't ever form a <c>*/</c> comment terminator), and removes every backslash
        /// (can't trigger C's phase-2 backslash-newline line splicing, which happens before
        /// comments are stripped and would otherwise let a trailing <c>\</c> merge the
        /// comment with the following source line). Used for the row-ordinal comment's
        /// <c>_Index</c> text and for field <c>Comment</c> text copied from struct metadata.
        /// </summary>
        internal static string EscapeCComment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                bool safe = c >= 0x20 && c <= 0x7E && c != '*' && c != '/' && c != '\\';
                sb.Append(safe ? c : '_');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Full-width hex row-ordinal designator: <c>0x000</c>, <c>0x001</c>, … — from the
        /// row's ordinal/list position, NEVER the (variable-width, possibly 2-digit)
        /// <c>_Index</c> column string. Width is at least 3 hex digits and grows so the
        /// highest ordinal in a <paramref name="rowCount"/>-row table is never truncated
        /// (e.g. row 300 of a 300-row table renders as <c>0x12B</c>, not a byte-truncated
        /// <c>0x2B</c>).
        /// </summary>
        internal static string FormatRowOrdinal(uint index, uint rowCount)
        {
            uint maxIndex = rowCount > 0 ? rowCount - 1 : 0;
            int width = Math.Max(3, maxIndex.ToString("X", CultureInfo.InvariantCulture).Length);
            return "0x" + index.ToString("X" + width, CultureInfo.InvariantCulture);
        }

        /// <summary>One member emitted in the row struct + its per-row initializer recipe.</summary>
        sealed class CResolvedMember
        {
            public CLayoutChunkKind Kind;
            public uint Offset;
            public uint Length;
            /// <summary>C member name used in the row initializer (the plain field member
            /// name for Field/Gap/Trailing, the union's raw-array arm name for Overlap).</summary>
            public string MemberName;
            /// <summary>Only set for <see cref="CLayoutChunkKind.Field"/>.</summary>
            public StructMetadata.FieldDef Field;
        }

        /// <summary>
        /// Format raw-byte-backed table rows as a self-contained GNU11 (devkitARM
        /// GCC/<c>arm-none-eabi-gcc</c>, or host GCC) C translation unit (#1939). Consumes
        /// exactly the shared <see cref="ExportTableRows"/> seam's typed field dictionaries
        /// (parsed here with the same strict, width-checked numeric parsing as JSON import —
        /// <see cref="TryNormalizeFieldValue"/> — so a malformed/overflowing field value is
        /// rejected rather than copied verbatim into C source) and raw per-row bytes (used
        /// verbatim, byte-for-byte, for every gap/trailing member and every union's raw arm).
        /// <list type="bullet">
        /// <item>a one-byte-packed <c>struct FEBuilder_&lt;StructName&gt;</c> row type built
        /// from <see cref="BuildCLayout"/>'s chunks: plain typed members, anonymous packed
        /// unions (one arm per connected overlapping field plus one raw byte-array arm) for
        /// overlap groups, and named raw gap/trailing members — every resolved stride byte
        /// appears exactly once;</item>
        /// <item><c>_Static_assert(sizeof(struct ...) == resolvedEntrySize, ...)</c>;</item>
        /// <item>a 4-byte-aligned <c>const struct ... gFEBuilder_&lt;table&gt;[N]</c> array
        /// (the GNU zero-length-array form with no initializer when <c>N == 0</c>) plus a
        /// matching <c>const uint32_t gFEBuilder_&lt;table&gt;Count</c>;</item>
        /// <item>full-width <c>[0x000]</c>-style row-ordinal comments (never the
        /// byte-truncatable <c>_Index</c> prefix) with the (comment-escaped) <c>_Index</c>
        /// text appended for human readability.</item>
        /// </list>
        /// <paramref name="dataSymbolOverride"/> (CLI <c>--c-symbol</c>, #1939 Phase B1),
        /// when non-null/empty, replaces the deterministic <c>gFEBuilder_&lt;table&gt;</c>
        /// data-array symbol verbatim; the count symbol always deterministically derives
        /// from whichever data symbol is actually emitted (<c>&lt;symbol&gt;Count</c>), and
        /// the row TYPE name (<c>struct FEBuilder_&lt;StructName&gt;</c>) is unaffected by
        /// the override either way. The override is re-validated here via
        /// <see cref="TryValidateCSymbol"/> — every caller gets the same "never silently
        /// sanitized" guarantee the CLI enforces up front.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="structDef"/> is null, or
        /// <paramref name="tableName"/> is null/empty.</exception>
        /// <exception cref="ArgumentException"><paramref name="dataSymbolOverride"/> is
        /// non-empty but not a valid, non-keyword C identifier.</exception>
        /// <exception cref="InvalidOperationException">the resolved stride is smaller than
        /// the furthest declared field end; a row's raw byte length does not equal the
        /// resolved stride; two members would collide on the same sanitized C identifier;
        /// or a row is missing/has an unparsable/overflowing value for a declared field.</exception>
        public static string FormatCData(
            List<(uint index, Dictionary<string, string> fields, byte[] raw)> rows,
            StructMetadata.StructDef structDef,
            string tableName,
            uint resolvedEntrySize,
            string dataSymbolOverride = null)
        {
            if (structDef == null) throw new ArgumentNullException(nameof(structDef));
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("tableName must not be null/empty.", nameof(tableName));
            if (!string.IsNullOrEmpty(dataSymbolOverride) && !TryValidateCSymbol(dataSymbolOverride, out string symbolError))
                throw new ArgumentException($"Invalid --c-symbol override '{dataSymbolOverride}': {symbolError}.", nameof(dataSymbolOverride));
            rows ??= new List<(uint, Dictionary<string, string>, byte[])>();

            foreach (var row in rows)
            {
                if (row.raw == null || (uint)row.raw.Length != resolvedEntrySize)
                {
                    throw new InvalidOperationException(
                        $"Row {FormatRowOrdinal(row.index, (uint)rows.Count)}: raw byte length " +
                        $"{(row.raw == null ? "null" : "0x" + row.raw.Length.ToString("X"))} does not match " +
                        $"the resolved runtime entry stride 0x{resolvedEntrySize:X}.");
                }
            }

            var (chunks, _) = BuildCLayout(structDef.Fields, resolvedEntrySize);

            string structTypeName = "FEBuilder_" + SanitizeCIdentifier(structDef.Name);
            // A validated --c-symbol override is used byte-for-byte verbatim (never
            // re-sanitized — TryValidateCSymbol above already guarantees it's a clean,
            // non-keyword C identifier); the count symbol always deterministically derives
            // from whichever data symbol is actually emitted.
            string dataSymbol = !string.IsNullOrEmpty(dataSymbolOverride)
                ? dataSymbolOverride
                : "gFEBuilder_" + SanitizeCIdentifier(tableName);
            string countSymbol = dataSymbol + "Count";

            var usedNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var members = new List<CResolvedMember>();
            var memberLines = new List<string>();
            int gapCounter = 0;
            int overlapCounter = 0;

            foreach (var chunk in chunks)
            {
                switch (chunk.Kind)
                {
                    case CLayoutChunkKind.Field:
                    {
                        var f = chunk.Fields[0];
                        string memberName = SanitizeCIdentifier(f.Name);
                        ClaimCIdentifier(usedNames, memberName, $"field '{f.Name}'");
                        string ctype = CTypeForField(f.Type);
                        string note = memberName == f.Name ? "" : $" (source name \"{EscapeCComment(f.Name)}\")";
                        string comment = string.IsNullOrEmpty(f.Comment) ? "" : " " + EscapeCComment(f.Comment);
                        memberLines.Add($"    {ctype} {memberName}; // 0x{chunk.Offset:X2}{note}{comment}");
                        members.Add(new CResolvedMember { Kind = CLayoutChunkKind.Field, Offset = chunk.Offset, Length = chunk.Length, MemberName = memberName, Field = f });
                        break;
                    }
                    case CLayoutChunkKind.Overlap:
                    {
                        memberLines.Add("    union {");
                        foreach (var f in chunk.Fields)
                        {
                            string innerName = SanitizeCIdentifier(f.Name);
                            string viewName = "as_" + innerName;
                            ClaimCIdentifier(usedNames, viewName, $"overlap view of field '{f.Name}'");
                            uint rel = f.Offset - chunk.Offset;
                            string ctype = CTypeForField(f.Type);
                            string comment = string.IsNullOrEmpty(f.Comment) ? "" : " " + EscapeCComment(f.Comment);
                            if (rel > 0)
                                memberLines.Add($"        struct __attribute__((packed)) {{ uint8_t _pad[{rel}]; {ctype} {innerName}; }} {viewName}; // 0x{f.Offset:X2}{comment}");
                            else
                                memberLines.Add($"        struct __attribute__((packed)) {{ {ctype} {innerName}; }} {viewName}; // 0x{f.Offset:X2}{comment}");
                        }
                        string rawArm = $"_overlap{overlapCounter}_raw";
                        overlapCounter++;
                        ClaimCIdentifier(usedNames, rawArm, $"overlap raw arm at 0x{chunk.Offset:X}");
                        memberLines.Add($"        uint8_t {rawArm}[{chunk.Length}]; // raw bytes backing every view above — only this arm is initialized");
                        memberLines.Add("    };");
                        members.Add(new CResolvedMember { Kind = CLayoutChunkKind.Overlap, Offset = chunk.Offset, Length = chunk.Length, MemberName = rawArm });
                        break;
                    }
                    case CLayoutChunkKind.Gap:
                    {
                        string gapName = $"_gap{gapCounter}";
                        gapCounter++;
                        ClaimCIdentifier(usedNames, gapName, $"metadata gap at 0x{chunk.Offset:X}");
                        memberLines.Add($"    uint8_t {gapName}[{chunk.Length}]; // 0x{chunk.Offset:X2}: unmapped metadata gap, raw ROM bytes");
                        members.Add(new CResolvedMember { Kind = CLayoutChunkKind.Gap, Offset = chunk.Offset, Length = chunk.Length, MemberName = gapName });
                        break;
                    }
                    case CLayoutChunkKind.Trailing:
                    {
                        string trailingName = "_trailing";
                        ClaimCIdentifier(usedNames, trailingName, "runtime trailing bytes");
                        memberLines.Add($"    uint8_t {trailingName}[{chunk.Length}]; // 0x{chunk.Offset:X2}: runtime trailing bytes beyond declared metadata, raw ROM bytes");
                        members.Add(new CResolvedMember { Kind = CLayoutChunkKind.Trailing, Offset = chunk.Offset, Length = chunk.Length, MemberName = trailingName });
                        break;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("/* Auto-generated by FEBuilderGBA (--export-data --format=c, #1939). GNU11");
            sb.AppendLine($" * translation unit for table \"{EscapeCComment(tableName)}\" (struct {EscapeCComment(structDef.Name)}).");
            sb.AppendLine(" * Requires GNU C extensions: __attribute__((packed)), __attribute__((aligned(4))),");
            sb.AppendLine(" * anonymous unions/structs, and GNU zero-length arrays. Compile with:");
            sb.AppendLine(" *   arm-none-eabi-gcc -std=gnu11 -Wall -Werror -c   (or host gcc for a smoke build)");
            sb.AppendLine(" * Read-only export: this is not a guaranteed C->ROM round-trip. Do not edit by hand.");
            sb.AppendLine(" */");
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine();
            sb.AppendLine($"struct __attribute__((packed)) {structTypeName} {{");
            foreach (var line in memberLines) sb.AppendLine(line);
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine($"_Static_assert(sizeof(struct {structTypeName}) == 0x{resolvedEntrySize:X}, \"sizeof(struct {structTypeName}) must equal the resolved runtime entry stride\");");
            sb.AppendLine();

            if (rows.Count == 0)
            {
                sb.AppendLine($"const struct {structTypeName} {dataSymbol}[0] __attribute__((aligned(4)));");
            }
            else
            {
                sb.AppendLine($"const struct {structTypeName} {dataSymbol}[{rows.Count}] __attribute__((aligned(4))) = {{");
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    string ordinal = FormatRowOrdinal(row.index, (uint)rows.Count);
                    string indexText = row.fields.TryGetValue("_Index", out string idxVal) ? idxVal : "";
                    var parts = new List<string>(members.Count);
                    foreach (var m in members)
                    {
                        parts.Add(BuildCInitializer(m, row.fields, row.raw, ordinal));
                    }
                    sb.AppendLine($"    /* [{ordinal}] {EscapeCComment(indexText)} */ {{ {string.Join(", ", parts)} }},");
                }
                sb.AppendLine("};");
            }
            sb.AppendLine();
            sb.AppendLine($"const uint32_t {countSymbol} = {rows.Count};");

            return sb.ToString();
        }

        /// <summary>Build one <c>.member = value</c> designated-initializer fragment for a
        /// resolved row member. Field members are strictly parsed/width-checked (reusing
        /// <see cref="TryNormalizeFieldValue"/>) and re-emitted as a fresh hex literal (never
        /// a copied raw token); Overlap/Gap/Trailing members are emitted as a raw
        /// <c>{ 0x.., 0x.., ... }</c> byte-array literal sliced directly from the row's raw
        /// bytes.</summary>
        static string BuildCInitializer(CResolvedMember m, Dictionary<string, string> fields, byte[] raw, string ordinal)
        {
            if (m.Kind == CLayoutChunkKind.Field)
            {
                var f = m.Field;
                if (!fields.TryGetValue(f.Name, out string rawValue))
                {
                    throw new InvalidOperationException($"Row {ordinal}: missing a value for field '{f.Name}'.");
                }
                if (!TryNormalizeFieldValue(rawValue, f.Type, out string canonical, out string error))
                {
                    throw new InvalidOperationException($"Row {ordinal}, field '{f.Name}': {error}.");
                }
                uint val = uint.Parse(canonical.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int width = f.Type switch
                {
                    StructMetadata.FieldType.Byte => 2,
                    StructMetadata.FieldType.Word => 4,
                    _ => 8,
                };
                return $".{m.MemberName} = 0x{val.ToString("X" + width, CultureInfo.InvariantCulture)}";
            }

            var bytes = new StringBuilder();
            for (uint b = 0; b < m.Length; b++)
            {
                if (b > 0) bytes.Append(", ");
                bytes.Append("0x").Append(raw[m.Offset + b].ToString("X2", CultureInfo.InvariantCulture));
            }
            return $".{m.MemberName} = {{ {bytes} }}";
        }

        /// <summary>
        /// Export a ROM table to a self-contained GNU11 C translation unit (see
        /// <see cref="FormatCData"/>). Reads rows via the shared <see cref="ExportTableRows"/>
        /// seam and resolves the runtime entry stride from <c>table.GetDataSize(rom)</c>.
        /// Written UTF-8 without a BOM (a leading BOM is not valid at the start of a C
        /// translation unit for every compiler). <paramref name="dataSymbolOverride"/> is
        /// the optional CLI <c>--c-symbol</c> passthrough — see <see cref="FormatCData"/>.
        /// </summary>
        public static void ExportToCData(ROM rom, TableDef table, StructMetadata.StructDef structDef, string outputPath, string dataSymbolOverride = null)
        {
            if (rom?.RomInfo == null) throw new ArgumentNullException(nameof(rom));
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (structDef == null) throw new ArgumentNullException(nameof(structDef));

            var rows = ExportTableRows(rom, table, structDef);
            uint resolvedEntrySize = table.GetDataSize(rom);
            string content = FormatCData(rows, structDef, table.Name, resolvedEntrySize, dataSymbolOverride);
            File.WriteAllText(outputPath, content, Utf8NoBom);
        }


        /// <summary>
        /// Export table data to a TSV file. Output is byte-identical to
        /// <see cref="FormatTSV"/>.
        /// </summary>
        public static void ExportToTSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatTSV(entries, structDef), Encoding.UTF8);
        }

        /// <summary>
        /// Export table data to a CSV file (RFC 4180 compliant). Output is
        /// byte-identical to <see cref="FormatCSV"/>.
        /// </summary>
        public static void ExportToCSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatCSV(entries, structDef), Encoding.UTF8);
        }

        /// <summary>
        /// Export table data to EA (Event Assembler) #define format.
        /// Each field becomes: #define {TableName}_{HexIndex}_{FieldName} 0xVALUE.
        /// Output is byte-identical to <see cref="FormatEA"/>.
        /// </summary>
        public static void ExportToEA(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatEA(entries, structDef), Encoding.UTF8);
        }

        /// <summary>
        /// Export table data to a JSON file (array of objects; see <see cref="FormatJSON"/>
        /// for the exact shape). Written UTF-8 without a BOM — JSON has no BOM convention
        /// and this keeps the file directly consumable by strict JSON parsers/LLM tooling.
        /// Output is byte-identical to <see cref="FormatJSON"/>.
        /// </summary>
        public static void ExportToJSON(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatJSON(entries, structDef), Utf8NoBom);
        }

        /// <summary>UTF-8 WITHOUT a BOM. The .h/.nmm files are consumed by
        /// external tools (a C compiler / No$gba), and a leading BOM corrupts
        /// the strict ".nmm" magic line ("1") and pollutes the .h; this also
        /// matches the WinForms U.WriteAllText default (UTF-8 no-BOM).</summary>
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Export the struct layout to a C-header (".h") file. Output is
        /// byte-identical to <see cref="FormatSTRUCT"/> (UTF-8, no BOM).
        /// </summary>
        public static void ExportToSTRUCT(StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatSTRUCT(structDef), Utf8NoBom);
        }

        /// <summary>
        /// Export the struct layout to a No$gba memory map (".nmm") file. Output
        /// is byte-identical to <see cref="FormatNMM"/> (UTF-8, no BOM — the
        /// strict ".nmm" magic line must be the very first byte).
        /// </summary>
        public static void ExportToNMM(ROM rom, TableDef table, StructMetadata.StructDef structDef, string outputPath)
        {
            File.WriteAllText(outputPath, FormatNMM(rom, table, structDef), Utf8NoBom);
        }

        /// <summary>RFC 4180 CSV quoting: double-quote fields containing commas, quotes, or newlines.</summary>
        internal static string CsvQuote(string value)
        {
            if (value == null) return "\"\"";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        /// <summary>
        /// Import table data from a TSV file.
        /// Returns list of (index, fieldName→value) entries.
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ImportFromTSV(string inputPath, StructMetadata.StructDef structDef)
        {
            var result = new List<(int, Dictionary<string, string>)>();
            if (!File.Exists(inputPath)) return result;

            string[] lines = File.ReadAllLines(inputPath, Encoding.UTF8);
            if (lines.Length < 2) return result;

            // Parse header to get column mapping
            string[] headers = ParseTSVLine(lines[0]);
            if (headers.Length < 2) return result;

            for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] cols = ParseTSVLine(line);
                if (cols.Length < 1) continue;

                // Parse index from first column: "0xNN name" → extract hex index
                int entryIndex = ParseIndexFromFirstColumn(cols[0]);
                if (entryIndex < 0) continue;

                var fields = new Dictionary<string, string>();
                for (int c = 1; c < headers.Length && c < cols.Length; c++)
                {
                    fields[headers[c]] = cols[c];
                }

                result.Add((entryIndex, fields));
            }

            return result;
        }

        /// <summary>Parse a TSV line into columns, handling tabs correctly.</summary>
        public static string[] ParseTSVLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return Array.Empty<string>();
            return line.Split('\t');
        }

        /// <summary>Parse entry index from first column (e.g., "0x0A Eirika" → 10).</summary>
        static int ParseIndexFromFirstColumn(string col)
        {
            if (string.IsNullOrWhiteSpace(col)) return -1;
            string trimmed = col.Trim();

            // Extract hex portion (up to first space)
            int space = trimmed.IndexOf(' ');
            string hexPart = space >= 0 ? trimmed.Substring(0, space) : trimmed;

            try
            {
                return (int)U.atoi0x(hexPart);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Write imported data back to ROM.
        /// Returns the number of entries written.
        /// </summary>
        public static int WriteTable(ROM rom, TableDef table, StructMetadata.StructDef structDef,
            List<(int index, Dictionary<string, string> fields)> entries)
        {
            if (rom?.RomInfo == null || table == null || structDef == null) return 0;

            uint baseAddr = table.GetBaseAddress(rom);
            uint dataSize = table.GetDataSize(rom);
            uint count = table.GetEntryCount(rom);

            if (baseAddr == 0 || baseAddr == U.NOT_FOUND || count == 0) return 0;

            int written = 0;

            foreach (var (index, fields) in entries)
            {
                if (index < 0 || (uint)index >= count) continue;

                uint entryAddr = baseAddr + ((uint)index * dataSize);
                if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) continue;

                foreach (var field in structDef.Fields)
                {
                    if (!fields.TryGetValue(field.Name, out string valStr)) continue;

                    uint val = U.atoi0x(valStr);
                    structDef.WriteField(rom, entryAddr, field, val);
                }

                written++;
            }

            return written;
        }

        /// <summary>
        /// Result of a data round-trip validation.
        /// </summary>
        public class DataRoundTripResult
        {
            public string TableName { get; set; }
            public int TotalEntries { get; set; }
            public int MatchCount { get; set; }
            public int MismatchCount { get; set; }
            public List<(int index, string fieldName, string before, string after)> Mismatches { get; set; }
                = new List<(int, string, string, string)>();
            public bool IsLossless => MismatchCount == 0;
        }

        /// <summary>
        /// Validate round-trip: export → import → write → re-export → diff.
        /// The ROM is modified in-place (caller should work on a copy).
        /// </summary>
        public static DataRoundTripResult ValidateRoundTrip(ROM rom, string tableName)
        {
            var table = GetTable(tableName);
            if (table == null) throw new ArgumentException($"Unknown table: {tableName}");

            var structDef = LoadStructDef(rom, table);
            if (structDef == null) throw new InvalidOperationException($"Could not load struct definition for table: {tableName}");

            // Phase 1: export
            var export1 = ExportTable(rom, table, structDef);

            // Phase 2: write same data back
            var importEntries = new List<(int index, Dictionary<string, string> fields)>();
            for (int i = 0; i < export1.Count; i++)
            {
                var fields = new Dictionary<string, string>(export1[i]);
                fields.Remove("_Index");
                importEntries.Add((i, fields));
            }
            WriteTable(rom, table, structDef, importEntries);

            // Phase 3: re-export
            var export2 = ExportTable(rom, table, structDef);

            // Phase 4: compare
            var result = new DataRoundTripResult
            {
                TableName = tableName,
                TotalEntries = export1.Count,
            };

            int minCount = Math.Min(export1.Count, export2.Count);
            for (int i = 0; i < minCount; i++)
            {
                bool allMatch = true;
                foreach (var field in structDef.Fields)
                {
                    string v1 = export1[i].TryGetValue(field.Name, out var s1) ? s1 : "";
                    string v2 = export2[i].TryGetValue(field.Name, out var s2) ? s2 : "";
                    if (v1 != v2)
                    {
                        allMatch = false;
                        result.Mismatches.Add((i, field.Name, v1, v2));
                    }
                }
                if (allMatch)
                    result.MatchCount++;
                else
                    result.MismatchCount++;
            }

            // Handle count differences
            for (int i = minCount; i < Math.Max(export1.Count, export2.Count); i++)
            {
                result.MismatchCount++;
                result.Mismatches.Add((i, "(count)", i < export1.Count ? "present" : "missing",
                    i < export2.Count ? "present" : "missing"));
            }

            return result;
        }

        /// <summary>
        /// Validate round-trip for all registered tables.
        /// </summary>
        public static List<DataRoundTripResult> ValidateRoundTripAll(ROM rom)
        {
            var results = new List<DataRoundTripResult>();
            foreach (var name in GetTableNames())
            {
                try
                {
                    results.Add(ValidateRoundTrip(rom, name));
                }
                catch (Exception ex)
                {
                    results.Add(new DataRoundTripResult
                    {
                        TableName = name,
                        MismatchCount = 1,
                        Mismatches = { (-1, "error", ex.Message, "") },
                    });
                }
            }
            return results;
        }
    }
}
