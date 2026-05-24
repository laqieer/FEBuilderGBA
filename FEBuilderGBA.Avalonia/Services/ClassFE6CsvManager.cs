// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia FE6-specific CSV exporter/importer for the Class table (#388).
//
// Sibling to ClassCsvManager.cs (#406). The FE6 class shape diverges from
// the FE7/8 class shape in three places:
//   - Weapon ranks live at offsets 40..47 (not 44..51).
//   - There is no rain/snow move-cost pointer (P52 is the only move cost).
//   - There is no FE8UMAGIC / MagicSplit patch.
//
// CSV layout (matches WF CsvManager + ClassFE6Form data shape):
//   [Name(UID), |  UID, ]                                  -- when includeName / includeUID
//   [HP, STR, SKL, SPD, DEF, RES, CON]    -- base stats   (offsets 11..17, same as FE7/8)
//   [HP, STR, SKL, SPD, DEF, RES, LUCK]   -- growths      (offsets 27..33, same as FE7/8)
//   [Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark]    -- weplevels (FE6 offsets 40..47)
//
// Pure I/O surface is in BuildExportCsv / ApplyImportCsv (testable headless);
// the file/clipboard dialogs live in ExportAllAsync / ExportSelectedAsync /
// ImportAllAsync / ImportSelectedAsync (Avalonia-only).
//
// FE6 has no FE8UMAGIC patch, so MagicSplit columns are out of scope by
// design (not the "depends on WF patch detection" reason from ClassCsvManager).
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
    /// Avalonia-side CSV exporter/importer for the FE6 Class table. Mirrors
    /// <see cref="ClassCsvManager"/> but with FE6 weapon-rank offsets
    /// (B40-B47 instead of B44-B51).
    /// </summary>
    public sealed class ClassFE6CsvManager
    {
        readonly bool _useClipboard;
        readonly bool _includeUID;
        readonly bool _includeHeader;
        readonly bool _includeName;
        readonly bool _includeBaseStats;
        readonly bool _includeGrowths;
        readonly bool _includeWepLevel;
        readonly bool _growthsAsDecimal;

        public ClassFE6CsvManager(
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
        /// in '\n' per data row. UIDs start at 0 (matches the WF Export-All path).
        /// </summary>
        public string BuildExportCsv(ROM rom, IReadOnlyList<uint> rowAddresses)
            => BuildExportCsv(rom, rowAddresses, startingUid: 0);

        /// <summary>
        /// Build the CSV text anchoring the UID column to <paramref name="startingUid"/>
        /// instead of 0. Used by <see cref="ExportSelectedAsync"/> so an exported
        /// single row carries the SELECTED class's UID (matches WF
        /// <c>CsvManager.ExportSingle(InputFormRef, index)</c>).
        /// </summary>
        public string BuildExportCsv(ROM rom, IReadOnlyList<uint> rowAddresses, uint startingUid)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

            var sb = new StringBuilder();
            if (_includeHeader)
                sb.Append(BuildHeader());
            for (int i = 0; i < rowAddresses.Count; i++)
                sb.Append(BuildDataRow(rom, startingUid + (uint)i, rowAddresses[i]));
            return sb.ToString();
        }

        /// <summary>
        /// Apply a previously-built CSV text to the ROM. Writes via the ROM's
        /// ambient-undo write methods - caller is expected to wrap in
        /// <c>UndoService.Begin/Commit</c>.
        ///
        /// UID routing matches <see cref="ClassCsvManager.ApplyImportCsv"/>:
        /// embedded UIDs route multi-row imports to the correct class even if
        /// the CSV is reordered; unparseable UIDs throw <see cref="FormatException"/>
        /// (no silent positional fallback when the user opted into UID routing).
        /// </summary>
        /// <returns>Number of rows written.</returns>
        public int ApplyImportCsv(ROM rom, string csv, IReadOnlyList<uint> rowAddresses)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            if (rowAddresses == null) throw new ArgumentNullException(nameof(rowAddresses));

            var rows = new List<string[]>();
            using (var sr = new StringReader(csv))
            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(sr))
            {
                parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                parser.Delimiters = new[] { ", " };
                parser.TrimWhiteSpace = true;
                parser.HasFieldsEnclosedInQuotes = true;
                int parserLineNumber = 1;
                while (!parser.EndOfData)
                {
                    string[]? fields;
                    try { fields = parser.ReadFields(); }
                    catch (Microsoft.VisualBasic.FileIO.MalformedLineException ex)
                    {
                        throw new FormatException(
                            $"ClassFE6CsvManager: malformed CSV at line {parserLineNumber}: {ex.Message}", ex);
                    }
                    parserLineNumber++;
                    if (fields == null) continue;
                    rows.Add(fields);
                }
            }
            int startRow = _includeHeader && rows.Count > 0 ? 1 : 0;
            int written = 0;
            bool isSingleRow = rowAddresses.Count == 1;
            for (int i = 0; i + startRow < rows.Count; i++)
            {
                string[] cols = rows[i + startRow];
                if (cols.Length == 0 || (cols.Length == 1 && string.IsNullOrWhiteSpace(cols[0]))) continue;
                int colIdx = 0;
                int csvLine = i + startRow + 1;

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
                    }
                    else if ((_includeUID || _includeName) && !parsedUid.HasValue)
                    {
                        throw new FormatException(
                            $"ClassFE6CsvManager: missing or invalid UID at CSV line {csvLine} (row index {i}). Aborting import.");
                    }
                    else if (i < rowAddresses.Count)
                    {
                        addr = rowAddresses[i];
                    }
                    else
                    {
                        continue;
                    }
                }

                if (_includeBaseStats)
                {
                    // Class-shape base-stats offsets 11..17 (same as FE7/8).
                    for (uint o = 11; o <= 17; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        rom.write_u8(addr + o, (uint)(byte)ParseStrictSbyte(cols[colIdx].Trim(), csvLine, "base stat"));
                        colIdx++;
                    }
                }

                if (_includeGrowths)
                {
                    // Class-shape growths offsets 27..33 (same as FE7/8).
                    int divisor = _growthsAsDecimal ? 100 : 1;
                    for (uint o = 27; o <= 33; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        float fv = ParseStrictFloat(cols[colIdx].Trim(), csvLine, "growth");
                        sbyte sv = (sbyte)Math.Round(fv * divisor);
                        rom.write_u8(addr + o, (uint)(byte)sv);
                        colIdx++;
                    }
                }

                if (_includeWepLevel)
                {
                    // FE6 weapon-rank offsets 40..47 = Sword, Lance, Axe, Bow,
                    // Staff, Anima, Light, Dark (NOT FE7/8's 44..51).
                    for (uint o = 40; o <= 47; o++)
                    {
                        if (colIdx >= cols.Length) break;
                        rom.write_u8(addr + o, (uint)(byte)ParseStrictSbyte(cols[colIdx].Trim(), csvLine, "weapon level"));
                        colIdx++;
                    }
                }

                written++;
            }
            return written;
        }

        // ------ private helpers ------

        static bool TryParseFloatTolerant(string s, out float result)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return true;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out result)) return true;
            return false;
        }

        static sbyte ParseStrictSbyte(string s, int csvLine, string fieldKind)
        {
            if (sbyte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sv)) return sv;
            throw new FormatException(
                $"ClassFE6CsvManager: invalid {fieldKind} value '{s}' at CSV line {csvLine}. Aborting import.");
        }

        static float ParseStrictFloat(string s, int csvLine, string fieldKind)
        {
            if (TryParseFloatTolerant(s, out float fv)) return fv;
            throw new FormatException(
                $"ClassFE6CsvManager: invalid {fieldKind} value '{s}' at CSV line {csvLine}. Aborting import.");
        }

        string BuildHeader()
        {
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
                // FE6 weapon-rank offsets 40..47 (NOT 44..51).
                for (uint o = 40; o <= 47; o++)
                    parts.Add(((sbyte)rom.u8(addr + o)).ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts) + "\n";
        }

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

        public async Task ExportAllAsync(Window owner, ROM rom, IReadOnlyList<uint> rowAddresses)
        {
            string csv = BuildExportCsv(rom, rowAddresses);
            await WriteCsvAsync(owner, csv);
        }

        public async Task ExportSelectedAsync(Window owner, ROM rom, uint addr, uint uid)
        {
            string csv = BuildExportCsv(rom, new[] { addr }, startingUid: uid);
            await WriteCsvAsync(owner, csv);
        }

        public Task ExportSelectedAsync(Window owner, ROM rom, uint addr)
            => ExportSelectedAsync(owner, rom, addr, uid: 0);

        public async Task<int> ImportAllAsync(Window owner, ROM rom, IReadOnlyList<uint> rowAddresses)
        {
            string? csv = await ReadCsvAsync(owner);
            if (csv == null) return 0;
            return ApplyImportCsv(rom, csv, rowAddresses);
        }

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
                Title = "Export Classes (FE6) CSV",
                SuggestedFileName = "classes_fe6.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[] { fileType },
            });
            if (file == null) return;
            using var stream = await file.OpenWriteAsync();
            byte[] bytes = Encoding.UTF8.GetBytes(csv);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

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
                Title = "Import Classes (FE6) CSV",
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
