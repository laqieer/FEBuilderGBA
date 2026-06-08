// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia port of the WinForms CsvManager (#413).
//
// Mirrors FEBuilderGBA/CsvManager.cs semantics for the Unit table case:
//   - 8 options: useClipboard, includeUID, includeHeader, includeName,
//     includeBaseStats, includeGrowths, includeWepLevel, growthsAsDecimal.
//   - 4 ops:     ExportAll, ExportSelected, ImportAll, ImportSelected.
//
// CSV layout (matches WF CsvManager.GetDataFromROM):
//   [Name(UID), |  UID, ]  ?? if includeName then "Name(UID)", else "UID, "
//   [HP, STR, SKL, SPD, DEF, RES, LUCK, CON]  ?? base stats (offsets 12..19)
//   [HP, STR, SKL, SPD, DEF, RES, LUCK]       ?? growths      (offsets 28..34)
//   [Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark]  ?? weplevels (20..27)
//
// Pure I/O surface is in BuildExportCsv / ApplyImportCsv (testable headless);
// the file/clipboard dialogs live in ExportAllAsync / ExportSelectedAsync /
// ImportAllAsync / ImportSelectedAsync (Avalonia-only).
//
// Note: this is a Unit-shape port. The original WF CsvManager also handles
// Class records via an exportAsClass flag (that lives in ClassCsvManager here).
//
// FE8UMAGIC magic-split (#1016): when the active ROM has the FE8U MagicSplit
// (FE8UMAGIC) patch installed, an extra "MAG" column is appended at the END of
// the base-stat block and the END of the growth block (mirrors WF CsvManager).
// The Core MagicSplitUtil.Get/Write{Unit}{Base,Grow}MagicExtends helpers index
// by the numeric UNIT id (uid*2), NOT the addr, so export threads
// `uid = startingUid + i` and import derives `rowId` in the same branch as
// `addr`. Every MAG addition is gated on `isUsingMagicSplit`, so vanilla /
// FE6 / FE8J / FE7J output stays byte-identical. The MAG path is only enabled
// when the passed ROM IS the active CoreState.ROM (the MagicExtends helpers
// read/write through CoreState.ROM).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Avalonia-side CSV exporter/importer for the Unit table. Mirrors the
    /// WinForms <c>FEBuilderGBA.CsvManager</c> semantics so feature parity
    /// matches when a ROM is round-tripped between the two UIs.
    /// </summary>
    public sealed class UnitCsvManager
    {
        readonly bool _useClipboard;
        readonly bool _includeUID;
        readonly bool _includeHeader;
        readonly bool _includeName;
        readonly bool _includeBaseStats;
        readonly bool _includeGrowths;
        readonly bool _includeWepLevel;
        readonly bool _growthsAsDecimal;

        public UnitCsvManager(
            bool useClipboard,
            bool includeUID,
            bool includeHeader,
            bool includeName,
            bool includeBaseStats,
            bool includeGrowths,
            bool includeWepLevel,
            bool growthsAsDecimal)
        {
            _useClipboard = useClipboard;
            _includeUID = includeUID;
            _includeHeader = includeHeader;
            _includeName = includeName;
            _includeBaseStats = includeBaseStats;
            _includeGrowths = includeGrowths;
            _includeWepLevel = includeWepLevel;
            _growthsAsDecimal = growthsAsDecimal;
        }

        /// <summary>
        /// Build the CSV text for a set of unit-row addresses. Pure function
        /// over ROM bytes - no dialogs, no clipboard. Returns a string ending
        /// in '\n' per data row (matches WF CsvManager output shape).
        /// </summary>
        public string BuildExportCsv(ROM rom, IReadOnlyList<uint> rowAddresses)
            => BuildExportCsv(rom, rowAddresses, startingUid: 0);

        /// <summary>
        /// #1016 — build the CSV anchoring the UID column to
        /// <paramref name="startingUid"/> rather than 0. Mirrors the
        /// <see cref="ClassCsvManager"/> overload. Used by
        /// <see cref="ExportSelectedAsync(Window,ROM,uint,uint)"/> so a single
        /// exported unit row carries the SELECTED unit's id. This also makes
        /// the FE8U MagicSplit MAG column read from the correct unit record —
        /// the <c>MagicSplitUtil.GetUnit*MagicExtends</c> helpers index by
        /// uid*2, NOT by addr, so selected export of any unit other than row 0
        /// would otherwise read MAG from unit 0.
        /// </summary>
        public string BuildExportCsv(ROM rom, IReadOnlyList<uint> rowAddresses, uint startingUid)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

            bool isUsingMagicSplit = IsUsingMagicSplit(rom);

            var sb = new StringBuilder();
            if (_includeHeader)
                sb.Append(BuildHeader(isUsingMagicSplit));
            for (int i = 0; i < rowAddresses.Count; i++)
                sb.Append(BuildDataRow(rom, startingUid + (uint)i, rowAddresses[i], isUsingMagicSplit));
            return sb.ToString();
        }

        /// <summary>
        /// #1016 — true when the active ROM has the FE8U MagicSplit (FE8UMAGIC)
        /// patch installed. Only enabled when the passed <paramref name="rom"/>
        /// IS the active <see cref="CoreState.ROM"/> with a non-null
        /// <c>RomInfo</c> (the <c>MagicSplitUtil</c> Get/Write helpers read and
        /// write through <c>CoreState.ROM</c>). Vanilla / FE6 / FE8J / FE7J
        /// return false, keeping CSV output byte-identical to pre-#1016.
        /// </summary>
        static bool IsUsingMagicSplit(ROM rom)
        {
            return ReferenceEquals(rom, CoreState.ROM)
                && rom?.RomInfo != null
                && MagicSplitUtil.SearchMagicSplit() == MagicSplitUtil.magic_split_enum.FE8UMAGIC;
        }

        /// <summary>
        /// Apply a previously-built CSV text to the ROM. Writes are performed
        /// via the ROM's ambient-undo write methods - the caller is expected
        /// to wrap the call in an <c>UndoService.Begin/Commit</c> scope.
        ///
        /// When the CSV contains UIDs (either via <see cref="_includeUID"/>
        /// alone or embedded as <c>Name(UID)</c>), each row is routed to the
        /// address indicated by that UID rather than positional order. This
        /// matches WF <c>CsvManager</c> behavior so a reordered or partial
        /// CSV imports onto the correct units. Rows without a parseable UID
        /// fall back to positional mapping.
        ///
        /// The CSV is parsed via <c>Microsoft.VisualBasic.FileIO.TextFieldParser</c>
        /// with a <c>", "</c> delimiter (matches WF <c>CsvManager.WriteDataToROM</c>).
        /// This tolerates quoted fields, embedded commas inside quotes, and
        /// trims whitespace.
        /// </summary>
        /// <returns>Number of rows written.</returns>
        public int ApplyImportCsv(ROM rom, string csv, IReadOnlyList<uint> rowAddresses)
            => ApplyImportCsv(rom, csv, rowAddresses, singleRowId: null);

        /// <summary>
        /// #1016 overload — same as <see cref="ApplyImportCsv(ROM,string,IReadOnlyList{uint})"/>
        /// but lets a single-row import carry the SELECTED unit id so the FE8U
        /// MagicSplit MAG column is written to the correct record. The
        /// <c>MagicSplitUtil.WriteUnit*MagicExtends</c> helpers index by the
        /// numeric unit id (uid*2), NOT by addr, so single-row import must know
        /// the id (the live UI threads <c>UnitList.SelectedOriginalIndex</c>).
        /// When <paramref name="singleRowId"/> is null the legacy positional id
        /// is used (back-compat with existing call sites/tests).
        /// </summary>
        public int ApplyImportCsv(ROM rom, string csv, IReadOnlyList<uint> rowAddresses, uint? singleRowId)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

            // #1016: MagicSplit gating + ambient-undo capture. The
            // MagicSplitUtil.Write* helpers require a non-null Undo.UndoData.
            // The live UI import always opens an UndoService.Begin scope (which
            // calls ROM.BeginUndoScope), and the round-trip tests open
            // ROM.BeginUndoScope, so the ambient undo is non-null there. If MAG
            // is active but there is NO ambient undo, FAIL FAST before any
            // mutation rather than silently skipping MAG after writing the
            // normal columns.
            bool isUsingMagicSplit = IsUsingMagicSplit(rom);
            Undo.UndoData? undo = ROM.GetAmbientUndoData();
            if (isUsingMagicSplit && undo == null)
            {
                throw new InvalidOperationException(
                    "UnitCsvManager: FE8U MagicSplit import requires an active undo scope " +
                    "(ROM.BeginUndoScope / UndoService.Begin). Aborting before mutation.");
            }

            // Parse via TextFieldParser to match WF CsvManager's tolerant
            // ", " delimiter handling (quoted fields, embedded commas, trim).
            var rows = new List<string[]>();
            using (var sr = new StringReader(csv))
            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(sr))
            {
                parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                parser.Delimiters = new[] { ", " };
                parser.TrimWhiteSpace = true;
                parser.HasFieldsEnclosedInQuotes = true;
                while (!parser.EndOfData)
                {
                    string[]? fields;
                    try { fields = parser.ReadFields(); }
                    catch { fields = null; }
                    if (fields == null) continue;
                    rows.Add(fields);
                }
            }
            // Skip the header line if includeHeader was set.
            int startRow = _includeHeader && rows.Count > 0 ? 1 : 0;
            int written = 0;
            // Single-row imports (ExportSelected/ImportSelected) always route to
            // rowAddresses[0]; multi-row imports honor the embedded UID when
            // present so a reordered CSV writes to the correct units.
            bool isSingleRow = rowAddresses.Count == 1;
            // #1016: the number of columns a row must have to carry the new
            // FE8U MagicSplit MAG columns. A legacy pre-#1016 CSV (no MAG) has
            // ExpectedColumnCount(false) columns; only consume MAG cells when a
            // row's actual count matches ExpectedColumnCount(true). Without this
            // a legacy CSV's first growth value would be read as base MAG and
            // shift every following column.
            int expectedWithMag = ExpectedColumnCount(withMag: true);
            for (int i = 0; i + startRow < rows.Count; i++)
            {
                string[] cols = rows[i + startRow];
                if (cols.Length == 0 || (cols.Length == 1 && string.IsNullOrWhiteSpace(cols[0]))) continue;
                int colIdx = 0;
                // #1016: this row carries MAG only when its column count matches
                // the with-MAG layout (not merely "there are more columns").
                bool rowHasMag = isUsingMagicSplit && cols.Length == expectedWithMag;

                // Determine the destination address. Default = positional
                // mapping (matches WF behavior when neither UID nor Name is
                // exported). When _includeUID is set OR _includeName carries
                // a "Name(UID)" tail, parse the embedded UID and look it up
                // in rowAddresses by index.
                uint addr;
                // #1016: derive the numeric unit id (rowId) in the SAME branch
                // as `addr` so the MagicSplit table writes the id that owns the
                // record we just wrote normal columns to (never a mismatch).
                uint rowId;
                if (isSingleRow)
                {
                    addr = rowAddresses[0];
                    rowId = singleRowId ?? 0u;
                    if (_includeName || _includeUID) colIdx++;
                }
                else
                {
                    uint? parsedUid = null;
                    if (_includeName)
                    {
                        // Format: "Name(UID)" - split on '(' / ')'.
                        string[] nameParts = cols[0].Split('(', ')');
                        if (nameParts.Length >= 2 && uint.TryParse(nameParts[1].Trim(), out uint nuid))
                            parsedUid = nuid;
                        colIdx++;
                    }
                    else if (_includeUID)
                    {
                        if (uint.TryParse(cols[0].Trim(), out uint uuid))
                            parsedUid = uuid;
                        colIdx++;
                    }

                    if (parsedUid.HasValue && parsedUid.Value < rowAddresses.Count)
                    {
                        addr = rowAddresses[(int)parsedUid.Value];
                        rowId = parsedUid.Value; // UID-routed: id == the accepted UID.
                    }
                    else if (i < rowAddresses.Count)
                    {
                        addr = rowAddresses[i]; // fall back to positional
                        rowId = (uint)i;        // positional id == the row index.
                    }
                    else
                        continue; // ran out of target rows
                }

                if (_includeBaseStats)
                {
                    // Offsets 12..19 = HP, STR, SKL, SPD, DEF, RES, LUCK, CON.
                    for (uint o = 12; o <= 19; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        if (sbyte.TryParse(cols[colIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sv))
                            rom.write_u8(addr + o, (uint)(byte)sv);
                        colIdx++;
                    }
                    // #1016: MAG base column (after the base block). Gated on
                    // rowHasMag (row column count matches the with-MAG layout) so
                    // a legacy no-MAG CSV imported into a MagicSplit ROM does NOT
                    // consume a growth cell as MAG and shift the rest. `undo` is
                    // non-null here (fail-fast above guarantees it). Lenient
                    // TryParse to match the Unit importer's style.
                    if (rowHasMag && colIdx < cols.Length)
                    {
                        if (sbyte.TryParse(cols[colIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte mv))
                            MagicSplitUtil.WriteUnitBaseMagicExtends(rowId, addr, (uint)(byte)mv, undo!);
                        colIdx++;
                    }
                }

                if (_includeGrowths)
                {
                    // Offsets 28..34 = HP, STR, SKL, SPD, DEF, RES, LUCK growths.
                    int divisor = _growthsAsDecimal ? 100 : 1;
                    for (uint o = 28; o <= 34; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        if (float.TryParse(cols[colIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                        {
                            sbyte sv = (sbyte)Math.Round(fv * divisor);
                            rom.write_u8(addr + o, (uint)(byte)sv);
                        }
                        colIdx++;
                    }
                    // #1016: MAG growth column (after the growth block). Gated on
                    // rowHasMag (see the base-MAG read) for legacy-CSV safety.
                    if (rowHasMag && colIdx < cols.Length)
                    {
                        if (float.TryParse(cols[colIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float fmv))
                        {
                            sbyte mv = (sbyte)Math.Round(fmv * divisor);
                            MagicSplitUtil.WriteUnitGrowMagicExtends(rowId, addr, (uint)(byte)mv, undo!);
                        }
                        colIdx++;
                    }
                }

                if (_includeWepLevel)
                {
                    // Offsets 20..27 = Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark.
                    for (uint o = 20; o <= 27; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        if (sbyte.TryParse(cols[colIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sv))
                            rom.write_u8(addr + o, (uint)(byte)sv);
                        colIdx++;
                    }
                }

                written++;
            }
            return written;
        }

        // ------ private helpers ------

        /// <summary>
        /// #1016 — number of CSV columns a data row is expected to have for the
        /// current export option flags. <paramref name="withMag"/> adds the FE8U
        /// MagicSplit "MAG" column at the end of the base block (offsets 12..19)
        /// and the end of the growth block (offsets 28..34). Import compares a
        /// row's actual column count to <c>ExpectedColumnCount(true)</c> to
        /// decide whether the row really carries the new MAG columns or is a
        /// legacy pre-#1016 CSV — so a legacy CSV imported into a FE8UMAGIC ROM
        /// is NOT mis-parsed by consuming a stat cell as MAG. Mirrors the
        /// column layout emitted by <see cref="BuildHeader"/>/<c>BuildDataRow</c>:
        /// Name/UID prefix (1), base (8), growth (7), weplevel (8).
        /// </summary>
        int ExpectedColumnCount(bool withMag)
        {
            int n = 0;
            if (_includeName || _includeUID) n += 1;
            if (_includeBaseStats) n += 8 + (withMag ? 1 : 0);
            if (_includeGrowths) n += 7 + (withMag ? 1 : 0);
            if (_includeWepLevel) n += 8;
            return n;
        }

        string BuildHeader(bool isUsingMagicSplit)
        {
            // Matches WF CsvManager.SetupHeader: emits "Name, " only when
            // includeName is set; UID alone does NOT introduce a header column
            // (the bare numeric UID still appears in the data rows when only
            // includeUID is set, but the header does not name it).
            var parts = new List<string>();
            if (_includeName) parts.Add("Name");

            if (_includeBaseStats)
            {
                parts.AddRange(new[] { "HP", "STR", "SKL", "SPD", "DEF", "RES", "LUCK", "CON" });
                // #1016: MAG column appended at the END of the base block.
                if (isUsingMagicSplit) parts.Add("MAG");
            }
            if (_includeGrowths)
            {
                parts.AddRange(new[] { "HP", "STR", "SKL", "SPD", "DEF", "RES", "LUCK" });
                // #1016: MAG column appended at the END of the growth block.
                if (isUsingMagicSplit) parts.Add("MAG");
            }
            if (_includeWepLevel)
            {
                parts.AddRange(new[] { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark" });
            }
            return string.Join(", ", parts) + "\n";
        }

        string BuildDataRow(ROM rom, uint uid, uint addr, bool isUsingMagicSplit)
        {
            var parts = new List<string>();

            if (_includeName)
            {
                string name = GetUnitNameAt(rom, addr);
                if (_includeUID) parts.Add($"{name}({uid})");
                else parts.Add(name);
            }
            else if (_includeUID)
            {
                parts.Add(uid.ToString(CultureInfo.InvariantCulture));
            }

            if (_includeBaseStats)
            {
                for (uint o = 12; o <= 19; o++)
                    parts.Add(((sbyte)rom.u8(addr + o)).ToString(CultureInfo.InvariantCulture));
                // #1016: MAG base appended after the base block (id-indexed via
                // the Core helper — NOT a fixed offset).
                if (isUsingMagicSplit)
                    parts.Add(((int)(sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(uid, addr)).ToString(CultureInfo.InvariantCulture));
            }

            if (_includeGrowths)
            {
                int divisor = _growthsAsDecimal ? 100 : 1;
                for (uint o = 28; o <= 34; o++)
                {
                    float val = (float)(sbyte)rom.u8(addr + o) / divisor;
                    parts.Add(val.ToString(_growthsAsDecimal ? "0.##" : "0", CultureInfo.InvariantCulture));
                }
                // #1016: MAG growth appended after the growth block.
                if (isUsingMagicSplit)
                {
                    float m = (float)(sbyte)MagicSplitUtil.GetUnitGrowMagicExtends(uid, addr) / divisor;
                    parts.Add(m.ToString(_growthsAsDecimal ? "0.##" : "0", CultureInfo.InvariantCulture));
                }
            }

            if (_includeWepLevel)
            {
                for (uint o = 20; o <= 27; o++)
                    parts.Add(((sbyte)rom.u8(addr + o)).ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts) + "\n";
        }

        /// <summary>
        /// Resolve the unit's display name from its name-id (u16 at offset 0).
        /// Mirrors WF <c>CsvManager.GetUnitNameByAddr</c> via the Avalonia
        /// <c>NameResolver.GetTextById</c> path (which calls into Core
        /// <c>FETextDecode</c> when a real ROM + RomInfo are loaded).
        /// Falls back to the numeric placeholder <c>#{id}</c> for headless
        /// tests with no RomInfo configured.
        /// </summary>
        static string GetUnitNameAt(ROM rom, uint addr)
        {
            try
            {
                uint textId = rom.u16(addr);
                // RomInfo is required for the FETextDecode path; without it
                // we cannot decode the Huffman-encoded name.
                if (rom.RomInfo == null) return $"#{textId}";
                string name = NameResolver.GetTextById(textId);
                // GetTextById returns "???" if decoding fails - preserve the
                // numeric fallback in that case so import can still parse.
                return string.IsNullOrEmpty(name) || name == "???" ? $"#{textId}" : name.Trim();
            }
            catch { return ""; }
        }

        // ============================================================
        // ----- Avalonia-only dialog/file/clipboard helpers below -----
        // ============================================================

        /// <summary>Export all rows. Routes to clipboard or file dialog.</summary>
        public async Task ExportAllAsync(Window owner, ROM rom, IReadOnlyList<uint> rowAddresses)
        {
            string csv = BuildExportCsv(rom, rowAddresses);
            await WriteCsvAsync(owner, csv);
        }

        /// <summary>
        /// Export a single row. Routes to clipboard or file dialog. The
        /// backwards-compatible overload exports with UID=0; prefer the
        /// uid-aware overload in new code so a single exported unit row carries
        /// the SELECTED unit's id (and reads the FE8U MagicSplit MAG column
        /// from the correct record). #1016.
        /// </summary>
        public Task ExportSelectedAsync(Window owner, ROM rom, uint addr)
            => ExportSelectedAsync(owner, rom, addr, uid: 0);

        /// <summary>
        /// #1016 — export a single row anchoring the UID column (and the FE8U
        /// MagicSplit MAG column) to the SELECTED unit's id. The live UI passes
        /// <c>UnitList.SelectedOriginalIndex</c>.
        /// </summary>
        public async Task ExportSelectedAsync(Window owner, ROM rom, uint addr, uint uid)
        {
            string csv = BuildExportCsv(rom, new[] { addr }, startingUid: uid);
            await WriteCsvAsync(owner, csv);
        }

        /// <summary>Import all rows from a clipboard or file source.</summary>
        public async Task<int> ImportAllAsync(Window owner, ROM rom, IReadOnlyList<uint> rowAddresses)
        {
            string? csv = await ReadCsvAsync(owner);
            if (csv == null) return 0;
            return ApplyImportCsv(rom, csv, rowAddresses);
        }

        /// <summary>Import a single row from a clipboard or file source.</summary>
        public Task<int> ImportSelectedAsync(Window owner, ROM rom, uint addr)
            => ImportSelectedAsync(owner, rom, addr, uid: null);

        /// <summary>
        /// #1016 — import a single row carrying the SELECTED unit id so the
        /// FE8U MagicSplit MAG column is read into the correct record (the
        /// <c>WriteUnit*MagicExtends</c> helpers index by uid, not addr). The
        /// live UI threads <c>UnitList.SelectedOriginalIndex</c>.
        /// </summary>
        public async Task<int> ImportSelectedAsync(Window owner, ROM rom, uint addr, uint? uid)
        {
            string? csv = await ReadCsvAsync(owner);
            if (csv == null) return 0;
            return ApplyImportCsv(rom, csv, new[] { addr }, uid);
        }

        async Task WriteCsvAsync(Window owner, string csv)
        {
            if (_useClipboard)
            {
                var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
                if (clipboard != null) await clipboard.SetTextAsync(csv);
                return;
            }
            var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
            if (sp == null) return;
            var fileType = new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } };
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Units CSV",
                SuggestedFileName = "units.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[] { fileType },
            });
            if (file == null) return;
            using var stream = await file.OpenWriteAsync();
            byte[] bytes = Encoding.UTF8.GetBytes(csv);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Public surface for the view layer to read the CSV from the user
        /// (clipboard or file picker) BEFORE starting the undo scope, so the
        /// ambient undo doesn't span the picker await. Returns null if the
        /// user cancelled.
        /// </summary>
        public Task<string?> ReadCsvForUiAsync(Window owner) => ReadCsvAsync(owner);

        async Task<string?> ReadCsvAsync(Window owner)
        {
            if (_useClipboard)
            {
                var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
                if (clipboard == null) return null;
                return await clipboard.GetTextAsync();
            }
            var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
            if (sp == null) return null;
            var fileType = new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } };
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Units CSV",
                AllowMultiple = false,
                FileTypeFilter = new[] { fileType },
            });
            if (files == null || files.Count == 0) return null;
            using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }
}
