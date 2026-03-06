using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TextBadCharPopupViewModel : ViewModelBase, IDataVerifiable
    {
        string _warningText = "";
        string _selectedAction = "";
        bool _isLoaded;

        public string WarningText { get => _warningText; set => SetField(ref _warningText, value); }
        public string SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Load(string warningText = "")
        {
            if (string.IsNullOrEmpty(warningText))
            {
                WarningText = "Bad characters were detected in the text.";
            }
            else
            {
                WarningText = warningText;
            }
            IsLoaded = true;
        }

        public int GetListCount() => 1;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["WarningTextLength"] = WarningText.Length.ToString(),
                ["SelectedAction"] = SelectedAction,
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
