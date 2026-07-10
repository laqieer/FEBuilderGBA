using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class StructExportFormatTests
    {
        static StructMetadata.StructDef MakeTestDef()
        {
            return new StructMetadata.StructDef
            {
                Name = "TestUnit",
                DataSize = 8,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef { Name = "Level", Offset = 0, Type = StructMetadata.FieldType.Byte },
                    new StructMetadata.FieldDef { Name = "HP", Offset = 1, Type = StructMetadata.FieldType.Byte },
                    new StructMetadata.FieldDef { Name = "ClassPtr", Offset = 4, Type = StructMetadata.FieldType.Pointer },
                }
            };
        }

        static List<Dictionary<string, string>> MakeTestEntries()
        {
            return new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "_Index", "0x00 Eirika" },
                    { "Level", "0x01" },
                    { "HP", "0x10" },
                    { "ClassPtr", "0x08123456" },
                },
                new Dictionary<string, string>
                {
                    { "_Index", "0x01 Seth" },
                    { "Level", "0x0A" },
                    { "HP", "0x20" },
                    { "ClassPtr", "0x08789ABC" },
                },
            };
        }

        [Fact]
        public void CsvQuote_PlainValue_NotQuoted()
        {
            Assert.Equal("hello", StructExportCore.CsvQuote("hello"));
        }

        [Fact]
        public void CsvQuote_ValueWithComma_Quoted()
        {
            Assert.Equal("\"hello,world\"", StructExportCore.CsvQuote("hello,world"));
        }

        [Fact]
        public void CsvQuote_ValueWithQuotes_DoubleQuoted()
        {
            Assert.Equal("\"he said \"\"hi\"\"\"", StructExportCore.CsvQuote("he said \"hi\""));
        }

        [Fact]
        public void CsvQuote_NullValue_EmptyQuoted()
        {
            Assert.Equal("\"\"", StructExportCore.CsvQuote(null));
        }

        [Fact]
        public void ExportToCSV_ProducesValidCSV()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.csv");
            try
            {
                StructExportCore.ExportToCSV(entries, def, path);
                string content = File.ReadAllText(path);
                // Header
                Assert.Contains("Index,Level,HP,ClassPtr", content);
                // Data: "0x00 Eirika" should be quoted (contains space but no comma — actually space is fine)
                Assert.Contains("0x01", content);
                Assert.Contains("0x08123456", content);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExportToEA_ProducesDefines()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.ea");
            try
            {
                StructExportCore.ExportToEA(entries, def, path);
                string content = File.ReadAllText(path);
                Assert.Contains("#define TestUnit_0x00_Level 0x01", content);
                Assert.Contains("#define TestUnit_0x00_ClassPtr 0x08123456", content);
                Assert.Contains("#define TestUnit_0x01_HP 0x20", content);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExportToEA_EmptyEntries_ProducesHeaderOnly()
        {
            var def = MakeTestDef();
            var entries = new List<Dictionary<string, string>>();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.ea");
            try
            {
                StructExportCore.ExportToEA(entries, def, path);
                string content = File.ReadAllText(path);
                Assert.Contains("Event Assembler definitions", content);
                Assert.DoesNotContain("#define TestUnit", content);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ====================================================================
        // FormatX / ExportToX byte-identity (#770). The string formatters were
        // extracted from the file writers; these synthetic-entry tests prove the
        // extraction is behavior-preserving without needing a real ROM.
        // ====================================================================

        [Fact]
        public void FormatTSV_MatchesExportToTSV_ByteForByte()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tsv");
            try
            {
                StructExportCore.ExportToTSV(entries, def, path);
                Assert.Equal(File.ReadAllText(path, System.Text.Encoding.UTF8),
                    StructExportCore.FormatTSV(entries, def));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FormatCSV_MatchesExportToCSV_ByteForByte()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.csv");
            try
            {
                StructExportCore.ExportToCSV(entries, def, path);
                Assert.Equal(File.ReadAllText(path, System.Text.Encoding.UTF8),
                    StructExportCore.FormatCSV(entries, def));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FormatEA_MatchesExportToEA_ByteForByte()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.ea");
            try
            {
                StructExportCore.ExportToEA(entries, def, path);
                Assert.Equal(File.ReadAllText(path, System.Text.Encoding.UTF8),
                    StructExportCore.FormatEA(entries, def));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FormatTSV_FirstLineIsHeader_NoBanner()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string text = StructExportCore.FormatTSV(entries, def);
            string firstLine = text.Split('\n')[0].TrimEnd('\r');
            Assert.Equal("Index\tLevel\tHP\tClassPtr", firstLine);
            Assert.DoesNotContain("Avalonia stub", text);
            Assert.DoesNotContain("#", firstLine);
        }

        [Fact]
        public void FormatCSV_FirstLineIsHeader_NoBanner()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string text = StructExportCore.FormatCSV(entries, def);
            string firstLine = text.Split('\n')[0].TrimEnd('\r');
            Assert.Equal("Index,Level,HP,ClassPtr", firstLine);
            Assert.DoesNotContain("Avalonia stub", text);
        }

        // ====================================================================
        // JSON export/import (#1937 — LLM-backend format). Public key is "Index"
        // (never the internal "_Index"); every value is a JSON *string* in the
        // same TSV-compatible hex/text representation.
        // ====================================================================

        [Fact]
        public void FormatJSON_UsesPublicIndexKey_NeverInternalUnderscoreIndex()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
            var row0 = doc.RootElement[0];
            Assert.True(row0.TryGetProperty("Index", out var idxEl));
            Assert.Equal("0x00 Eirika", idxEl.GetString());
            Assert.False(row0.TryGetProperty("_Index", out _));
        }

        [Fact]
        public void FormatJSON_AllValuesAreJsonStrings()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                foreach (var prop in row.EnumerateObject())
                {
                    Assert.Equal(System.Text.Json.JsonValueKind.String, prop.Value.ValueKind);
                }
            }
        }

        [Fact]
        public void FormatJSON_FieldValuesMatchTsvHexRepresentation()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var row1 = doc.RootElement[1];
            Assert.Equal("0x0A", row1.GetProperty("Level").GetString());
            Assert.Equal("0x20", row1.GetProperty("HP").GetString());
            Assert.Equal("0x08789ABC", row1.GetProperty("ClassPtr").GetString());
        }

        [Fact]
        public void ExportToJSON_MatchesFormatJSON_ByteForByte()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
            try
            {
                StructExportCore.ExportToJSON(entries, def, path);
                Assert.Equal(File.ReadAllText(path, System.Text.Encoding.UTF8),
                    StructExportCore.FormatJSON(entries, def));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExportToJSON_WritesWithoutBom()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
            try
            {
                StructExportCore.ExportToJSON(entries, def, path);
                byte[] bytes = File.ReadAllBytes(path);
                // UTF-8 BOM is EF BB BF; the first byte of "[" is 0x5B.
                Assert.Equal((byte)'[', bytes[0]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ParseJSON_RoundTripsFormatJSON()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);

            var parsed = StructExportCore.ParseJSON(json);

            Assert.Equal(2, parsed.Count);
            Assert.Equal(0, parsed[0].index);
            Assert.Equal("0x01", parsed[0].fields["Level"]);
            Assert.Equal(1, parsed[1].index);
            Assert.Equal("0x0A", parsed[1].fields["Level"]);
            Assert.Equal("0x08789ABC", parsed[1].fields["ClassPtr"]);
            // "Index" must not leak into the fields dictionary (WriteTable only knows field names).
            Assert.False(parsed[0].fields.ContainsKey("Index"));
        }

        [Fact]
        public void ParseJSON_RootNotArray_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() => StructExportCore.ParseJSON("{\"Index\":\"0x00\"}"));
            Assert.Contains("root element must be an array", ex.Message);
        }

        [Fact]
        public void ParseJSON_RowNotObject_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() => StructExportCore.ParseJSON("[\"not an object\"]"));
            Assert.Contains("must be an object", ex.Message);
        }

        [Fact]
        public void ParseJSON_NumberValue_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 Eirika\",\"Level\":1}]"));
            Assert.Contains("must be a JSON string", ex.Message);
            Assert.Contains("Level", ex.Message);
        }

        [Fact]
        public void ParseJSON_BooleanValue_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 Eirika\",\"Level\":true}]"));
            Assert.Contains("must be a JSON string", ex.Message);
        }

        [Fact]
        public void ParseJSON_NullValue_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 Eirika\",\"Level\":null}]"));
            Assert.Contains("must be a JSON string", ex.Message);
        }

        [Fact]
        public void ParseJSON_ArrayValue_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 Eirika\",\"Level\":[1,2]}]"));
            Assert.Contains("must be a JSON string", ex.Message);
        }

        [Fact]
        public void ParseJSON_ObjectValue_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 Eirika\",\"Level\":{\"nested\":true}}]"));
            Assert.Contains("must be a JSON string", ex.Message);
        }

        [Fact]
        public void ParseJSON_MissingIndex_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Level\":\"0x01\"}]"));
            Assert.Contains("missing the required 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_UnparsableIndex_ThrowsFormatException()
        {
            // A blank Index (IsNullOrWhiteSpace) is the one value ParseIndexFromFirstColumn
            // cannot resolve; non-hex text like "abc" is tolerated the same permissive way
            // TSV import already parses it (atoi0x falls back to atoi(), which is lenient).
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"   \",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_MalformedSyntax_ThrowsJsonException()
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => StructExportCore.ParseJSON("[{ this is not json"));
        }

        [Fact]
        public void ImportFromJSON_ReadsFileAndParses()
        {
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                var parsed = StructExportCore.ImportFromJSON(path);
                Assert.Equal(2, parsed.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
