// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>Public contract tests for the schema-v1 font-library seams.</summary>
    public sealed class FontLibraryCoreTests
    {
        const string ZeroHash =
            "0000000000000000000000000000000000000000000000000000000000000000";

        [Fact]
        public void BulkManifest_LegacyMode_PreservesHistoricalAcceptance()
        {
            string text =
                "// an old comment\r\n"
                + "\r\n"
                + "A\t text \t 0 \ttext_0000004a.PnG\textra\r\n";

            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.Parse(
                    text, FontBulkManifestMode.LegacyCompatible);

            Assert.True(parsed.Success, parsed.Error);
            FontBulkManifestRow row = Assert.Single(parsed.Rows);
            Assert.Equal("A", row.Character);
            Assert.Equal("text", row.Type);
            Assert.Equal(0, row.Width);
            Assert.Equal(0x4Au, row.Moji);
            Assert.Equal("text_0000004a.PnG", row.Filename);
            Assert.True(FontBulkImportCore.TryParseMojiFromFilename(
                row.Filename, out uint wrapped));
            Assert.Equal(row.Moji, wrapped);
        }

        [Fact]
        public void BulkManifest_LegacyStreamingVisitorRunsBeforeLaterParseError()
        {
            int visits = 0;
            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.ParseRows(
                    "A\ttext\t0\ttext_004a.PnG\textra\n"
                    + "malformed-later-row\n",
                    FontBulkManifestMode.LegacyCompatible,
                    row =>
                    {
                        visits++;
                        Assert.Equal(0x4Au, row.Moji);
                        return "";
                    });

            Assert.False(parsed.Success);
            Assert.Equal(1, visits);
        }

        [Fact]
        public void BulkManifest_LegacyMode_RetainsExactLowercaseTypes()
        {
            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.Parse(
                    "A\tTEXT\t9\ttext_41.png\n",
                    FontBulkManifestMode.LegacyCompatible);

            Assert.False(parsed.Success);
            Assert.Empty(parsed.Rows);
        }

        [Theory]
        [InlineData("A\ttext\t1\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\r\nA\ttext\t1\ttext_41.png\r\n")]
        [InlineData("//char\ttype\tWidth\tFilename\n\nA\ttext\t1\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t0\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\tTEXT\t1\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t01\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t1\ttext_0041.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t1\ttext_41.png\textra\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nAB\ttext\t1\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t1\ttext_41.png\nA\ttext\t2\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nB\ttext\t1\ttext_42.png\nA\ttext\t1\ttext_41.png\n")]
        [InlineData("//char\ttype\tWidth\tFilename\nA\ttext\t1\ttext_41.png\nA\titem\t1\titem_41.png\n")]
        public void BulkManifest_StrictMode_RejectsNonCanonicalForms(
            string text)
        {
            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.Parse(
                    text, FontBulkManifestMode.StrictLibrary);
            Assert.False(parsed.Success);
            Assert.NotEmpty(parsed.Error);
        }

        [Fact]
        public void BulkManifest_StrictFormatter_IsCanonicalAndRoundTripsScalar()
        {
            string character = char.ConvertFromUtf32(0x1F642);
            string text = FontBulkManifestCore.Format(
                new[]
                {
                    new FontBulkManifestRow
                    {
                        Character = character,
                        Type = "item",
                        Width = 16,
                        Filename = "item_1234.png",
                        Moji = 0x1234,
                    },
                },
                FontBulkManifestMode.StrictLibrary);

            Assert.Equal(
                FontBulkManifestCore.Header
                + "\n" + character + "\titem\t16\titem_1234.png\n",
                text);
            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.Parse(
                    text, FontBulkManifestMode.StrictLibrary);
            Assert.True(parsed.Success, parsed.Error);
            Assert.Equal(0x1234u, Assert.Single(parsed.Rows).Moji);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZhIndexShift_IsAnInverseAndRequiresZeroPadding(
            bool item)
        {
            var native = new byte[
                FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int i = 0; i < native.Length; i++)
                native[i] = (byte)(i & 3);

            byte[] shifted =
                FontGlyphZHIndexCore.ShiftTo16x16(native, item);

            Assert.NotNull(shifted);
            Assert.True(FontGlyphZHIndexCore.HasZeroPadding(shifted, item));
            Assert.Equal(
                native,
                FontGlyphZHIndexCore.UnshiftTo16x13(shifted, item));
            shifted[0] = 3;
            if (FontGlyphZHIndexCore.VanillaShift(item) != 0)
            {
                Assert.False(
                    FontGlyphZHIndexCore.HasZeroPadding(shifted, item));
                Assert.Throws<ArgumentException>(() =>
                    FontGlyphZHIndexCore.UnshiftTo16x13(shifted, item));
            }

            byte[] packed =
                FontGlyphZHCore.PackGlyphZHBytes(native, 7);
            byte[] unpacked =
                FontGlyphZHIndexCore.UnpackGlyphBytes(packed, 7);
            Assert.NotNull(unpacked);
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                Assert.Equal(
                    native.Skip(y * FontGlyphZHCore.GLYPH_W)
                        .Take(7),
                    unpacked.Skip(y * FontGlyphZHCore.GLYPH_W)
                        .Take(7));
                Assert.All(
                    unpacked.Skip(
                            y * FontGlyphZHCore.GLYPH_W + 7)
                        .Take(FontGlyphZHCore.GLYPH_W - 7),
                    value => Assert.Equal((byte)0, value));
            }
        }

        [Theory]
        [InlineData(FontLibraryFormat.Main16x16, FontLibraryStyle.Item)]
        [InlineData(FontLibraryFormat.Main16x16, FontLibraryStyle.Text)]
        [InlineData(FontLibraryFormat.Zh16x13, FontLibraryStyle.Item)]
        [InlineData(FontLibraryFormat.Zh16x13, FontLibraryStyle.Text)]
        public void IndexedPng_IsExactCanonicalGrammarForAllFourPalettes(
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            var indices = new byte[256];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = (byte)(i & 3);

            byte[] png = IndexedPngCore.Encode(indices, format, style);

            Assert.Equal(188, png.Length);
            Assert.Equal(
                new[] { "IHDR", "PLTE", "tRNS", "IDAT", "IEND" },
                ReadChunkTypes(png));
            // External PNG canonical vector: the complete tRNS chunk bytes and
            // CRC are fixed by the PNG specification, not generated by the
            // codec under test.
            Assert.Equal(
                Convert.FromHexString(
                    "0000000474524E5300FFFFFFB32D4088"),
                png.AsSpan(57, 16).ToArray());
            Assert.Equal(
                CanonicalPalette(format, style),
                png.AsSpan(41, 12).ToArray());
            // The one IDAT uses zlib 0x78 0x01 and one final stored block.
            Assert.Equal(
                CanonicalStoredZlib,
                png.AsSpan(81, CanonicalStoredZlib.Length).ToArray());
            // Complete external byte vector with only the five CRC fields
            // normalized to zero. Every non-CRC byte is hard-coded below; the
            // independent CRC oracle then covers those remaining fields.
            byte[] normalized = (byte[])png.Clone();
            ZeroChunkCrcs(normalized);
            Assert.Equal(
                CanonicalPngWithZeroedCrcs(format, style),
                normalized);
            AssertChunkCrcs(png);

            IndexedPngCoreReadResult read =
                IndexedPngCore.Read(png, format, style);
            Assert.True(read.Success, read.Error);
            Assert.Equal(indices, read.Indices);
            Assert.Equal(
                CanonicalPalette(format, style),
                IndexedPngCore.GetPaletteRgb(format, style));
            FontLibraryStyle otherStyle =
                style == FontLibraryStyle.Item
                    ? FontLibraryStyle.Text
                    : FontLibraryStyle.Item;
            Assert.False(
                IndexedPngCore.Read(png, format, otherStyle).Success);

            byte[] trailing = png.Concat(new byte[] { 0 }).ToArray();
            Assert.False(
                IndexedPngCore.Read(trailing, format, style).Success);
            byte[] corruptCrc = (byte[])png.Clone();
            corruptCrc[32] ^= 1;
            Assert.False(
                IndexedPngCore.Read(corruptCrc, format, style).Success);
            byte[] shortTrns = (byte[])png.Clone();
            shortTrns[60] = 3;
            Assert.Contains(
                "length",
                IndexedPngCore.Read(
                    shortTrns, format, style).Error,
                StringComparison.OrdinalIgnoreCase);
            byte[] wrongChunk = (byte[])png.Clone();
            wrongChunk[61] = (byte)'x';
            Assert.Contains(
                "Expected tRNS",
                IndexedPngCore.Read(
                    wrongChunk, format, style).Error);
            byte[] trnsCrc = (byte[])png.Clone();
            trnsCrc[72] ^= 1;
            Assert.Contains(
                "CRC-32",
                IndexedPngCore.Read(
                    trnsCrc, format, style).Error);
        }

        [Fact]
        public void IndexedPng_RemapAndEnginePackingHaveLockedIndexOrder()
        {
            var indices = new byte[256];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = (byte)(i & 3);

            Assert.Equal(
                indices,
                IndexedPngCore.RemapRasterizerIndices(
                    indices, FontLibraryFormat.Main16x16));
            byte[] zh = IndexedPngCore.RemapRasterizerIndices(
                indices, FontLibraryFormat.Zh16x13);
            Assert.Equal(new byte[] { 0, 2, 1, 3 }, zh.Take(4).ToArray());

            byte[] packed = IndexedPngCore.PackEngineTile(indices);
            Assert.Equal(0xE4, packed[0]);
            Assert.Equal(indices, IndexedPngCore.UnpackEngineTile(packed));
        }

        [Fact]
        public void SchemaV1Parser_RejectsUnknownDuplicateAndNonUtf8Input()
        {
            string valid = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":[{\"scalar\":\"U+0041\",\"moji\":65}]}");
            Assert.True(FontLibraryManifestCore.Parse(valid).Success);

            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "\"schemaVersion\":1,",
                    "\"schemaVersion\":1,\"schemaVersion\":1,",
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "\"jobs\":",
                    "\"unexpected\":true,\"jobs\":",
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                new byte[] { 0xC3, 0x28 }).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "\"path\":\"font.ttf\"",
                    "\"path\":\"../font.ttf\"",
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    ZeroHash,
                    new string('A', 64),
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "\"id\":\"job\"",
                    "\"id\":\"CON\"",
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "U+0041",
                    "U+041",
                    StringComparison.Ordinal)).Success);
            Assert.False(FontLibraryManifestCore.Parse(
                valid.Replace(
                    "\"start\":\"U+0041\",\"end\":\"U+0041\"",
                    "\"start\":\"U+D7FF\",\"end\":\"U+E000\"",
                    StringComparison.Ordinal)).Success);
        }

        [Fact]
        public void SchemaV1_RequiresCompleteFontAndLicenseProvenance()
        {
            Assert.Equal(
                16 * 1024 * 1024,
                FontLibraryManifestCore.MaxLicenseBytes);
            string valid = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":[{\"scalar\":\"U+0041\",\"moji\":65}]}");
            Assert.True(FontLibraryManifestCore.Parse(valid).Success);

            string[] requiredFragments =
            {
                "\"byteLength\":1,",
                "\"family\":\"Test Family\",",
                "\"version\":\"Version 1.0\",",
                "\"sourceUrl\":\"https://example.invalid/font\",",
                "\"licenseId\":\"OFL-1.1\",",
                "\"licenseFile\":\"OFL.txt\",",
                ",\"sourceUrl\":\"https://example.invalid/license\"",
            };
            foreach (string fragment in requiredFragments)
            {
                FontLibraryManifestParseResult missing =
                    FontLibraryManifestCore.Parse(
                        valid.Replace(
                            fragment, "", StringComparison.Ordinal));
                Assert.False(
                    missing.Success,
                    "Manifest unexpectedly accepted missing " + fragment);
                Assert.Equal(
                    FontLibraryFailureKind.Schema,
                    missing.FailureKind);
            }
        }

        [Fact]
        public void Planner_IsPureStableMojiThenItemText_AndRejectsWhitespace()
        {
            string json = ManifestJson(
                styles: "[\"text\",\"item\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0042\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":100},"
                    + "{\"scalar\":\"U+0042\",\"moji\":2}]}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult result = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.True(result.Success, result.Error);
            Assert.Equal(
                new[]
                {
                    (0x42, 2u, FontLibraryStyle.Item),
                    (0x42, 2u, FontLibraryStyle.Text),
                    (0x41, 100u, FontLibraryStyle.Item),
                    (0x41, 100u, FontLibraryStyle.Text),
                },
                result.Plan.Jobs[0].Slots
                    .Select(s => (
                        s.UnicodeScalar, s.Moji, s.Style))
                    .ToArray());

            string whitespace = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0020\",\"end\":\"U+0020\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":[{\"scalar\":\"U+0020\",\"moji\":32}]}");
            FontLibraryManifestParseResult whitespaceParsed =
                FontLibraryManifestCore.Parse(whitespace);
            Assert.True(whitespaceParsed.Success, whitespaceParsed.Error);
            FontLibraryPlanResult whitespacePlan =
                FontLibraryPlanner.Plan(
                whitespaceParsed.Manifest,
                new FontLibraryPlannerInput());
            Assert.False(whitespacePlan.Success);
            Assert.Equal(
                FontLibraryFailureKind.Validation,
                whitespacePlan.FailureKind);
            Assert.Contains("scalarRanges", whitespacePlan.Error);
            Assert.Contains("U+0020", whitespacePlan.Error);
        }

        [Fact]
        public void Planner_DiagnosesWhitespaceInFixedMappingSource()
        {
            string json = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":1},"
                    + "{\"scalar\":\"U+0020\",\"moji\":2}]}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.False(planned.Success);
            Assert.Equal(
                FontLibraryFailureKind.Validation,
                planned.FailureKind);
            Assert.Contains("mapping.entries", planned.Error);
            Assert.Contains("U+0020", planned.Error);
        }

        [Fact]
        public void Planner_RejectsMojiCollisions()
        {
            string json = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0042\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":1},"
                    + "{\"scalar\":\"U+0042\",\"moji\":1}]}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);
            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());
            Assert.False(planned.Success);
            Assert.Contains("same moji", planned.Error);
            Assert.Equal(
                FontLibraryFailureKind.Conflict,
                planned.FailureKind);
        }

        [Fact]
        public void Planner_ZhValidatesResolvedEngineSlots()
        {
            string highBits = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":16810369}]}",
                format: FontLibraryFormat.Zh16x13);
            FontLibraryManifestParseResult highBitsParsed =
                FontLibraryManifestCore.Parse(highBits);
            Assert.True(highBitsParsed.Success, highBitsParsed.Error);
            FontLibraryPlanResult highBitsPlan =
                FontLibraryPlanner.Plan(
                    highBitsParsed.Manifest,
                    new FontLibraryPlannerInput());
            Assert.False(highBitsPlan.Success);
            Assert.Equal(
                FontLibraryFailureKind.Validation,
                highBitsPlan.FailureKind);
            Assert.Contains("16-bit", highBitsPlan.Error);

            string alias = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0042\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":65},"
                    + "{\"scalar\":\"U+0042\",\"moji\":16449}]}",
                format: FontLibraryFormat.Zh16x13);
            FontLibraryManifestParseResult aliasParsed =
                FontLibraryManifestCore.Parse(alias);
            Assert.True(aliasParsed.Success, aliasParsed.Error);
            FontLibraryPlanResult aliasPlan = FontLibraryPlanner.Plan(
                aliasParsed.Manifest,
                new FontLibraryPlannerInput());
            Assert.False(aliasPlan.Success);
            Assert.Equal(
                FontLibraryFailureKind.Conflict,
                aliasPlan.FailureKind);
            Assert.Contains("same ZH engine glyph slot", aliasPlan.Error);

            string missing = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":256}]}",
                format: FontLibraryFormat.Zh16x13);
            FontLibraryManifestParseResult missingParsed =
                FontLibraryManifestCore.Parse(missing);
            Assert.True(missingParsed.Success, missingParsed.Error);
            FontLibraryPlanResult missingPlan =
                FontLibraryPlanner.Plan(
                    missingParsed.Manifest,
                    new FontLibraryPlannerInput());
            Assert.False(missingPlan.Success);
            Assert.Equal(
                FontLibraryFailureKind.Validation,
                missingPlan.FailureKind);
            Assert.Contains("no engine glyph slot", missingPlan.Error);

            string valid = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+4F60\",\"end\":\"U+4F60\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+4F60\",\"moji\":33153}]}",
                format: FontLibraryFormat.Zh16x13);
            FontLibraryManifestParseResult validParsed =
                FontLibraryManifestCore.Parse(valid);
            Assert.True(validParsed.Success, validParsed.Error);
            FontLibraryPlanResult validPlan = FontLibraryPlanner.Plan(
                validParsed.Manifest,
                new FontLibraryPlannerInput());
            Assert.True(validPlan.Success, validPlan.Error);
            Assert.Equal(
                0x8181u,
                Assert.Single(validPlan.Plan.Jobs[0].Slots).Moji);
        }

        [Fact]
        public void Planner_ClassifiesDuplicateFixedScalarAsConflict()
        {
            string json = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0041\",\"moji\":1},"
                    + "{\"scalar\":\"U+0041\",\"moji\":2}]}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.False(planned.Success);
            Assert.Equal(
                FontLibraryFailureKind.Conflict,
                planned.FailureKind);
            Assert.Equal(2, FontLibraryFailure.ExitCode(
                planned.FailureKind));
            Assert.Contains("duplicates U+0041", planned.Error);
        }

        [Fact]
        public void Planner_RangeCompactsSelectedAscendingScalarsAndClassifiesExhaustion()
        {
            string compact = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+0041\",\"end\":\"U+0041\"},"
                    + "{\"start\":\"U+0043\",\"end\":\"U+0043\"}]",
                mapping:
                    "{\"mode\":\"range\","
                    + "\"unicodeStart\":\"U+0041\","
                    + "\"unicodeEnd\":\"U+005A\","
                    + "\"mojiStart\":10,\"mojiEnd\":11}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(compact);
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.True(planned.Success, planned.Error);
            Assert.Equal(
                new[] { (0x41, 10u), (0x43, 11u) },
                planned.Plan.Jobs[0].Slots
                    .Select(slot => (slot.UnicodeScalar, slot.Moji))
                    .ToArray());

            string exhausted = compact.Replace(
                "{\"start\":\"U+0043\",\"end\":\"U+0043\"}",
                "{\"start\":\"U+0042\",\"end\":\"U+0043\"}",
                StringComparison.Ordinal);
            FontLibraryManifestParseResult exhaustedParsed =
                FontLibraryManifestCore.Parse(exhausted);
            Assert.True(exhaustedParsed.Success, exhaustedParsed.Error);
            FontLibraryPlanResult exhaustion = FontLibraryPlanner.Plan(
                exhaustedParsed.Manifest, new FontLibraryPlannerInput());
            Assert.False(exhaustion.Success);
            Assert.Equal(
                FontLibraryFailureKind.Exhaustion,
                exhaustion.FailureKind);
            Assert.Equal(
                2,
                FontLibraryFailure.ExitCode(exhaustion.FailureKind));
        }

        [Fact]
        public void Planner_CorpusSkipsOnlyConfiguredSeparatorsAndDiagnosesWhitespace()
        {
            string json = ManifestJson(
                styles: "[\"text\"]",
                ranges: "[]",
                mapping:
                    "{\"mode\":\"range\","
                    + "\"unicodeStart\":\"U+0041\","
                    + "\"unicodeEnd\":\"U+0043\","
                    + "\"mojiStart\":1,\"mojiEnd\":3}")
                .Replace(
                    "\"scalarRanges\":[],",
                    "\"corpus\":{\"path\":\"corpus.txt\",\"sha256\":\""
                    + ZeroHash
                    + "\",\"separators\":[\"U+0020\",\"U+000A\"]},"
                    + "\"scalarRanges\":[],",
                    StringComparison.Ordinal);
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);
            var input = new FontLibraryPlannerInput();
            input.Corpora.Add("job", "A \nC");

            FontLibraryPlanResult planned =
                FontLibraryPlanner.Plan(parsed.Manifest, input);

            Assert.True(planned.Success, planned.Error);
            Assert.Equal(
                new[] { 0x41, 0x43 },
                planned.Plan.Jobs[0].Slots
                    .Select(slot => slot.UnicodeScalar)
                    .ToArray());

            input.Corpora["job"] = "A\tC";
            FontLibraryPlanResult unconfigured =
                FontLibraryPlanner.Plan(parsed.Manifest, input);
            Assert.False(unconfigured.Success);
            Assert.Equal(
                FontLibraryFailureKind.Validation,
                unconfigured.FailureKind);
            Assert.Contains("U+0009", unconfigured.Error);
        }

        [Fact]
        public void Planner_EnforcesRowCapAfterStyleExpansionBeforeSlotAllocation()
        {
            string json = ManifestJson(
                styles: "[\"item\",\"text\"]",
                ranges:
                    "[{\"start\":\"U+10000\",\"end\":\"U+18000\"}]",
                mapping:
                    "{\"mode\":\"range\","
                    + "\"unicodeStart\":\"U+10000\","
                    + "\"unicodeEnd\":\"U+18000\","
                    + "\"mojiStart\":0,\"mojiEnd\":65535}");
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.False(planned.Success);
            Assert.Null(planned.Plan);
            Assert.Equal(
                FontLibraryFailureKind.Exhaustion,
                planned.FailureKind);
            Assert.Contains("65538 rows", planned.Error);
        }

        [Fact]
        public void Planner_EnforcesSingleSelectedRunRowCapAcrossJobs()
        {
            const string prefix = "{\"schemaVersion\":1,\"jobs\":[";
            string firstManifest = ManifestJson(
                styles: "[\"text\"]",
                ranges:
                    "[{\"start\":\"U+10000\",\"end\":\"U+17FFF\"}]",
                mapping:
                    "{\"mode\":\"range\","
                    + "\"unicodeStart\":\"U+10000\","
                    + "\"unicodeEnd\":\"U+17FFF\","
                    + "\"mojiStart\":0,\"mojiEnd\":65535}");
            Assert.StartsWith(prefix, firstManifest);
            string firstJob = firstManifest.Substring(
                prefix.Length,
                firstManifest.Length - prefix.Length - 2);
            string secondJob = firstJob
                .Replace(
                    "\"id\":\"job\"",
                    "\"id\":\"job-b\"",
                    StringComparison.Ordinal)
                .Replace(
                    "U+10000",
                    "U+18000",
                    StringComparison.Ordinal)
                .Replace(
                    "U+17FFF",
                    "U+20000",
                    StringComparison.Ordinal);
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(
                    prefix + firstJob + "," + secondJob + "]}");
            Assert.True(parsed.Success, parsed.Error);

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());

            Assert.False(planned.Success);
            Assert.Null(planned.Plan);
            Assert.Equal(
                FontLibraryFailureKind.Exhaustion,
                planned.FailureKind);
            Assert.Contains("Selected-run", planned.Error);
            Assert.Contains("65536-row cap", planned.Error);
        }

        [Fact]
        public void Planner_ConsumesVerifiedCorpusAsUnicodeScalarsWithoutInference()
        {
            string json = ManifestJson(
                styles: "[\"text\"]",
                ranges: "[]",
                mapping:
                    "{\"mode\":\"fixed\",\"entries\":["
                    + "{\"scalar\":\"U+0042\",\"moji\":9},"
                    + "{\"scalar\":\"U+1F642\",\"moji\":8}]}")
                .Replace(
                    "\"scalarRanges\":[],",
                    "\"corpus\":{\"path\":\"corpus.txt\",\"sha256\":\""
                    + ZeroHash + "\"},\"scalarRanges\":[],",
                    StringComparison.Ordinal);
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);
            var input = new FontLibraryPlannerInput();
            input.Corpora.Add(
                "job", "B" + char.ConvertFromUtf32(0x1F642));

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, input);

            Assert.True(planned.Success, planned.Error);
            Assert.Equal(
                new[] { 0x1F642, 0x42 },
                planned.Plan.Jobs[0].Slots
                    .Select(slot => slot.UnicodeScalar)
                    .ToArray());
        }

        [Fact]
        public void BuilderValidatorRoundTrip_AreDeterministicAndDoNotRerasterize()
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16,
                "[\"text\",\"item\"]");
            var factory = new FakeFaceFactory();

            FontLibraryBuildResult first = Build(
                plan, font, license, factory);
            Assert.True(first.Success, first.Error);
            int renderCalls = factory.RenderCalls;
            Assert.Equal(2, renderCalls);
            Assert.Contains(
                "\t7\t",
                Encoding.UTF8.GetString(
                    first.Package.Files["job/job.fontall.txt"]));

            FontLibraryValidationResult validation =
                FontLibraryPackageValidatorCore.Validate(
                    plan, first.Package);
            Assert.True(validation.Success, validation.Error);
            FontLibraryValidationResult roundTrip =
                FontLibraryPackageValidatorCore.RoundTrip(
                    plan, first.Package);
            Assert.True(roundTrip.Success, roundTrip.Error);
            Assert.Equal(renderCalls, factory.RenderCalls);
            Assert.NotEqual(
                validation.Report.Jobs[0].PackedSha256,
                validation.Report.Jobs[0].PngSha256);
            Assert.True(FontLibraryReportFormatter.TryParse(
                first.Package.Files[
                    FontLibraryBuilderCore.PackageReportPath],
                out FontLibraryReport embedded,
                out string embeddedError),
                embeddedError);
            Assert.Equal(
                first.Report.PayloadTreeSha256,
                embedded.PayloadTreeSha256);
            Assert.Equal("", embedded.FullTreeSha256);
            Assert.Equal(
                first.Report.FullTreeSha256,
                FontLibraryHashCore.ComputeTreeSha256(
                    first.Package.Files));
            FontLibraryValidationResult oracleValidation =
                FontLibraryPackageValidatorCore.ValidateWithOracle(
                    plan, first.Package, first.Report);
            Assert.True(
                oracleValidation.Success,
                oracleValidation.Error);

            FontLibraryBuildResult second = Build(
                plan, font, license, new FakeFaceFactory());
            Assert.True(second.Success, second.Error);
            Assert.Equal(
                first.Report.FullTreeSha256,
                second.Report.FullTreeSha256);
            AssertTreesEqual(first.Package, second.Package);

            second.Package.Files.Add(
                "job/unexpected.txt", new byte[] { 1 });
            Assert.False(FontLibraryPackageValidatorCore.Validate(
                plan, second.Package).Success);
        }

        [Fact]
        public void DryRun_VerifiesProvenanceButNeverOpensFaceOrCreatesPackage()
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");
            var throwing = new ThrowingFaceFactory();
            var input = new FontLibraryBuildInput
            {
                Plan = plan,
                FaceFactory = throwing,
            };
            input.Assets.Add("job", new FontLibraryJobAssets
            {
                FontFileData = font,
                LicenseFileData = license,
            });

            FontLibraryBuildResult result =
                FontLibraryBuilderCore.DryRun(input);

            Assert.True(result.Success, result.Error);
            Assert.Null(result.Package);
            Assert.Equal(0, throwing.OpenCalls);
            Assert.Equal("dry-run", result.Report.Mode);
            FontLibraryJobReport report = Assert.Single(result.Report.Jobs);
            Assert.Equal(font.Length, report.FontByteLength);
            Assert.Equal("Test Family", report.FontFamily);
            Assert.Equal("Version 1.0", report.FontVersion);
            Assert.Equal(
                FontLibraryHashCore.ComputeSha256(font),
                report.FontSha256);
            Assert.Equal(
                "https://example.invalid/font",
                report.FontSourceUrl);
            Assert.Equal("OFL-1.1", report.LicenseId);
            Assert.Equal("OFL.txt", report.LicenseFile);
            Assert.Equal(
                FontLibraryHashCore.ComputeSha256(license),
                report.LicenseSha256);
            Assert.Equal(
                "https://example.invalid/license",
                report.LicenseSourceUrl);
            Assert.Equal("", report.PackedSha256);
            Assert.Equal("", report.PngSha256);
            Assert.Equal("", result.Report.PayloadTreeSha256);
            Assert.Equal("", result.Report.FullTreeSha256);

            var rasterThrowing = new ThrowingRasterFaceFactory();
            input.FaceFactory = rasterThrowing;
            FontLibraryBuildResult second =
                FontLibraryBuilderCore.DryRun(input);
            Assert.True(second.Success, second.Error);
            Assert.Equal(0, rasterThrowing.OpenCalls);
            Assert.Equal(0, rasterThrowing.RasterCalls);
        }

        [Fact]
        public void ExternalFullTreeOracle_IsCheckedBeforeEmbeddedReport()
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");
            FontLibraryBuildResult built = Build(
                plan, font, license, new FakeFaceFactory());
            Assert.True(built.Success, built.Error);
            Assert.True(FontLibraryReportFormatter.TryParse(
                FontLibraryText.Utf8(
                    FontLibraryReportFormatter.Format(built.Report)),
                out FontLibraryReport wrongOracle,
                out string parseError),
                parseError);
            wrongOracle.FullTreeSha256 = ZeroHash;
            built.Package.Files[
                FontLibraryBuilderCore.PackageReportPath] =
                Encoding.UTF8.GetBytes("not-json\n");

            FontLibraryValidationResult result =
                FontLibraryPackageValidatorCore.ValidateWithOracle(
                    plan, built.Package, wrongOracle);

            Assert.False(result.Success);
            Assert.Equal(
                FontLibraryFailureKind.Tamper,
                result.FailureKind);
            Assert.Contains("full-tree", result.Error);
            Assert.DoesNotContain("package-report.json is invalid", result.Error);
        }

        [Fact]
        public void EmbeddedReport_RejectsEvenEmptyFullTreeField()
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");
            FontLibraryBuildResult built = Build(
                plan, font, license, new FakeFaceFactory());
            Assert.True(built.Success, built.Error);
            Assert.True(FontLibraryReportFormatter.TryParse(
                built.Package.Files[
                    FontLibraryBuilderCore.PackageReportPath],
                out FontLibraryReport embedded,
                out string parseError),
                parseError);
            Assert.Equal("", embedded.FullTreeSha256);
            built.Package.Files[
                FontLibraryBuilderCore.PackageReportPath] =
                FontLibraryText.Utf8(
                    FontLibraryReportFormatter.Format(embedded));

            FontLibraryValidationResult result =
                FontLibraryPackageValidatorCore.Validate(
                    plan, built.Package);

            Assert.False(result.Success);
            Assert.Equal(
                FontLibraryFailureKind.Tamper,
                result.FailureKind);
            Assert.Contains("payload-only shape", result.Error);
        }

        [Fact]
        public void DryRun_EnforcesSixteenMiBLicenseCapBeforeFaceOpen()
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] declaredLicense =
                Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font,
                declaredLicense,
                FontLibraryFormat.Main16x16,
                "[\"text\"]");
            var throwing = new ThrowingFaceFactory();
            var input = new FontLibraryBuildInput
            {
                Plan = plan,
                FaceFactory = throwing,
            };
            input.Assets.Add("job", new FontLibraryJobAssets
            {
                FontFileData = font,
                LicenseFileData = new byte[
                    FontLibraryManifestCore.MaxLicenseBytes + 1],
            });

            FontLibraryBuildResult result =
                FontLibraryBuilderCore.DryRun(input);

            Assert.False(result.Success);
            Assert.Equal(
                FontLibraryFailureKind.Provenance,
                result.FailureKind);
            Assert.Contains("license asset size", result.Error);
            Assert.Equal(0, throwing.OpenCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Builder_TypedBlankGlyphIsSemanticExitTwo(
            bool disposeThrows)
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");

            FontLibraryBuildResult result = Build(
                plan,
                font,
                license,
                new TypedBlankFaceFactory(disposeThrows));

            Assert.False(result.Success);
            Assert.Equal(
                FontLibraryFailureKind.MissingGlyph,
                result.FailureKind);
            Assert.Equal(2, FontLibraryFailure.ExitCode(result.FailureKind));
            Assert.Contains("all-background", result.Error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Builder_DisposesFaceWhenStrictOpenFailsOrThrows(
            bool throwAfterAssign)
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");
            var factory = new AssignedFailingFaceFactory(throwAfterAssign);

            FontLibraryBuildResult result = Build(
                plan, font, license, factory);

            Assert.False(result.Success);
            Assert.Equal(FontLibraryFailureKind.Font, result.FailureKind);
            Assert.Equal(1, FontLibraryFailure.ExitCode(result.FailureKind));
            Assert.Equal(1, factory.DisposeCalls);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Builder_ClassifiesNativeFaceExceptionsAndDisposes(
            bool throwDuringGlyphQuery)
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Main16x16, "[\"text\"]");
            var factory =
                new ThrowingNativeFaceFactory(throwDuringGlyphQuery);

            FontLibraryBuildResult result = Build(
                plan, font, license, factory);

            Assert.False(result.Success);
            Assert.Equal(FontLibraryFailureKind.Font, result.FailureKind);
            Assert.Equal(1, FontLibraryFailure.ExitCode(result.FailureKind));
            Assert.Equal(1, factory.DisposeCalls);
            Assert.Contains(
                throwDuringGlyphQuery
                    ? "availability query"
                    : "rasterization threw",
                result.Error);
        }

        [Theory]
        [InlineData(FontLibraryFailureKind.None, 0)]
        [InlineData(FontLibraryFailureKind.Usage, 1)]
        [InlineData(FontLibraryFailureKind.Schema, 1)]
        [InlineData(FontLibraryFailureKind.Path, 1)]
        [InlineData(FontLibraryFailureKind.Io, 1)]
        [InlineData(FontLibraryFailureKind.Font, 1)]
        [InlineData(FontLibraryFailureKind.Provenance, 1)]
        [InlineData(FontLibraryFailureKind.MissingGlyph, 2)]
        [InlineData(FontLibraryFailureKind.Conflict, 2)]
        [InlineData(FontLibraryFailureKind.Exhaustion, 2)]
        [InlineData(FontLibraryFailureKind.Tamper, 2)]
        [InlineData(FontLibraryFailureKind.Validation, 2)]
        [InlineData(FontLibraryFailureKind.RoundtripMismatch, 2)]
        public void FailureKinds_HaveExactExitContract(
            FontLibraryFailureKind kind,
            int expected)
        {
            Assert.Equal(expected, FontLibraryFailure.ExitCode(kind));
        }

        [Theory]
        [InlineData(FontLibraryStyle.Item)]
        [InlineData(FontLibraryStyle.Text)]
        public void Builder_ZhOutputAlwaysHasZeroPadding(
            FontLibraryStyle style)
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            string styles = style == FontLibraryStyle.Item
                ? "[\"item\"]" : "[\"text\"]";
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font, license, FontLibraryFormat.Zh16x13, styles);

            FontLibraryBuildResult built = Build(
                plan, font, license, new FakeFaceFactory());

            Assert.True(built.Success, built.Error);
            Assert.True(FontLibraryReportFormatter.TryParse(
                FontLibraryText.Utf8(
                    FontLibraryReportFormatter.Format(built.Report)),
                out FontLibraryReport immutableOracle,
                out string oracleError),
                oracleError);
            byte[] png = built.Package.Files[
                "job/" + (style == FontLibraryStyle.Item
                    ? "item_41.png" : "text_41.png")];
            IndexedPngCoreReadResult read = IndexedPngCore.Read(
                png, FontLibraryFormat.Zh16x13, style);
            Assert.True(read.Success, read.Error);
            Assert.True(FontGlyphZHIndexCore.HasZeroPadding(
                read.Indices, style == FontLibraryStyle.Item));
            Assert.True(FontLibraryPackageValidatorCore.RoundTrip(
                plan, built.Package).Success);

            // A structurally canonical PNG can still contain pixels that the
            // declared-width 40-byte engine stream cannot represent. Validate
            // accepts the internally hash-consistent input; roundtrip
            // canonicalizes through packed bytes and classifies byte drift.
            int shift = FontGlyphZHIndexCore.VanillaShift(
                style == FontLibraryStyle.Item);
            read.Indices[shift * 16 + 15] = 3; // outside fake width 7
            byte[] lossyPng = IndexedPngCore.Encode(
                read.Indices, FontLibraryFormat.Zh16x13, style);
            built.Package.Files[
                "job/" + (style == FontLibraryStyle.Item
                    ? "item_41.png" : "text_41.png")] = lossyPng;
            List<FontLibrarySlotRecord> records =
                FontLibrarySlotTsv.Parse(FontLibraryText.DecodeStrictUtf8NoBom(
                    built.Package.Files["job/slots.tsv"]));
            Assert.Single(records);
            records[0].PngSha256 =
                FontLibraryHashCore.ComputeSha256(lossyPng);
            built.Package.Files["job/slots.tsv"] =
                FontLibraryText.Utf8(FontLibrarySlotTsv.Format(records));
            built.Report.Jobs[0].PngSha256 =
                FontLibraryHashCore.ComputeSha256(lossyPng);
            built.Report.Jobs[0].Glyphs[0].PngSha256 =
                FontLibraryHashCore.ComputeSha256(lossyPng);
            built.Report.PayloadTreeSha256 =
                FontLibraryHashCore.ComputeTreeSha256(
                    built.Package.Files.Where(file =>
                        file.Key
                            != FontLibraryBuilderCore.PackageReportPath));
            built.Package.Files[
                FontLibraryBuilderCore.PackageReportPath] =
                FontLibraryText.Utf8(
                    FontLibraryReportFormatter.FormatEmbedded(built.Report));
            built.Report.FullTreeSha256 =
                FontLibraryHashCore.ComputeTreeSha256(
                    built.Package.Files);
            Assert.True(FontLibraryPackageValidatorCore.Validate(
                plan, built.Package).Success);
            FontLibraryValidationResult oracleMismatch =
                FontLibraryPackageValidatorCore.ValidateWithOracle(
                    plan, built.Package, immutableOracle);
            Assert.False(oracleMismatch.Success);
            Assert.Equal(
                FontLibraryFailureKind.Tamper,
                oracleMismatch.FailureKind);
            FontLibraryValidationResult drift =
                FontLibraryPackageValidatorCore.RoundTrip(
                    plan, built.Package);
            Assert.False(drift.Success);
            Assert.True(drift.ReproducibilityDrift);
        }

        [Theory]
        [InlineData(FontLibraryStyle.Item)]
        [InlineData(FontLibraryStyle.Text)]
        public void Builder_ZhCropsOrdinaryRasterTop13BeforePackageShift(
            FontLibraryStyle style)
        {
            byte[] font = Encoding.ASCII.GetBytes("verified-font");
            byte[] license = Encoding.UTF8.GetBytes("OFL test license");
            FontLibraryPlan plan = MakeSingleScalarPlan(
                font,
                license,
                FontLibraryFormat.Zh16x13,
                style == FontLibraryStyle.Item
                    ? "[\"item\"]" : "[\"text\"]");

            FontLibraryBuildResult built = Build(
                plan,
                font,
                license,
                new DiscardedRowsOnlyFaceFactory());

            // The only ink is in ordinary raster row 13. Selecting shifted
            // rows (+2/+1) would incorrectly retain it; the established top-13
            // crop discards it and classifies the resulting blank glyph.
            Assert.False(built.Success);
            Assert.Equal(
                FontLibraryFailureKind.MissingGlyph,
                built.FailureKind);
            Assert.Contains("all-background", built.Error);
        }

        [Fact]
        public void TreeHash_IsOrdinalSlashPathAndEnumerationIndependent()
        {
            var first = new Dictionary<string, byte[]>
            {
                ["z/file"] = new byte[] { 2 },
                ["a/file"] = new byte[] { 1 },
            };
            var second = new Dictionary<string, byte[]>
            {
                ["a/file"] = new byte[] { 1 },
                ["z/file"] = new byte[] { 2 },
            };
            Assert.Equal(
                FontLibraryHashCore.ComputeTreeSha256(first),
                FontLibraryHashCore.ComputeTreeSha256(second));
            Assert.Throws<ArgumentException>(() =>
                FontLibraryHashCore.ComputeTreeSha256(
                    new Dictionary<string, byte[]>
                    {
                        ["a\\file"] = new byte[] { 1 },
                    }));
        }

        [Fact]
        public void Report_LargeCjkRowSetOverOneMiB_RoundTripsWithinCap()
        {
            const int glyphCount = 5000;
            string hash = new string('a', 64);
            var report = new FontLibraryReport
            {
                Mode = "generate",
                Oracle = "generation",
                ManifestSha256 = hash,
                PayloadTreeSha256 = hash,
                FullTreeSha256 = hash,
            };
            var job = new FontLibraryJobReport
            {
                Id = "cjk",
                Locale = "zh-Hans",
                Format = "zh-16x13",
                ScalarCount = glyphCount,
                RowCount = glyphCount,
                NativeWidth = 16,
                NativeHeight = 13,
                PngWidth = 16,
                PngHeight = 16,
                FontSize = 12,
                FontByteLength = 1,
                FontFamily = "Fixture",
                FontVersion = "Version 1",
                FontSha256 = hash,
                FontSourceUrl = "https://example.invalid/font",
                LicenseId = "OFL-1.1",
                LicenseFile = "OFL.txt",
                LicenseSha256 = hash,
                LicenseSourceUrl = "https://example.invalid/license",
                PackedSha256 = hash,
                PngSha256 = hash,
            };
            for (int i = 0; i < glyphCount; i++)
            {
                int scalar = 0x1000 + i;
                job.Glyphs.Add(new FontLibraryGlyphReport
                {
                    UnicodeScalar = scalar,
                    Character = char.ConvertFromUtf32(scalar),
                    Style = "item",
                    Moji = (uint)i,
                    Width = 8,
                    NativeWidth = 16,
                    NativeHeight = 13,
                    PngWidth = 16,
                    PngHeight = 16,
                    Filename = FontBulkManifestCore.CanonicalFilename(
                        "item", (uint)i),
                    PackedSha256 = hash,
                    PngSha256 = hash,
                });
            }
            report.Jobs.Add(job);

            byte[] bytes = Encoding.UTF8.GetBytes(
                FontLibraryReportFormatter.Format(report));

            Assert.True(bytes.Length > 1024 * 1024);
            Assert.True(bytes.Length < FontLibraryReportFormatter.MaxReportBytes);
            Assert.True(FontLibraryReportFormatter.TryParse(
                bytes,
                out FontLibraryReport parsed,
                out string error),
                error);
            Assert.Equal(glyphCount, parsed.Jobs[0].Glyphs.Count);
            Assert.Equal(bytes, Encoding.UTF8.GetBytes(
                FontLibraryReportFormatter.Format(parsed)));
        }

        [Fact]
        public void SfntIdentity_DecodesMacintoshRomanInsteadOfLatin1()
        {
            byte[] family = { 0x43, 0x61, 0x66, 0x8E }; // Café in Mac Roman
            byte[] version = Encoding.ASCII.GetBytes("Version 1");
            const int nameOffset = 28;
            const int stringsOffset = 30;
            var sfnt = new byte[
                nameOffset + stringsOffset + family.Length + version.Length];
            WriteBe32(sfnt, 0, 0x00010000);
            WriteBe16(sfnt, 4, 1);
            WriteBe32(sfnt, 12, 0x6E616D65); // name
            WriteBe32(sfnt, 20, nameOffset);
            WriteBe32(sfnt, 24, sfnt.Length - nameOffset);
            WriteBe16(sfnt, nameOffset + 2, 2);
            WriteBe16(sfnt, nameOffset + 4, stringsOffset);
            WriteMacNameRecord(
                sfnt, nameOffset + 6, nameId: 1,
                length: family.Length, stringOffset: 0);
            WriteMacNameRecord(
                sfnt, nameOffset + 18, nameId: 5,
                length: version.Length, stringOffset: family.Length);
            Array.Copy(
                family, 0, sfnt, nameOffset + stringsOffset, family.Length);
            Array.Copy(
                version, 0, sfnt,
                nameOffset + stringsOffset + family.Length,
                version.Length);

            Assert.True(SfntFontIdentityCore.TryRead(
                sfnt, out SfntFontIdentity identity, out string error),
                error);
            Assert.Equal("Café", identity.Family);
            Assert.Equal("Version 1", identity.Version);
        }

        static void WriteMacNameRecord(
            byte[] data,
            int offset,
            int nameId,
            int length,
            int stringOffset)
        {
            WriteBe16(data, offset, 1);
            WriteBe16(data, offset + 2, 0);
            WriteBe16(data, offset + 4, 0);
            WriteBe16(data, offset + 6, nameId);
            WriteBe16(data, offset + 8, length);
            WriteBe16(data, offset + 10, stringOffset);
        }

        static void WriteBe16(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        static void WriteBe32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        [Fact]
        public void StrictSkiaFace_LoadsExactBytesChecksCoverageAndRejectsBlank()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory, "Fonts", "Tuffy-Regular.ttf");
            Assert.True(File.Exists(path), "Tuffy fixture is missing.");
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(218708, bytes.Length);
            Assert.True(SfntFontIdentityCore.TryRead(
                bytes,
                out SfntFontIdentity tuffyIdentity,
                out string tuffyIdentityError),
                tuffyIdentityError);
            Assert.Equal("Tuffy", tuffyIdentity.Family);
            Assert.Equal(
                "Version 1.272; ttfautohint (v1.6)",
                tuffyIdentity.Version);
            var factory = new SkiaFontLibraryFaceFactory();

            Assert.False(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = Array.Empty<byte>(),
                    ExpectedFamily = "Tuffy",
                    ExpectedVersion =
                        "Version 1.272; ttfautohint (v1.6)",
                    Size = 12,
                },
                out _, out _));
            Assert.False(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = bytes,
                    ExpectedFamily = "Wrong Family",
                    ExpectedVersion =
                        "Version 1.272; ttfautohint (v1.6)",
                    Size = 12,
                },
                out _, out string familyError));
            Assert.Contains("does not match", familyError);
            Assert.False(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = bytes,
                    ExpectedFamily = "Tuffy",
                    ExpectedVersion = "Version 0 (wrong)",
                    Size = 12,
                },
                out _, out string versionError));
            Assert.Contains("does not match", versionError);
            Assert.False(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = Encoding.ASCII.GetBytes("not-a-font"),
                    ExpectedFamily = "Tuffy",
                    ExpectedVersion =
                        "Version 1.272; ttfautohint (v1.6)",
                    Size = 12,
                },
                out _, out _));
            Assert.True(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = bytes,
                    ExpectedFamily = "Tuffy",
                    ExpectedVersion =
                        "Version 1.272; ttfautohint (v1.6)",
                    Size = 12,
                },
                out IFontLibraryFace face,
                out string openError),
                openError);
            using (face)
            {
                Assert.True(face.HasGlyph('A'));
                Assert.True(face.TryRasterizeGlyph(
                    'A', false, 0,
                    out byte[] tile, out int width, out string renderError),
                    renderError);
                Assert.Equal(64, tile.Length);
                Assert.InRange(width, 1, 16);
                Assert.False(face.HasGlyph(0x10FFFF));

                Assert.True(face.HasGlyph(' '));
                Assert.False(face.TryRasterizeGlyph(
                    ' ', false, 0,
                    out _, out _,
                    out FontLibraryFaceFailureKind blankKind,
                    out string blankError));
                Assert.Equal(
                    FontLibraryFaceFailureKind.MissingGlyph,
                    blankKind);
                Assert.Contains("all-background", blankError);
            }
            Assert.False(face.HasGlyph('A'));

            string notoPath = Path.Combine(
                AppContext.BaseDirectory,
                "Fonts",
                "NotoSansCJKsc-Subset.otf");
            Assert.True(File.Exists(notoPath), "Noto CJK fixture is missing.");
            byte[] notoBytes = File.ReadAllBytes(notoPath);
            Assert.Equal(2901316, notoBytes.Length);
            Assert.True(SfntFontIdentityCore.TryRead(
                notoBytes,
                out SfntFontIdentity notoIdentity,
                out string notoIdentityError),
                notoIdentityError);
            Assert.Equal("Noto Sans CJK SC", notoIdentity.Family);
            Assert.Equal(
                "Version 2.004;hotconv 1.0.118;makeotfexe 2.5.65603",
                notoIdentity.Version);
            Assert.True(factory.TryOpenFace(
                new FontLibraryFaceSpec
                {
                    FontFileData = notoBytes,
                    ExpectedFamily = "Noto Sans CJK SC",
                    ExpectedVersion =
                        "Version 2.004;hotconv 1.0.118;makeotfexe 2.5.65603",
                    Size = 12,
                },
                out IFontLibraryFace notoFace,
                out string notoError),
                notoError);
            using (notoFace)
            {
                Assert.True(notoFace.HasGlyph(0x4F60));
                Assert.True(notoFace.TryRasterizeGlyph(
                    0x4F60, true, 0,
                    out byte[] tile, out int width, out string renderError),
                    renderError);
                Assert.Equal(64, tile.Length);
                Assert.InRange(width, 1, 16);
            }
        }

        [Fact]
        public void RealNotoFixture_BuildsStrictZhTopCropAndShiftGeometry()
        {
            byte[] font = File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "Fonts",
                "NotoSansCJKsc-Subset.otf"));
            byte[] license = File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "Fonts",
                "Noto-OFL.txt"));
            string json = ManifestJson(
                    "[\"item\",\"text\"]",
                    "[{\"start\":\"U+4F60\",\"end\":\"U+4F60\"}]",
                    "{\"mode\":\"fixed\",\"entries\":[{"
                    + "\"scalar\":\"U+4F60\",\"moji\":33153}]}",
                    FontLibraryFormat.Zh16x13,
                    FontLibraryHashCore.ComputeSha256(font),
                    FontLibraryHashCore.ComputeSha256(license),
                    font.Length)
                .Replace(
                    "\"family\":\"Test Family\"",
                    "\"family\":\"Noto Sans CJK SC\"",
                    StringComparison.Ordinal)
                .Replace(
                    "\"version\":\"Version 1.0\"",
                    "\"version\":\"Version 2.004;hotconv 1.0.118;"
                    + "makeotfexe 2.5.65603\"",
                    StringComparison.Ordinal);
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);
            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());
            Assert.True(planned.Success, planned.Error);

            FontLibraryBuildResult built = Build(
                planned.Plan,
                font,
                license,
                new SkiaFontLibraryFaceFactory());

            Assert.True(built.Success, built.Error);
            foreach (FontLibraryStyle style in new[]
                { FontLibraryStyle.Item, FontLibraryStyle.Text })
            {
                string filename = style == FontLibraryStyle.Item
                    ? "item_8181.png" : "text_8181.png";
                IndexedPngCoreReadResult read = IndexedPngCore.Read(
                    built.Package.Files["job/" + filename],
                    FontLibraryFormat.Zh16x13,
                    style);
                Assert.True(read.Success, read.Error);
                Assert.True(FontGlyphZHIndexCore.HasZeroPadding(
                    read.Indices,
                    style == FontLibraryStyle.Item));
                Assert.Contains(
                    FontGlyphZHIndexCore.UnshiftTo16x13(
                        read.Indices,
                        style == FontLibraryStyle.Item),
                    index => index != 0);
            }
            Assert.True(FontLibraryPackageValidatorCore.RoundTrip(
                planned.Plan, built.Package).Success);
        }

        static FontLibraryPlan MakeSingleScalarPlan(
            byte[] font,
            byte[] license,
            FontLibraryFormat format,
            string styles)
        {
            string json = ManifestJson(
                styles,
                "[{\"start\":\"U+0041\",\"end\":\"U+0041\"}]",
                "{\"mode\":\"fixed\",\"entries\":[{\"scalar\":\"U+0041\",\"moji\":65}]}",
                format,
                FontLibraryHashCore.ComputeSha256(font),
                FontLibraryHashCore.ComputeSha256(license),
                font.Length);
            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(json);
            Assert.True(parsed.Success, parsed.Error);
            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, new FontLibraryPlannerInput());
            Assert.True(planned.Success, planned.Error);
            return planned.Plan;
        }

        static FontLibraryBuildResult Build(
            FontLibraryPlan plan,
            byte[] font,
            byte[] license,
            IFontLibraryFaceFactory factory)
        {
            var input = new FontLibraryBuildInput
            {
                Plan = plan,
                FaceFactory = factory,
            };
            input.Assets.Add("job", new FontLibraryJobAssets
            {
                FontFileData = font,
                LicenseFileData = license,
            });
            return FontLibraryBuilderCore.Build(input);
        }

        static string ManifestJson(
            string styles,
            string ranges,
            string mapping,
            FontLibraryFormat format = FontLibraryFormat.Main16x16,
            string fontHash = ZeroHash,
            string licenseHash = ZeroHash,
            int fontByteLength = 1)
            => "{"
                + "\"schemaVersion\":1,"
                + "\"jobs\":[{"
                + "\"id\":\"job\","
                + "\"locale\":\"en-US\","
                + "\"format\":\""
                + (format == FontLibraryFormat.Main16x16
                    ? "main-16x16" : "zh-16x13") + "\","
                + "\"styles\":" + styles + ","
                + "\"scalarRanges\":" + ranges + ","
                + "\"font\":{\"path\":\"font.ttf\",\"sha256\":\""
                + fontHash
                + "\",\"byteLength\":" + fontByteLength + ","
                + "\"family\":\"Test Family\","
                + "\"version\":\"Version 1.0\","
                + "\"sourceUrl\":\"https://example.invalid/font\","
                + "\"size\":12},"
                + "\"license\":{\"licenseId\":\"OFL-1.1\","
                + "\"licenseFile\":\"OFL.txt\",\"sha256\":\""
                + licenseHash
                + "\",\"sourceUrl\":\"https://example.invalid/license\"},"
                + "\"verticalOffset\":0,"
                + "\"mapping\":" + mapping
                + "}]}";

        // Independent, hard-coded package palette vectors. The legacy GBA
        // colors are quantized to RGB555 and expanded by <<3; ZH swaps white
        // and gray relative to main.
        static byte[] CanonicalPalette(
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            string hex = (format, style) switch
            {
                (FontLibraryFormat.Main16x16, FontLibraryStyle.Item)
                    => "6888A8A8A8A0F8F8F8282828",
                (FontLibraryFormat.Main16x16, FontLibraryStyle.Text)
                    => "E0E0E0A8A8A0F8F8F8282828",
                (FontLibraryFormat.Zh16x13, FontLibraryStyle.Item)
                    => "6888A8F8F8F8A8A8A0282828",
                (FontLibraryFormat.Zh16x13, FontLibraryStyle.Text)
                    => "E0E0E0F8F8F8A8A8A0282828",
                _ => throw new ArgumentOutOfRangeException(),
            };
            return Convert.FromHexString(hex);
        }

        // External zlib/DEFLATE vector for 16 scanlines of
        // 00 1B 1B 1B 1B. Adler-32 is 0E5F06C1.
        static readonly byte[] CanonicalStoredZlib = Convert.FromHexString(
            "7801015000AFFF"
            + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
            + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
            + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
            + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
            + "0E5F06C1");

        // Complete 188-byte external canonical PNG grammar. CRC bytes are zero
        // in this vector so they can be checked by the separate independent
        // CRC-32 oracle rather than copied from the encoder under test.
        static byte[] CanonicalPngWithZeroedCrcs(
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            string palette = (format, style) switch
            {
                (FontLibraryFormat.Main16x16, FontLibraryStyle.Item)
                    => "6888A8A8A8A0F8F8F8282828",
                (FontLibraryFormat.Main16x16, FontLibraryStyle.Text)
                    => "E0E0E0A8A8A0F8F8F8282828",
                (FontLibraryFormat.Zh16x13, FontLibraryStyle.Item)
                    => "6888A8F8F8F8A8A8A0282828",
                (FontLibraryFormat.Zh16x13, FontLibraryStyle.Text)
                    => "E0E0E0F8F8F8A8A8A0282828",
                _ => throw new ArgumentOutOfRangeException(),
            };
            return Convert.FromHexString(
                "89504E470D0A1A0A"
                + "0000000D49484452"
                + "00000010000000100203000000"
                + "00000000"
                + "0000000C504C5445"
                + palette
                + "00000000"
                + "0000000474524E53"
                + "00FFFFFF"
                + "00000000"
                + "0000005B49444154"
                + "7801015000AFFF"
                + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
                + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
                + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
                + "001B1B1B1B001B1B1B1B001B1B1B1B001B1B1B1B"
                + "0E5F06C1"
                + "00000000"
                + "0000000049454E44"
                + "00000000");
        }

        static void ZeroChunkCrcs(byte[] png)
        {
            int position = 8;
            while (position < png.Length)
            {
                int length = checked((int)ReadU32BigEndian(png, position));
                int crc = checked(position + 8 + length);
                Assert.InRange(crc + 3, 0, png.Length - 1);
                png[crc] = 0;
                png[crc + 1] = 0;
                png[crc + 2] = 0;
                png[crc + 3] = 0;
                position = checked(crc + 4);
            }
            Assert.Equal(png.Length, position);
        }

        static string[] ReadChunkTypes(byte[] png)
        {
            var result = new List<string>();
            int position = 8;
            while (position < png.Length)
            {
                int length = (png[position] << 24)
                    | (png[position + 1] << 16)
                    | (png[position + 2] << 8)
                    | png[position + 3];
                string type = Encoding.ASCII.GetString(
                    png, position + 4, 4);
                result.Add(type);
                position += 12 + length;
            }
            Assert.Equal(png.Length, position);
            return result.ToArray();
        }

        static void AssertChunkCrcs(byte[] png)
        {
            int position = 8;
            while (position < png.Length)
            {
                int length = (int)ReadU32BigEndian(png, position);
                uint expected = ReadU32BigEndian(
                    png, position + 8 + length);
                uint actual = 0xFFFFFFFFu;
                for (int i = position + 4;
                    i < position + 8 + length;
                    i++)
                {
                    actual ^= png[i];
                    for (int bit = 0; bit < 8; bit++)
                        actual = (actual & 1) != 0
                            ? 0xEDB88320u ^ (actual >> 1)
                            : actual >> 1;
                }
                Assert.Equal(expected, actual ^ 0xFFFFFFFFu);
                position += 12 + length;
            }
        }

        static uint ComputeAdler32(ReadOnlySpan<byte> data)
        {
            uint a = 1;
            uint b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521u;
                b = (b + a) % 65521u;
            }
            return (b << 16) | a;
        }

        static uint ReadU32BigEndian(byte[] data, int offset)
            => ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];

        static void AssertTreesEqual(
            FontLibraryPackage expected,
            FontLibraryPackage actual)
        {
            Assert.Equal(
                expected.Files.Keys.ToArray(),
                actual.Files.Keys.ToArray());
            foreach (string path in expected.Files.Keys)
                Assert.Equal(expected.Files[path], actual.Files[path]);
        }

        sealed class FakeFaceFactory :
            IFontLibraryFaceFactory
        {
            public int RenderCalls { get; private set; }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                error = "";
                face = new FakeFace(this);
                return true;
            }

            sealed class FakeFace : IFontLibraryFace
            {
                readonly FakeFaceFactory _owner;

                internal FakeFace(FakeFaceFactory owner)
                {
                    _owner = owner;
                }

                public bool HasGlyph(int unicodeScalar)
                    => unicodeScalar == 'A';

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    _owner.RenderCalls++;
                    engineTile = new byte[64];
                    // Row 3 survives both accepted ZH shifts (item=2,text=1).
                    engineTile[12] = 3;
                    glyphWidth = 7;
                    error = "";
                    return unicodeScalar == 'A';
                }

                public void Dispose() { }
            }
        }

        sealed class AssignedFailingFaceFactory :
            IFontLibraryFaceFactory
        {
            readonly bool _throwAfterAssign;

            internal AssignedFailingFaceFactory(bool throwAfterAssign)
            {
                _throwAfterAssign = throwAfterAssign;
            }

            public int DisposeCalls { get; private set; }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                face = new Face(this);
                error = "Strict open was rejected.";
                if (_throwAfterAssign)
                    throw new InvalidOperationException(
                        "Native face open failed after allocation.");
                return false;
            }

            sealed class Face : IFontLibraryFace
            {
                readonly AssignedFailingFaceFactory _owner;

                internal Face(AssignedFailingFaceFactory owner)
                {
                    _owner = owner;
                }

                public bool HasGlyph(int unicodeScalar) => true;

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    throw new InvalidOperationException(
                        "A rejected face must not rasterize.");
                }

                public void Dispose()
                {
                    _owner.DisposeCalls++;
                }
            }
        }

        sealed class ThrowingNativeFaceFactory :
            IFontLibraryFaceFactory
        {
            readonly bool _throwDuringGlyphQuery;

            internal ThrowingNativeFaceFactory(bool throwDuringGlyphQuery)
            {
                _throwDuringGlyphQuery = throwDuringGlyphQuery;
            }

            public int DisposeCalls { get; private set; }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                face = new Face(this);
                error = "";
                return true;
            }

            sealed class Face : IFontLibraryFace
            {
                readonly ThrowingNativeFaceFactory _owner;

                internal Face(ThrowingNativeFaceFactory owner)
                {
                    _owner = owner;
                }

                public bool HasGlyph(int unicodeScalar)
                {
                    if (_owner._throwDuringGlyphQuery)
                        throw new InvalidOperationException(
                            "Native glyph lookup failed.");
                    return true;
                }

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    throw new InvalidOperationException(
                        "Native rasterization failed.");
                }

                public void Dispose()
                {
                    _owner.DisposeCalls++;
                }
            }
        }

        sealed class ThrowingFaceFactory : IFontLibraryFaceFactory
        {
            public int OpenCalls { get; private set; }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                OpenCalls++;
                throw new InvalidOperationException(
                    "Dry-run attempted to open a font face.");
            }
        }

        sealed class TypedBlankFaceFactory : IFontLibraryFaceFactory
        {
            readonly bool _disposeThrows;

            internal TypedBlankFaceFactory(bool disposeThrows = false)
            {
                _disposeThrows = disposeThrows;
            }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                face = new Face(_disposeThrows);
                error = "";
                return true;
            }

            sealed class Face : IFontLibraryFace
            {
                readonly bool _disposeThrows;

                internal Face(bool disposeThrows)
                {
                    _disposeThrows = disposeThrows;
                }

                public bool HasGlyph(int unicodeScalar)
                    => unicodeScalar == 'A';

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    engineTile = Array.Empty<byte>();
                    glyphWidth = 0;
                    error = "The glyph rasterized to an all-background tile.";
                    return false;
                }

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out FontLibraryFaceFailureKind failureKind,
                    out string error)
                {
                    engineTile = Array.Empty<byte>();
                    glyphWidth = 0;
                    failureKind = FontLibraryFaceFailureKind.MissingGlyph;
                    error = "The glyph rasterized to an all-background tile.";
                    return false;
                }

                public void Dispose()
                {
                    if (_disposeThrows)
                        throw new InvalidOperationException(
                            "Native disposal failed after semantic failure.");
                }
            }
        }

        sealed class ThrowingRasterFaceFactory :
            IFontLibraryFaceFactory
        {
            public int OpenCalls { get; private set; }
            public int RasterCalls { get; private set; }

            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                OpenCalls++;
                face = new Face(this);
                error = "";
                return true;
            }

            sealed class Face : IFontLibraryFace
            {
                readonly ThrowingRasterFaceFactory _owner;

                internal Face(ThrowingRasterFaceFactory owner)
                {
                    _owner = owner;
                }

                public bool HasGlyph(int unicodeScalar)
                    => throw new InvalidOperationException(
                        "Dry-run queried a font face.");

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    _owner.RasterCalls++;
                    throw new InvalidOperationException(
                        "Dry-run rasterized a glyph.");
                }

                public void Dispose() { }
            }
        }

        sealed class DiscardedRowsOnlyFaceFactory :
            IFontLibraryFaceFactory
        {
            public bool TryOpenFace(
                FontLibraryFaceSpec spec,
                out IFontLibraryFace face,
                out string error)
            {
                face = new Face();
                error = "";
                return true;
            }

            sealed class Face : IFontLibraryFace
            {
                public bool HasGlyph(int unicodeScalar)
                    => unicodeScalar == 'A';

                public bool TryRasterizeGlyph(
                    int unicodeScalar,
                    bool isItemFont,
                    int verticalOffset,
                    out byte[] engineTile,
                    out int glyphWidth,
                    out string error)
                {
                    engineTile = new byte[64];
                    // Ordinary 16x16 row 13, x=0 (four pixels per byte).
                    engineTile[(13 * 16) / 4] = 1;
                    glyphWidth = 7;
                    error = "";
                    return true;
                }

                public void Dispose() { }
            }
        }
    }
}
