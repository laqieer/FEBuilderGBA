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
    }
}
