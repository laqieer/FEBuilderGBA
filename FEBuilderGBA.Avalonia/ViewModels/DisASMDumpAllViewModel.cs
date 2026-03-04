using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        int _selectedAction; // 0=DisASM, 1=IDA MAP, 2=No$GBA SYM
        string _output = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }
        public string Output { get => _output; set => SetField(ref _output, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["SelectedAction"] = $"{SelectedAction}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
