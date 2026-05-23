// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia port of the WinForms CsvManager Class-shape (#406).
//
// Sibling to UnitCsvManager.cs (#413). The WF CsvManager handles both
// units and classes via the `exportAsClass` flag; this Avalonia port
// dedicates one service per record shape so the offsets are static and
// the per-shape tests stay small. Mirrors FEBuilderGBA/CsvManager.cs
// semantics when constructed with `isClass: true`.
//
// CSV layout (matches WF CsvManager.GetDataFromROM with exportAsClass=true):
//   [Name(UID), |  UID, ]  -- if includeName then "Name(UID)", else "UID, "
//   [HP, STR, SKL, SPD, DEF, RES, CON]    -- base stats   (offsets 11..17)
//   [HP, STR, SKL, SPD, DEF, RES, LUCK]   -- growths      (offsets 27..33)
//   [Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark]  -- weplevels (44..51)
//
// Pure I/O surface is in BuildExportCsv / ApplyImportCsv (testable headless);
// the file/clipboard dialogs live in ExportAllAsync / ExportSelectedAsync /
// ImportAllAsync / ImportSelectedAsync (Avalonia-only).
//
// FE8UMAGIC magic-split bytes are out of scope (same as UnitCsvManager — the
// MagicSplit columns require WinForms-only patch detection; default returns
// false). The HardCoding-warning hook on the editor host is a separate path.
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
    /// Avalonia-side CSV exporter/importer for the Class table. Mirrors the
    /// WinForms <c>FEBuilderGBA.CsvManager</c> semantics with
    /// <c>exportAsClass=true</c> so feature parity matches when a ROM is
    /// round-tripped between the two UIs.
    /// </summary>
    public sealed class ClassCsvManager
    {
        readonly bool _useClipboard;
        readonly bool _includeUID;
        readonly bool _includeHeader;
        readonly bool _includeName;
        readonly bool _includeBaseStats;
        readonly bool _includeGrowths;
        readonly bool _includeWepLevel;
        readonly bool _growthsAsDecimal;

        public ClassCsvManager(
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
        /// Build the CSV text for a set of class-row addresses. Pure function
        /// over ROM bytes - no dialogs, no clipboard. Returns a string ending
        /// in '\n' per data row (matches WF CsvManager output shape).
        /// </summary>
        public string BuildExportCsv(ROM rom, IReadOnlyList<uint> rowAddresses)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

            var sb = new StringBuilder();
            if (_includeHeader)
                sb.Append(BuildHeader());
            for (int i = 0; i < rowAddresses.Count; i++)
                sb.Append(BuildDataRow(rom, (uint)i, rowAddresses[i]));
            return sb.ToString();
        }

        /// <summary>
        /// Apply a previously-built CSV text to the ROM. Writes are performed
        /// via the ROM's ambient-undo write methods - the caller is expected
        /// to wrap the call in an <c>UndoService.Begin/Commit</c> scope.
        ///
        /// When the CSV contains UIDs (either via <c>_includeUID</c> alone
        /// or embedded as <c>Name(UID)</c>), each row is routed to the
        /// address indicated by that UID rather than positional order. This
        /// matches WF <c>CsvManager</c> behavior so a reordered or partial
        /// CSV imports onto the correct classes. Rows without a parseable UID
        /// fall back to positional mapping.
        ///
        /// The CSV is parsed via <c>Microsoft.VisualBasic.FileIO.TextFieldParser</c>
        /// with a <c>", "</c> delimiter (matches WF <c>CsvManager.WriteDataToROM</c>).
        /// This tolerates quoted fields, embedded commas inside quotes, and
        /// trims whitespace.
        /// </summary>
        /// <returns>Number of rows written.</returns>
        public int ApplyImportCsv(ROM rom, string csv, IReadOnlyList<uint> rowAddresses)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

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
            // present so a reordered CSV writes to the correct classes.
            bool isSingleRow = rowAddresses.Count == 1;
            for (int i = 0; i + startRow < rows.Count; i++)
            {
                string[] cols = rows[i + startRow];
                if (cols.Length == 0 || (cols.Length == 1 && string.IsNullOrWhiteSpace(cols[0]))) continue;
                int colIdx = 0;

                // Determine the destination address. Default = positional
                // mapping (matches WF behavior when neither UID nor Name is
                // exported). When _includeUID is set OR _includeName carries
                // a "Name(UID)" tail, parse the embedded UID and look it up
                // in rowAddresses by index.
                uint addr;
                if (isSingleRow)
                {
                    addr = rowAddresses[0];
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
                        addr = rowAddresses[(int)parsedUid.Value];
                    else if (i < rowAddresses.Count)
                        addr = rowAddresses[i]; // fall back to positional
                    else
                        continue; // ran out of target rows
                }

                if (_includeBaseStats)
                {
                    // Class-shape offsets 11..17 = HP, STR, SKL, SPD, DEF, RES, CON.
                    for (uint o = 11; o <= 17; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        if (sbyte.TryParse(cols[colIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sv))
                            rom.write_u8(addr + o, (uint)(byte)sv);
                        colIdx++;
                    }
                }

                if (_includeGrowths)
                {
                    // Class-shape offsets 27..33 = HP, STR, SKL, SPD, DEF, RES, LUCK growths.
                    int divisor = _growthsAsDecimal ? 100 : 1;
                    for (uint o = 27; o <= 33; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        if (float.TryParse(cols[colIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                        {
                            sbyte sv = (sbyte)Math.Round(fv * divisor);
                            rom.write_u8(addr + o, (uint)(byte)sv);
                        }
                        colIdx++;
                    }
                }

                if (_includeWepLevel)
                {
                    // Class-shape offsets 44..51 = Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark.
                    for (uint o = 44; o <= 51; o++)
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

        string BuildHeader()
        {
            // Matches WF CsvManager.SetupHeader: emits "Name" only when
            // includeName is set; UID alone does NOT introduce a header column
            // (the bare numeric UID still appears in the data rows when only
            // includeUID is set, but the header does not name it).
            var parts = new List<string>();
            if (_includeName) parts.Add("Name");

            if (_includeBaseStats)
            {
                parts.AddRange(new[] { "HP", "STR", "SKL", "SPD", "DEF", "RES", "CON" });
            }
            if (_includeGrowths)
            {
                parts.AddRange(new[] { "HP", "STR", "SKL", "SPD", "DEF", "RES", "LUCK" });
            }
            if (_includeWepLevel)
            {
                parts.AddRange(new[] { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark" });
            }
            return string.Join(", ", parts) + "\n";
        }

        string BuildDataRow(ROM rom, uint uid, uint addr)
        {
            var parts = new List<string>();

            if (_includeName)
            {
                string name = GetClassNameAt(rom, addr);
                if (_includeUID) parts.Add($"{name}({uid})");
                else parts.Add(name);
            }
            else if (_includeUID)
            {
                parts.Add(uid.ToString(CultureInfo.InvariantCulture));
            }

            if (_includeBaseStats)
            {
                // Class-shape offsets 11..17 (no LUCK).
                for (uint o = 11; o <= 17; o++)
                    parts.Add(((sbyte)rom.u8(addr + o)).ToString(CultureInfo.InvariantCulture));
            }

            if (_includeGrowths)
            {
                int divisor = _growthsAsDecimal ? 100 : 1;
                for (uint o = 27; o <= 33; o++)
                {
                    float val = (float)(sbyte)rom.u8(addr + o) / divisor;
                    parts.Add(val.ToString(_growthsAsDecimal ? "0.##" : "0", CultureInfo.InvariantCulture));
                }
            }

            if (_includeWepLevel)
            {
                // Class-shape weplevel offsets 44..51.
                for (uint o = 44; o <= 51; o++)
                    parts.Add(((sbyte)rom.u8(addr + o)).ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts) + "\n";
        }

        /// <summary>
        /// Resolve the class's display name from its name-id (u16 at offset 0).
        /// Mirrors WF <c>ClassForm.GetClassNameLow</c> via the Avalonia
        /// <c>NameResolver.GetTextById</c> path (which calls into Core
        /// <c>FETextDecode</c> when a real ROM + RomInfo are loaded).
        /// Falls back to the numeric placeholder <c>#{id}</c> for headless
        /// tests with no RomInfo configured.
        /// </summary>
        static string GetClassNameAt(ROM rom, uint addr)
        {
            try
            {
                uint textId = rom.u16(addr);
                if (rom.RomInfo == null) return $"#{textId}";
                string name = NameResolver.GetTextById(textId);
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

        /// <summary>Export a single row. Routes to clipboard or file dialog.</summary>
        public async Task ExportSelectedAsync(Window owner, ROM rom, uint addr)
        {
            string csv = BuildExportCsv(rom, new[] { addr });
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
        public async Task<int> ImportSelectedAsync(Window owner, ROM rom, uint addr)
        {
            string? csv = await ReadCsvAsync(owner);
            if (csv == null) return 0;
            return ApplyImportCsv(rom, csv, new[] { addr });
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
                Title = "Export Classes CSV",
                SuggestedFileName = "classes.csv",
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
                Title = "Import Classes CSV",
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
