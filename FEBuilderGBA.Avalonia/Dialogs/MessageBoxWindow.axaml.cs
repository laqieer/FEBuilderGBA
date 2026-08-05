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
            Configure(message, title, mode, selectable: false);
        }

        internal MessageBoxWindow(string message, string title, MessageBoxMode mode, bool selectable) : this()
        {
            Configure(message, title, mode, selectable);
        }

        void Configure(string message, string title, MessageBoxMode mode, bool selectable)
        {
            Title = title;
            _content ??= new MessageBoxContent();
            _content.Configure(message, title, mode, selectable);
            _content.CloseRequested += (_, _) =>
            {
                Result = _content.Result;
                Close();
            };
        }

        /// <summary>Show the dialog and return the result.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> Show(
            Window? owner, string message, string title, MessageBoxMode mode)
            => await ShowCore(owner, message, title, mode, selectable: false);

        /// <summary>Show a message whose body can be selected and copied.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> ShowSelectable(
            Window? owner, string message, string title, MessageBoxMode mode)
            => await ShowCore(owner, message, title, mode, selectable: true);

        static async System.Threading.Tasks.Task<MessageBoxResult> ShowCore(
            Window? owner, string message, string title, MessageBoxMode mode, bool selectable)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                return await WindowManager.Instance.OpenModal<MessageBoxContent, MessageBoxResult>(
                    owner,
                    content => content.Configure(message, title, mode, selectable));
            }

            var dlg = new MessageBoxWindow(message, title, mode, selectable);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.Result;
        }
    }
}
