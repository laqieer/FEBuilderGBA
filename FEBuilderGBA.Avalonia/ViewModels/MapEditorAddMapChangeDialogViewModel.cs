using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorAddMapChangeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "";
        string _questionText = "Do you want to create additional map changes?";
        string _newButtonText = "Assign new map change";
        string _editButtonText = "Open map change settings";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>"New", "Edit", or "" (cancel).</summary>
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>Question text displayed at the top of the dialog.</summary>
        public string QuestionText { get => _questionText; set => SetField(ref _questionText, value); }
        /// <summary>Text for the "New" button.</summary>
        public string NewButtonText { get => _newButtonText; set => SetField(ref _newButtonText, value); }
        /// <summary>Text for the "Edit" button.</summary>
        public string EditButtonText { get => _editButtonText; set => SetField(ref _editButtonText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
