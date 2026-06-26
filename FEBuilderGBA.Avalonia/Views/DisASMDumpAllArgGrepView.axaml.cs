using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DisASMDumpAllArgGrepView : TranslatedWindow, IEditorView, IDataVerifiableView
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
            // Pull the live option values from the controls into the VM.
            _vm.TargetFunctionAddress = TargetFunctionInput.Text ?? string.Empty;
            _vm.SearchRegisterIndex = SearchRegisterCombo.SelectedIndex < 0 ? 0 : SearchRegisterCombo.SelectedIndex;
            _vm.AllowedRows = (int)(AllowedRowsInput.Value ?? _vm.AllowedRows);
            _vm.HideFunctionCalls = HideFunctionCallsCheck.IsChecked == true;
            _vm.HideUnknownArgs = HideUnknownArgsCheck.IsChecked == true;

            if (string.IsNullOrWhiteSpace(_vm.TargetFunctionAddress))
            {
                _vm.Results = "Please enter a target function (a symbol name such as m4aSongNumStart, or a hex address such as D01FC).";
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

            // Normalize the target function (hex address -> 0x pointer string; symbol name pass-through).
            string searchFunction = DisASMArgGrepCore.NormalizeSearchFunction(_vm.TargetFunctionAddress.Trim());
            string registerText = _vm.SearchRegisterIndex >= 0 && _vm.SearchRegisterIndex < _vm.RegisterItems.Count
                ? _vm.RegisterItems[_vm.SearchRegisterIndex]
                : "r0";
            string searchReg = DisASMArgGrepCore.BuildSearchReg(registerText);
            int allowNumber = _vm.AllowedRows;
            bool hideFunctionCall = _vm.HideFunctionCalls;
            bool hideUnknownArg = _vm.HideUnknownArgs;

            string? resultText = null;
            string? error = null;

            await Task.Run(() =>
            {
                try
                {
                    // Disassemble once and cache the result.
                    if (_cachedLines == null)
                    {
                        var core = new DisassemblerCore();
                        _cachedLines = core.DisassembleToLines();
                    }

                    string grep = DisASMArgGrepCore.Grep(
                        _cachedLines,
                        searchFunction,
                        searchReg,
                        allowNumber,
                        hideFunctionCall,
                        hideUnknownArg);

                    if (string.IsNullOrEmpty(grep))
                    {
                        resultText = $"No argument blocks found for \"{searchFunction}\" feeding register \"{registerText}\" within {allowNumber} rows.";
                    }
                    else
                    {
                        resultText = $"; ArgGrep {searchFunction} {registerText}\n\n" + grep;
                    }
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
