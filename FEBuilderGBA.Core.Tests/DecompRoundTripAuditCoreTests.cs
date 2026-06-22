using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the maintained decomp round-trip coverage matrix (#1150).
    /// Pure (no CoreState / ROM), so no SharedState collection is required.
    /// </summary>
    public class DecompRoundTripAuditCoreTests
    {
        [Fact]
        public void BuildMatrix_IsNonEmpty()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.NotNull(rows);
            Assert.True(rows.Count >= 20, $"expected a real matrix (>=20 rows), got {rows.Count}");
            Assert.True(rows.Count <= 60, $"matrix should stay reasonable (<=60 rows), got {rows.Count}");
        }

        [Fact]
        public void EveryRow_HasDefinedEnum_AndNonNullStrings()
        {
            var defined = new HashSet<DecompCoverage>((DecompCoverage[])Enum.GetValues(typeof(DecompCoverage)));
            foreach (DecompAuditRow r in DecompRoundTripAuditCore.BuildMatrix())
            {
                Assert.Contains(r.Coverage, defined);
                Assert.False(string.IsNullOrEmpty(r.Editor), "Editor must be non-empty");
                Assert.False(string.IsNullOrEmpty(r.Table), "Table must be non-empty");
                Assert.False(string.IsNullOrEmpty(r.Action), "Action must be non-empty");
                Assert.NotNull(r.Notes);
            }
        }

        [Fact]
        public void NoDuplicate_EditorTableAction_Keys()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DecompAuditRow r in DecompRoundTripAuditCore.BuildMatrix())
            {
                string key = r.Editor + "" + r.Table + "" + r.Action;
                Assert.True(seen.Add(key), $"duplicate (Editor,Table,Action) key: {r.Editor} / {r.Table} / {r.Action}");
            }
        }

        [Fact]
        public void SourceBackedRowSave_Tables_Equal_CanonicalSet_BothDirections()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();

            // SourceBackedWriter rows whose Action is "Row save".
            var matrixTables = rows
                .Where(r => r.Coverage == DecompCoverage.SourceBackedWriter
                            && string.Equals(r.Action, "Row save", StringComparison.Ordinal))
                .Select(r => r.Table)
                .ToHashSet(StringComparer.Ordinal);

            var canonical = DecompSourceWriterCore.SourceBackedTables.ToHashSet(StringComparer.Ordinal);

            // Both directions: exact set equality.
            Assert.True(matrixTables.SetEquals(canonical),
                $"matrix SourceBackedWriter Row-save tables [{string.Join(",", matrixTables.OrderBy(x => x))}] " +
                $"!= canonical [{string.Join(",", canonical.OrderBy(x => x))}]");

            // Each canonical table's row note is the documented sentinel.
            foreach (DecompAuditRow r in rows.Where(r =>
                r.Coverage == DecompCoverage.SourceBackedWriter && r.Action == "Row save"))
            {
                Assert.StartsWith("Main structured-row save only", r.Notes);
            }
        }

        [Fact]
        public void MapChangeOverlay_Row_IsSourceTreeExporter_ImportVerify()
        {
            // #1355: the map-change overlay import/verify row is a SourceTreeExporter row.
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            var row = rows.SingleOrDefault(r =>
                r.Table == "map_change_overlay"
                && r.Action == "Map-change overlay import/verify");
            Assert.NotNull(row);
            Assert.Equal(DecompCoverage.SourceTreeExporter, row.Coverage);
            Assert.Contains("--verify-asset", row.Notes);
            Assert.Contains("NOT the .mar layout", row.Notes);
        }

        [Fact]
        public void MapAssetBinaries_Row_StillManualMigration_NoLongerMentionsOverlay()
        {
            // #1355: the map-change OVERLAY tile data block is now source-backed, so the
            // map_asset_binaries ManualMigration row must no longer claim "overlay".
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            var row = rows.SingleOrDefault(r => r.Table == "map_asset_binaries");
            Assert.NotNull(row);
            Assert.Equal(DecompCoverage.ManualMigration, row.Coverage);
            // The row must EXCLUDE the overlay (it is now source-backed export/import/verify),
            // and must no longer LIST it among the remaining LZ77/pointer binaries.
            Assert.Contains("NOT the map-change overlay tile data block", row.Notes);
            Assert.DoesNotContain("tile animations 1/2, map-change overlay", row.Notes);
        }

        [Fact]
        public void ManualAndRomOnly_Tables_DoNotAppear_AsSourceBackedRowSave_ForSameEditor()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();

            // (Editor, Table) pairs that are a SourceBackedWriter Row save.
            var sourceBacked = rows
                .Where(r => r.Coverage == DecompCoverage.SourceBackedWriter && r.Action == "Row save")
                .Select(r => (r.Editor, r.Table))
                .ToHashSet();

            // A Manual/RomOnly row may share an editor+table with a Row-save row only via a
            // DIFFERENT action (the mixed-action sub-row). But there must NEVER be a Manual/
            // RomOnly row that is ALSO classified SourceBackedWriter for the SAME action.
            foreach (DecompAuditRow r in rows.Where(r =>
                r.Coverage == DecompCoverage.ManualMigration || r.Coverage == DecompCoverage.RomOnlyUnsupported))
            {
                bool clash = rows.Any(o =>
                    o.Coverage == DecompCoverage.SourceBackedWriter
                    && o.Editor == r.Editor && o.Table == r.Table && o.Action == r.Action);
                Assert.False(clash,
                    $"{r.Editor}/{r.Table}/{r.Action} is both {r.Coverage} and SourceBackedWriter");
            }
        }

        [Fact]
        public void FormatMatrix_Tsv_HasHeader_AndOneDataLinePerRow()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            string tsv = DecompRoundTripAuditCore.FormatMatrix(rows, "tsv");

            string[] lines = tsv.TrimEnd('\n', '\r').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.Equal("Editor\tTable\tAction\tCoverage\tNotes", lines[0]);
            Assert.Equal(rows.Count, lines.Length - 1);

            // Every data line has exactly 4 tabs (5 columns).
            for (int i = 1; i < lines.Length; i++)
                Assert.Equal(4, lines[i].Count(c => c == '\t'));
        }

        [Fact]
        public void FormatMatrix_Md_HasMarkdownTableHeader_AndSeparator()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            string md = DecompRoundTripAuditCore.FormatMatrix(rows, "md");
            string[] lines = md.TrimEnd('\n', '\r').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            Assert.Equal("| Editor | Table | Action | Coverage | Notes |", lines[0]);
            Assert.Equal("| --- | --- | --- | --- | --- |", lines[1]);
            Assert.Equal(rows.Count, lines.Length - 2);
        }

        [Fact]
        public void FormatMatrix_NullOrUnknownFormat_DefaultsToTsv()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.StartsWith("Editor\tTable\tAction\tCoverage\tNotes",
                DecompRoundTripAuditCore.FormatMatrix(rows, null));
            Assert.StartsWith("Editor\tTable\tAction\tCoverage\tNotes",
                DecompRoundTripAuditCore.FormatMatrix(rows, "xml"));
        }

        [Fact]
        public void FormatMatrix_NullRows_DoesNotThrow()
        {
            string tsv = DecompRoundTripAuditCore.FormatMatrix(null, "tsv");
            Assert.StartsWith("Editor\tTable\tAction\tCoverage\tNotes", tsv);
        }

        [Fact]
        public void MapLayoutImportVerify_Row_Exists_AsSourceTreeExporter()
        {
            // #1148: the .mar map-layout import/verify direction is recorded as a
            // distinct SourceTreeExporter row (not a SourceBackedWriter row, so the
            // canonical SourceBackedTables mirror invariant stays unaffected).
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.Contains(rows, r =>
                r.Action == "Map layout import/verify"
                && r.Coverage == DecompCoverage.SourceTreeExporter);
        }

        [Fact]
        public void Shops_HaveTwoDistinctRows_SaveManual_And_ExportSourceTree()
        {
            // #1149: shops have NO in-place C-array owner — the in-place GUI save stays
            // ManualMigration ("Shop list save"), but their lists CAN be migrated to source
            // via an EA .event export ("Shop list export" = SourceTreeExporter). Two distinct
            // rows with distinct Actions for the same editor/table.
            var rows = DecompRoundTripAuditCore.BuildMatrix();

            Assert.Contains(rows, r =>
                r.Editor == "Item Shop Editor" && r.Table == "shops"
                && r.Action == "Shop list save" && r.Coverage == DecompCoverage.ManualMigration);

            Assert.Contains(rows, r =>
                r.Editor == "Item Shop Editor" && r.Table == "shops"
                && r.Action == "Shop list export" && r.Coverage == DecompCoverage.SourceTreeExporter);
        }

        [Fact]
        public void Shops_AreNotInSourceBackedTables()
        {
            // #1149: shops must never be a SourceBackedWriter "Row save" row (they have no
            // rectangular fixed-row C-array owner), and the canonical fixed-row source-backed
            // set must not contain "shops". #1347 adds a DISTINCT "Shop list source save"
            // SourceBackedWriter row (variable-length list writer), which is NOT a "Row save"
            // and so is excluded here; the fixed-row mirror invariant over SourceBackedTables
            // is unaffected.
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.DoesNotContain(rows, r =>
                r.Table == "shops" && r.Coverage == DecompCoverage.SourceBackedWriter
                && r.Action == "Row save");
            Assert.DoesNotContain("shops", DecompSourceWriterCore.SourceBackedTables);
        }

        [Fact]
        public void Shops_HaveSourceBackedListSaveRow()
        {
            // #1347: shops gain an in-place source-backed VARIABLE-LENGTH list rewrite
            // ("Shop list source save" = SourceBackedWriter) via --write-shop — distinct
            // from the fixed-row "Row save" path, so the SourceBackedTables mirror invariant
            // is unaffected (see Shops_AreNotInSourceBackedTables).
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.Contains(rows, r =>
                r.Editor == "Item Shop Editor" && r.Table == "shops"
                && r.Action == "Shop list source save"
                && r.Coverage == DecompCoverage.SourceBackedWriter);
        }

        // ---- #1150 (reopened) COMPLETENESS: the matrix is "complete relative to the
        //      maintained inventory" — enforced against an INDEPENDENT editor registry,
        //      not just self-consistency. ----

        [Fact]
        public void EveryExpectedEditor_HasAtLeastOneRow()
        {
            // The real "no decomp-relevant editor is missing/unclassified" enforcement:
            // every editor in the INDEPENDENT ExpectedDecompEditors registry must have at
            // least one matrix row. A registered editor dropped from the matrix fails here.
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            var matrixEditors = rows.Select(r => r.Editor).ToHashSet(StringComparer.Ordinal);

            foreach (string editor in DecompRoundTripAuditCore.ExpectedDecompEditors)
            {
                Assert.True(matrixEditors.Contains(editor),
                    $"registered decomp editor '{editor}' has no row in the coverage matrix");
            }
        }

        [Fact]
        public void EveryMatrixEditor_IsRegistered()
        {
            // The inverse lockstep: a NEW matrix editor that wasn't added to the registry
            // also fails, so the two sources of truth can't silently diverge.
            var registry = DecompRoundTripAuditCore.ExpectedDecompEditors.ToHashSet(StringComparer.Ordinal);
            foreach (DecompAuditRow r in DecompRoundTripAuditCore.BuildMatrix())
            {
                Assert.True(registry.Contains(r.Editor),
                    $"matrix editor '{r.Editor}' is not in ExpectedDecompEditors (update the registry)");
            }
        }

        [Fact]
        public void ExpectedDecompEditors_IsNonEmpty_AndHasNoDuplicates()
        {
            var reg = DecompRoundTripAuditCore.ExpectedDecompEditors;
            Assert.NotNull(reg);
            Assert.True(reg.Count >= 10, $"expected a real editor inventory (>=10), got {reg.Count}");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string e in reg)
            {
                Assert.False(string.IsNullOrEmpty(e), "registry editor name must be non-empty");
                Assert.True(seen.Add(e), $"duplicate registry editor: {e}");
            }
        }

        [Fact]
        public void EveryCoverageTier_HasAtLeastOneRow()
        {
            // Completeness floor: the matrix exercises ALL five coverage tiers, so the
            // summary's per-tier counts are all > 0 and no tier is silently absent.
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            var present = rows.Select(r => r.Coverage).ToHashSet();
            foreach (DecompCoverage tier in (DecompCoverage[])Enum.GetValues(typeof(DecompCoverage)))
            {
                Assert.True(present.Contains(tier),
                    $"coverage tier {tier} has no representative row in the matrix");
            }
        }

        // ---- #1150 (reopened) SUMMARY: per-tier counts + explicit Unclassified=0. ----

        [Fact]
        public void BuildSummary_CountsEqual_MatrixRows_AndZeroUnclassified()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            DecompCoverageSummary s = DecompRoundTripAuditCore.BuildSummary(rows);

            Assert.Equal(rows.Count, s.Total);
            Assert.Equal(0, s.Unclassified);

            // Per-tier counts must sum to Total.
            int sum = s.SourceBackedWriter + s.SourceTreeExporter + s.ImportPreviewOnly
                      + s.ManualMigration + s.RomOnlyUnsupported + s.Unclassified;
            Assert.Equal(s.Total, sum);

            // Each per-tier count matches a direct matrix tally.
            Assert.Equal(rows.Count(r => r.Coverage == DecompCoverage.SourceBackedWriter), s.SourceBackedWriter);
            Assert.Equal(rows.Count(r => r.Coverage == DecompCoverage.SourceTreeExporter), s.SourceTreeExporter);
            Assert.Equal(rows.Count(r => r.Coverage == DecompCoverage.ImportPreviewOnly), s.ImportPreviewOnly);
            Assert.Equal(rows.Count(r => r.Coverage == DecompCoverage.ManualMigration), s.ManualMigration);
            Assert.Equal(rows.Count(r => r.Coverage == DecompCoverage.RomOnlyUnsupported), s.RomOnlyUnsupported);
        }

        [Fact]
        public void BuildSummary_NullRows_DoesNotThrow_AndIsZero()
        {
            DecompCoverageSummary s = DecompRoundTripAuditCore.BuildSummary(null);
            Assert.Equal(0, s.Total);
            Assert.Equal(0, s.Unclassified);
        }

        [Fact]
        public void FormatSummary_HasCounts_TotalUnclassified_AndHonestCaveat()
        {
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            DecompCoverageSummary s = DecompRoundTripAuditCore.BuildSummary(rows);
            string text = DecompRoundTripAuditCore.FormatSummary(s);

            Assert.Contains("SourceBackedWriter = ", text);
            Assert.Contains("SourceTreeExporter = ", text);
            Assert.Contains("ImportPreviewOnly", text);
            Assert.Contains("ManualMigration", text);
            Assert.Contains("RomOnlyUnsupported", text);
            Assert.Contains($"Total              = {s.Total}", text);
            Assert.Contains($"Unclassified       = {s.Unclassified}", text);
            Assert.Contains("MaintainedInventory", text);
            // Honest caveat + release-visibility note.
            Assert.Contains("not exhaustive byte-level runtime round-trip proof", text);
            Assert.Contains("ahead of any tagged release", text);
        }

        [Fact]
        public void Map148_And_Shop149_Rows_RemainPresent()
        {
            // Regression lock: the #1148 map-layout import/verify + #1149 shop-export rows
            // (reflected per the reopened ask) must stay in the matrix.
            var rows = DecompRoundTripAuditCore.BuildMatrix();
            Assert.Contains(rows, r => r.Action == "Map layout import/verify"
                && r.Coverage == DecompCoverage.SourceTreeExporter);
            Assert.Contains(rows, r => r.Editor == "Item Shop Editor" && r.Action == "Shop list export"
                && r.Coverage == DecompCoverage.SourceTreeExporter);
        }
    }
}
