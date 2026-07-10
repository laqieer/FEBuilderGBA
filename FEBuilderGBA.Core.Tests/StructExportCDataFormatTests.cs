using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Additive Core tests for the GNU11 raw-byte-backed C struct/array formatter
    /// (<see cref="StructExportCore.FormatCData"/> / <see cref="StructExportCore.ExportToCData"/>,
    /// #1939 Phase A). Covers the layout partitioner (<see cref="StructExportCore.BuildCLayout"/>),
    /// identifier sanitization/collision rejection, comment neutralization, numeric
    /// strictness, the zero/nonzero-row array contract, and a byte-coverage/reconstruction
    /// oracle proving every resolved-stride byte is represented exactly once. None of these
    /// tests touch <c>FormatTSV</c>/<c>FormatCSV</c>/<c>FormatEA</c>/<c>FormatJSON</c>/
    /// <c>FormatSTRUCT</c>/<c>FormatNMM</c> — those keep their own, already-passing test
    /// coverage in <c>StructExportCoreTests.cs</c> / <c>StructExportFormatTests.cs</c>
    /// unchanged, which is itself the "existing formatter output is byte-for-byte
    /// unchanged" regression proof for this addition.
    /// </summary>
    public class StructExportCDataFormatTests
    {
        // ====================================================================
        // Fixture helpers
        // ====================================================================

        static StructMetadata.FieldDef F(string name, uint offset, StructMetadata.FieldType type, string comment = null)
            => new StructMetadata.FieldDef { Name = name, Offset = offset, Type = type, Comment = comment };

        static StructMetadata.StructDef Def(string name, params StructMetadata.FieldDef[] fields)
            => new StructMetadata.StructDef { Name = name, DataSize = 0, Fields = new List<StructMetadata.FieldDef>(fields) };

        static (uint index, Dictionary<string, string> fields, byte[] raw) MakeRow(
            uint index, byte[] raw, params (string name, string value)[] fieldValues)
        {
            var d = new Dictionary<string, string> { ["_Index"] = "0x" + index.ToString("X2") + " " };
            foreach (var (n, v) in fieldValues) d[n] = v;
            return (index, d, raw);
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "config", "data")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
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

        static int CountSubstring(string s, string sub)
        {
            int count = 0, idx = 0;
            while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0) { count++; idx += sub.Length; }
            return count;
        }

        static string ExtractRowLine(string cSource, string ordinalMarker)
        {
            foreach (var line in cSource.Split('\n'))
            {
                if (line.Contains("[" + ordinalMarker + "]")) return line;
            }
            return null;
        }

        static byte[] ExtractFieldLiteralBytes(string line, string memberName, int width)
        {
            var m = Regex.Match(line, @"\." + Regex.Escape(memberName) + @"\s*=\s*0x([0-9A-Fa-f]+)");
            Assert.True(m.Success, $"Field literal for '{memberName}' not found in: {line}");
            uint val = Convert.ToUInt32(m.Groups[1].Value, 16);
            var bytes = new byte[width];
            for (int i = 0; i < width; i++) bytes[i] = (byte)(val >> (8 * i));
            return bytes;
        }

        static byte[] ExtractArrayBytes(string line, string memberName)
        {
            var m = Regex.Match(line, @"\." + Regex.Escape(memberName) + @"\s*=\s*\{([^}]*)\}");
            Assert.True(m.Success, $"Array literal for '{memberName}' not found in: {line}");
            var tokens = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++) bytes[i] = Convert.ToByte(tokens[i].Trim(), 16);
            return bytes;
        }

        // ====================================================================
        // 1. Exact mixed byte/word/dword/pointer output + stride assertion
        // ====================================================================

        [Fact]
        public void FormatCData_MixedWidths_EmitsExactTypesAndStrideAssertion()
        {
            var structDef = Def("Mixed",
                F("Byte", 0, StructMetadata.FieldType.Byte),
                F("Word", 1, StructMetadata.FieldType.Word),
                F("DWord", 4, StructMetadata.FieldType.DWord),
                F("Ptr", 8, StructMetadata.FieldType.Pointer));
            byte[] raw = { 0x7F, 0x34, 0x12, 0xAB, 0xEF, 0xBE, 0xAD, 0xDE, 0x56, 0x34, 0x12, 0x08 };
            var row = MakeRow(0, raw, ("Byte", "0x7F"), ("Word", "0x1234"), ("DWord", "0xDEADBEEF"), ("Ptr", "0x08123456"));

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "mixed_table", 12);

            Assert.Contains("#include <stdint.h>", c);
            Assert.Contains("uint8_t Byte;", c);
            Assert.Contains("uint16_t Word;", c);
            Assert.Contains("uint8_t _gap0[1]", c); // byte 3 is an interior metadata gap
            Assert.Contains("uint32_t DWord;", c);
            Assert.Contains("uint32_t Ptr;", c);
            Assert.Contains("_Static_assert(sizeof(struct FEBuilder_Mixed) == 0xC,", c);
            Assert.Contains(".Byte = 0x7F", c);
            Assert.Contains(".Word = 0x1234", c);
            Assert.Contains("._gap0 = { 0xAB }", c);
            Assert.Contains(".DWord = 0xDEADBEEF", c);
            Assert.Contains(".Ptr = 0x08123456", c);
        }

        // ====================================================================
        // 2. Raw gap / dynamic trailing initialization
        // ====================================================================

        [Fact]
        public void FormatCData_GapAndTrailingMembers_InitializedFromRawBytes()
        {
            var structDef = Def("GapTrailing",
                F("A", 0, StructMetadata.FieldType.Byte),
                F("B", 3, StructMetadata.FieldType.Byte));
            byte[] raw = { 0x10, 0xAA, 0xBB, 0x20, 0xCC, 0xDD };
            var row = MakeRow(0, raw, ("A", "0x10"), ("B", "0x20"));

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "gap_table", 6);

            Assert.Contains("uint8_t _gap0[2]", c);
            Assert.Contains("uint8_t _trailing[2]", c);
            Assert.Contains("._gap0 = { 0xAA, 0xBB }", c);
            Assert.Contains("._trailing = { 0xCC, 0xDD }", c);
        }

        [Fact]
        public void FormatCData_NoTrailingBytes_EmitsNoTrailingMember()
        {
            var structDef = Def("NoTrailing", F("A", 0, StructMetadata.FieldType.DWord));
            byte[] raw = { 0x01, 0x02, 0x03, 0x04 };
            var row = MakeRow(0, raw, ("A", "0x04030201"));

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "no_trailing_table", 4);

            Assert.DoesNotContain("uint8_t _trailing[", c);
            Assert.DoesNotContain("._trailing =", c);
        }

        // ====================================================================
        // 3. Overlap grouping: contained, crossing, transitive chains, and the
        //    real, already-shipped FE6 map_settings BGM1/Field15-style overlaps.
        // ====================================================================

        [Fact]
        public void BuildCLayout_ContainedOverlap_MergesIntoOneOverlapChunk()
        {
            var fields = new List<StructMetadata.FieldDef>
            {
                F("BGM1", 0, StructMetadata.FieldType.Word),
                F("Field15", 1, StructMetadata.FieldType.Byte),
            };
            var (chunks, furthest) = StructExportCore.BuildCLayout(fields, 2);
            var chunk = Assert.Single(chunks);
            Assert.Equal(StructExportCore.CLayoutChunkKind.Overlap, chunk.Kind);
            Assert.Equal(0u, chunk.Offset);
            Assert.Equal(2u, chunk.Length);
            Assert.Equal(2, chunk.Fields.Count);
            Assert.Equal(2u, furthest);
        }

        [Fact]
        public void BuildCLayout_CrossingOverlap_MergesIntoOneOverlapChunk()
        {
            var fields = new List<StructMetadata.FieldDef>
            {
                F("A", 0, StructMetadata.FieldType.Word), // [0,2)
                F("B", 1, StructMetadata.FieldType.Word), // [1,3) crosses A
            };
            var (chunks, _) = StructExportCore.BuildCLayout(fields, 3);
            var chunk = Assert.Single(chunks);
            Assert.Equal(StructExportCore.CLayoutChunkKind.Overlap, chunk.Kind);
            Assert.Equal(0u, chunk.Offset);
            Assert.Equal(3u, chunk.Length);
        }

        [Fact]
        public void BuildCLayout_TransitiveChain_MergesAllIntoOneGroup()
        {
            // A:[0,2) B:[1,3) C:[2,4) — A and C never directly touch, but both
            // connect through B, so all three must land in ONE union, not two.
            var fields = new List<StructMetadata.FieldDef>
            {
                F("A", 0, StructMetadata.FieldType.Word),
                F("B", 1, StructMetadata.FieldType.Word),
                F("C", 2, StructMetadata.FieldType.Word),
            };
            var (chunks, _) = StructExportCore.BuildCLayout(fields, 4);
            var chunk = Assert.Single(chunks);
            Assert.Equal(StructExportCore.CLayoutChunkKind.Overlap, chunk.Kind);
            Assert.Equal(3, chunk.Fields.Count);
            Assert.Equal(0u, chunk.Offset);
            Assert.Equal(4u, chunk.Length);
        }

        [Fact]
        public void BuildCLayout_FE6MapSettings_GroupsKnownRealOverlapsAndCoversEveryByte()
        {
            var structDef = LoadMetadataStruct("struct_map_setting_fe6.txt", "MapSetting_FE6");
            var (chunks, furthest) = StructExportCore.BuildCLayout(structDef.Fields, structDef.DataSize);

            // Byte-coverage oracle at the model level: the chunk partition must
            // reconstruct the full declared span with zero gaps/overlaps between
            // chunks — every declared byte belongs to exactly one chunk.
            uint covered = 0;
            foreach (var chunk in chunks) covered += chunk.Length;
            Assert.Equal(structDef.DataSize, covered);
            Assert.True(furthest <= structDef.DataSize);

            // BGM1 (word @0x14) contains Field15 (byte @0x15): a real,
            // already-shipped metadata overlap this phase must group into one
            // union, never silently dropping the aliased byte.
            var bgm1Group = chunks.Find(ch => ch.Kind == StructExportCore.CLayoutChunkKind.Overlap
                && ch.Fields.Exists(f => f.Name == "BGM1"));
            Assert.NotNull(bgm1Group);
            Assert.Contains(bgm1Group.Fields, f => f.Name == "Field15");

            // Same shape repeats for BGM2/Field17 and BGM3/Field19.
            var bgm2Group = chunks.Find(ch => ch.Kind == StructExportCore.CLayoutChunkKind.Overlap
                && ch.Fields.Exists(f => f.Name == "BGM2"));
            Assert.NotNull(bgm2Group);
            Assert.Contains(bgm2Group.Fields, f => f.Name == "Field17");
        }

        [Fact]
        public void FormatCData_OverlapGroup_InitializesOnlyTheRawArm()
        {
            var structDef = Def("Overlap",
                F("BGM1", 0, StructMetadata.FieldType.Word),
                F("Field15", 1, StructMetadata.FieldType.Byte));
            byte[] raw = { 0x14, 0x00 };
            var row = MakeRow(0, raw); // no field values needed: only the raw arm is initialized

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "overlap_table", 2);

            Assert.Contains("union {", c);
            Assert.Contains("as_BGM1;", c);
            Assert.Contains("as_Field15;", c);
            Assert.Contains("uint8_t _overlap0_raw[2];", c);
            Assert.Contains("._overlap0_raw = { 0x14, 0x00 }", c);
            // Never a second initialized member of the same union.
            Assert.DoesNotContain(".as_BGM1 =", c);
            Assert.DoesNotContain(".as_Field15 =", c);
        }

        // ====================================================================
        // 4. Byte-coverage / reconstruction oracle
        // ====================================================================

        [Fact]
        public void FormatCData_ByteCoverageOracle_ReconstructsRawRowExactly()
        {
            var fields = new List<StructMetadata.FieldDef>
            {
                F("FieldA", 0, StructMetadata.FieldType.Byte),
                F("FieldB", 1, StructMetadata.FieldType.Word),
                F("FieldC", 2, StructMetadata.FieldType.Byte),
                F("FieldD", 5, StructMetadata.FieldType.DWord),
            };
            var structDef = Def("Oracle", fields.ToArray());
            uint resolvedEntrySize = 12;

            byte[] raw = { 0x11, 0x33, 0x44, 0xAA, 0xBB, 0x21, 0x22, 0x23, 0x24, 0x55, 0x66, 0x77 };
            var row = MakeRow(0, raw, ("FieldA", "0x11"), ("FieldD", "0x24232221"));

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "oracle_table", resolvedEntrySize);

            var (chunks, _) = StructExportCore.BuildCLayout(fields, resolvedEntrySize);
            string line = ExtractRowLine(c, "0x000");
            Assert.NotNull(line);

            var reconstructed = new byte[resolvedEntrySize];
            int gapCounter = 0, overlapCounter = 0;
            foreach (var chunk in chunks)
            {
                byte[] chunkBytes;
                switch (chunk.Kind)
                {
                    case StructExportCore.CLayoutChunkKind.Field:
                    {
                        var f = chunk.Fields[0];
                        string memberName = StructExportCore.SanitizeCIdentifier(f.Name);
                        int width = f.Type switch
                        {
                            StructMetadata.FieldType.Byte => 1,
                            StructMetadata.FieldType.Word => 2,
                            _ => 4,
                        };
                        chunkBytes = ExtractFieldLiteralBytes(line, memberName, width);
                        break;
                    }
                    case StructExportCore.CLayoutChunkKind.Overlap:
                    {
                        string memberName = $"_overlap{overlapCounter}_raw";
                        overlapCounter++;
                        chunkBytes = ExtractArrayBytes(line, memberName);
                        break;
                    }
                    case StructExportCore.CLayoutChunkKind.Gap:
                    {
                        string memberName = $"_gap{gapCounter}";
                        gapCounter++;
                        chunkBytes = ExtractArrayBytes(line, memberName);
                        break;
                    }
                    default: // Trailing
                        chunkBytes = ExtractArrayBytes(line, "_trailing");
                        break;
                }

                Assert.Equal((int)chunk.Length, chunkBytes.Length);
                Array.Copy(chunkBytes, 0, reconstructed, (int)chunk.Offset, chunkBytes.Length);
            }

            Assert.Equal(raw, reconstructed);
        }

        // ====================================================================
        // 5. Invalid stride / raw length rejection
        // ====================================================================

        [Fact]
        public void BuildCLayout_StrideSmallerThanFurthestFieldEnd_Throws()
        {
            var fields = new List<StructMetadata.FieldDef> { F("A", 0, StructMetadata.FieldType.DWord) };
            var ex = Assert.Throws<InvalidOperationException>(() => StructExportCore.BuildCLayout(fields, 2));
            Assert.Contains("smaller than", ex.Message);
        }

        [Fact]
        public void FormatCData_RawRowLengthMismatch_Throws()
        {
            var structDef = Def("Mismatch", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x01, 0x02 }, ("A", "0x01")), // raw is 2 bytes, stride is 1
            };
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "mismatch_table", 1));
            Assert.Contains("does not match", ex.Message);
        }

        // ====================================================================
        // 6. 300-row fixture: compiler-visible full-width designators above 0xFF
        // ====================================================================

        [Fact]
        public void FormatCData_300Rows_UsesListPositionsForUniqueFullWidthArrayDesignators()
        {
            var structDef = Def("Wide", F("Value", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>();
            for (uint i = 0; i < 300; i++)
            {
                byte b = (byte)(i % 256);
                // The tuple index is intentionally duplicate and outside the array.
                // Only list position may become a compiler-visible designator.
                rows.Add(MakeRow(0x1000, new byte[] { b }, ("Value", "0x" + b.ToString("X2"))));
            }

            string c = StructExportCore.FormatCData(rows, structDef, "wide_table", 1);

            Assert.Contains("[0x000] = /* [0x000]", c);
            Assert.Contains("[0x0FF] = /* [0x0FF]", c);
            Assert.Contains("[0x100] = /* [0x100]", c);
            Assert.Contains("[0x12B] = /* [0x12B]", c); // row 299 (last row), never a byte-truncated "0x2B"
            Assert.DoesNotContain("[0x1000] =", c);
            Assert.Contains("gFEBuilder_wide_table[300]", c);
            Assert.Contains("const uint32_t gFEBuilder_wide_tableCount = 300;", c);

            MatchCollection matches = Regex.Matches(c, @"(?m)^\s+\[(0x[0-9A-F]{3,})\]\s*=");
            var designators = new HashSet<string>();
            foreach (Match match in matches)
                designators.Add(match.Groups[1].Value);
            Assert.Equal(300, matches.Count);
            Assert.Equal(300, designators.Count);
        }

        // ====================================================================
        // 7. Packed row / 4-byte-aligned array object
        // ====================================================================

        [Fact]
        public void FormatCData_EmitsPackedStructAndAlignedArray()
        {
            var structDef = Def("Simple", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")) };

            string c = StructExportCore.FormatCData(rows, structDef, "simple", 1);

            Assert.Contains("struct __attribute__((packed)) FEBuilder_Simple {", c);
            Assert.Contains("gFEBuilder_simple[1] __attribute__((aligned(4))) = {", c);
        }

        // ====================================================================
        // 8. Zero-row GNU array + count contract, and normal nonzero count
        // ====================================================================

        [Fact]
        public void FormatCData_ZeroRows_EmitsGNUZeroLengthArrayWithNoInitializerAndZeroCount()
        {
            var structDef = Def("Empty", F("A", 0, StructMetadata.FieldType.Byte));

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])>(), structDef, "empty_table", 1);

            Assert.Contains("const struct FEBuilder_Empty gFEBuilder_empty_table[0] __attribute__((aligned(4)));", c);
            Assert.DoesNotContain("gFEBuilder_empty_table[0] __attribute__((aligned(4))) = {", c);
            Assert.Contains("const uint32_t gFEBuilder_empty_tableCount = 0;", c);
        }

        [Fact]
        public void FormatCData_NonzeroRows_EmitsExactCountSymbol()
        {
            var structDef = Def("NonZero", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")),
                MakeRow(1, new byte[] { 0x02 }, ("A", "0x02")),
            };

            string c = StructExportCore.FormatCData(rows, structDef, "nonzero_table", 1);

            Assert.Contains("gFEBuilder_nonzero_table[2] __attribute__((aligned(4))) = {", c);
            Assert.Contains("const uint32_t gFEBuilder_nonzero_tableCount = 2;", c);
        }

        // ====================================================================
        // 9. _Index comment neutralization
        // ====================================================================

        [Fact]
        public void FormatCData_NeutralizesCommentHazards_CRLFControlStarSlashBackslash()
        {
            var structDef = Def("Comment", F("A", 0, StructMetadata.FieldType.Byte));
            byte[] raw = { 0x01 };
            string hazardText = "0x00 Evil*/\\\r\nInjected\u0001Name";
            var fields = new Dictionary<string, string> { ["_Index"] = hazardText, ["A"] = "0x01" };
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { (0u, fields, raw) };

            string c = StructExportCore.FormatCData(rows, structDef, "hazard_table", 1);

            string[] lines = c.Split('\n');
            string rowLine = Array.Find(lines, l => l.Contains("[0x000]"));
            Assert.NotNull(rowLine);
            string trimmed = rowLine.TrimEnd('\r');

            // The comment + initializer must stay on ONE physical line despite the
            // injected CR/LF — proving the hazard text didn't split the comment.
            Assert.EndsWith("},", trimmed);
            // Exactly one "*/" — the real terminator we ourselves emit.
            Assert.Equal(1, CountSubstring(trimmed, "*/"));
            // No raw backslash or CR anywhere on the physical line.
            Assert.DoesNotContain("\\", trimmed);
            Assert.DoesNotContain("\r", trimmed);
        }

        [Fact]
        public void EscapeCComment_NeutralizesAllHazardCharacters()
        {
            string escaped = StructExportCore.EscapeCComment("a*/b\\c\r\nd\u0001e");
            Assert.DoesNotContain("*", escaped);
            Assert.DoesNotContain("/", escaped);
            Assert.DoesNotContain("\\", escaped);
            Assert.DoesNotContain("\r", escaped);
            Assert.DoesNotContain("\n", escaped);
        }

        // ====================================================================
        // 10. Identifier sanitization, keywords, leading digits, collisions
        // ====================================================================

        [Fact]
        public void SanitizeCIdentifier_HandlesKeywordsLeadingDigitsAndInvalidChars()
        {
            Assert.Equal("int_", StructExportCore.SanitizeCIdentifier("int"));
            Assert.Equal("_0Value", StructExportCore.SanitizeCIdentifier("0Value"));
            Assert.Equal("Foo_Bar_", StructExportCore.SanitizeCIdentifier("Foo Bar!"));
            Assert.Equal("field__thread", StructExportCore.SanitizeCIdentifier("__thread"));
            Assert.Equal("field_Upper", StructExportCore.SanitizeCIdentifier("_Upper"));
            Assert.Equal("_field", StructExportCore.SanitizeCIdentifier(""));
        }

        [Fact]
        public void FormatCData_EmitsSanitizedKeywordFieldName()
        {
            var structDef = Def("KeywordField", F("int", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x2A }, ("int", "0x2A")) };

            string c = StructExportCore.FormatCData(rows, structDef, "keyword_table", 1);

            Assert.Contains("uint8_t int_;", c);
            Assert.Contains(".int_ = 0x2A", c);
        }

        [Fact]
        public void FormatCData_PostSanitizationCollision_Throws()
        {
            var structDef = Def("Collide",
                F("Foo!", 0, StructMetadata.FieldType.Byte),
                F("Foo?", 1, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x01, 0x02 }, ("Foo!", "0x01"), ("Foo?", "0x02")),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "collide_table", 2));
            Assert.Contains("Foo_", ex.Message);
        }

        // ====================================================================
        // 11. Malformed / overflow numeric rejection
        // ====================================================================

        [Fact]
        public void FormatCData_OverflowFieldValue_Throws()
        {
            var structDef = Def("Overflow", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x00 }, ("A", "0x100")),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "overflow_table", 1));
            Assert.Contains("exceeds the maximum", ex.Message);
        }

        [Fact]
        public void FormatCData_MalformedFieldValue_Throws()
        {
            var structDef = Def("Malformed", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x00 }, ("A", "banana")),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "malformed_table", 1));
            Assert.Contains("Row 0x000, field 'A'", ex.Message);
        }

        [Fact]
        public void FormatCData_MissingFieldValue_Throws()
        {
            var structDef = Def("Missing", F("A", 0, StructMetadata.FieldType.Byte));
            var fields = new Dictionary<string, string> { ["_Index"] = "0x00 " }; // no "A"
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { (0u, fields, new byte[] { 0x00 }) };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "missing_table", 1));
            Assert.Contains("missing a value", ex.Message);
        }

        // ====================================================================
        // 12. Shared row-extraction seam: ExportTableRows/ExportTable never drift
        // ====================================================================

        [Fact]
        public void ExportTableRows_And_ExportTable_ProduceIdenticalTypedFieldsAndByteExactRaw()
        {
            byte[] data = new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("cdata-shared-seam.gba", data, "BE8E01"));

            var table = new StructExportCore.TableDef
            {
                Name = "cdata_shared_seam",
                GetBaseAddress = _ => 0x200,
                GetDataSize = _ => 4,
                GetEntryCount = _ => 3,
            };
            var structDef = new StructMetadata.StructDef
            {
                Name = "SharedSeam",
                DataSize = 4,
                Fields = new List<StructMetadata.FieldDef>
                {
                    new StructMetadata.FieldDef { Name = "A", Offset = 0, Type = StructMetadata.FieldType.Byte },
                    new StructMetadata.FieldDef { Name = "B", Offset = 2, Type = StructMetadata.FieldType.Word },
                },
            };

            for (int i = 0; i < 3; i++)
            {
                int addr = 0x200 + i * 4;
                data[addr + 0] = (byte)(0x10 + i);
                data[addr + 2] = (byte)(0x40 + i);
                data[addr + 3] = 0x00;
            }

            var rows = StructExportCore.ExportTableRows(rom, table, structDef);
            var flat = StructExportCore.ExportTable(rom, table, structDef);

            Assert.Equal(3, rows.Count);
            Assert.Equal(3, flat.Count);

            for (int i = 0; i < 3; i++)
            {
                Assert.Equal((uint)i, rows[i].index);
                Assert.Equal(flat[i].Count, rows[i].fields.Count);
                foreach (var kv in flat[i])
                    Assert.Equal(kv.Value, rows[i].fields[kv.Key]);

                byte[] expectedRaw = new byte[4];
                Array.Copy(data, 0x200 + i * 4, expectedRaw, 0, 4);
                Assert.Equal(expectedRaw, rows[i].raw);
            }
        }

        [Fact]
        public void ExportTable_TypedOnlyPath_DoesNotAllocateRawStrideBuffers()
        {
            const uint stride = 0x10000;
            const uint measuredRowCount = 64;
            uint rowCount = 1;
            byte[] data = new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("cdata-typed-only.gba", data, "BE8E01"));

            var table = new StructExportCore.TableDef
            {
                Name = "cdata_typed_only",
                GetBaseAddress = _ => 0x200,
                GetDataSize = _ => stride,
                GetEntryCount = _ => rowCount,
            };
            var structDef = Def("TypedOnly", F("A", 0, StructMetadata.FieldType.Byte));

            // Warm both paths so JIT/cold-start allocations cannot dominate the comparison.
            StructExportCore.ExportTable(rom, table, structDef);
            StructExportCore.ExportTableRows(rom, table, structDef);
            rowCount = measuredRowCount;

            long before = GC.GetAllocatedBytesForCurrentThread();
            var typedRows = StructExportCore.ExportTable(rom, table, structDef);
            long typedAllocations = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            var rawRows = StructExportCore.ExportTableRows(rom, table, structDef);
            long rawAllocations = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal((int)measuredRowCount, typedRows.Count);
            Assert.Equal((int)measuredRowCount, rawRows.Count);
            long rawPayloadBytes = (long)stride * measuredRowCount;
            Assert.True(
                rawAllocations >= typedAllocations + (rawPayloadBytes / 2),
                $"typed-only export allocated {typedAllocations:N0} bytes versus " +
                $"{rawAllocations:N0} for raw capture; expected the raw path to include " +
                $"approximately {rawPayloadBytes:N0} stride bytes.");
        }

        [Fact]
        public void ExportToCData_ReturnsRowCountAndResolvesTableLayoutOnce()
        {
            byte[] data = new byte[0x1000000];
            data[0x200] = 0x11;
            data[0x201] = 0x22;
            var rom = new ROM();
            Assert.True(rom.LoadLow("cdata-single-traversal.gba", data, "BE8E01"));

            int baseCalls = 0;
            int sizeCalls = 0;
            int countCalls = 0;
            var table = new StructExportCore.TableDef
            {
                Name = "cdata_single_traversal",
                GetBaseAddress = _ => { baseCalls++; return 0x200; },
                GetDataSize = _ => { sizeCalls++; return 1; },
                GetEntryCount = _ => { countCalls++; return 2; },
            };
            var structDef = Def("SingleTraversal", F("A", 0, StructMetadata.FieldType.Byte));
            // A SharedState test deliberately mutates process-global TEMP/TMP. Keep this
            // parallel-safe by writing beside the already-loaded, writable test assembly.
            string path = Path.Combine(AppContext.BaseDirectory, $"test_{Guid.NewGuid():N}.c");

            try
            {
                int exportedCount = StructExportCore.ExportToCData(rom, table, structDef, path);

                Assert.Equal(2, exportedCount);
                Assert.Equal(1, baseCalls);
                Assert.Equal(1, sizeCalls);
                Assert.Equal(1, countCalls);
                Assert.True(File.Exists(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ExportToCData_NullRom_Throws()
        {
            var table = new StructExportCore.TableDef
            {
                Name = "t",
                GetBaseAddress = _ => 0x200,
                GetDataSize = _ => 1,
                GetEntryCount = _ => 1,
            };
            var structDef = Def("T", F("A", 0, StructMetadata.FieldType.Byte));
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.c");
            Assert.Throws<ArgumentNullException>(() => StructExportCore.ExportToCData(null, table, structDef, path));
        }

        // ====================================================================
        // 13. Compiler smoke: compile the ACTUAL FormatCData output (never a
        //     hand-written lookalike) for the five accepted-plan representative
        //     shapes. Mirrors the repo's established "opportunistic tool" pattern
        //     (EventAssemblerCompileCoreTests' ColorzCore.exe detection): prefer an
        //     already-installed arm-none-eabi-gcc, else host gcc, download/install
        //     nothing, and SkippableFact-skip cleanly when neither is present so the
        //     run reports a genuine skip rather than silently no-op'ing. Uses the
        //     existing cross-platform ProcessRunnerCore (ArgumentList, never a shell
        //     string, capped timeout, never throws) instead of hand-rolling process
        //     invocation.
        // ====================================================================

        /// <summary>
        /// Detect an already-installed C compiler capable of compiling the ACTUAL
        /// <see cref="StructExportCore.FormatCData"/> output: prefer devkitARM/ARM
        /// <c>arm-none-eabi-gcc</c> (the real GBA target), else host <c>gcc</c>.
        /// Downloads/installs nothing — a missing compiler is a clean
        /// environment-limitation SKIP, never a failure (Ubuntu CI is the
        /// authoritative compile gate per the accepted plan). Returns null when
        /// neither is found.
        /// </summary>
        static string DetectCCompiler()
        {
            foreach (string candidate in new[] { "arm-none-eabi-gcc", "gcc" })
            {
                var probe = ProcessRunnerCore.Run(candidate, new[] { "--version" }, null, 10_000);
                if (probe.Started && probe.ExitCode == 0) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Write <paramref name="cSource"/> (UTF-8, no BOM) into <paramref name="tempDir"/>
        /// as <c>&lt;caseName&gt;.c</c> and compile it with exactly
        /// <c>-std=gnu11 -Wall -Werror -c &lt;source&gt; -o &lt;object&gt;</c> via
        /// <see cref="ProcessRunnerCore"/>. Every assertion message is prefixed with
        /// <paramref name="caseName"/> so a failure in one of the five cases is
        /// unambiguous even though all five share one <c>[SkippableFact]</c>.
        /// </summary>
        static void CompileGeneratedC(
            string compiler,
            string tempDir,
            string caseName,
            string cSource,
            params string[] extraCompilerArgs)
        {
            string srcPath = Path.Combine(tempDir, caseName + ".c");
            string objPath = Path.Combine(tempDir, caseName + ".o");
            File.WriteAllText(srcPath, cSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var arguments = new List<string> { "-std=gnu11", "-Wall", "-Werror" };
            if (extraCompilerArgs != null) arguments.AddRange(extraCompilerArgs);
            arguments.AddRange(new[] { "-c", srcPath, "-o", objPath });
            var result = ProcessRunnerCore.Run(
                compiler,
                arguments,
                tempDir,
                60_000);

            Assert.True(result.Started, $"[{caseName}] failed to start '{compiler}': {result.ErrorMessage}");
            Assert.True(result.ExitCode == 0,
                $"[{caseName}] '{compiler} {string.Join(" ", arguments)}' exited " +
                $"{result.ExitCode}.\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");
            Assert.True(File.Exists(objPath),
                $"[{caseName}] compiler reported exit 0 but no object file was produced at {objPath}.");
        }

        [SkippableFact]
        public void FormatCData_CompilerSmoke_FiveRepresentativeShapesCompileCleanly()
        {
            string compiler = DetectCCompiler();
            Skip.If(compiler == null,
                "Neither arm-none-eabi-gcc nor host gcc is installed in this environment — " +
                "skipping the GNU11 compiler smoke (downloads/installs nothing; Ubuntu CI is " +
                "the authoritative compile gate per the accepted plan).");

            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-cdata-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // 1) FE8-style mixed byte/word/dword/pointer row (+ an interior gap byte).
                {
                    var structDef = Def("Mixed",
                        // These are real GNU tokens/reserved prefixes. Metadata sanitization
                        // must repair every one while preserving the mixed-width layout.
                        F("__thread", 0, StructMetadata.FieldType.Byte),
                        F("__auto_type", 1, StructMetadata.FieldType.Word),
                        F("__label__", 4, StructMetadata.FieldType.DWord),
                        F("__alignof__", 8, StructMetadata.FieldType.Pointer));
                    byte[] raw = { 0x7F, 0x34, 0x12, 0xAB, 0xEF, 0xBE, 0xAD, 0xDE, 0x56, 0x34, 0x12, 0x08 };
                    var row = MakeRow(0, raw,
                        ("__thread", "0x7F"), ("__auto_type", "0x1234"),
                        ("__label__", "0xDEADBEEF"), ("__alignof__", "0x08123456"));
                    string c = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "mixed_table", 12);
                    CompileGeneratedC(compiler, tempDir, "fe8_style_mixed", c);

                    // The nearest valid spelling to a stdint typedef collision must still
                    // compile when used verbatim as the public data/count symbol pair.
                    string nearCollision = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])> { row },
                        structDef,
                        "mixed_table",
                        12,
                        "uint8_t_");
                    CompileGeneratedC(compiler, tempDir, "stdint_near_collision_override", nearCollision);

                    var macroStruct = Def("MacroNames",
                        F("linux", 0, StructMetadata.FieldType.Byte),
                        F("unix", 1, StructMetadata.FieldType.Byte));
                    var macroRow = MakeRow(
                        0,
                        new byte[] { 0x11, 0x22 },
                        ("linux", "0x11"),
                        ("unix", "0x22"));
                    string macroC = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])> { macroRow },
                        macroStruct,
                        "macro_names",
                        2,
                        "linux");
                    CompileGeneratedC(
                        compiler,
                        tempDir,
                        "predefined_macro_identifiers",
                        macroC,
                        "-Dlinux=1",
                        "-Dunix=1",
                        "-DlinuxCount=1",
                        "-DFEBuilder_MacroNames=1");
                }

                // 2) FE6-style connected (contained) overlap: word BGM1 aliasing byte Field15.
                {
                    var structDef = Def("Overlap",
                        F("BGM1", 0, StructMetadata.FieldType.Word),
                        F("Field15", 1, StructMetadata.FieldType.Byte));
                    byte[] raw = { 0x14, 0x00 };
                    var row = MakeRow(0, raw);
                    string c = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "overlap_table", 2);
                    CompileGeneratedC(compiler, tempDir, "fe6_style_overlap", c);
                }

                // 3) Arbitrary crossing overlap: two words offset by 1 byte.
                {
                    var structDef = Def("Crossing",
                        F("A", 0, StructMetadata.FieldType.Word),
                        // Deliberately collide with the formatter's preferred synthetic
                        // positioning-pad name; the generated nested struct must choose a
                        // distinct helper and still compile cleanly.
                        F("_pad", 1, StructMetadata.FieldType.Word));
                    byte[] raw = { 0x01, 0x02, 0x03 };
                    var row = MakeRow(0, raw);
                    string c = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "crossing_table", 3);
                    CompileGeneratedC(compiler, tempDir, "crossing_overlap", c);
                }

                // 4) >255-row output (300 rows): exercises full-width [0x12B]-style
                // list-position designators independently of caller tuple indices.
                {
                    var structDef = Def("Wide", F("Value", 0, StructMetadata.FieldType.Byte));
                    var rows = new List<(uint, Dictionary<string, string>, byte[])>();
                    for (uint i = 0; i < 300; i++)
                    {
                        byte b = (byte)(i % 256);
                        rows.Add(MakeRow(0x1000, new byte[] { b }, ("Value", "0x" + b.ToString("X2"))));
                    }
                    string c = StructExportCore.FormatCData(rows, structDef, "wide_table", 1);
                    CompileGeneratedC(compiler, tempDir, "over_255_rows", c);
                }

                // 5) Zero-row output: the GNU zero-length-array + zero-count contract.
                {
                    var structDef = Def("Empty", F("A", 0, StructMetadata.FieldType.Byte));
                    string c = StructExportCore.FormatCData(
                        new List<(uint, Dictionary<string, string>, byte[])>(), structDef, "empty_table", 1);
                    CompileGeneratedC(compiler, tempDir, "zero_rows", c);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        // ====================================================================
        // 14. Blue-team-requested direct regressions
        // ====================================================================

        [Fact]
        public void FormatCData_CrossingOverlap_EmitsExpectedPadPositioningMember()
        {
            var structDef = Def("Crossing",
                F("A", 0, StructMetadata.FieldType.Word),
                F("B", 1, StructMetadata.FieldType.Word));
            byte[] raw = { 0x01, 0x02, 0x03 };
            var row = MakeRow(0, raw);

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "crossing_table", 3);

            // A starts exactly at the group's base offset (rel==0) → no pad member.
            Assert.Contains("struct __attribute__((packed)) { uint16_t A; } as_A;", c);
            // B starts 1 byte into the group → a synthetic 1-byte _pad positions it.
            Assert.Contains("struct __attribute__((packed)) { uint8_t _pad[1]; uint16_t B; } as_B;", c);
            Assert.Contains("uint8_t _overlap0_raw[3];", c);
        }

        [Fact]
        public void FormatCData_OverlapFieldNamedPad_UsesDistinctPositioningHelper()
        {
            var structDef = Def("PadCollision",
                F("A", 0, StructMetadata.FieldType.Word),
                F("_pad", 1, StructMetadata.FieldType.Byte));
            byte[] raw = { 0x01, 0x02 };
            var row = MakeRow(0, raw);

            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row }, structDef, "pad_collision", 2);

            Assert.Contains(
                "struct __attribute__((packed)) { uint8_t _pad_[1]; uint8_t _pad; } as__pad;",
                c);
            Assert.DoesNotContain("uint8_t _pad[1]; uint8_t _pad;", c);
        }

        [Fact]
        public void FormatCData_FieldNameCollidesWithSyntheticGapHelper_ThrowsDeterministically()
        {
            // "_gap0" sanitizes to the literal name BuildCData would otherwise pick
            // for the FIRST unmapped metadata gap. A real field named "_gap0" plus a
            // genuine interior gap (offsets 1..3, between the two declared fields)
            // must collide deterministically rather than one silently shadowing the
            // other in the row struct's flat (anonymous-union-promoted) namespace.
            var structDef = Def("GapCollision",
                F("_gap0", 0, StructMetadata.FieldType.Byte),
                F("Z", 3, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])>
            {
                MakeRow(0, new byte[] { 0x01, 0xAA, 0xBB, 0x02 }, ("_gap0", "0x01"), ("Z", "0x02")),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                StructExportCore.FormatCData(rows, structDef, "gap_collision_table", 4));
            Assert.Contains("_gap0", ex.Message);
            Assert.Contains("field '_gap0'", ex.Message);
            Assert.Contains("metadata gap", ex.Message);
        }

        // ====================================================================
        // 15. --c-symbol (#1939 Phase B1): TryValidateCSymbol + FormatCData override
        // ====================================================================

        [Theory]
        [InlineData("gMyTable")]
        [InlineData("g_Item_Data_2")]
        [InlineData("A")]
        public void TryValidateCSymbol_WellFormedIdentifier_ReturnsTrue(string symbol)
        {
            Assert.True(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Null(error);
        }

        [Fact]
        public void TryValidateCSymbol_Empty_ReturnsFalse()
        {
            Assert.False(StructExportCore.TryValidateCSymbol("", out string error));
            Assert.Contains("empty", error);
        }

        [Fact]
        public void TryValidateCSymbol_Null_ReturnsFalse()
        {
            Assert.False(StructExportCore.TryValidateCSymbol(null, out string error));
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("0Value")]
        [InlineData("9gTable")]
        public void TryValidateCSymbol_LeadingDigit_ReturnsFalse(string symbol)
        {
            Assert.False(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Contains("start with a letter", error);
        }

        [Theory]
        [InlineData("g-Item")]
        [InlineData("g Item")]
        [InlineData("g.Item")]
        [InlineData("g!Item")]
        public void TryValidateCSymbol_InvalidCharacter_ReturnsFalse(string symbol)
        {
            Assert.False(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Contains("invalid character", error);
        }

        [Theory]
        [InlineData("int")]
        [InlineData("struct")]
        [InlineData("_Static_assert")]
        [InlineData("__attribute__")]
        [InlineData("__thread")]
        [InlineData("__auto_type")]
        [InlineData("__label__")]
        [InlineData("__alignof__")]
        [InlineData("asm")]
        [InlineData("typeof")]
        public void TryValidateCSymbol_ReservedKeyword_ReturnsFalse(string symbol)
        {
            Assert.False(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Contains("reserved keyword", error);
        }

        [Theory]
        [InlineData("_leadingUnderscore")]
        [InlineData("_Upper")]
        [InlineData("__implementationName")]
        public void TryValidateCSymbol_FileScopeReservedPrefix_ReturnsFalse(string symbol)
        {
            Assert.False(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Contains("reserved", error);
        }

        [Theory]
        [InlineData("uint8_t")]
        [InlineData("uint16_t")]
        [InlineData("uint32_t")]
        [InlineData("int24_t")]
        [InlineData("uint_least32_t")]
        [InlineData("int_fast64_t")]
        [InlineData("INT32_MAX")]
        [InlineData("UINT_FAST16_MAX")]
        [InlineData("SIZE_MAX")]
        public void TryValidateCSymbol_StdintReservedIdentifier_ReturnsFalse(string symbol)
        {
            Assert.False(StructExportCore.TryValidateCSymbol(symbol, out string error));
            Assert.Contains("<stdint.h>", error);
        }

        [Fact]
        public void FormatCData_PreprocessorMacroNames_AreUndefGuardedAfterIncludes()
        {
            var structDef = Def("MacroNames",
                F("linux", 0, StructMetadata.FieldType.Byte),
                F("unix", 1, StructMetadata.FieldType.Byte));
            var row = MakeRow(
                0,
                new byte[] { 0x11, 0x22 },
                ("linux", "0x11"),
                ("unix", "0x22"));

            Assert.True(StructExportCore.TryValidateCSymbol("linux", out string error), error);
            string c = StructExportCore.FormatCData(
                new List<(uint, Dictionary<string, string>, byte[])> { row },
                structDef,
                "macro_names",
                2,
                "linux").Replace("\r\n", "\n", StringComparison.Ordinal);

            foreach (string identifier in new[] { "FEBuilder_MacroNames", "linux", "linuxCount", "unix" })
            {
                Assert.Contains($"#ifdef {identifier}\n#undef {identifier}\n#endif", c);
            }
            Assert.Contains("const struct FEBuilder_MacroNames linux[1]", c);
            Assert.Contains("const uint32_t linuxCount = 1;", c);
        }

        [Fact]
        public void FormatCData_NoSymbolOverride_UsesDeterministicDefaultSymbols()
        {
            var structDef = Def("Sym", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")) };

            string c = StructExportCore.FormatCData(rows, structDef, "sym_table", 1);

            Assert.Contains("gFEBuilder_sym_table[1]", c);
            Assert.Contains("const uint32_t gFEBuilder_sym_tableCount = 1;", c);
        }

        [Fact]
        public void FormatCData_WithSymbolOverride_UsesOverrideVerbatimForDataAndCountSymbols()
        {
            var structDef = Def("Sym", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")) };

            string c = StructExportCore.FormatCData(rows, structDef, "sym_table", 1, "gCustomItemData");

            // The override replaces the data/count symbols verbatim...
            Assert.Contains("gCustomItemData[1]", c);
            Assert.Contains("const uint32_t gCustomItemDataCount = 1;", c);
            // ...but the row TYPE name still derives from the struct name, not the override
            // or the table name — type naming stays deterministic either way.
            Assert.Contains("struct FEBuilder_Sym", c);
            Assert.DoesNotContain("gFEBuilder_sym_table", c);
        }

        [Fact]
        public void FormatCData_InvalidSymbolOverride_Throws()
        {
            var structDef = Def("Sym", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")) };

            var ex = Assert.Throws<ArgumentException>(() =>
                StructExportCore.FormatCData(rows, structDef, "sym_table", 1, "0BadSymbol"));
            Assert.Contains("0BadSymbol", ex.Message);
        }

        [Fact]
        public void FormatCData_KeywordSymbolOverride_Throws()
        {
            var structDef = Def("Sym", F("A", 0, StructMetadata.FieldType.Byte));
            var rows = new List<(uint, Dictionary<string, string>, byte[])> { MakeRow(0, new byte[] { 0x01 }, ("A", "0x01")) };

            var ex = Assert.Throws<ArgumentException>(() =>
                StructExportCore.FormatCData(rows, structDef, "sym_table", 1, "struct"));
            Assert.Contains("struct", ex.Message);
        }
    }
}
