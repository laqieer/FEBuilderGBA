using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>
    /// Small modal dialog that prompts the user for a single unsigned integer
    /// (e.g. a target row count for a table-expansion request). Mirrors the
    /// WinForms `MoveToFreeSapceForm.SimpleNewDataCount` numeric input but
    /// shrunk down to the one widget the Avalonia callers actually need.
    /// Returns the chosen value via <see cref="Value"/> when
    /// <see cref="DialogResult"/> is true; null/false on cancel.
    /// </summary>
    public partial class NumberInputDialog : Window
    {
        NumberInputContent? _content;

        /// <summary>True when the user clicked OK.</summary>
        public bool DialogResult { get; private set; }

        /// <summary>The selected value (only meaningful when <see cref="DialogResult"/> is true).</summary>
        public uint Value { get; private set; }

        public NumberInputDialog()
        {
            InitializeComponent();
            _content = new NumberInputContent();
            Content = _content;
        }

        public NumberInputDialog(string prompt, string title, uint defaultValue, uint min, uint max)
            : this()
        {
            Title = title;
            _content ??= new NumberInputContent();
            _content.Configure(prompt, title, defaultValue, min, max);
            _content.CloseRequested += (_, _) =>
            {
                DialogResult = _content.Confirmed;
                Value = _content.Value;
                Close();
            };
        }

        /// <summary>
        /// Show the dialog modally and return the chosen value, or null when
        /// the user cancelled. Mirrors the convenience helper in
        /// <see cref="MessageBoxWindow.Show"/>.
        /// </summary>
        public static async System.Threading.Tasks.Task<uint?> Show(
            Window? owner, string prompt, string title, uint defaultValue, uint min, uint max)
        {
            if (WindowManager.Instance.Service is AndroidNavigationService)
            {
                return await WindowManager.Instance.OpenModal<NumberInputContent, uint?>(
                    owner,
                    content => content.Configure(prompt, title, defaultValue, min, max));
            }

            var dlg = new NumberInputDialog(prompt, title, defaultValue, min, max);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.DialogResult ? dlg.Value : (uint?)null;
        }
    }
}
