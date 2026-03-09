using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolExportEAEventViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _outputPath = string.Empty;
        bool _addEndGuards = true;
        int _selectedMapIndex = -1;
        string _statusText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Output file path for export.
        /// </summary>
        public string OutputPath { get => _outputPath; set => SetField(ref _outputPath, value); }

        /// <summary>
        /// Whether to add EndGuards when exporting.
        /// WinForms: addEndGuardsCheckBox (Checked=true by default).
        /// </summary>
        public bool AddEndGuards { get => _addEndGuards; set => SetField(ref _addEndGuards, value); }

        /// <summary>
        /// Selected index in the map list.
        /// WinForms: MAP_LISTBOX.
        /// </summary>
        public int SelectedMapIndex { get => _selectedMapIndex; set => SetField(ref _selectedMapIndex, value); }

        /// <summary>
        /// Status text showing export progress or results.
        /// </summary>
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        /// <summary>
        /// List of map names for the left-side map list.
        /// WinForms: MAP_LISTBOX populated in ToolExportEAEventForm_Load.
        /// </summary>
        public ObservableCollection<string> MapList { get; } = new();

        public void Initialize()
        {
            StatusText = "Export events in EA format.\n" +
                "For import, use Run > Event Assembler Add.";
            IsLoaded = true;
        }
    }
}
