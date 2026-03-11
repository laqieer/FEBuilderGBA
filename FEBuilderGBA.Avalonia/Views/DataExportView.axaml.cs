using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DataExportView : Window
    {
        readonly DataExportViewModel _vm = new();

        public DataExportView()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_vm.SelectedTable))
            {
                _vm.StatusMessage = "Please select a table.";
                return;
            }

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                _vm.StatusMessage = "No ROM loaded.";
                return;
            }

            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export TSV",
                SuggestedFileName = $"{_vm.SelectedTable}.tsv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (file == null) return;

            try
            {
                var table = StructExportCore.GetTable(_vm.SelectedTable);
                if (table == null)
                {
                    _vm.StatusMessage = $"Table '{_vm.SelectedTable}' not found.";
                    return;
                }

                var structDef = StructExportCore.LoadStructDef(rom, table);
                if (structDef == null)
                {
                    _vm.StatusMessage = $"No struct definition for '{_vm.SelectedTable}'. Ensure config/data/ has the definition file.";
                    return;
                }

                var rows = StructExportCore.ExportTable(rom, table, structDef);
                if (rows.Count == 0)
                {
                    _vm.StatusMessage = "No data to export.";
                    return;
                }

                var sb = new StringBuilder();
                var headers = rows[0].Keys.ToList();
                sb.AppendLine(string.Join("\t", headers));
                foreach (var row in rows)
                    sb.AppendLine(string.Join("\t", headers.Select(h => row.TryGetValue(h, out var v) ? v : "")));

                var path = file.Path.LocalPath;
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                _vm.StatusMessage = $"Exported {rows.Count} rows to {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = $"Export failed: {ex.Message}";
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_vm.SelectedTable))
            {
                _vm.StatusMessage = "Please select a table.";
                return;
            }

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                _vm.StatusMessage = "No ROM loaded.";
                return;
            }

            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import TSV",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("TSV Files") { Patterns = new[] { "*.tsv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return;

            try
            {
                var path = files[0].Path.LocalPath;
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    _vm.StatusMessage = "TSV file has no data rows.";
                    return;
                }

                var table = StructExportCore.GetTable(_vm.SelectedTable);
                if (table == null)
                {
                    _vm.StatusMessage = $"Table '{_vm.SelectedTable}' not found.";
                    return;
                }

                var structDef = StructExportCore.LoadStructDef(rom, table);
                if (structDef == null)
                {
                    _vm.StatusMessage = $"No struct definition for '{_vm.SelectedTable}'. Ensure config/data/ has the definition file.";
                    return;
                }

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
                _vm.StatusMessage = $"Imported {written} entries from {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = $"Import failed: {ex.Message}";
            }
        }
    }
}
