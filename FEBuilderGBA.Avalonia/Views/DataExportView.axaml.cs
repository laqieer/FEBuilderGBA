using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DataExportView : Window
    {
        readonly DataExportViewModel _vm = new();
        readonly UndoService _undoService = new();

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

            var tableName = _vm.SelectedTable;
            var path = file.Path.LocalPath;

            try
            {
                await ProgressDialogService.RunWithProgress(this, "Exporting Data...",
                    async (progress, ct) =>
                {
                    progress.Report(new ProgressInfo
                    {
                        Message = $"Loading table definition for '{tableName}'...",
                        PercentComplete = -1
                    });

                    var table = StructExportCore.GetTable(tableName);
                    if (table == null)
                        throw new InvalidOperationException($"Table '{tableName}' not found.");

                    ct.ThrowIfCancellationRequested();

                    var structDef = StructExportCore.LoadStructDef(rom, table);
                    if (structDef == null)
                        throw new InvalidOperationException(
                            $"No struct definition for '{tableName}'. Ensure config/data/ has the definition file.");

                    ct.ThrowIfCancellationRequested();

                    progress.Report(new ProgressInfo
                    {
                        Message = "Reading ROM data...",
                        PercentComplete = 10
                    });

                    var rows = StructExportCore.ExportTable(rom, table, structDef);
                    if (rows.Count == 0)
                        throw new InvalidOperationException("No data to export.");

                    ct.ThrowIfCancellationRequested();

                    progress.Report(new ProgressInfo
                    {
                        Message = $"Writing {rows.Count} rows...",
                        PercentComplete = 50
                    });

                    var sb = new StringBuilder();
                    var headers = rows[0].Keys.ToList();
                    sb.AppendLine(string.Join("\t", headers));

                    for (int i = 0; i < rows.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var row = rows[i];
                        sb.AppendLine(string.Join("\t",
                            headers.Select(h => row.TryGetValue(h, out var v) ? v : "")));

                        if (rows.Count > 50 && i % 50 == 0)
                        {
                            int pct = 50 + (int)(50.0 * i / rows.Count);
                            progress.Report(new ProgressInfo
                            {
                                Message = $"Writing row {i + 1} of {rows.Count}...",
                                PercentComplete = pct
                            });
                        }
                    }

                    progress.Report(new ProgressInfo
                    {
                        Message = "Saving file...",
                        PercentComplete = 95
                    });

                    await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);

                    progress.Report(new ProgressInfo
                    {
                        Message = "Done.",
                        PercentComplete = 100
                    });
                });

                _vm.StatusMessage = $"Exported to {Path.GetFileName(path)}";
            }
            catch (InvalidOperationException ex) when (ex.InnerException != null)
            {
                _vm.StatusMessage = $"Export failed: {ex.InnerException.Message}";
            }
            catch (InvalidOperationException ex)
            {
                _vm.StatusMessage = $"Export failed: {ex.Message}";
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

            var tableName = _vm.SelectedTable;
            var importPath = files[0].Path.LocalPath;

            _undoService.Begin("Import Data");
            try
            {
                await ProgressDialogService.RunWithProgress(this, "Importing Data...",
                    async (progress, ct) =>
                {
                    progress.Report(new ProgressInfo
                    {
                        Message = "Reading TSV file...",
                        PercentComplete = -1
                    });

                    var lines = await File.ReadAllLinesAsync(importPath, Encoding.UTF8, ct);
                    if (lines.Length < 2)
                        throw new InvalidOperationException("TSV file has no data rows.");

                    ct.ThrowIfCancellationRequested();

                    var table = StructExportCore.GetTable(tableName);
                    if (table == null)
                        throw new InvalidOperationException($"Table '{tableName}' not found.");

                    var structDef = StructExportCore.LoadStructDef(rom, table);
                    if (structDef == null)
                        throw new InvalidOperationException(
                            $"No struct definition for '{tableName}'. Ensure config/data/ has the definition file.");

                    progress.Report(new ProgressInfo
                    {
                        Message = "Parsing data...",
                        PercentComplete = 30
                    });

                    var headers = lines[0].Split('\t');
                    var entries = new List<(int index, Dictionary<string, string> fields)>();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(lines[i])) continue;
                        var cols = lines[i].Split('\t');
                        var fields = new Dictionary<string, string>();
                        for (int c = 0; c < headers.Length && c < cols.Length; c++)
                            fields[headers[c]] = cols[c];
                        entries.Add((i - 1, fields));
                    }

                    progress.Report(new ProgressInfo
                    {
                        Message = $"Writing {entries.Count} entries to ROM...",
                        PercentComplete = 60
                    });

                    int written = StructExportCore.WriteTable(rom, table, structDef, entries);

                    progress.Report(new ProgressInfo
                    {
                        Message = $"Imported {written} entries.",
                        PercentComplete = 100
                    });
                });

                _undoService.Commit();
                _vm.StatusMessage = $"Imported from {Path.GetFileName(importPath)}";
            }
            catch (InvalidOperationException ex) when (ex.InnerException != null)
            {
                _undoService.Rollback();
                _vm.StatusMessage = $"Import failed: {ex.InnerException.Message}";
            }
            catch (InvalidOperationException ex)
            {
                _undoService.Rollback();
                _vm.StatusMessage = $"Import failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                _vm.StatusMessage = $"Import failed: {ex.Message}";
            }
        }
    }
}
