using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolChangeProjectnameViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentName = string.Empty;
        string _newName = string.Empty;
        string _statusMessage = string.Empty;
        string _helpText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Current project name (read-only in UI).
        /// WinForms: CurrentName TextBoxEx (ReadOnly=true).
        /// </summary>
        public string CurrentName { get => _currentName; set => SetField(ref _currentName, value); }

        /// <summary>
        /// New project name entered by the user.
        /// WinForms: NewName TextBoxEx (editable).
        /// </summary>
        public string NewName { get => _newName; set => SetField(ref _newName, value); }

        /// <summary>
        /// Status/error message shown after rename attempt.
        /// </summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Help text explaining what the rename does.
        /// WinForms: textBox1 (readonly multiline).
        /// </summary>
        public string HelpText { get => _helpText; set => SetField(ref _helpText, value); }

        public void Initialize()
        {
            HelpText = "Safely renames the project files.\nPast backup names will also be updated.";

            if (CoreState.ROM != null && !string.IsNullOrEmpty(CoreState.ROM.Filename))
            {
                CurrentName = Path.GetFileNameWithoutExtension(CoreState.ROM.Filename);
                NewName = CurrentName;
            }

            IsLoaded = true;
        }
    }
}
