namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text preview and save dialog for struct dumps.</summary>
    public class DumpStructSelectToTextDialogViewModel : ViewModelBase
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
    }
}
