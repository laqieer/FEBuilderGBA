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
    }
}
