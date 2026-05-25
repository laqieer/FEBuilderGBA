using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

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
        /// <summary>True when the user clicked OK.</summary>
        public bool DialogResult { get; private set; }

        /// <summary>The selected value (only meaningful when <see cref="DialogResult"/> is true).</summary>
        public uint Value { get; private set; }

        public NumberInputDialog()
        {
            InitializeComponent();
        }

        public NumberInputDialog(string prompt, string title, uint defaultValue, uint min, uint max)
            : this()
        {
            Title = title;
            PromptText.Text = prompt;
            ValueBox.Minimum = min;
            ValueBox.Maximum = max;
            ValueBox.Value = defaultValue;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Value = (uint)(ValueBox.Value ?? 0);
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Show the dialog modally and return the chosen value, or null when
        /// the user cancelled. Mirrors the convenience helper in
        /// <see cref="MessageBoxWindow.Show"/>.
        /// </summary>
        public static async System.Threading.Tasks.Task<uint?> Show(
            Window? owner, string prompt, string title, uint defaultValue, uint min, uint max)
        {
            var dlg = new NumberInputDialog(prompt, title, defaultValue, min, max);
            if (owner != null)
                await dlg.ShowDialog(owner);
            else
                dlg.Show();
            return dlg.DialogResult ? dlg.Value : (uint?)null;
        }
    }
}
