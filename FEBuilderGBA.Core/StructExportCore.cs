using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        /// Export a ROM table to a list of (index, fieldName→value) entries.
        /// </summary>
        public static List<Dictionary<string, string>> ExportTable(ROM rom, TableDef table, StructMetadata.StructDef structDef)
        {
            var result = new List<Dictionary<string, string>>();
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
                entry["_Index"] = U.To0xHexString((byte)i) + " " + name;

                // All fields as hex values
                foreach (var field in structDef.Fields)
                {
                    uint val = structDef.ReadField(rom, entryAddr, field);
                    entry[field.Name] = FormatFieldValue(val, field);
                }

                result.Add(entry);
            }

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
        /// Export table data to a TSV file.
        /// </summary>
        public static void ExportToTSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
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

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
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
