using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorMarkViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _selectedMark = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedMark { get => _selectedMark; set => SetField(ref _selectedMark, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        public ObservableCollection<string> Marks { get; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void AddMark(string address)
        {
            if (!string.IsNullOrWhiteSpace(address) && !Marks.Contains(address))
                Marks.Add(address);
        }

        public void RemoveMark(string address)
        {
            Marks.Remove(address);
        }

        public int GetListCount() => Marks.Count;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["MarkCount"] = $"{Marks.Count}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
