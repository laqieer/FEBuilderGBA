using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>Embeddable message-box body for single-view modal hosting.</summary>
    public partial class MessageBoxContent : UserControl, IEmbeddableEditor
    {
        public string ViewTitle { get; private set; } = "FEBuilderGBA";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new(
            ViewTitle,
            400,
            200,
            CanResize: false);
        public object? DialogResult => Result;
        public event EventHandler? CloseRequested;

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;
        string _message = "";

        /// <summary>
        /// Instance-scoped test seam. Production resolves the live visual root's
        /// clipboard only when the Copy button is clicked.
        /// </summary>
        internal Func<string, Task>? ClipboardWriterOverride { get; set; }

        public MessageBoxContent()
        {
            InitializeComponent();
        }

        public MessageBoxContent(string message, string title, MessageBoxMode mode) : this()
        {
            Configure(message, title, mode);
        }

        public void Configure(string message, string title, MessageBoxMode mode, bool selectable = false)
        {
            ViewTitle = title;
            _message = message ?? "";
            MessageText.Text = _message;
            SelectableMessageText.Text = _message;

            StandardMessageScroller.IsVisible = !selectable;
            SelectableMessageText.IsVisible = selectable;
            SelectableMessageText.Focusable = selectable;
            SelectableMessageText.IsTabStop = selectable;

            CopyButton.Content = R._("Copy to clipboard");
            CopyButton.IsVisible = selectable;
            CopyButton.Focusable = selectable;
            CopyButton.IsTabStop = selectable;
            CopyStatus.Text = "";
            CopyStatus.IsVisible = false;

            OkButton.IsVisible = mode != MessageBoxMode.YesNo;
            YesButton.IsVisible = mode == MessageBoxMode.YesNo;
            NoButton.IsVisible = mode == MessageBoxMode.YesNo;
            Result = MessageBoxResult.No;
        }

        public void NavigateTo(uint address) { }

        void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        private async void CopyButton_Click(object? sender, RoutedEventArgs e)
        {
            await CopyMessageAsync();
        }

        internal async Task CopyMessageAsync()
        {
            try
            {
                if (ClipboardWriterOverride != null)
                {
                    await ClipboardWriterOverride(_message);
                }
                else
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard == null)
                    {
                        ShowCopyStatus(R._("Clipboard is not available."));
                        return;
                    }
                    await clipboard.SetTextAsync(_message);
                }

                ShowCopyStatus(R._("Copied to clipboard"));
            }
            catch (Exception ex)
            {
                Log.Error($"MessageBoxContent copy failed: {ex}");
                ShowCopyStatus(R._("Clipboard operation failed: {0}", ex.Message));
            }
        }

        void ShowCopyStatus(string message)
        {
            CopyStatus.Text = message;
            CopyStatus.IsVisible = true;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Ok;
            RequestClose();
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            RequestClose();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            RequestClose();
        }
    }
}
