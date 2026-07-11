// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the decomp diff-to-source migration assistant (#1131 slice 3).
//
// Synthetic-first: two in-memory FE8U ROMs differing at KNOWN offsets, plus a
// throwaway project dir with a .map placing symbols at those GBA addresses. The
// tests assert each changed range is classified to the right symbol / category /
// confidence, that gap-coalescing tracks changed-bytes vs span separately, that
// compressed beats everything (Low/manual), and that null/identity/malformed
// inputs never throw.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DecompDiffMigrationCoreTests
    {
        // ---------------------------------------------------------------- fixtures

        static ROM MakeFe8uRom(byte[] data = null)
        {
            data ??= new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("decomp-migrate-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        // 32 MB FE8U ROM so a symbol span can reach the EWRAM upper boundary.
        static ROM MakeFe8uRom32mb(byte[] data)
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("decomp-migrate-fe8u-32m.gba", data, "BE8E01"));
            return rom;
        }

        // Build a resolver + merged map from .map text (throwaway project dir).
        static (MergedAsmMapFile Map, DecompSymbolResolver Resolver) BuildMap(string mapText)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"migmap_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rom.gba"), "x");
            File.WriteAllText(Path.Combine(dir, "rom.map"), mapText);
            var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "rom.gba") };
            try
            {
                var resolver = DecompSymbolResolver.Load(project);
                var merged = new MergedAsmMapFile(new AsmMapSymbolFile(new ROM()), resolver);
                return (merged, resolver);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // A built+edited ROM pair that differs in [offset, offset+len).
        static (byte[] Built, byte[] Edited) MakePair(int size, uint offset, int len, byte editVal = 0xAA)
        {
            var built = new byte[size];
            var edited = new byte[size];
            for (int i = 0; i < len; i++)
                edited[offset + i] = editVal;       // built stays 0, edited differs
            return (built, edited);
        }

        // ---------------------------------------------------------------- coalesce

        [Fact]
        public void Coalesce_TwoDiffsWithinGap_MergeIntoOneSpan_KeepingChangedBytesSeparate()
        {
            // Two 1-byte diffs at 0x100 and 0x108 (gap = 7 <= maxGap 16).
            var ranges = new List<RomDiffCore.DiffRange>
            {
                new RomDiffCore.DiffRange { Offset = 0x100, Length = 1 },
                new RomDiffCore.DiffRange { Offset = 0x108, Length = 1 },
            };
            var merged = DecompDiffMigrationCore.Coalesce(ranges, 16);
            Assert.Single(merged);
            Assert.Equal(0x100u, merged[0].Offset);
            Assert.Equal(9u, merged[0].SpanLength);     // 0x100..0x108 inclusive
            Assert.Equal(2u, merged[0].ChangedBytes);   // only 2 bytes really changed
        }

        [Fact]
        public void Coalesce_TwoDiffsBeyondGap_StaySeparate()
        {
            var ranges = new List<RomDiffCore.DiffRange>
            {
                new RomDiffCore.DiffRange { Offset = 0x100, Length = 1 },
                new RomDiffCore.DiffRange { Offset = 0x200, Length = 1 },   // gap 0xFF > 16
            };
            var merged = DecompDiffMigrationCore.Coalesce(ranges, 16);
            Assert.Equal(2, merged.Count);
            Assert.Equal(1u, merged[0].ChangedBytes);
            Assert.Equal(1u, merged[1].ChangedBytes);
        }

        // ---------------------------------------------------------------- symbol + category

        [Fact]
        public void Analyze_TextRegionChange_ClassifiesAsText()
        {
            // FE8U text_data_start_address = 0xE8414 .. 0x15AB80. Put a diff inside.
            uint off = 0xE8500;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);
            Assert.Equal(MigrationCategory.Text, report.Ranges[0].Category);
            // No symbol/covering → Medium (range inferred), never High.
            Assert.NotEqual(MigrationConfidence.High, report.Ranges[0].Confidence);
        }

        [Fact]
        public void Analyze_CoveringProjectSymbol_GivesHighConfidenceAndSourceFile()
        {
            // Symbol gUnitTable @ 0x0808E500 (file off 0x8E500) covering a 0x40 span,
            // owned by build/src/unit.o; diff a few bytes inside it.
            uint off = 0x8E510;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .rodata        0x0808E500       0x40 build/src/unit.o",
                "                0x0808E500                gUnitTable",
            }));

            var report = DecompDiffMigrationCore.Analyze(rom, edited, map, resolver);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal("gUnitTable", r.Symbol);
            Assert.True(r.SymbolCovers);
            Assert.Equal(MigrationConfidence.High, r.Confidence);
            Assert.Equal(DecompArtifactSource.Map, r.SymbolSource);
            Assert.Equal("build/src/unit.o", r.SourceFile);   // object path from .map
            Assert.Equal(".rodata", r.Section);
        }

        [Fact]
        public void Analyze_GraphicsSectionSymbol_NotCovering_GivesGraphicsLowOrMedium()
        {
            // Symbol in a gfx section but the diff sits BEYOND the symbol span → not
            // covering → name/section heuristic, never High.
            uint off = 0x300000;
            var (built, edited) = MakePair(0x1000000, off, 8);
            var rom = MakeFe8uRom(built);

            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .rodata        0x08300000        0x4 build/gfx/portrait.o",
                "                0x08300000                gPortraitPalette",  // len ~4
            }));

            var report = DecompDiffMigrationCore.Analyze(rom, edited, map, resolver);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal(MigrationCategory.GraphicsPalette, r.Category);
            Assert.NotEqual(MigrationConfidence.High, r.Confidence);
        }

        [Fact]
        public void Analyze_CoveringSymbol_UnknownCategory_StaysLowManual_NotHigh()
        {
            // A covering project symbol whose name/section carry NO data-category
            // keyword (a neutral code-like symbol) must stay Low + manual — High is
            // gated on a KNOWN category (Copilot PR #1139 finding 1).
            uint off = 0x250000;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .text          0x08250000      0x100 build/src/proc.o",
                "                0x08250000                SomeNeutralProc",   // no data keyword
            }));

            var report = DecompDiffMigrationCore.Analyze(rom, edited, map, resolver);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal("SomeNeutralProc", r.Symbol);
            Assert.True(r.SymbolCovers);
            Assert.Equal(MigrationCategory.Unknown, r.Category);
            Assert.Equal(MigrationConfidence.Low, r.Confidence);
            Assert.True(r.Manual);
        }

        // ---------------------------------------------------------------- compressed

        [Fact]
        public void Analyze_CompressedHeader_AlwaysLowManual_BeatsSymbol()
        {
            // Place a real LZ77 stream in BOTH ROMs at 0x400000, then edit a byte
            // inside it. The sniff must classify Compressed + Low + manual even with
            // a covering project symbol present.
            int size = 0x1000000;
            byte[] raw = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x11, 0x22, 0x33, 0x44 };
            byte[] comp = LZ77.compress(raw);
            uint baseOff = 0x400000;

            var built = new byte[size];
            var edited = new byte[size];
            Array.Copy(comp, 0, built, baseOff, comp.Length);
            Array.Copy(comp, 0, edited, baseOff, comp.Length);
            // Mutate a byte a few positions into the compressed stream.
            edited[baseOff + 5] ^= 0xFF;

            var rom = MakeFe8uRom(built);
            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .rodata        0x08400000      0x100 build/gfx/sprite.o",
                "                0x08400000                gCompressedSprite",
            }));

            var report = DecompDiffMigrationCore.Analyze(rom, edited, map, resolver);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal(MigrationCategory.Compressed, r.Category);
            Assert.Equal(MigrationConfidence.Low, r.Confidence);
            Assert.True(r.Manual);
            Assert.Contains("manual", r.Suggestion, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- unknown

        [Fact]
        public void Analyze_NoSymbolNoTable_GivesUnknownLowManual()
        {
            // A diff in a region with no symbol and no known table → Unknown + Low.
            uint off = 0x900000;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal(MigrationCategory.Unknown, r.Category);
            Assert.Equal(MigrationConfidence.Low, r.Confidence);
            Assert.True(r.Manual);
        }

        [Fact]
        public void Analyze_JustPastTextRegion_NotClassifiedAsText()
        {
            // FE8U text_data_end_address = 0x15AB80. A diff at/after the end must NOT
            // be claimed as the text region (boundary correctness; conservative
            // bounds, Copilot PR #1139 finding 2 family).
            uint off = 0x160000;   // > 0x15AB80
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);
            Assert.NotEqual(MigrationCategory.Text, report.Ranges[0].Category);
        }

        [Fact]
        public void Analyze_ZeroRom_StructTablesNotOverClaimed()
        {
            // On a zero ROM the unit/class/item table pointers don't dereference to a
            // valid populated table, so a diff in a low region must NOT be falsely
            // classified as a struct table (the table scan must decline, not grab a
            // generic 2048-row span). Conservative bounds (Copilot PR #1139 finding 2).
            uint off = 0x5000;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);
            Assert.NotEqual(MigrationCategory.StructTable, report.Ranges[0].Category);
        }

        [Fact]
        public void Analyze_SpanEndingAtEwramBoundary_CoverageComputedCorrectly()
        {
            // A symbol whose span ends exactly at file offset 0x02000000 (32MB ROM
            // upper boundary). The exclusive end pointer must be computed by pointer
            // arithmetic (startPtr + span), NOT U.toPointer(endOffset) which would
            // leave 0x02000000 unconverted and mis-judge coverage (Copilot PR #1139).
            int size = 0x2000000;                 // 32 MB
            uint off = 0x1FFFFF0;                 // last 16 bytes before the boundary
            var built = new byte[size];
            var edited = new byte[size];
            for (int i = 0; i < 8; i++) edited[off + i] = 0xAA;
            var rom = MakeFe8uRom32mb(built);

            // Symbol gTail @ 0x09FFFFF0 covering [0x09FFFFF0 .. 0x0A000000) = the
            // final 16 bytes; the 8-byte diff at 0x1FFFFF0 is fully inside it.
            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .rodata        0x09FFFFF0       0x10 build/src/tail.o",
                "                0x09FFFFF0                gTailTable",
            }));

            var report = DecompDiffMigrationCore.Analyze(rom, edited, map, resolver);
            Assert.Single(report.Ranges);
            var r = report.Ranges[0];
            Assert.Equal("gTailTable", r.Symbol);
            Assert.True(r.SymbolCovers);          // coverage correct at the boundary
            Assert.Equal(MigrationConfidence.High, r.Confidence);
        }

        // ---------------------------------------------------------------- robustness

        [Fact]
        public void Analyze_IdenticalRoms_EmptyReport()
        {
            var rom = MakeFe8uRom(new byte[0x1000000]);
            // Compare the built ROM bytes against an identical copy → no diffs.
            byte[] edited = (byte[])rom.Data.Clone();
            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Equal(0, report.RangeCount);
            Assert.False(report.BuiltMissing);
        }

        [Fact]
        public void ClassifyRangeSafe_OverrideThrows_FallsBackToStableUnclassifiedRecord()
        {
            // A faulting classifier override must degrade ONLY the one range to a stable
            // Unknown/Low/manual record — it must never propagate the exception, and the
            // caller's exact offset/length/changed-bytes must be preserved (Copilot review
            // finding: AnalyzeWithFill's previous single try/catch could silently truncate the
            // whole report after one classifier fault).
            var rom = MakeFe8uRom(new byte[0x1000000]);
            RangeClassifierOverride faulty = (r, built, offset, span, changed, map, resolver) =>
                throw new InvalidOperationException("injected classifier fault");

            MigrationRange result = DecompDiffMigrationCore.ClassifyRangeSafe(
                rom, rom.Data, offset: 0x1234, spanLength: 8, changedBytes: 8,
                map: null, resolver: null, overrideClassifier: faulty);

            Assert.Equal(0x1234u, result.Offset);
            Assert.Equal(8u, result.SpanLength);
            Assert.Equal(8u, result.ChangedBytes);
            Assert.Equal(MigrationCategory.Unknown, result.Category);
            Assert.Equal(MigrationConfidence.Low, result.Confidence);
            Assert.True(result.Manual);
        }

        [Fact]
        public void ClassifyRangeSafe_OverrideReturnsNull_FallsBackToStableUnclassifiedRecord()
        {
            // An override that returns null (a "bad" advisory classifier, not necessarily a
            // thrown exception) must be treated exactly like a fault — never omit the range.
            var rom = MakeFe8uRom(new byte[0x1000000]);
            RangeClassifierOverride returnsNull = (r, built, offset, span, changed, map, resolver) => null;

            MigrationRange result = DecompDiffMigrationCore.ClassifyRangeSafe(
                rom, rom.Data, offset: 0x5678, spanLength: 3, changedBytes: 3,
                map: null, resolver: null, overrideClassifier: returnsNull);

            Assert.Equal(0x5678u, result.Offset);
            Assert.Equal(3u, result.SpanLength);
            Assert.Equal(MigrationCategory.Unknown, result.Category);
            Assert.Equal(MigrationConfidence.Low, result.Confidence);
        }

        [Fact]
        public void ClassifyRangeSafe_NoOverride_MatchesRealClassifierResult()
        {
            // With no override (the production default), ClassifyRangeSafe must return exactly
            // what the real single-range classifier used by AnalyzeInternal would produce —
            // proving the exporter's direct per-range use of this seam is behavior-identical to
            // the analyzer's own internal loop.
            uint off = 0x900000;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);

            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);

            MigrationRange direct = DecompDiffMigrationCore.ClassifyRangeSafe(
                rom, built, off, spanLength: 4, changedBytes: 4, map: null, resolver: null);

            Assert.Equal(report.Ranges[0].Category, direct.Category);
            Assert.Equal(report.Ranges[0].Confidence, direct.Confidence);
            Assert.Equal(report.Ranges[0].Offset, direct.Offset);
            Assert.Equal(report.Ranges[0].SpanLength, direct.SpanLength);
        }

        [Fact]
        public void Analyze_NullEdited_NoThrow_EmptyReport()
        {
            var rom = MakeFe8uRom(new byte[0x1000000]);
            var report = DecompDiffMigrationCore.Analyze(rom, null, null, null);
            Assert.Equal(0, report.RangeCount);
        }

        [Fact]
        public void Analyze_NullBuiltRom_FlagsBuiltMissing_NoThrow()
        {
            var report = DecompDiffMigrationCore.Analyze(null, new byte[16], null, null);
            Assert.True(report.BuiltMissing);
            Assert.Equal(0, report.RangeCount);
        }

        [Fact]
        public void Analyze_NullMapAndResolver_StillClassifies_NoThrow()
        {
            uint off = 0x800000;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);
            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);
            Assert.Single(report.Ranges);
            Assert.Equal("", report.Ranges[0].Symbol);   // no symbol resolved
        }

        // ---------------------------------------------------------------- TryGetSection

        [Fact]
        public void Resolver_TryGetSection_ReturnsSectionForMapSymbol()
        {
            var (map, resolver) = BuildMap(string.Join("\n", new[]
            {
                " .rodata        0x0808E500       0x40 build/src/unit.o",
                "                0x0808E500                gUnitTable",
            }));
            Assert.True(resolver.TryGetSection(0x0808E500u, out string sec));
            Assert.Equal(".rodata", sec);
            Assert.True(resolver.TryGetObjectPath(0x0808E500u, out string op));
            Assert.Equal("build/src/unit.o", op);
            // Merged passthrough.
            Assert.True(map.TryGetSection(0x0808E500u, out string sec2));
            Assert.Equal(".rodata", sec2);
        }

        // ---------------------------------------------------------------- formatting

        [Fact]
        public void FormatTSV_And_Summary_NeverThrow_AndHaveHeader()
        {
            uint off = 0xE8500;
            var (built, edited) = MakePair(0x1000000, off, 4);
            var rom = MakeFe8uRom(built);
            var report = DecompDiffMigrationCore.Analyze(rom, edited, null, null);

            string tsv = DecompDiffMigrationCore.FormatTSV(report);
            Assert.StartsWith("StartAddr\tSpanLength\tChangedBytes", tsv);

            string summary = DecompDiffMigrationCore.FormatSummary(report);
            Assert.Contains("changed", summary, StringComparison.OrdinalIgnoreCase);

            // Null-safety.
            Assert.NotNull(DecompDiffMigrationCore.FormatTSV(null));
            Assert.NotNull(DecompDiffMigrationCore.FormatSummary(null));
        }

        [Fact]
        public void Format_NullRangeElement_DoesNotThrow()
        {
            // The Ranges list is publicly mutable; a null element must not crash
            // either formatter (Copilot PR #1139 review).
            var report = new MigrationReport();
            report.Ranges.Add(null);
            report.Ranges.Add(new MigrationRange { Offset = 0x10, SpanLength = 2, ChangedBytes = 2, Symbol = "Sym" });

            string tsv = DecompDiffMigrationCore.FormatTSV(report);
            Assert.Contains("Sym", tsv);                 // the real row survives
            string summary = DecompDiffMigrationCore.FormatSummary(report);
            Assert.Contains("Sym", summary);
        }
    }
}
