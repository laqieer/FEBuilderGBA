using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllArgGrepViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _searchPattern = string.Empty;
        string _results = string.Empty;
        string _sourceFilePath = string.Empty;
        string _targetFunctionAddress = string.Empty;
        int _searchRegisterIndex;
        int _allowedRows = 5;
        bool _hideUnknownArgs;
        bool _hideFunctionCalls;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Grep search pattern text (legacy; superseded by register-flow search).</summary>
        public string SearchPattern { get => _searchPattern; set => SetField(ref _searchPattern, value); }
        /// <summary>Output results text.</summary>
        public string Results { get => _results; set => SetField(ref _results, value); }
        /// <summary>Path to the source ASM file to search.</summary>
        public string SourceFilePath { get => _sourceFilePath; set => SetField(ref _sourceFilePath, value); }
        /// <summary>Target function: a symbol name (e.g. m4aSongNumStart) or a hex address (e.g. D01FC).</summary>
        public string TargetFunctionAddress { get => _targetFunctionAddress; set => SetField(ref _targetFunctionAddress, value); }
        /// <summary>Index of the register to search for (r0-r8; matches WinForms SearhRegister).</summary>
        public int SearchRegisterIndex { get => _searchRegisterIndex; set => SetField(ref _searchRegisterIndex, value); }
        /// <summary>Allowed rows window between the register-set and the function call (1..20, default 5).</summary>
        public int AllowedRows { get => _allowedRows; set => SetField(ref _allowedRows, value); }
        /// <summary>Show only function calls whose argument purpose is unknown (skip register-sets containing '(').</summary>
        public bool HideUnknownArgs { get => _hideUnknownArgs; set => SetField(ref _hideUnknownArgs, value); }
        /// <summary>Do not include the function-call line in the emitted block.</summary>
        public bool HideFunctionCalls { get => _hideFunctionCalls; set => SetField(ref _hideFunctionCalls, value); }

        /// <summary>Registers selectable in the SearhRegister combo (verbatim WinForms Designer list: r0..r8).</summary>
        public IReadOnlyList<string> RegisterItems { get; } = new[]
        {
            "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8"
        };

        /// <summary>Maximum allowed-rows value (WinForms NumericUpDown Maximum = 20).</summary>
        public int AllowedRowsMaximum => 20;
        /// <summary>Minimum allowed-rows value (WinForms NumericUpDown Minimum = 1).</summary>
        public int AllowedRowsMinimum => 1;

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
