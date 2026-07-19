#nullable enable

using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public partial class GenerateRandomMapDialog : TranslatedWindow
    {
        GenerateRandomMapDialogContent? _content;
        bool _allowClose;

        public GenerateRandomMapDialogResult? DialogResult { get; private set; }

        public GenerateRandomMapDialog()
            : this(new GenerateRandomMapDialogContent())
        {
        }

        internal GenerateRandomMapDialog(GenerateRandomMapDialogContent content)
        {
            InitializeComponent();
            _content = content ?? throw new System.ArgumentNullException(nameof(content));
            Content = _content;
            Closing += GenerateRandomMapDialog_Closing;
        }

        public GenerateRandomMapDialog(int width, int height)
            : this()
        {
            _content ??= new GenerateRandomMapDialogContent();
            _content.Configure(width, height);
            _content.CloseRequested += (_, _) =>
            {
                DialogResult = _content.Result;
                _allowClose = true;
                Close();
            };
        }

        void GenerateRandomMapDialog_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!_allowClose && _content?.CanClose == false)
                e.Cancel = true;
        }

        public static async System.Threading.Tasks.Task<GenerateRandomMapDialogResult?> Show(
            Window? owner,
            int width,
            int height)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                return await WindowManager.Instance.OpenModal<GenerateRandomMapDialogContent, GenerateRandomMapDialogResult?>(
                    owner,
                    content => content.Configure(width, height));
            }

            var dlg = new GenerateRandomMapDialog(width, height);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.DialogResult;
        }
    }
}
