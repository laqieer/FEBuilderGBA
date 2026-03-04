using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text preview and save dialog for struct dumps.</summary>
    public class DumpStructSelectToTextDialogViewModel : ViewModelBase, IDataVerifiable
    {
        string _fileName = "dump.txt";
        string _textContent = "";
        bool _isLoaded;

        public string FileName { get => _fileName; set => SetField(ref _fileName, value); }
        public string TextContent { get => _textContent; set => SetField(ref _textContent, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Load(string fileName, string content)
        {
            FileName = fileName ?? "dump.txt";
            TextContent = content ?? "";
            IsLoaded = true;
        }

        public int GetListCount() => 1;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["FileName"] = FileName,
                ["ContentLength"] = TextContent.Length.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
