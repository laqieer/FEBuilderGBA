using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorMarkViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        ObservableCollection<string> _marks = new();
        string _selectedMark = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public ObservableCollection<string> Marks { get => _marks; set => SetField(ref _marks, value); }
        public string SelectedMark { get => _selectedMark; set => SetField(ref _selectedMark, value); }

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
