using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>Embeddable message-box body for single-view modal hosting.</summary>
    public partial class MessageBoxContent : UserControl, IEmbeddableEditor
    {
        public string ViewTitle { get; private set; } = "FEBuilderGBA";
        public bool IsLoaded => true;
        public EditorDescriptor Descriptor => new(
            ViewTitle,
            400,
            200,
            CanResize: false);
        public object? DialogResult => Result;
        public event EventHandler? CloseRequested;

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;

        public MessageBoxContent()
        {
            InitializeComponent();
        }

        public MessageBoxContent(string message, string title, MessageBoxMode mode) : this()
        {
            Configure(message, title, mode);
        }

        public void Configure(string message, string title, MessageBoxMode mode)
        {
            ViewTitle = title;
            MessageText.Text = message;
            OkButton.IsVisible = mode != MessageBoxMode.YesNo;
            YesButton.IsVisible = mode == MessageBoxMode.YesNo;
            NoButton.IsVisible = mode == MessageBoxMode.YesNo;
            Result = MessageBoxResult.No;
        }

        public void NavigateTo(uint address) { }

        void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
