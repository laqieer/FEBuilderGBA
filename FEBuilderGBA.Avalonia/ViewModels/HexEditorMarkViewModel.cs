using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorMarkViewModel : ViewModelBase
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
    }
}
