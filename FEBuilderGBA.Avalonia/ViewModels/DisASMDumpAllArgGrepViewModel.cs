using System;

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
        int _allowedRows = 10;
        bool _hideUnknownArgs;
        bool _hideFunctionCalls;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Grep search pattern text.</summary>
        public string SearchPattern { get => _searchPattern; set => SetField(ref _searchPattern, value); }
        /// <summary>Output results text.</summary>
        public string Results { get => _results; set => SetField(ref _results, value); }
        /// <summary>Path to the source ASM file to search.</summary>
        public string SourceFilePath { get => _sourceFilePath; set => SetField(ref _sourceFilePath, value); }
        /// <summary>Target function address for grep.</summary>
        public string TargetFunctionAddress { get => _targetFunctionAddress; set => SetField(ref _targetFunctionAddress, value); }
        /// <summary>Index of the register to search for (R0-R12).</summary>
        public int SearchRegisterIndex { get => _searchRegisterIndex; set => SetField(ref _searchRegisterIndex, value); }
        /// <summary>Maximum rows to scan around the match.</summary>
        public int AllowedRows { get => _allowedRows; set => SetField(ref _allowedRows, value); }
        /// <summary>Hide results with unknown arguments.</summary>
        public bool HideUnknownArgs { get => _hideUnknownArgs; set => SetField(ref _hideUnknownArgs, value); }
        /// <summary>Hide function call results.</summary>
        public bool HideFunctionCalls { get => _hideFunctionCalls; set => SetField(ref _hideFunctionCalls, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
