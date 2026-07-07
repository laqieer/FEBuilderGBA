using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class NotifyPleaseWaitView : TranslatedUserControl, IEmbeddableEditor
    {
        public string ViewTitle => "Progress";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Progress", 420, 180, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void NavigateTo(uint address) { }

        /// <summary>Raised when the user clicks Cancel.</summary>
        public event Action? CancelRequested;

        public NotifyPleaseWaitView() : this(new NotifyPleaseWaitViewModel()) { }

        public NotifyPleaseWaitView(NotifyPleaseWaitViewModel vm)
        {
            InitializeComponent();
            SetViewModel(vm);
        }

        public void SetViewModel(NotifyPleaseWaitViewModel vm)
        {
            DataContext = vm;
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke();
        }

        /// <summary>
        /// Close the dialog programmatically (from the background task completion).
        /// </summary>
        public void ForceClose()
        {
            RequestClose();
        }
    }
}
