using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public enum MessageBoxMode { Ok, YesNo }
    public enum MessageBoxResult { Ok, Yes, No }

    public partial class MessageBoxWindow : TitleTranslatedWindow
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
            Configure(message, title, mode, selectable: false, titleIsKey: false);
        }

        internal MessageBoxWindow(string message, string title, MessageBoxMode mode, bool selectable)
            : this(message, title, mode, selectable, titleIsKey: false)
        {
        }

        public MessageBoxWindow(string message, string title, MessageBoxMode mode, bool selectable, bool titleIsKey) : this()
        {
            Configure(message, title, mode, selectable, titleIsKey);
        }

        void Configure(string message, string title, MessageBoxMode mode, bool selectable, bool titleIsKey)
        {
            SetTitle(title, titleIsKey ? title : null);
            _content ??= new MessageBoxContent();
            _content.Configure(message, title, mode, selectable, titleIsKey);
            _content.CloseRequested += (_, _) =>
            {
                Result = _content.Result;
                Close();
            };
        }

        /// <summary>Show the dialog and return the result.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> Show(
            Window? owner, string message, string title, MessageBoxMode mode, bool titleIsKey = false)
            => await ShowCore(owner, message, title, mode, selectable: false, titleIsKey);

        /// <summary>Show a message whose body can be selected and copied.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> ShowSelectable(
            Window? owner, string message, string title, MessageBoxMode mode, bool titleIsKey = false)
            => await ShowCore(owner, message, title, mode, selectable: true, titleIsKey);

        static async System.Threading.Tasks.Task<MessageBoxResult> ShowCore(
            Window? owner, string message, string title, MessageBoxMode mode, bool selectable, bool titleIsKey)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                return await WindowManager.Instance.OpenModal<MessageBoxContent, MessageBoxResult>(
                    owner,
                    content => content.Configure(message, title, mode, selectable, titleIsKey));
            }

            var dlg = new MessageBoxWindow(message, title, mode, selectable, titleIsKey);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.Result;
        }
    }
}
