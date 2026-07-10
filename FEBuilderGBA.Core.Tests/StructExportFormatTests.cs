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
            // A blank Index (IsNullOrWhiteSpace) is unparsable under both the strict JSON
            // parser and TSV's permissive ParseIndexFromFirstColumn.
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"   \",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_GarbageIndex_ThrowsFormatException_InsteadOfAliasingToRowZero()
        {
            // "banana" is not a 0x/$/decimal token. TSV import's ParseIndexFromFirstColumn
            // would silently alias this to index 0 via U.atoi0x's truncating U.atoi() fallback
            // (a[0] is not a digit, so the numeric prefix is "", and int.TryParse("") fails,
            // returning 0) — the strict JSON parser must reject it instead of risking a
            // silent write to row 0.
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"banana\",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_Index_AcceptsDollarHexForm()
        {
            var parsed = StructExportCore.ParseJSON("[{\"Index\":\"$0A Eirika\",\"Level\":\"0x01\"}]");
            Assert.Equal(0x0A, parsed[0].index);
        }

        [Fact]
        public void ParseJSON_Index_AcceptsPlainDecimalForm()
        {
            var parsed = StructExportCore.ParseJSON("[{\"Index\":\"10\",\"Level\":\"0x01\"}]");
            Assert.Equal(10, parsed[0].index);
        }

        [Fact]
        public void ParseJSON_Index_RejectsNegativeDecimalForm()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"-1\",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_Index_RejectsOverflowingHexForm()
        {
            // 9 hex digits overflows uint (max 8 hex digits / 0xFFFFFFFF).
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x1FFFFFFFF\",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_Index_RejectsBarePrefixWithNoDigits()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x\",\"Level\":\"0x01\"}]"));
            Assert.Contains("unparsable 'Index'", ex.Message);
        }

        [Fact]
        public void ParseJSON_DuplicateIndexProperty_ThrowsFormatException_InsteadOfLastWins()
        {
            // JsonDocument tolerates duplicate keys and would otherwise silently keep only
            // the last "Index" value on enumeration — reject instead of a silent last-wins.
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 A\",\"Index\":\"0x01 B\",\"Level\":\"0x01\"}]"));
            Assert.Contains("duplicate property", ex.Message);
            Assert.Contains("Index", ex.Message);
        }

        [Fact]
        public void ParseJSON_DuplicateFieldProperty_ThrowsFormatException_InsteadOfLastWins()
        {
            var ex = Assert.Throws<FormatException>(() =>
                StructExportCore.ParseJSON("[{\"Index\":\"0x00 A\",\"Level\":\"0x01\",\"Level\":\"0x02\"}]"));
            Assert.Contains("duplicate property", ex.Message);
            Assert.Contains("Level", ex.Message);
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

        // ------------------------------------------------------------------
        // #1937 follow-up: ValidateJSONEntries / ParseAndValidateJSON semantic
        // preflight (unknown fields, strict numeric parsing/width, duplicate
        // indices, out-of-range indices). MakeTestDef() has Level(Byte),
        // HP(Byte), ClassPtr(Pointer) — entryCount below is deliberately >=
        // the highest Index used so only the behavior under test fails.

        [Fact]
        public void ValidateJSONEntries_UnknownFieldName_ThrowsFormatException_WithRowAndPropertyName()
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"Weigth\":\"0x05\"}]"); // typo: Weigth

            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("row 1", ex.Message);
            Assert.Contains("unknown property 'Weigth'", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_MissingField_IsAllowed_ForPartialUpdates()
        {
            var def = MakeTestDef();
            // Only "Level" present; "HP"/"ClassPtr" omitted — must not throw.
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"Level\":\"0x05\"}]");

            var result = Record.Exception(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Null(result);
        }

        [Theory]
        [InlineData("banana")]
        [InlineData("-1")]
        [InlineData("0x")]
        [InlineData("$")]
        [InlineData("0x0A garbage")]
        [InlineData("0xGG")]
        [InlineData("")]
        public void ValidateJSONEntries_GarbageOrNegativeOrBarePrefixOrTrailingTokenFieldValue_ThrowsFormatException(string badValue)
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON($"[{{\"Index\":\"0x00\",\"Level\":\"{badValue}\"}}]");

            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("row 1", ex.Message);
            Assert.Contains("'Level'", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_ByteFieldOverflow_ThrowsFormatException()
        {
            var def = MakeTestDef();
            // "Level"/"HP" are Byte fields (max 0xFF); 0x100 overflows a byte.
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"Level\":\"0x100\"}]");

            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("exceeds the maximum for a Byte field", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_DecimalOverflowsUintRange_ThrowsFormatException()
        {
            var def = MakeTestDef();
            // "ClassPtr" is a Pointer field (max 0xFFFFFFFF); this decimal string
            // overflows even a full 32-bit unsigned integer.
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"ClassPtr\":\"99999999999\"}]");

            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("overflow", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_PointerFieldAcceptsFullDWordRange_ButRejectsHexOverflow()
        {
            var def = MakeTestDef();
            var valid = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"ClassPtr\":\"0xFFFFFFFF\"}]");
            var exNone = Record.Exception(() => StructExportCore.ValidateJSONEntries(valid, def, entryCount: 4));
            Assert.Null(exNone);
            Assert.Equal("0xFFFFFFFF", valid[0].fields["ClassPtr"]);

            // 9 hex digits overflow a 32-bit pointer field.
            var overflow = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"ClassPtr\":\"0x1FFFFFFFF\"}]");
            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(overflow, def, entryCount: 4));
            Assert.Contains("overflow", ex.Message);
        }

        [Theory]
        [InlineData(StructMetadata.FieldType.DWord)]
        [InlineData(StructMetadata.FieldType.Pointer)]
        public void ValidateJSONEntries_MaxDecimalDWord_NormalizesAndWritesFullUnsignedValue(
            StructMetadata.FieldType type)
        {
            var def = new StructMetadata.StructDef
            {
                Name = "WideValue",
                DataSize = 4,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef { Name = "Value", Offset = 0, Type = type },
                },
            };
            var entries = StructExportCore.ParseJSON(
                "[{\"Index\":\"0x00\",\"Value\":\"4294967295\"}]");

            StructExportCore.ValidateJSONEntries(entries, def, entryCount: 1);
            Assert.Equal("0xFFFFFFFF", entries[0].fields["Value"]);

            var rom = new ROM();
            Assert.True(rom.LoadLow("wide-value.gba", new byte[0x1000000], "BE8E01"));
            var table = new StructExportCore.TableDef
            {
                Name = "wide_value",
                GetBaseAddress = _ => 0x200,
                GetDataSize = _ => 4,
                GetEntryCount = _ => 1,
            };

            Assert.Equal(1, StructExportCore.WriteTable(rom, table, def, entries));
            Assert.Equal(uint.MaxValue, rom.u32(0x200));
        }

        [Fact]
        public void ValidateJSONEntries_DuplicateRowIndex_ThrowsFormatException()
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON(
                "[{\"Index\":\"0x00\",\"Level\":\"0x01\"},{\"Index\":\"0x00\",\"Level\":\"0x02\"}]");

            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("duplicate Index", ex.Message);
            Assert.Contains("row 2", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_IndexOutsideResolvedEntryCount_ThrowsFormatException()
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x05\",\"Level\":\"0x01\"}]");

            // Table resolved to only 4 entries — Index 5 is out of range.
            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Contains("Index 5", ex.Message);
            Assert.Contains("outside the valid range", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_ZeroEntryCount_ThrowsFormatException_InsteadOfSilentSkip()
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON("[{\"Index\":\"0x00\",\"Level\":\"0x01\"}]");

            // entryCount 0 means the table/base address was not resolved — WriteTable
            // would silently skip every row; the preflight must reject instead.
            var ex = Assert.Throws<FormatException>(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 0));
            Assert.Contains("outside the valid range", ex.Message);
        }

        [Fact]
        public void ValidateJSONEntries_ValidDecimalDollarAndUppercase0X_NormalizeToLowercase0xCanonicalForm()
        {
            var def = MakeTestDef();
            var entries = StructExportCore.ParseJSON(
                "[{\"Index\":\"0x00\",\"Level\":\"10\"}," +
                "{\"Index\":\"0x01\",\"Level\":\"$0A\"}," +
                "{\"Index\":\"0x02\",\"Level\":\"0X0A\"}]");

            var result = Record.Exception(() => StructExportCore.ValidateJSONEntries(entries, def, entryCount: 4));
            Assert.Null(result);

            // Every accepted form normalizes to a lowercase "0x" prefix so U.atoi0x
            // parses the complete unsigned range without decimal/int truncation.
            Assert.Equal("0xA", entries[0].fields["Level"]);
            Assert.Equal("0xA", entries[1].fields["Level"]);
            Assert.Equal("0xA", entries[2].fields["Level"]);
            Assert.Equal((uint)10, U.atoi0x(entries[0].fields["Level"]));
            Assert.Equal((uint)10, U.atoi0x(entries[1].fields["Level"]));
            Assert.Equal((uint)10, U.atoi0x(entries[2].fields["Level"]));
        }

        [Fact]
        public void ParseAndValidateJSON_ValidDocument_ReturnsNormalizedEntries()
        {
            var def = MakeTestDef();
            string json = "[{\"Index\":\"0x00\",\"Level\":\"$0A\",\"HP\":\"0x10\"}]";

            var entries = StructExportCore.ParseAndValidateJSON(json, def, entryCount: 4);

            Assert.Single(entries);
            Assert.Equal(0, entries[0].index);
            Assert.Equal("0xA", entries[0].fields["Level"]);
            Assert.Equal("0x10", entries[0].fields["HP"]);
        }

        [Fact]
        public void ImportFromJSON_StructAndCountAware_UnknownField_ThrowsFormatException()
        {
            var def = MakeTestDef();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "[{\"Index\":\"0x00\",\"NotAField\":\"0x01\"}]", System.Text.Encoding.UTF8);
                var ex = Assert.Throws<FormatException>(() => StructExportCore.ImportFromJSON(path, def, entryCount: 4));
                Assert.Contains("unknown property 'NotAField'", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ImportFromJSON_ShapeOnlyOverload_StillWorksUnchanged_ForBackwardCompatibility()
        {
            // The original shape-only ImportFromJSON(string) overload must keep working
            // exactly as before — no struct/count-aware validation is applied to it.
            var def = MakeTestDef();
            var entries = MakeTestEntries();
            string json = StructExportCore.FormatJSON(entries, def);
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                var parsed = StructExportCore.ImportFromJSON(path);
                Assert.Equal(2, parsed.Count);
                // Unnormalized: the original hex casing/padding from FormatJSON is preserved.
                Assert.Equal("0x01", parsed[0].fields["Level"]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
