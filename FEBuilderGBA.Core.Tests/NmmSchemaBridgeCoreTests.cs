using System;
using System.Collections.Generic;
using System.Text.Json;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the No$gba memory map ↔ manifest schema bridge (#1150). Pure (no ROM).
    /// </summary>
    public class NmmSchemaBridgeCoreTests
    {
        /// <summary>A synthetic NMM with 4 fields incl. one size=4 pointer and one size=3 (odd).</summary>
        static string SampleNmm()
        {
            // header: magic / title / base / count / blockSize / NULL / NULL / blank
            return
                "1\n" +
                "ItemSample by FEBuilderGBA\n" +
                "0x809B7B4\n" +
                "255\n" +
                "36\n" +
                "NULL\n" +
                "NULL\n" +
                "\n" +
                // field NameTextID (offset 0, size 2)
                "NameTextID\n0\n2\nNEHU\nNULL\n\n" +
                // field Might (offset 4, size 1)
                "Might\n4\n1\nNEHU\nNULL\n\n" +
                // field UsePointer (offset 8, size 4, PTR type) -> unsupported (pointer)
                "UsePointer\n8\n4\nNEPU\nNULL\n\n" +
                // field WeirdSize (offset 12, size 3) -> unsupported (odd size)
                "WeirdSize\n12\n3\nNEHU\nNULL\n\n";
        }

        [Fact]
        public void ParseNmm_ReadsHeader()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(SampleNmm());
            Assert.True(p.Ok);
            Assert.Equal("ItemSample", p.ModuleName);
            Assert.Equal(0x809B7B4u, p.BaseAddress);
            Assert.Equal(255, p.EntryCount);
            Assert.Equal(36, p.BlockSize);
            Assert.Equal(4, p.Fields.Count);
        }

        [Fact]
        public void ParseNmm_ReadsFieldOffsetsAndSizes()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(SampleNmm());
            Assert.Equal("NameTextID", p.Fields[0].Name);
            Assert.Equal(0, p.Fields[0].Offset);
            Assert.Equal(2, p.Fields[0].Size);
            Assert.Equal("Might", p.Fields[1].Name);
            Assert.Equal(4, p.Fields[1].Offset);
            Assert.Equal(1, p.Fields[1].Size);
        }

        [Fact]
        public void ParseNmm_PointerField_IsUnsupported_AndNotDropped()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(SampleNmm());
            NmmField ptr = p.Fields.Find(f => f.Name == "UsePointer");
            Assert.NotNull(ptr);
            Assert.True(ptr.Unsupported);
            Assert.False(string.IsNullOrEmpty(ptr.UnsupportedReason));
        }

        [Fact]
        public void ParseNmm_OddSizeField_IsUnsupported_AndNotDropped()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(SampleNmm());
            NmmField weird = p.Fields.Find(f => f.Name == "WeirdSize");
            Assert.NotNull(weird);
            Assert.True(weird.Unsupported);
            Assert.Contains("3", weird.UnsupportedReason);
        }

        [Fact]
        public void BuildManifestTablesEntry_ParsesAsJson_AndContainsUnsupportedMarker()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(SampleNmm());
            string json = NmmSchemaBridgeCore.BuildManifestTablesEntry(p, "items");

            using JsonDocument doc = JsonDocument.Parse(json); // must be valid JSON
            JsonElement root = doc.RootElement;
            Assert.Equal("items", root.GetProperty("table").GetString());
            Assert.Equal(36, root.GetProperty("entrySize").GetInt32());
            Assert.Equal(255, root.GetProperty("count").GetInt32());

            // baseAddress in the Extra bag (System.Text.Json flattens JsonExtensionData).
            Assert.Equal("0x809B7B4", root.GetProperty("baseAddress").GetString());

            // The pointer field carries the unsupported marker.
            JsonElement fields = root.GetProperty("fields");
            bool foundMarker = false;
            foreach (JsonElement f in fields.EnumerateArray())
            {
                if (f.GetProperty("name").GetString() == "UsePointer")
                {
                    Assert.True(f.GetProperty("unsupported").GetBoolean());
                    Assert.True(f.TryGetProperty("unsupportedReason", out _));
                    foundMarker = true;
                }
            }
            Assert.True(foundMarker, "UsePointer field should carry an unsupported marker");
        }

        [Fact]
        public void BuildManifestTablesEntry_EscapesQuotesAndBackslashes()
        {
            // A field name with a quote + backslash must survive JSON escaping.
            string nmm =
                "1\nX by FEBuilderGBA\n0x0\n1\n4\nNULL\nNULL\n\n" +
                "weird\"name\\here\n0\n2\nNEHU\nNULL\n\n";
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(nmm);
            string json = NmmSchemaBridgeCore.BuildManifestTablesEntry(p, "t");

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement first = doc.RootElement.GetProperty("fields")[0];
            Assert.Equal("weird\"name\\here", first.GetProperty("name").GetString());
        }

        [Fact]
        public void RoundTrip_ExportTableToNmm_ReusesGrammar_AndParsesBack()
        {
            // Build an owner directly (matching the parsed sample), export to NMM, parse back.
            var owner = new DecompTableEntry
            {
                Table = "ItemSample",
                EntrySize = 36,
                Count = 255,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "NameTextID", Offset = 0, Width = 2 },
                    new DecompTableField { Name = "Might", Offset = 4, Width = 1 },
                },
                Extra = new Dictionary<string, JsonElement>
                {
                    ["baseAddress"] = JsonDocument.Parse("\"0x809B7B4\"").RootElement.Clone(),
                },
            };

            string nmm = NmmSchemaBridgeCore.ExportTableToNmm(owner, out List<string> warnings);
            Assert.NotNull(warnings);

            NmmParseResult back = NmmSchemaBridgeCore.ParseNmm(nmm);
            Assert.True(back.Ok);
            Assert.Equal("ItemSample", back.ModuleName);
            Assert.Equal(0x809B7B4u, back.BaseAddress);
            Assert.Equal(255, back.EntryCount);
            Assert.Equal(36, back.BlockSize);
            Assert.Equal(2, back.Fields.Count);
            Assert.Equal("NameTextID", back.Fields[0].Name);
            Assert.Equal(2, back.Fields[0].Size);
            Assert.Equal("Might", back.Fields[1].Name);
            Assert.Equal(1, back.Fields[1].Size);
        }

        [Fact]
        public void ExportTableToNmm_MissingBase_EmitsZero_AndWarns()
        {
            var owner = new DecompTableEntry
            {
                Table = "NoBase",
                EntrySize = 4,
                Count = 1,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "B0", Offset = 0, Width = 1 },
                },
                // no Extra/baseAddress
            };
            string nmm = NmmSchemaBridgeCore.ExportTableToNmm(owner, out List<string> warnings);
            Assert.Contains("0x0", nmm);
            Assert.Contains(warnings, w => w.Contains("baseAddress", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ExportTableToNmm_OddWidthField_Warns_AndDoesNotThrow()
        {
            var owner = new DecompTableEntry
            {
                Table = "Odd",
                EntrySize = 3,
                Count = 1,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "W3", Offset = 0, Width = 3 },
                },
            };
            string nmm = NmmSchemaBridgeCore.ExportTableToNmm(owner, out List<string> warnings);
            Assert.False(string.IsNullOrEmpty(nmm));
            Assert.Contains(warnings, w => w.Contains("W3"));
        }

        [Fact]
        public void ParseNmm_Garbage_NoThrow_OkFalse()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm("garbage\nmore garbage\n");
            Assert.False(p.Ok);
            Assert.NotEmpty(p.Warnings);
        }

        [Fact]
        public void ParseNmm_Null_NoThrow_OkFalse()
        {
            NmmParseResult p = NmmSchemaBridgeCore.ParseNmm(null);
            Assert.False(p.Ok);
        }

        [Fact]
        public void BuildManifestTablesEntry_NullParsed_NoThrow()
        {
            string json = NmmSchemaBridgeCore.BuildManifestTablesEntry(null, "t");
            using JsonDocument doc = JsonDocument.Parse(json); // "{}" is valid JSON
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }

        [Fact]
        public void ExportTableToNmm_NullOwner_NoThrow()
        {
            string nmm = NmmSchemaBridgeCore.ExportTableToNmm(null, out List<string> warnings);
            Assert.False(string.IsNullOrEmpty(nmm));
            Assert.NotEmpty(warnings);
            // A stub still parses as a valid NMM header.
            Assert.True(NmmSchemaBridgeCore.ParseNmm(nmm).Ok);
        }
    }
}
