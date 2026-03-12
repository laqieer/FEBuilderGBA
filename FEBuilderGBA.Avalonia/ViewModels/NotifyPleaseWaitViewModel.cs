namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the progress dialog (NotifyPleaseWaitView).
    /// Supports determinate (0-100%) and indeterminate (spinning) progress.
    /// </summary>
    public class NotifyPleaseWaitViewModel : ViewModelBase
    {
        string _title = "Please Wait";
        string _statusMessage = "";
        int _percentComplete;
        bool _isIndeterminate = true;
        bool _isCancelVisible;

        /// <summary>Dialog title.</summary>
        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        /// <summary>Current status message shown below the progress bar.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        /// <summary>Percentage (0-100) when in determinate mode.</summary>
        public int PercentComplete
        {
            get => _percentComplete;
            set => SetField(ref _percentComplete, value);
        }

        /// <summary>True for a spinning/indeterminate progress bar, false for percentage-based.</summary>
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetField(ref _isIndeterminate, value);
        }

        /// <summary>Whether the Cancel button is shown.</summary>
        public bool IsCancelVisible
        {
            get => _isCancelVisible;
            set => SetField(ref _isCancelVisible, value);
        }
    }
}
