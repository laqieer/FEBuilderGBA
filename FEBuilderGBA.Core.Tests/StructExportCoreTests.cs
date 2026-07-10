using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class StructExportCoreTests
    {
        static string FindRepoRoot()
        {
            // Walk up from test assembly to find the repo root (where config/ exists)
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "config", "data")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        static void EnsureBaseDirectory()
        {
            if (CoreState.BaseDirectory != null) return;
            string root = FindRepoRoot();
            if (root != null)
                CoreState.BaseDirectory = root;
        }

        static StructMetadata.StructDef LoadMetadataStruct(string fileName, string structName)
        {
            string root = FindRepoRoot();
            Assert.False(string.IsNullOrEmpty(root), "Repository root with config/data must be available.");

            string path = Path.Combine(root, "config", "data", fileName);
            Assert.True(File.Exists(path), $"Metadata file not found: {path}");

            var metadata = new StructMetadata();
            metadata.LoadFromFile(path);
            var structDef = metadata.GetStruct(structName);
            Assert.NotNull(structDef);
            return structDef;
        }

        static List<(string name, uint offset, StructMetadata.FieldType type)> CommonUnitLayout()
        {
            return new List<(string, uint, StructMetadata.FieldType)>
            {
                ("NameTextID", 0x00, StructMetadata.FieldType.Word),
                ("DescTextID", 0x02, StructMetadata.FieldType.Word),
                ("UnitNumber", 0x04, StructMetadata.FieldType.Byte),
                ("ClassID", 0x05, StructMetadata.FieldType.Byte),
                ("PortraitID", 0x06, StructMetadata.FieldType.Word),
                ("MapFace", 0x08, StructMetadata.FieldType.Byte),
                ("Affinity", 0x09, StructMetadata.FieldType.Byte),
                ("SortOrder", 0x0A, StructMetadata.FieldType.Byte),
                ("Level", 0x0B, StructMetadata.FieldType.Byte),
                ("BaseHP", 0x0C, StructMetadata.FieldType.Byte),
                ("BasePow", 0x0D, StructMetadata.FieldType.Byte),
                ("BaseSkl", 0x0E, StructMetadata.FieldType.Byte),
                ("BaseSpd", 0x0F, StructMetadata.FieldType.Byte),
                ("BaseDef", 0x10, StructMetadata.FieldType.Byte),
                ("BaseRes", 0x11, StructMetadata.FieldType.Byte),
                ("BaseLck", 0x12, StructMetadata.FieldType.Byte),
                ("BaseCon", 0x13, StructMetadata.FieldType.Byte),
                ("SwordRank", 0x14, StructMetadata.FieldType.Byte),
                ("LanceRank", 0x15, StructMetadata.FieldType.Byte),
                ("AxeRank", 0x16, StructMetadata.FieldType.Byte),
                ("BowRank", 0x17, StructMetadata.FieldType.Byte),
                ("StaffRank", 0x18, StructMetadata.FieldType.Byte),
                ("AnimaRank", 0x19, StructMetadata.FieldType.Byte),
                ("LightRank", 0x1A, StructMetadata.FieldType.Byte),
                ("DarkRank", 0x1B, StructMetadata.FieldType.Byte),
                ("GrowthHP", 0x1C, StructMetadata.FieldType.Byte),
                ("GrowthPow", 0x1D, StructMetadata.FieldType.Byte),
                ("GrowthSkl", 0x1E, StructMetadata.FieldType.Byte),
                ("GrowthSpd", 0x1F, StructMetadata.FieldType.Byte),
                ("GrowthDef", 0x20, StructMetadata.FieldType.Byte),
                ("GrowthRes", 0x21, StructMetadata.FieldType.Byte),
                ("GrowthLck", 0x22, StructMetadata.FieldType.Byte),
            };
        }

        static void AssertUnitLayout(
            string fileName,
            string structName,
            uint expectedSize,
            List<(string name, uint offset, StructMetadata.FieldType type)> expected)
        {
            var structDef = LoadMetadataStruct(fileName, structName);
            Assert.Equal(expectedSize, structDef.DataSize);
            Assert.Equal(expected.Count, structDef.Fields.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].name, structDef.Fields[i].Name);
                Assert.Equal(expected[i].offset, structDef.Fields[i].Offset);
                Assert.Equal(expected[i].type, structDef.Fields[i].Type);
            }
        }

        [Fact]
        public void GetTableNames_ReturnsAllRegisteredTables()
        {
            var names = new List<string>(StructExportCore.GetTableNames());
            // Core tables
            Assert.Contains("units", names);
            Assert.Contains("classes", names);
            Assert.Contains("items", names);
            // Portrait tables
            Assert.Contains("portraits", names);
            // Sound tables
            Assert.Contains("sound_room", names);
            Assert.Contains("sound_boss_bgm", names);
            // Support tables
            Assert.Contains("support_units", names);
            Assert.Contains("support_talks", names);
            Assert.Contains("support_attributes", names);
            // Event tables
            Assert.Contains("event_haiku", names);
            Assert.Contains("event_battle_talk", names);
            Assert.Contains("event_force_sortie", names);
            // World map tables
            Assert.Contains("worldmap_points", names);
            Assert.Contains("worldmap_paths", names);
            Assert.Contains("worldmap_bgm", names);
            // Misc tables
            Assert.Contains("map_settings", names);
            Assert.Contains("link_arena_deny", names);
            Assert.Contains("cc_branch", names);
            Assert.Contains("menu_definitions", names);
            // Universal tables 2
            Assert.Contains("item_weapon_triangle", names);
            Assert.Contains("map_exit_points", names);
            Assert.Contains("ai_map_settings", names);
            Assert.Contains("ai_perform_items", names);
            Assert.Contains("ai_perform_staff", names);
            Assert.Contains("ai_steal_items", names);
            Assert.Contains("ai_targets", names);
            Assert.Contains("generic_enemy_portraits", names);
            Assert.Contains("status_options", names);
            // Ending tables
            Assert.Contains("ed_retreat", names);
            Assert.Contains("ed_epithet", names);
            Assert.Contains("ed_epilogue_a", names);
            Assert.Contains("ed_epilogue_b", names);
            Assert.Contains("ed_epilogue_c", names);
            // OP tables
            Assert.Contains("op_class_demo", names);
            Assert.Contains("op_class_font", names);
            Assert.Contains("op_prologue", names);
            Assert.Contains("class_alpha_names", names);
            // FE8 tables
            Assert.Contains("summon_units", names);
            Assert.Contains("summons_demon_king", names);
            Assert.Contains("monster_probability", names);
            // Total count
            Assert.True(names.Count >= 40, $"Expected at least 40 tables, got {names.Count}");
        }

        [Fact]
        public void GetTable_ReturnsNull_ForUnknown()
        {
            Assert.Null(StructExportCore.GetTable("nonexistent"));
        }

        [Fact]
        public void GetTable_ReturnsDef_ForKnownTable()
        {
            var units = StructExportCore.GetTable("units");
            Assert.NotNull(units);
            Assert.Equal("units", units.Name);
        }

        [Fact]
        public void GetTable_CaseInsensitive()
        {
            Assert.NotNull(StructExportCore.GetTable("Units"));
            Assert.NotNull(StructExportCore.GetTable("CLASSES"));
            Assert.NotNull(StructExportCore.GetTable("Items"));
        }

        [Fact]
        public void ParseTSVLine_SplitsByTabs()
        {
            string[] cols = StructExportCore.ParseTSVLine("A\tB\tC");
            Assert.Equal(3, cols.Length);
            Assert.Equal("A", cols[0]);
            Assert.Equal("B", cols[1]);
            Assert.Equal("C", cols[2]);
        }

        [Fact]
        public void ParseTSVLine_EmptyString_ReturnsEmpty()
        {
            Assert.Empty(StructExportCore.ParseTSVLine(""));
            Assert.Empty(StructExportCore.ParseTSVLine(null));
        }

        [Fact]
        public void ExportToTSV_ProducesValidFormat()
        {
            var structDef = CreateTestStructDef();
            var entries = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "_Index", "0x00 Test" },
                    { "FieldA", "0x01" },
                    { "FieldB", "0x0002" },
                },
                new Dictionary<string, string>
                {
                    { "_Index", "0x01 Second" },
                    { "FieldA", "0x03" },
                    { "FieldB", "0x0004" },
                },
            };

            string tmpFile = Path.GetTempFileName();
            try
            {
                StructExportCore.ExportToTSV(entries, structDef, tmpFile);

                string[] lines = File.ReadAllLines(tmpFile);
                Assert.True(lines.Length >= 3, "Expected header + 2 data lines");

                // Header
                Assert.Equal("Index\tFieldA\tFieldB", lines[0]);

                // Data
                Assert.StartsWith("0x00 Test\t0x01\t0x0002", lines[1]);
                Assert.StartsWith("0x01 Second\t0x03\t0x0004", lines[2]);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void ImportFromTSV_ParsesExportedData()
        {
            var structDef = CreateTestStructDef();
            string tsvContent = "Index\tFieldA\tFieldB\n0x00 Test\t0x01\t0x0002\n0x01 Second\t0x03\t0x0004\n";

            string tmpFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpFile, tsvContent);

                var entries = StructExportCore.ImportFromTSV(tmpFile, structDef);
                Assert.Equal(2, entries.Count);

                Assert.Equal(0, entries[0].index);
                Assert.Equal("0x01", entries[0].fields["FieldA"]);
                Assert.Equal("0x0002", entries[0].fields["FieldB"]);

                Assert.Equal(1, entries[1].index);
                Assert.Equal("0x03", entries[1].fields["FieldA"]);
                Assert.Equal("0x0004", entries[1].fields["FieldB"]);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void ImportFromTSV_NonExistentFile_ReturnsEmpty()
        {
            var structDef = CreateTestStructDef();
            var entries = StructExportCore.ImportFromTSV("/nonexistent/file.tsv", structDef);
            Assert.Empty(entries);
        }

        [Fact]
        public void ExportImport_RoundTrip_IsLossless()
        {
            var structDef = CreateTestStructDef();
            var original = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "_Index", "0x00 Alpha" },
                    { "FieldA", "0xFF" },
                    { "FieldB", "0x1234" },
                },
            };

            string tmpFile = Path.GetTempFileName();
            try
            {
                StructExportCore.ExportToTSV(original, structDef, tmpFile);
                var imported = StructExportCore.ImportFromTSV(tmpFile, structDef);

                Assert.Single(imported);
                Assert.Equal(0, imported[0].index);
                Assert.Equal("0xFF", imported[0].fields["FieldA"]);
                Assert.Equal("0x1234", imported[0].fields["FieldB"]);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void WriteField_And_ReadField_RoundTrip()
        {
            // Create a minimal ROM-like structure
            var structDef = new StructMetadata.StructDef
            {
                Name = "Test",
                DataSize = 8,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef { Name = "ByteF", Offset = 0, Type = StructMetadata.FieldType.Byte },
                    new StructMetadata.FieldDef { Name = "WordF", Offset = 2, Type = StructMetadata.FieldType.Word },
                    new StructMetadata.FieldDef { Name = "DWordF", Offset = 4, Type = StructMetadata.FieldType.DWord },
                }
            };

            // Create a fake ROM with enough data
            byte[] data = new byte[256];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            // Write fields
            structDef.WriteField(rom, 0, structDef.Fields[0], 0x42);
            structDef.WriteField(rom, 0, structDef.Fields[1], 0x1234);
            structDef.WriteField(rom, 0, structDef.Fields[2], 0xDEADBEEF);

            // Read back
            Assert.Equal(0x42u, structDef.ReadField(rom, 0, structDef.Fields[0]));
            Assert.Equal(0x1234u, structDef.ReadField(rom, 0, structDef.Fields[1]));
            Assert.Equal(0xDEADBEEFu, structDef.ReadField(rom, 0, structDef.Fields[2]));
        }

        [Fact]
        public void ExportTable_NullRom_ReturnsEmpty()
        {
            var table = StructExportCore.GetTable("units");
            var structDef = CreateTestStructDef();
            var result = StructExportCore.ExportTable(null, table, structDef);
            Assert.Empty(result);
        }

        [Fact]
        public void ExportTable_IndexAboveByteRange_RoundTripsWithoutAliasing()
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("wide-index.gba", new byte[0x1000000], "BE8E01"));

            var table = new StructExportCore.TableDef
            {
                Name = "wide_index",
                GetBaseAddress = _ => 0x200,
                GetDataSize = _ => 1,
                GetEntryCount = _ => 257,
            };
            var structDef = new StructMetadata.StructDef
            {
                Name = "WideIndex",
                DataSize = 1,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef
                    {
                        Name = "Value",
                        Offset = 0,
                        Type = StructMetadata.FieldType.Byte,
                    },
                },
            };

            var entries = StructExportCore.ExportTable(rom, table, structDef);
            Assert.Equal(257, entries.Count);
            Assert.Equal("0xFF ", entries[255]["_Index"]);
            Assert.Equal("0x0100 ", entries[256]["_Index"]);

            string json = StructExportCore.FormatJSON(entries, structDef);
            var parsed = StructExportCore.ParseAndValidateJSON(json, structDef, 257);
            Assert.Equal(256, parsed[256].index);
            Assert.Contains("\"Index\": \"0x0100 \"", json);
            Assert.Contains("0x0100 ", StructExportCore.FormatTSV(entries, structDef));
            Assert.Contains("#define WideIndex_0x0100_Value 0x00",
                StructExportCore.FormatEA(entries, structDef));
        }

        [Fact]
        public void UnitMetadata_FE6_MatchesCanonicalLayout()
        {
            var expected = CommonUnitLayout();
            expected.AddRange(new (string, uint, StructMetadata.FieldType)[]
            {
                ("Unknown23", 0x23, StructMetadata.FieldType.Byte),
                ("Unknown24", 0x24, StructMetadata.FieldType.Byte),
                ("Unknown25", 0x25, StructMetadata.FieldType.Byte),
                ("Unknown26", 0x26, StructMetadata.FieldType.Byte),
                ("Unknown27", 0x27, StructMetadata.FieldType.Byte),
                ("Ability1", 0x28, StructMetadata.FieldType.Byte),
                ("Ability2", 0x29, StructMetadata.FieldType.Byte),
                ("Ability3", 0x2A, StructMetadata.FieldType.Byte),
                ("Ability4", 0x2B, StructMetadata.FieldType.Byte),
                ("SupportPointer", 0x2C, StructMetadata.FieldType.Pointer),
            });

            AssertUnitLayout("struct_unit_fe6.txt", "Unit_FE6", 0x30, expected);
        }

        [Fact]
        public void UnitMetadata_FE78_MatchesCanonicalLayout()
        {
            var expected = CommonUnitLayout();
            expected.AddRange(new (string, uint, StructMetadata.FieldType)[]
            {
                ("LowerClassPalette", 0x23, StructMetadata.FieldType.Byte),
                ("UpperClassPalette", 0x24, StructMetadata.FieldType.Byte),
                ("LowerClassAnime", 0x25, StructMetadata.FieldType.Byte),
                ("UpperClassAnime", 0x26, StructMetadata.FieldType.Byte),
                ("Unknown27", 0x27, StructMetadata.FieldType.Byte),
                ("Ability1", 0x28, StructMetadata.FieldType.Byte),
                ("Ability2", 0x29, StructMetadata.FieldType.Byte),
                ("Ability3", 0x2A, StructMetadata.FieldType.Byte),
                ("Ability4", 0x2B, StructMetadata.FieldType.Byte),
                ("SupportPointer", 0x2C, StructMetadata.FieldType.Pointer),
                ("TalkGroup", 0x30, StructMetadata.FieldType.Byte),
                ("Unknown31", 0x31, StructMetadata.FieldType.Byte),
                ("Unknown32", 0x32, StructMetadata.FieldType.Byte),
                ("Unknown33", 0x33, StructMetadata.FieldType.Byte),
            });

            AssertUnitLayout("struct_unit_fe78.txt", "Unit_FE78", 0x34, expected);
        }

        [Theory]
        [InlineData("struct_unit_fe6.txt", "Unit_FE6", "Affinity", 0x09, 0x30)]
        [InlineData("struct_unit_fe6.txt", "Unit_FE6", "BasePow", 0x0D, 0x30)]
        [InlineData("struct_unit_fe78.txt", "Unit_FE78", "Affinity", 0x09, 0x34)]
        [InlineData("struct_unit_fe78.txt", "Unit_FE78", "BasePow", 0x0D, 0x34)]
        public void UnitMetadata_PartialWrite_ChangesOnlyNamedByte(
            string fileName,
            string structName,
            string fieldName,
            int expectedOffset,
            int dataSize)
        {
            var structDef = LoadMetadataStruct(fileName, structName);
            var field = Assert.Single(structDef.Fields, f => f.Name == fieldName);
            Assert.Equal((uint)expectedOffset, field.Offset);

            const int baseAddress = 0x200;
            byte[] data = new byte[0x1000000];
            for (int i = 0; i < dataSize; i++)
                data[baseAddress + i] = 0xA5;

            var rom = new ROM();
            Assert.True(rom.LoadLow("unit-layout.gba", data, "BE8E01"));
            var table = new StructExportCore.TableDef
            {
                Name = "unit_layout",
                GetBaseAddress = _ => baseAddress,
                GetDataSize = _ => (uint)dataSize,
                GetEntryCount = _ => 1,
            };
            var entries = new List<(int index, Dictionary<string, string> fields)>
            {
                (0, new Dictionary<string, string> { [fieldName] = "0x5A" }),
            };

            Assert.Equal(1, StructExportCore.WriteTable(rom, table, structDef, entries));
            for (int i = 0; i < dataSize; i++)
            {
                byte expected = i == expectedOffset ? (byte)0x5A : (byte)0xA5;
                Assert.Equal(expected, rom.Data[baseAddress + i]);
            }
        }

        [Theory]
        [InlineData("portraits", "struct_portrait_fe6.txt", "Portrait_FE6")]
        [InlineData("portraits", "struct_portrait_fe78.txt", "Portrait_FE78")]
        [InlineData("sound_room", "struct_soundroom_fe6.txt", "SoundRoom_FE6")]
        [InlineData("sound_room", "struct_soundroom_fe78.txt", "SoundRoom_FE78")]
        [InlineData("support_units", "struct_support_unit_fe6.txt", "SupportUnit_FE6")]
        [InlineData("support_units", "struct_support_unit_fe78.txt", "SupportUnit_FE78")]
        [InlineData("support_talks", "struct_support_talk_fe6.txt", "SupportTalk_FE6")]
        [InlineData("support_talks", "struct_support_talk_fe78.txt", "SupportTalk_FE78")]
        [InlineData("support_attributes", "struct_support_attribute.txt", "SupportAttribute")]
        [InlineData("event_haiku", "struct_event_haiku_fe6.txt", "EventHaiku_FE6")]
        [InlineData("event_haiku", "struct_event_haiku_fe78.txt", "EventHaiku_FE78")]
        [InlineData("event_battle_talk", "struct_event_battle_talk_fe6.txt", "EventBattleTalk_FE6")]
        [InlineData("event_battle_talk", "struct_event_battle_talk_fe78.txt", "EventBattleTalk_FE78")]
        [InlineData("event_force_sortie", "struct_event_force_sortie.txt", "EventForceSortie")]
        [InlineData("worldmap_points", "struct_worldmap_point.txt", "WorldMapPoint")]
        [InlineData("worldmap_paths", "struct_worldmap_path.txt", "WorldMapPath")]
        [InlineData("worldmap_bgm", "struct_worldmap_bgm.txt", "WorldMapBGM")]
        [InlineData("sound_boss_bgm", "struct_sound_boss_bgm.txt", "SoundBossBGM")]
        [InlineData("link_arena_deny", "struct_link_arena_deny.txt", "LinkArenaDeny")]
        [InlineData("cc_branch", "struct_cc_branch.txt", "CCBranch")]
        [InlineData("menu_definitions", "struct_menu_definition.txt", "MenuDefinition")]
        [InlineData("map_settings", "struct_map_setting_fe6.txt", "MapSetting_FE6")]
        [InlineData("map_settings", "struct_map_setting_fe78.txt", "MapSetting_FE78")]
        // Universal tables 2
        [InlineData("item_weapon_triangle", "struct_item_weapon_triangle.txt", "ItemWeaponTriangle")]
        [InlineData("map_exit_points", "struct_map_exit_point.txt", "MapExitPoint")]
        [InlineData("ai_map_settings", "struct_ai_map_setting.txt", "AIMapSetting")]
        [InlineData("ai_perform_items", "struct_ai_perform_item.txt", "AIPerformItem")]
        [InlineData("ai_perform_staff", "struct_ai_perform_staff.txt", "AIPerformStaff")]
        [InlineData("ai_steal_items", "struct_ai_steal_item.txt", "AIStealItem")]
        [InlineData("ai_targets", "struct_ai_target.txt", "AITarget")]
        [InlineData("generic_enemy_portraits", "struct_generic_enemy_portrait.txt", "GenericEnemyPortrait")]
        [InlineData("status_options", "struct_status_option.txt", "StatusOption")]
        // Ending tables
        [InlineData("ed_retreat", "struct_ed_retreat.txt", "EDRetreat")]
        [InlineData("ed_epithet", "struct_ed_epithet.txt", "EDEpithet")]
        [InlineData("ed_epilogue_a", "struct_ed_epilogue.txt", "EDEpilogue")]
        [InlineData("ed_epilogue_b", "struct_ed_epilogue.txt", "EDEpilogue")]
        [InlineData("ed_epilogue_c", "struct_ed_epilogue.txt", "EDEpilogue")]
        // OP tables
        [InlineData("op_class_demo", "struct_op_class_demo.txt", "OPClassDemo")]
        [InlineData("op_class_font", "struct_op_class_font.txt", "OPClassFont")]
        [InlineData("op_prologue", "struct_op_prologue.txt", "OPPrologue")]
        [InlineData("class_alpha_names", "struct_class_alpha_name.txt", "ClassAlphaName")]
        // FE8 tables
        [InlineData("summon_units", "struct_summon_unit.txt", "SummonUnit")]
        [InlineData("summons_demon_king", "struct_summons_demon_king.txt", "SummonsDemonKing")]
        [InlineData("monster_probability", "struct_monster_probability.txt", "MonsterProbability")]
        public void MetadataFile_LoadsSuccessfully(string tableName, string fileName, string structName)
        {
            EnsureBaseDirectory();
            if (CoreState.BaseDirectory == null) return; // Skip if can't find repo root

            // Verify table is registered
            var table = StructExportCore.GetTable(tableName);
            Assert.NotNull(table);

            // Verify metadata file exists and loads
            string dir = Path.Combine(CoreState.BaseDirectory, "config", "data");
            string path = Path.Combine(dir, fileName);
            Assert.True(File.Exists(path), $"Metadata file not found: {path}");

            var meta = new StructMetadata();
            meta.LoadFromFile(path);
            var structDef = meta.GetStruct(structName);
            Assert.NotNull(structDef);
            Assert.True(structDef.Fields.Count > 0, $"No fields in struct {structName}");
            Assert.True(structDef.DataSize > 0, $"DataSize is 0 for {structName}");

            // Verify fields don't exceed struct size
            foreach (var field in structDef.Fields)
            {
                Assert.True(field.Offset + field.Size <= structDef.DataSize,
                    $"Field {field.Name} at offset 0x{field.Offset:X} with size {field.Size} exceeds struct size 0x{structDef.DataSize:X}");
            }
        }

        [Fact]
        public void AllTables_HaveMetadataFiles()
        {
            EnsureBaseDirectory();
            if (CoreState.BaseDirectory == null) return; // Skip if can't find repo root

            string dir = Path.Combine(CoreState.BaseDirectory, "config", "data");
            foreach (string tableName in StructExportCore.GetTableNames())
            {
                var table = StructExportCore.GetTable(tableName);
                Assert.NotNull(table);

                // Check FE6 metadata
                string fe6File = Path.Combine(dir, table.MetadataFileFE6);
                Assert.True(File.Exists(fe6File), $"FE6 metadata missing for {tableName}: {table.MetadataFileFE6}");

                // Check FE78 metadata
                string fe78File = Path.Combine(dir, table.MetadataFileFE78);
                Assert.True(File.Exists(fe78File), $"FE78 metadata missing for {tableName}: {table.MetadataFileFE78}");
            }
        }

        [Fact]
        public void AllTables_HaveNonNullCallbacks()
        {
            foreach (string tableName in StructExportCore.GetTableNames())
            {
                var table = StructExportCore.GetTable(tableName);
                Assert.NotNull(table.GetBaseAddress);
                Assert.NotNull(table.GetDataSize);
                Assert.NotNull(table.GetEntryCount);
            }
        }

        static StructMetadata.StructDef CreateTestStructDef()
        {
            return new StructMetadata.StructDef
            {
                Name = "TestStruct",
                DataSize = 4,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef { Name = "FieldA", Offset = 0, Type = StructMetadata.FieldType.Byte },
                    new StructMetadata.FieldDef { Name = "FieldB", Offset = 2, Type = StructMetadata.FieldType.Word },
                }
            };
        }

        // ====================================================================
        // ResolveTableAt + FormatX byte-identity — real-ROM tests (#770).
        // These load a real ROM from roms/ and verify the address→table
        // resolver and the extracted string formatters. They skip cleanly when
        // no ROM is available (CI without roms.zip).
        // ====================================================================

        /// <summary>Locate a preferred test ROM by walking up to the repo root.</summary>
        static string FindTestRom()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string romsDir = Path.Combine(dir, "roms");
                    if (Directory.Exists(romsDir))
                    {
                        string[] preferred = { "FE8U.gba", "FE7U.gba", "FE8J.gba", "FE7J.gba", "FE6.gba" };
                        foreach (string name in preferred)
                        {
                            string path = Path.Combine(romsDir, name);
                            if (File.Exists(path)) return path;
                        }
                        string[] gbaFiles = Directory.GetFiles(romsDir, "*.gba");
                        if (gbaFiles.Length > 0) return gbaFiles[0];
                    }
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Load a real ROM into CoreState, set BaseDirectory so struct metadata
        /// loads, run the action, then restore previous CoreState. The action is
        /// skipped (no-op) if no ROM is available.
        /// </summary>
        static void WithRealRom(Action<ROM> action)
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip — no ROM

            EnsureBaseDirectory();
            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                action(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void ResolveTableAt_UnitsBaseAddress_ResolvesUnitsTable()
        {
            WithRealRom(rom =>
            {
                var unitsDef = StructExportCore.GetTable("units");
                uint baseAddr = unitsDef.GetBaseAddress(rom);
                Assert.True(baseAddr != 0 && baseAddr != U.NOT_FOUND, "units base must resolve");

                // A small positive offset into the units table (entry 1) should
                // resolve to the units table.
                uint entrySize = unitsDef.GetDataSize(rom);
                var resolved = StructExportCore.ResolveTableAt(rom, baseAddr + entrySize);
                Assert.NotNull(resolved);
                Assert.Equal("units", resolved.Name);
            });
        }

        [Fact]
        public void ResolveTableAt_ClassEntryAddress_ResolvesClassesTable()
        {
            WithRealRom(rom =>
            {
                var classesDef = StructExportCore.GetTable("classes");
                uint baseAddr = classesDef.GetBaseAddress(rom);
                Assert.True(baseAddr != 0 && baseAddr != U.NOT_FOUND, "classes base must resolve");

                uint entrySize = classesDef.GetDataSize(rom);
                var resolved = StructExportCore.ResolveTableAt(rom, baseAddr + entrySize);
                Assert.NotNull(resolved);
                Assert.Equal("classes", resolved.Name);
            });
        }

        [Fact]
        public void ResolveTableAt_GbaPointerForm_ResolvesSameAsOffsetForm()
        {
            WithRealRom(rom =>
            {
                var unitsDef = StructExportCore.GetTable("units");
                uint baseAddr = unitsDef.GetBaseAddress(rom);
                Assert.True(baseAddr != 0 && baseAddr != U.NOT_FOUND);
                uint entrySize = unitsDef.GetDataSize(rom);

                uint offsetForm = baseAddr + entrySize;
                uint pointerForm = offsetForm + 0x08000000; // GBA-pointer form

                var byOffset = StructExportCore.ResolveTableAt(rom, offsetForm);
                var byPointer = StructExportCore.ResolveTableAt(rom, pointerForm);

                Assert.NotNull(byOffset);
                Assert.NotNull(byPointer);
                Assert.Equal(byOffset.Name, byPointer.Name);
                Assert.Equal("units", byPointer.Name);
            });
        }

        [Fact]
        public void ResolveTableAt_EndBoundary_IsExclusive()
        {
            WithRealRom(rom =>
            {
                var unitsDef = StructExportCore.GetTable("units");
                uint baseAddr = unitsDef.GetBaseAddress(rom);
                uint size = unitsDef.GetDataSize(rom);
                uint count = unitsDef.GetEntryCount(rom);
                Assert.True(baseAddr != 0 && baseAddr != U.NOT_FOUND && size != 0 && count != 0);

                // base + count*size is the end-exclusive boundary: it must NOT
                // resolve back to units (either null or a different table).
                uint endAddr = baseAddr + count * size;
                var resolved = StructExportCore.ResolveTableAt(rom, endAddr);
                if (resolved != null)
                    Assert.NotEqual("units", resolved.Name);
            });
        }

        [Fact]
        public void ResolveTableAt_HeaderAddress_ReturnsNull()
        {
            WithRealRom(rom =>
            {
                // 0x100 is inside the GBA cartridge header — no struct table
                // lives there.
                var resolved = StructExportCore.ResolveTableAt(rom, 0x100);
                Assert.Null(resolved);
            });
        }

        [Fact]
        public void ResolveTableAt_NullRom_ReturnsNull()
        {
            Assert.Null(StructExportCore.ResolveTableAt(null, 0x1000));
        }

        [Theory]
        [InlineData("units")]
        [InlineData("classes")]
        public void FormatX_MatchesExportToX_ByteForByte(string tableName)
        {
            WithRealRom(rom =>
            {
                var table = StructExportCore.GetTable(tableName);
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);
                var entries = StructExportCore.ExportTable(rom, table, sd);
                Assert.NotEmpty(entries);

                string tsvTmp = Path.GetTempFileName();
                string csvTmp = Path.GetTempFileName();
                string eaTmp = Path.GetTempFileName();
                try
                {
                    StructExportCore.ExportToTSV(entries, sd, tsvTmp);
                    StructExportCore.ExportToCSV(entries, sd, csvTmp);
                    StructExportCore.ExportToEA(entries, sd, eaTmp);

                    // The file writers use Encoding.UTF8; read back as UTF-8 and
                    // compare the decoded string to the formatter output.
                    Assert.Equal(File.ReadAllText(tsvTmp, System.Text.Encoding.UTF8),
                        StructExportCore.FormatTSV(entries, sd));
                    Assert.Equal(File.ReadAllText(csvTmp, System.Text.Encoding.UTF8),
                        StructExportCore.FormatCSV(entries, sd));
                    Assert.Equal(File.ReadAllText(eaTmp, System.Text.Encoding.UTF8),
                        StructExportCore.FormatEA(entries, sd));
                }
                finally
                {
                    File.Delete(tsvTmp);
                    File.Delete(csvTmp);
                    File.Delete(eaTmp);
                }
            });
        }

        // ResolveTableAt normalizes the input address with U.toOffset, then does
        // an exact end-EXCLUSIVE range check (start <= off < base + count*size).
        // This locks the load-bearing assumption that toOffset only strips the
        // GBA-pointer base (0x08000000) and NEVER rounds to a word boundary —
        // otherwise a non-word-aligned exclusive end could be padded back inside
        // a small/odd-count table and resolve incorrectly. (All registered
        // tables happen to be word-aligned, so this pure check is the only place
        // the non-aligned boundary is provably covered.)
        [Fact]
        public void ToOffset_NonWordAlignedValue_IsNotRoundedDown()
        {
            // Raw offsets (below the GBA-pointer range) pass through byte-exact.
            Assert.Equal(0x1235u, U.toOffset(0x1235u)); // odd
            Assert.Equal(0x1236u, U.toOffset(0x1236u)); // 2-aligned, not 4-aligned
            Assert.Equal(0x1237u, U.toOffset(0x1237u));
            // GBA-pointer form: strip the base, preserve the low (non-aligned) bits.
            Assert.Equal(0x1235u, U.toOffset(0x08001235u));
            Assert.Equal(0x1237u, U.toOffset(0x08001237u));
        }
    }
}
