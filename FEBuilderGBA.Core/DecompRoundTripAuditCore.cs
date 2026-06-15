using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// How a FEBuilder editor's save/export round-trips back to a decomp source tree
    /// (#1150). The matrix in <see cref="DecompRoundTripAuditCore"/> classifies each
    /// editor/table/action against one of these coverage tiers so a contributor can
    /// see — at a glance — which edits are SOURCE-WRITABLE today, which migrate via an
    /// exporter, and which still require a manual hand-edit of the source.
    /// </summary>
    public enum DecompCoverage
    {
        /// <summary>
        /// A structured ROW SAVE is rewritten in-place in the owning C/JSON source by
        /// <see cref="DecompSourceWriterCore"/> (the canonical
        /// <see cref="DecompSourceWriterCore.SourceBackedTables"/> set).
        /// </summary>
        SourceBackedWriter,

        /// <summary>
        /// The data is EXPORTED to a source-tree asset (palette/graphics/map/text) by a
        /// source-tree exporter (<see cref="DecompAssetExportCore"/>) — the contributor
        /// drops the exported file into the decomp tree and rebuilds.
        /// </summary>
        SourceTreeExporter,

        /// <summary>
        /// The editor can VIEW the data in decomp preview mode but offers no source
        /// write-back path: edits are preview-only and must be made in source by hand.
        /// </summary>
        ImportPreviewOnly,

        /// <summary>
        /// The edit requires a MANUAL source migration: variable-length / pointer /
        /// raw-binary data with no clean source-of-truth array the writer can rewrite.
        /// </summary>
        ManualMigration,

        /// <summary>
        /// The data is ROM-only with no supported decomp round-trip at all (edit the
        /// ROM directly; not representable as a clean source edit yet).
        /// </summary>
        RomOnlyUnsupported,
    }

    /// <summary>
    /// One row of the decomp round-trip coverage matrix (#1150): an editor, the ROM
    /// table it edits, the specific ACTION (e.g. "Row save", "Palette export"), its
    /// <see cref="DecompCoverage"/> tier, and a short note. Immutable value type.
    /// </summary>
    public readonly struct DecompAuditRow
    {
        /// <summary>Editor / feature name (e.g. "Item Editor").</summary>
        public string Editor { get; }

        /// <summary>ROM table or asset kind (e.g. "items", "palette"). Never null.</summary>
        public string Table { get; }

        /// <summary>The specific action (e.g. "Row save", "Palette export"). Never null.</summary>
        public string Action { get; }

        /// <summary>Coverage tier for this editor/table/action.</summary>
        public DecompCoverage Coverage { get; }

        /// <summary>Short human note explaining the classification. Never null.</summary>
        public string Notes { get; }

        /// <summary>Construct an audit row; null fields are coerced to "".</summary>
        public DecompAuditRow(string editor, string table, string action, DecompCoverage coverage, string notes)
        {
            Editor = editor ?? "";
            Table = table ?? "";
            Action = action ?? "";
            Coverage = coverage;
            Notes = notes ?? "";
        }
    }

    /// <summary>
    /// Maintained, hand-curated decomp ROUND-TRIP COVERAGE MATRIX (#1150).
    ///
    /// This is a documentation-grade lookup that maps each FEBuilder editor / action to
    /// the way its edit round-trips back to a decomp source tree. It is READ-ONLY,
    /// PURE, and NEVER throws. The matrix is intentionally hand-maintained (not derived)
    /// so it can record the honest residuals — pointer fields, variable-length shops,
    /// raw map-asset binaries — that no automatic scan would surface.
    ///
    /// The single invariant a test enforces: the
    /// <see cref="DecompCoverage.SourceBackedWriter"/> rows whose action is "Row save"
    /// must be EXACTLY the tables in
    /// <see cref="DecompSourceWriterCore.SourceBackedTables"/> — so the matrix can never
    /// silently drift from the writer's real coverage.
    /// </summary>
    public static class DecompRoundTripAuditCore
    {
        /// <summary>
        /// Build the maintained coverage matrix. Returns a fresh immutable list on every
        /// call (callers may sort/filter freely). NEVER throws.
        /// </summary>
        public static IReadOnlyList<DecompAuditRow> BuildMatrix()
        {
            var rows = new List<DecompAuditRow>();

            // ---- SourceBackedWriter "Row save" rows: MUST mirror
            //      DecompSourceWriterCore.SourceBackedTables exactly (a test asserts
            //      both directions). Each note is "Main structured-row save only". ----
            rows.Add(new DecompAuditRow("Item Editor", "items", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));
            rows.Add(new DecompAuditRow("Unit Editor", "units", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only (manifest alias: characters)"));
            rows.Add(new DecompAuditRow("Class Editor", "classes", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));
            rows.Add(new DecompAuditRow("Map Settings Editor", "map_settings", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));
            rows.Add(new DecompAuditRow("Support Unit Editor", "support_units", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));
            rows.Add(new DecompAuditRow("Support Attribute Editor", "support_attributes", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));
            rows.Add(new DecompAuditRow("Support Talk Editor", "support_talks", "Row save",
                DecompCoverage.SourceBackedWriter, "Main structured-row save only"));

            // ---- Mixed-action SECOND rows: an editor that is source-backed for its
            //      row save but has a pointer/import sub-action that is NOT source-backed
            //      gets a distinct ManualMigration row (different Action). ----
            rows.Add(new DecompAuditRow("Map Settings Editor", "map_settings", "Chapter pointer fields (EventDataPtr, difficulty)",
                DecompCoverage.ManualMigration, "Pointer fields (D0/EventDataPtr, D96-D108 difficulty) are not source-backed"));

            // ---- SourceTreeExporter rows: each asset kind the exporter handles. ----
            rows.Add(new DecompAuditRow("Palette Editor", "palette", "Palette export",
                DecompCoverage.SourceTreeExporter, "JASC .pal export (faithful, lossless round-trip)"));
            rows.Add(new DecompAuditRow("Graphics Editor", "graphics", "Graphics export",
                DecompCoverage.SourceTreeExporter, "Indexed PNG (color type 3) + sidecar .pal"));
            rows.Add(new DecompAuditRow("Portrait Editor", "portrait", "Portrait export",
                DecompCoverage.SourceTreeExporter, "Export via --export-portrait-all (PNG package)"));
            rows.Add(new DecompAuditRow("Icon Editor", "icon", "Icon export",
                DecompCoverage.SourceTreeExporter, "Indexed PNG via graphics exporter (16x16 tiles)"));
            rows.Add(new DecompAuditRow("Map Editor", "map", "Map layout export",
                DecompCoverage.SourceTreeExporter, ".mar tilemap + sidecar .mar.json (faithful)"));
            rows.Add(new DecompAuditRow("Text Editor", "text", "Text export",
                DecompCoverage.SourceTreeExporter, "texts.txt + textdefs.txt (migration format, not lossless macro round-trip)"));

            // ---- ManualMigration rows: variable-length / raw-binary / pointer data. ----
            rows.Add(new DecompAuditRow("Item Shop Editor", "shops", "Shop list save",
                DecompCoverage.ManualMigration, "Sentinel-terminated variable-length lists; no clean source-of-truth C array"));
            rows.Add(new DecompAuditRow("Map Editor", "map_asset_binaries", "Raw map asset save (.mar/OBJ/TSA/anim)",
                DecompCoverage.ManualMigration, "Raw map binaries (tile layout, OBJ tileset, chipset TSA, tile animations) - migrate via --export-asset"));
            rows.Add(new DecompAuditRow("Event Editor", "chapter_event_pointers", "Event/difficulty pointer fields",
                DecompCoverage.ManualMigration, "Chapter pointer fields (EventDataPtr, difficulty pointers) are not source-backed"));

            // ---- ImportPreviewOnly rows: representative viewer-only editors. ----
            rows.Add(new DecompAuditRow("Battle Animation Editor", "battle_anime", "Animation view",
                DecompCoverage.ImportPreviewOnly, "Preview-only in decomp mode; no source write-back (export via --export-battle-anime)"));
            rows.Add(new DecompAuditRow("Song Table Editor", "song_table", "Song view",
                DecompCoverage.ImportPreviewOnly, "Preview-only; song data edits must be made in source by hand"));
            rows.Add(new DecompAuditRow("Magic Editor", "magic_effects", "Magic view",
                DecompCoverage.ImportPreviewOnly, "Preview-only; magic effect edits are not source-backed yet"));

            // ---- RomOnlyUnsupported rows. ----
            rows.Add(new DecompAuditRow("Hex Editor", "raw_rom", "Raw byte edit",
                DecompCoverage.RomOnlyUnsupported, "Arbitrary ROM bytes; not representable as a clean source edit"));
            rows.Add(new DecompAuditRow("Patch Manager", "patches", "Patch install/uninstall",
                DecompCoverage.RomOnlyUnsupported, "ASM/binary patches apply to the built ROM; not a decomp source migration"));

            return rows;
        }

        /// <summary>
        /// Format the matrix as TSV ("tsv") or a GitHub markdown table ("md"). PURE and
        /// NEVER throws — a null/unknown/empty <paramref name="fmt"/> defaults to TSV.
        /// The TSV header is <c>Editor\tTable\tAction\tCoverage\tNotes</c>; the markdown
        /// form emits a leading header row + separator row + one row per entry.
        /// </summary>
        public static string FormatMatrix(IReadOnlyList<DecompAuditRow> rows, string fmt)
        {
            try
            {
                rows ??= Array.Empty<DecompAuditRow>();
                bool md = string.Equals(fmt, "md", StringComparison.OrdinalIgnoreCase);

                var sb = new StringBuilder();
                if (md)
                {
                    sb.AppendLine("| Editor | Table | Action | Coverage | Notes |");
                    sb.AppendLine("| --- | --- | --- | --- | --- |");
                    foreach (DecompAuditRow r in rows)
                    {
                        sb.Append("| ");
                        sb.Append(MdCell(r.Editor)); sb.Append(" | ");
                        sb.Append(MdCell(r.Table)); sb.Append(" | ");
                        sb.Append(MdCell(r.Action)); sb.Append(" | ");
                        sb.Append(MdCell(r.Coverage.ToString())); sb.Append(" | ");
                        sb.Append(MdCell(r.Notes)); sb.AppendLine(" |");
                    }
                }
                else
                {
                    sb.AppendLine("Editor\tTable\tAction\tCoverage\tNotes");
                    foreach (DecompAuditRow r in rows)
                    {
                        sb.Append(TsvCell(r.Editor)); sb.Append('\t');
                        sb.Append(TsvCell(r.Table)); sb.Append('\t');
                        sb.Append(TsvCell(r.Action)); sb.Append('\t');
                        sb.Append(r.Coverage.ToString()); sb.Append('\t');
                        sb.AppendLine(TsvCell(r.Notes));
                    }
                }
                return sb.ToString();
            }
            catch
            {
                // never throw at the boundary
                return "Editor\tTable\tAction\tCoverage\tNotes\n";
            }
        }

        /// <summary>Escape a markdown table cell: pipes and newlines would break the row.</summary>
        static string MdCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>Flatten tabs/newlines in a TSV cell so the column structure stays intact.</summary>
        static string TsvCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
