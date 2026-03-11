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
            // Total count
            Assert.True(names.Count >= 19, $"Expected at least 19 tables, got {names.Count}");
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
    }
}
