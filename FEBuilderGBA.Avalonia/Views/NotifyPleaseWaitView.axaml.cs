using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class NotifyPleaseWaitView : Window
    {
        bool _forceClosing;

        /// <summary>Raised when the user clicks Cancel.</summary>
        public event Action? CancelRequested;

        public NotifyPleaseWaitView() : this(new NotifyPleaseWaitViewModel()) { }

        public NotifyPleaseWaitView(NotifyPleaseWaitViewModel vm)
        {
            InitializeComponent();
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
            _forceClosing = true;
            Close();
        }

        /// <summary>
        /// Prevent the user from closing the dialog via the window chrome close button
        /// while work is in progress. Only <see cref="ForceClose"/> can close it.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_forceClosing)
            {
                // Don't let the user dismiss the dialog by clicking X — they should use Cancel.
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}
