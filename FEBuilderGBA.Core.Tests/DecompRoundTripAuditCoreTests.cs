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
    }
}
