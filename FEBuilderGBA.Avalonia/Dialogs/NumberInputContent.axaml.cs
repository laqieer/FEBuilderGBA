using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>Embeddable numeric-input body for single-view modal hosting.</summary>
    public partial class NumberInputContent : UserControl, IEmbeddableEditor
    {
        public string ViewTitle { get; private set; } = "FEBuilderGBA";
        public string? TitleKey { get; private set; }
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new(
            ViewTitle,
            420,
            180,
            CanResize: false);
        public object? DialogResult => Confirmed ? Value : null;
        public event EventHandler? CloseRequested;

        public bool Confirmed { get; private set; }
        public uint Value { get; private set; }

        public NumberInputContent()
        {
            InitializeComponent();
        }

        public NumberInputContent(string prompt, string title, uint defaultValue, uint min, uint max, bool titleIsKey = false) : this()
        {
            Configure(prompt, title, defaultValue, min, max, titleIsKey);
        }

        /// <param name="titleIsKey">Retain an untranslated catalog key; otherwise preserve title literally.</param>
        public void Configure(string prompt, string title, uint defaultValue, uint min, uint max, bool titleIsKey = false)
        {
            ViewTitle = title;
            TitleKey = titleIsKey ? title : null;
            PromptText.Text = prompt;
            ValueBox.Minimum = min;
            ValueBox.Maximum = max;
            ValueBox.Value = defaultValue;
            Confirmed = false;
            Value = 0;
        }

        public void NavigateTo(uint address) { }

        void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Value = (uint)(ValueBox.Value ?? 0);
            RequestClose();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed = false;
            RequestClose();
        }
    }
}
