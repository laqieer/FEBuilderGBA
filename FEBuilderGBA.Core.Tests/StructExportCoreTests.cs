using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class StructExportCoreTests
    {
        [Fact]
        public void GetTableNames_ReturnsRegisteredTables()
        {
            var names = new List<string>(StructExportCore.GetTableNames());
            Assert.Contains("units", names);
            Assert.Contains("classes", names);
            Assert.Contains("items", names);
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
