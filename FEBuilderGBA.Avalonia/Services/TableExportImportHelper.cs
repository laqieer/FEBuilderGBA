using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared helper for per-editor TSV export/import using StructExportCore.
    /// </summary>
    public static class TableExportImportHelper
    {
        /// <summary>
        /// Export a single table to a user-selected TSV file.
        /// </summary>
        public static async Task ExportTableAsync(Window owner, string tableName)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                CoreState.Services.ShowInfo("No ROM loaded.");
                return;
            }

            var storage = owner.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export {tableName} TSV",
                SuggestedFileName = $"{tableName}.tsv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (file == null) return;

            var path = file.Path.LocalPath;

            try
            {
                var table = StructExportCore.GetTable(tableName);
                if (table == null)
                    throw new InvalidOperationException($"Table '{tableName}' not found.");

                var structDef = StructExportCore.LoadStructDef(rom, table);
                if (structDef == null)
                    throw new InvalidOperationException(
                        $"No struct definition for '{tableName}'.");

                var rows = StructExportCore.ExportTable(rom, table, structDef);
                if (rows.Count == 0)
                    throw new InvalidOperationException("No data to export.");

                var sb = new StringBuilder();
                var headers = rows[0].Keys.ToList();
                sb.AppendLine(string.Join("\t", headers));

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    sb.AppendLine(string.Join("\t",
                        headers.Select(h => row.TryGetValue(h, out var v) ? v : "")));
                }

                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
                CoreState.Services.ShowInfo($"Exported {rows.Count} entries to {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError($"Export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Export the struct table that CONTAINS <paramref name="addr"/> to a
        /// user-selected file in the requested format (TSV/CSV/EA). Resolves the
        /// table from the address alone via <see cref="StructExportCore.ResolveTableAt"/>
        /// — the same address-driven seam the DumpStruct dispatcher already uses
        /// for its preview text (#770). Writes the file via the Core formatters
        /// (<see cref="StructExportCore.ExportToTSV"/> / ExportToCSV / ExportToEA),
        /// so the on-disk bytes are identical to the CLI <c>--export-data</c>
        /// command for that table. Returns true when a file was written.
        /// <paramref name="format"/> must be "TSV", "CSV", or "EA" (case-insensitive).
        /// </summary>
        public static async Task<bool> ExportTableByAddressAsync(Window owner, uint addr, string format)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                CoreState.Services.ShowInfo(R._("No ROM loaded."));
                return false;
            }

            var storage = owner.StorageProvider;
            if (storage == null) return false;

            string fmt = (format ?? "TSV").ToUpperInvariant();
            string ext = fmt switch { "CSV" => ".csv", "EA" => ".event", _ => ".tsv" };

            var table = StructExportCore.ResolveTableAt(rom, addr);
            if (table == null)
            {
                CoreState.Services.ShowInfo(R._(
                    "This address is not inside a known struct data table, so it cannot be exported as TSV/CSV/EA."));
                return false;
            }

            var structDef = StructExportCore.LoadStructDef(rom, table);
            if (structDef == null)
            {
                CoreState.Services.ShowError(string.Format(
                    R._("No struct definition for '{0}'."), table.Name));
                return false;
            }

            var fileType = fmt switch
            {
                "CSV" => new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } },
                "EA" => new FilePickerFileType("Event Assembler Files") { Patterns = new[] { "*.event" } },
                _ => new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
            };

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = string.Format(R._("Export {0} {1}"), table.Name, fmt),
                SuggestedFileName = $"{table.Name}{ext}",
                FileTypeChoices = new[]
                {
                    fileType,
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (file == null) return false;

            try
            {
                // NO 0-entries guard: an empty-but-valid table must still export a
                // HEADER-ONLY file, matching the CLI --export-data (StructExportCore
                // FormatTSV/CSV/EA always emit the header). The "no table resolved"
                // case is already handled above via ResolveTableAt == null.
                var entries = StructExportCore.ExportTable(rom, table, structDef);

                // #1639: StructExportCore writes by path, so route the path-based
                // export through the SAF bridge (temp + write-back on Android).
                string? written = await FileDialogHelper.WriteViaAsync(file, path =>
                {
                    switch (fmt)
                    {
                        case "CSV":
                            StructExportCore.ExportToCSV(entries, structDef, path);
                            break;
                        case "EA":
                            StructExportCore.ExportToEA(entries, structDef, path);
                            break;
                        default:
                            StructExportCore.ExportToTSV(entries, structDef, path);
                            break;
                    }
                });
                if (written == null) return false;

                CoreState.Services.ShowInfo(string.Format(
                    R._("Exported {0} entries to {1}"), entries.Count, Path.GetFileName(written)));
                return true;
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError(string.Format(R._("Export failed: {0}"), ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Import the struct table that CONTAINS <paramref name="addr"/> from a
        /// user-selected TSV file, with undo support. Resolves the table from the
        /// address alone via <see cref="StructExportCore.ResolveTableAt"/>, then
        /// parses + writes the ROM via the Core seam
        /// (<see cref="StructExportCore.ImportFromTSV"/> +
        /// <see cref="StructExportCore.WriteTable"/>) — the SAME path the CLI
        /// <c>--import-data</c> command uses (hex-index parsed from the first
        /// column, not positional). All ROM writes run inside an
        /// <see cref="UndoService"/> Begin/Commit scope; a parse/write failure
        /// rolls back so no partial mutation survives. Returns true when entries
        /// were written.
        /// </summary>
        public static async Task<bool> ImportTableByAddressAsync(Window owner, uint addr, UndoService undoService, Action? onImported = null)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                CoreState.Services.ShowInfo(R._("No ROM loaded."));
                return false;
            }

            var storage = owner.StorageProvider;
            if (storage == null) return false;

            var table = StructExportCore.ResolveTableAt(rom, addr);
            if (table == null)
            {
                CoreState.Services.ShowInfo(R._(
                    "This address is not inside a known struct data table, so it cannot be imported from TSV."));
                return false;
            }

            var structDef = StructExportCore.LoadStructDef(rom, table);
            if (structDef == null)
            {
                CoreState.Services.ShowError(string.Format(
                    R._("No struct definition for '{0}'."), table.Name));
                return false;
            }

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = string.Format(R._("Import {0} TSV"), table.Name),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return false;

            // #1639: StructExportCore.ImportFromTSV reads by path, so bridge a
            // SAF source (no local path) to a temp file on Android.
            var importPath = await FileDialogHelper.ResolveReadPathAsync(files[0]);
            if (importPath == null) return false;

            undoService.Begin($"Import {table.Name}");
            try
            {
                var entries = StructExportCore.ImportFromTSV(importPath, structDef);
                if (entries.Count == 0)
                    throw new InvalidOperationException(R._("TSV file has no data rows."));

                int written = StructExportCore.WriteTable(rom, table, structDef, entries);
                undoService.Commit();
                CoreState.Services.ShowInfo(string.Format(
                    R._("Imported {0} entries from {1}"), written, Path.GetFileName(importPath)));
                onImported?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                CoreState.Services.ShowError(string.Format(R._("Import failed: {0}"), ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Import a table from a user-selected TSV file, with undo support.
        /// </summary>
        public static async Task ImportTableAsync(Window owner, string tableName, UndoService undoService, Action? onImported = null)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                CoreState.Services.ShowInfo("No ROM loaded.");
                return;
            }

            var storage = owner.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Import {tableName} TSV",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return;

            var importPath = files[0].Path.LocalPath;

            undoService.Begin($"Import {tableName}");
            try
            {
                var lines = await File.ReadAllLinesAsync(importPath, Encoding.UTF8);
                if (lines.Length < 2)
                    throw new InvalidOperationException("TSV file has no data rows.");

                var table = StructExportCore.GetTable(tableName);
                if (table == null)
                    throw new InvalidOperationException($"Table '{tableName}' not found.");

                var structDef = StructExportCore.LoadStructDef(rom, table);
                if (structDef == null)
                    throw new InvalidOperationException(
                        $"No struct definition for '{tableName}'.");

                var headers = lines[0].Split('\t');
                var entries = new List<(int index, Dictionary<string, string> fields)>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cols = lines[i].Split('\t');
                    var fields = new Dictionary<string, string>();
                    for (int c = 0; c < headers.Length && c < cols.Length; c++)
                        fields[headers[c]] = cols[c];
                    entries.Add((i - 1, fields));
                }

                int written = StructExportCore.WriteTable(rom, table, structDef, entries);
                undoService.Commit();
                CoreState.Services.ShowInfo($"Imported {written} entries from {Path.GetFileName(importPath)}");
                onImported?.Invoke();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                CoreState.Services.ShowError($"Import failed: {ex.Message}");
            }
        }
    }
}
