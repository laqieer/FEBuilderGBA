using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Bad character warning popup for text editing.</summary>
    public class TextBadCharPopupViewModel : ViewModelBase, IDataVerifiable
    {
        string _warningText = "";
        bool _isLoaded;

        public string WarningText { get => _warningText; set => SetField(ref _warningText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Load(string warningText = "")
        {
            if (string.IsNullOrEmpty(warningText))
            {
                WarningText =
                    "Bad characters were detected in the text.\n\n" +
                    "Characters that cannot be encoded in the ROM's text encoding table\n" +
                    "will cause display errors in-game.\n\n" +
                    "Common issues:\n" +
                    "  - Using characters outside the ROM's supported character set\n" +
                    "  - Pasting text from external sources with special Unicode characters\n" +
                    "  - Using control characters that are not valid escape sequences\n\n" +
                    "Please review and correct the text before saving.";
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
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
