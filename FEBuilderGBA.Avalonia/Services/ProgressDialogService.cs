using System;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Information about the current progress of a long-running operation.
    /// </summary>
    public class ProgressInfo
    {
        /// <summary>Status message shown to the user.</summary>
        public string Message { get; set; } = "";

        /// <summary>Percentage complete (0-100), or -1 for indeterminate.</summary>
        public int PercentComplete { get; set; } = -1;
    }

    /// <summary>
    /// Shows a modal progress dialog while running a long operation on a background thread.
    /// Supports both determinate (0-100%) and indeterminate (spinning) progress modes.
    /// </summary>
    public static class ProgressDialogService
    {
        /// <summary>
        /// Run a long operation with a progress dialog.
        /// The dialog is shown modally over <paramref name="parent"/>.
        /// The <paramref name="work"/> delegate receives an IProgress reporter and a CancellationToken.
        /// </summary>
        public static async Task RunWithProgress(
            Window parent,
            string title,
            Func<IProgress<ProgressInfo>, CancellationToken, Task> work)
        {
            var cts = new CancellationTokenSource();
            var vm = new NotifyPleaseWaitViewModel();
            vm.Title = title;
            vm.StatusMessage = "Starting...";
            vm.IsIndeterminate = true;
            vm.PercentComplete = 0;
            vm.IsCancelVisible = true;

            var dialog = new NotifyPleaseWaitView(vm);
            dialog.CancelRequested += () => cts.Cancel();

            Exception? workException = null;

            var progress = new Progress<ProgressInfo>(info =>
            {
                // Progress<T> callbacks are marshalled to the captured SynchronizationContext,
                // which for Avalonia is the UI thread — so this is safe.
                vm.StatusMessage = info.Message;
                if (info.PercentComplete < 0)
                {
                    vm.IsIndeterminate = true;
                    vm.PercentComplete = 0;
                }
                else
                {
                    vm.IsIndeterminate = false;
                    vm.PercentComplete = Math.Clamp(info.PercentComplete, 0, 100);
                }
            });

            // Start the background work. When it finishes (success or failure), close the dialog.
            _ = Task.Run(async () =>
            {
                try
                {
                    await work(progress, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // User cancelled — not an error
                }
                catch (Exception ex)
                {
                    workException = ex;
                }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        dialog.ForceClose();
                    });
                }
            });

            // ShowDialog blocks (awaits) until the dialog is closed.
            await dialog.ShowDialog(parent);

            if (workException != null)
                throw new InvalidOperationException(
                    $"Operation failed: {workException.Message}", workException);
        }

        /// <summary>
        /// Convenience overload without cancellation support.
        /// </summary>
        public static Task RunWithProgress(
            Window parent,
            string title,
            Func<IProgress<ProgressInfo>, Task> work)
        {
            return RunWithProgress(parent, title, (progress, _) => work(progress));
        }
    }
}
