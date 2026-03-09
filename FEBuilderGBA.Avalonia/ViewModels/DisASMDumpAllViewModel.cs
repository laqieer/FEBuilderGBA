using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _selectedAction; // 0=DisASM, 1=IDA MAP, 2=No$GBA SYM
        string _output = string.Empty;
        string _statusMessage = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Selected dump action: 0=Full Disassembly, 1=IDA MAP File, 2=No$GBA SYM File.</summary>
        public int SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }
        /// <summary>Output text from the dump operation.</summary>
        public string Output { get => _output; set => SetField(ref _output, value); }
        /// <summary>Status message during operation.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
