using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMDumpAllArgGrepView : Window, IEditorView, IDataVerifiableView
    {
        readonly DisASMDumpAllArgGrepViewModel _vm = new();

        /// <summary>Cached disassembly lines to avoid re-disassembling on each search.</summary>
        List<string>? _cachedLines;

        public string ViewTitle => "Disassembly Argument Grep";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public DisASMDumpAllArgGrepView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SearchPattern = GrepPatternInput.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_vm.SearchPattern))
            {
                _vm.Results = "Please enter a search pattern.";
                ResultsBox.Text = _vm.Results;
                return;
            }

            if (CoreState.ROM == null)
            {
                _vm.Results = "Error: No ROM loaded.";
                ResultsBox.Text = _vm.Results;
                return;
            }

            _vm.Results = "Searching... please wait.";
            ResultsBox.Text = _vm.Results;

            string pattern = _vm.SearchPattern;
            string? resultText = null;
            string? error = null;

            await Task.Run(() =>
            {
                try
                {
                    // Disassemble once and cache the result
                    if (_cachedLines == null)
                    {
                        var core = new DisassemblerCore();
                        _cachedLines = core.DisassembleToLines();
                    }

                    // Grep: find lines containing the pattern (case-insensitive)
                    var sb = new StringBuilder();
                    int matchCount = 0;
                    string patternLower = pattern.ToLowerInvariant();

                    for (int i = 0; i < _cachedLines.Count; i++)
                    {
                        string line = _cachedLines[i];
                        if (line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            sb.AppendLine(line);
                            matchCount++;
                        }
                    }

                    if (matchCount == 0)
                        resultText = $"No matches found for \"{pattern}\".";
                    else
                        resultText = $"; {matchCount} matches for \"{pattern}\"\n\n" + sb.ToString();
                }
                catch (Exception ex)
                {
                    error = $"Error: {ex.Message}";
                }
            });

            if (error != null)
                _vm.Results = error;
            else if (resultText != null)
                _vm.Results = resultText;

            ResultsBox.Text = _vm.Results;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
