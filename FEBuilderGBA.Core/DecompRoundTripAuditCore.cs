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
    /// Per-tier tally of the decomp coverage matrix (#1150). Immutable value type.
    /// <see cref="Total"/> is the row count; <see cref="Unclassified"/> counts rows whose
    /// coverage is not a defined <see cref="DecompCoverage"/> value (0 for the maintained
    /// matrix — surfaced so the "no editor is unclassified" contract is explicit).
    /// </summary>
    public readonly struct DecompCoverageSummary
    {
        /// <summary>Count of <see cref="DecompCoverage.SourceBackedWriter"/> rows.</summary>
        public int SourceBackedWriter { get; }
        /// <summary>Count of <see cref="DecompCoverage.SourceTreeExporter"/> rows.</summary>
        public int SourceTreeExporter { get; }
        /// <summary>Count of <see cref="DecompCoverage.ImportPreviewOnly"/> rows.</summary>
        public int ImportPreviewOnly { get; }
        /// <summary>Count of <see cref="DecompCoverage.ManualMigration"/> rows.</summary>
        public int ManualMigration { get; }
        /// <summary>Count of <see cref="DecompCoverage.RomOnlyUnsupported"/> rows.</summary>
        public int RomOnlyUnsupported { get; }
        /// <summary>Count of rows whose coverage is not a defined enum value (expect 0).</summary>
        public int Unclassified { get; }
        /// <summary>Total row count (sum of all tiers + unclassified).</summary>
        public int Total { get; }

        /// <summary>Construct a coverage summary from per-tier counts.</summary>
        public DecompCoverageSummary(int sourceBackedWriter, int sourceTreeExporter,
            int importPreviewOnly, int manualMigration, int romOnlyUnsupported,
            int unclassified, int total)
        {
            SourceBackedWriter = sourceBackedWriter;
            SourceTreeExporter = sourceTreeExporter;
            ImportPreviewOnly = importPreviewOnly;
            ManualMigration = manualMigration;
            RomOnlyUnsupported = romOnlyUnsupported;
            Unclassified = unclassified;
            Total = total;
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
        /// INDEPENDENT registry of the decomp-relevant FEBuilder editors that MUST appear
        /// in the coverage matrix (#1150). This is a SEPARATE source of truth from
        /// <see cref="BuildMatrix"/>: a completeness test asserts every name here has at
        /// least one matrix row AND every matrix editor is registered here, so the matrix
        /// cannot silently drop (or grow past) the maintained inventory.
        ///
        /// IMPORTANT — honest scope: "complete" here means complete RELATIVE TO THIS
        /// MAINTAINED INVENTORY. It is a maintained classification, NOT an exhaustive
        /// byte-level runtime round-trip proof, and NOT a guarantee that no FEBuilder
        /// editor anywhere is unlisted. When a new decomp-relevant editor is added, it is
        /// added here AND to <see cref="BuildMatrix"/> together.
        /// </summary>
        public static readonly IReadOnlyList<string> ExpectedDecompEditors = Array.AsReadOnly(new[]
        {
            "Item Editor",
            "Unit Editor",
            "Class Editor",
            "Map Settings Editor",
            "Support Unit Editor",
            "Support Attribute Editor",
            "Support Talk Editor",
            "Palette Editor",
            "Graphics Editor",
            "Portrait Editor",
            "Icon Editor",
            "Map Editor",
            "Text Editor",
            "Item Shop Editor",
            "Event Editor",
            "Battle Animation Editor",
            "Song Table Editor",
            "Magic Editor",
            "Hex Editor",
            "Patch Manager",
        });

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
                DecompCoverage.SourceTreeExporter, ".mar tilemap + sidecar .mar.json — export AND re-import/verify (lossless u16 layout body for raw entries < 0x2000, i.e. palette/flag bits 13-15 clear); compressed container re-derived by the build, not byte-pinned"));
            rows.Add(new DecompAuditRow("Map Editor", "map", "Map layout import/verify",
                DecompCoverage.SourceTreeExporter, "Re-import .mar to raw uncompressed tilemap blob + roundtrip-verify; never mutates the preview ROM"));
            rows.Add(new DecompAuditRow("Map Editor", "map_change_overlay", "Map-change overlay import/verify",
                DecompCoverage.SourceTreeExporter, "Raw uncompressed u16 overlay tile data block — export (--export-asset --kind=mapchange) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapchange) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact ROM compare; NOT the .mar layout and NOT the 12-byte change-record chain"));
            rows.Add(new DecompAuditRow("Text Editor", "text", "Text export",
                DecompCoverage.SourceTreeExporter, "texts.txt + textdefs.txt (migration format, not lossless macro round-trip)"));

            // ---- ManualMigration rows: variable-length / raw-binary / pointer data. ----
            rows.Add(new DecompAuditRow("Item Shop Editor", "shops", "Shop list save",
                DecompCoverage.ManualMigration, "Decomp-mode GUI save now routes to SOURCE when the shop's ROM address resolves to a manifest u16-list owner (symbol-resolved) AND the source list is literal-only (#1347 Slice 5a); otherwise ROM-only/manual (variable-length ITEM_NONE-terminated lists via scattered hensei/worldmap/event-cond pointers, unresolved/unnamed shops degrade to --export-asset --kind=shop)"));
            // #1149: shop lists have no manifest-owned rectangular C-array the in-place row
            // writer can target, but they CAN be migrated to source via an EA .event export
            // (distinct Action, so the SourceBackedWriter mirror invariant in
            // DecompSourceWriterCore.SourceBackedTables is unaffected).
            rows.Add(new DecompAuditRow("Item Shop Editor", "shops", "Shop list export",
                DecompCoverage.SourceTreeExporter, "EA .event migration artifact via --export-asset --kind=shop; recreates each u16 ITEM_NONE-terminated list at its source address (migration aid, not source-backed in-place editing, not a byte-pinned round-trip)"));
            // #1347: an in-place source-backed list rewrite IS possible once a manifest
            // declares a u16-list owner for the shop's resolved DATA symbol — a distinct
            // Action ("Shop list source save") so the SourceBackedWriter "Row save" mirror
            // invariant over DecompSourceWriterCore.SourceBackedTables is unaffected.
            rows.Add(new DecompAuditRow("Item Shop Editor", "shops", "Shop list source save",
                DecompCoverage.SourceBackedWriter, "In-place source-backed rewrite of a u16 ITEM_NONE-terminated list (manifest list-owner: format=u16-list, symbol-resolved) via --write-shop; requires decomp-mode .map/.elf carrying the list symbol AND a manifest list-owner; degrades to --export-asset --kind=shop otherwise (#1347). Supports BOTH a LITERAL raw-hex list AND a SYMBOLIC ITEM_* (item-id-only, quantity 0) list whose macro names resolve from the constants header (owner.constantsHeader / artifacts.itemConstants / include/constants/items.h); a non-zero quantity or an id with no ITEM_* constant is an actionable refusal, not a clobber (#1354)"));
            rows.Add(new DecompAuditRow("Map Editor", "map_asset_binaries", "Raw map asset save (GUI: OBJ/TSA/anim/map-change)",
                DecompCoverage.ManualMigration, "GUI raw-ROM-save path for the remaining LZ77/pointer map binaries (OBJ tileset, chipset TSA/config, tile animations 1/2) AND the 12-byte map-change RECORD chain (terminator/flagID/PLIST metadata) — NOT the map-change overlay tile data block (which is source-backed export/import/verify above) and NOT the .mar tile layout; migrate these via --export-asset"));
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

        /// <summary>
        /// Tally the matrix into a per-tier coverage summary (#1150). PURE; NEVER throws.
        /// <see cref="DecompCoverageSummary.Unclassified"/> counts rows whose
        /// <see cref="DecompAuditRow.Coverage"/> is not a defined <see cref="DecompCoverage"/>
        /// value — structurally 0 for the maintained matrix, but computed + surfaced so the
        /// "no decomp-relevant editor is unclassified" contract is explicit and a future bad
        /// row would show up instead of hiding.
        /// </summary>
        public static DecompCoverageSummary BuildSummary(IReadOnlyList<DecompAuditRow> rows)
        {
            int sourceBacked = 0, exporter = 0, preview = 0, manual = 0, romOnly = 0, unclassified = 0, total = 0;
            try
            {
                rows ??= Array.Empty<DecompAuditRow>();
                foreach (DecompAuditRow r in rows)
                {
                    total++;
                    switch (r.Coverage)
                    {
                        case DecompCoverage.SourceBackedWriter: sourceBacked++; break;
                        case DecompCoverage.SourceTreeExporter: exporter++; break;
                        case DecompCoverage.ImportPreviewOnly: preview++; break;
                        case DecompCoverage.ManualMigration: manual++; break;
                        case DecompCoverage.RomOnlyUnsupported: romOnly++; break;
                        default: unclassified++; break;
                    }
                }
            }
            catch
            {
                // never throw at the boundary
            }
            return new DecompCoverageSummary(sourceBacked, exporter, preview, manual, romOnly, unclassified, total);
        }

        /// <summary>
        /// Format a coverage summary as a stable plaintext block (#1150): one
        /// <c>Coverage = N</c> line per tier, a <c>Total = N</c> line, an explicit
        /// <c>Unclassified = N</c> line, an inventory-size line, and a one-line HONEST
        /// caveat. PURE; NEVER throws.
        /// </summary>
        public static string FormatSummary(DecompCoverageSummary s)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Decomp round-trip coverage summary (#1150)");
                sb.AppendLine($"SourceBackedWriter = {s.SourceBackedWriter}");
                sb.AppendLine($"SourceTreeExporter = {s.SourceTreeExporter}");
                sb.AppendLine($"ImportPreviewOnly  = {s.ImportPreviewOnly}");
                sb.AppendLine($"ManualMigration    = {s.ManualMigration}");
                sb.AppendLine($"RomOnlyUnsupported = {s.RomOnlyUnsupported}");
                sb.AppendLine($"Total              = {s.Total}");
                sb.AppendLine($"Unclassified       = {s.Unclassified}");
                sb.AppendLine($"MaintainedInventory = {ExpectedDecompEditors.Count} editors");
                sb.AppendLine("Note: complete relative to the maintained audit inventory; a maintained");
                sb.AppendLine("classification matrix, not exhaustive byte-level runtime round-trip proof.");
                sb.AppendLine("Decomp feature set currently lives on master, ahead of any tagged release");
                sb.AppendLine("(see docs/DECOMP-FEATURE-INVENTORY.md).");
                return sb.ToString();
            }
            catch
            {
                return "Decomp round-trip coverage summary (#1150)\n";
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
