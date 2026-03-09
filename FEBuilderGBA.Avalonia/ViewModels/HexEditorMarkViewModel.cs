using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorMarkViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _selectedMark = string.Empty;
        string _dialogResult = "";
        int _selectedIndex = -1;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Currently selected marked address string.</summary>
        public string SelectedMark { get => _selectedMark; set => SetField(ref _selectedMark, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>Index of the currently selected mark in the list.</summary>
        public int SelectedIndex { get => _selectedIndex; set => SetField(ref _selectedIndex, value); }
        /// <summary>Collection of bookmarked hex addresses.</summary>
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

        /// <summary>Returns the count of bookmarked addresses.</summary>
        public int MarkCount => Marks.Count;
    }
}
