using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public enum MessageBoxMode { Ok, YesNo }
    public enum MessageBoxResult { Ok, Yes, No }

    public partial class MessageBoxWindow : Window
    {
        MessageBoxContent? _content;
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;

        public MessageBoxWindow()
        {
            InitializeComponent();
            _content = new MessageBoxContent();
            Content = _content;
        }

        public MessageBoxWindow(string message, string title, MessageBoxMode mode) : this()
        {
            Title = title;
            _content ??= new MessageBoxContent();
            _content.Configure(message, title, mode);
            _content.CloseRequested += (_, _) =>
            {
                Result = _content.Result;
                Close();
            };
        }

        /// <summary>Show the dialog and return the result.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> Show(
            Window? owner, string message, string title, MessageBoxMode mode)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                return await WindowManager.Instance.OpenModal<MessageBoxContent, MessageBoxResult>(
                    owner,
                    content => content.Configure(message, title, mode));
            }

            var dlg = new MessageBoxWindow(message, title, mode);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.Result;
        }
    }
}
