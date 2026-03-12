using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;

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

            var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
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

            var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
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
