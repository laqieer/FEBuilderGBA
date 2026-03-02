using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public enum MessageBoxMode { Ok, YesNo }
    public enum MessageBoxResult { Ok, Yes, No }

    public partial class MessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;

        public MessageBoxWindow()
        {
            InitializeComponent();
        }

        public MessageBoxWindow(string message, string title, MessageBoxMode mode) : this()
        {
            Title = title;
            MessageText.Text = message;

            if (mode == MessageBoxMode.YesNo)
            {
                OkButton.IsVisible = false;
                YesButton.IsVisible = true;
                NoButton.IsVisible = true;
            }
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Ok;
            Close();
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        /// <summary>Show the dialog and return the result.</summary>
        public static async System.Threading.Tasks.Task<MessageBoxResult> Show(
            Window? owner, string message, string title, MessageBoxMode mode)
        {
            var dlg = new MessageBoxWindow(message, title, mode);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.Result;
        }
    }
}
