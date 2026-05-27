using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia
{
    /// <summary>
    /// Avalonia implementation of IAppServices.
    /// Shows Avalonia modal dialogs for errors, questions, etc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Core <see cref="IAppServices"/> contract is synchronous (returns <c>bool</c>
    /// for <c>ShowQuestion</c>/<c>ShowYesNo</c>) and is invoked from many synchronous
    /// call sites in Core (e.g. <c>Rom.cs</c>, <c>U.cs</c>, <c>ItemUsagePointerCore.cs</c>)
    /// and from Avalonia synchronous click handlers (e.g. <c>ItemShopViewerView</c>).
    /// </para>
    /// <para>
    /// Avalonia's <see cref="Window.ShowDialog(Window)"/> returns a <see cref="Task"/>
    /// that completes only after the dialog window is closed, which requires the UI
    /// thread to keep pumping its message loop. A naive sync-over-async bridge
    /// (<c>task.Wait()</c> on the UI thread) deadlocks because the click events on the
    /// dialog's Yes/No buttons can never be dispatched (see issue #655).
    /// </para>
    /// <para>
    /// The fix uses a <see cref="DispatcherFrame"/> nested message loop, the
    /// Avalonia/WPF-supported pattern for synchronous modal dialogs. This mirrors
    /// what WinForms <c>MessageBox.Show</c> does internally and is the standard
    /// way to bridge sync APIs to modal Avalonia dialogs.
    /// </para>
    /// </remarks>
    public class AvaloniaAppServices : IAppServices
    {
        /// <summary>
        /// Resolves the modal-dialog owner window. Overridable so headless
        /// tests can supply a stand-in <see cref="Window"/> when there is no
        /// <see cref="IClassicDesktopStyleApplicationLifetime"/> available.
        /// </summary>
        protected virtual Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }

        public void ShowError(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                var owner = GetMainWindow();
                if (owner != null)
                    _ = MessageBoxWindow.Show(owner, message, "Error", MessageBoxMode.Ok);
                else
                    Console.Error.WriteLine("[ERROR] " + message);
            }
            else
            {
                // From background thread: safe to use InvokeAsync + GetResult.
                // The UI thread is not blocked because we're on a worker thread.
                Dispatcher.UIThread.InvokeAsync(() => ShowError(message)).GetAwaiter().GetResult();
            }
        }

        public void ShowInfo(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                var owner = GetMainWindow();
                if (owner != null)
                    _ = MessageBoxWindow.Show(owner, message, "Information", MessageBoxMode.Ok);
                else
                    Console.WriteLine("[INFO] " + message);
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() => ShowInfo(message)).GetAwaiter().GetResult();
            }
        }

        public bool ShowQuestion(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                var owner = GetMainWindow();
                if (owner == null)
                {
                    // No main window — no UI to host the modal. Returning a safe
                    // default (false) matches HeadlessAppServices behaviour for
                    // headless / shutdown paths. Log via the regular Log facade so
                    // test hosts do not interpret it as a stderr crash signal.
                    Log.Notify("AvaloniaAppServices.ShowQuestion: no main window - returning false. Message:", message);
                    return false;
                }

                // UI-thread synchronous modal: drive the dialog with a nested
                // dispatcher frame so the UI message loop keeps pumping while we
                // wait. This is the established Avalonia/WPF pattern for
                // sync-over-modal dialogs (issue #655).
                return ShowQuestionWithNestedFrame(owner, message);
            }
            else
            {
                // From background thread: marshal to UI thread and unwrap the
                // result. The UI thread itself uses the nested-frame pump, so the
                // caller observes a synchronous return value without deadlock.
                return Dispatcher.UIThread
                    .InvokeAsync(() => ShowQuestion(message))
                    .GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// UI-thread synchronous modal pump. Opens the message box, runs a
        /// nested <see cref="DispatcherFrame"/> until the dialog closes, then
        /// returns the result.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This intentionally does NOT call <c>task.Wait()</c> or read
        /// <c>task.Result</c>. Those block the UI thread and prevent the
        /// dialog's button-click events from being dispatched (issue #655).
        /// </para>
        /// <para>
        /// If the dialog task faults, the exception is captured and rethrown
        /// after the frame exits, so the call site observes the same error
        /// behaviour as a normal synchronous call.
        /// </para>
        /// </remarks>
        static bool ShowQuestionWithNestedFrame(Window owner, string message)
        {
            var frame = new DispatcherFrame();
            bool result = false;
            ExceptionDispatchInfo? caught = null;

            // Start the dialog. The continuation runs on the UI thread (no
            // ConfigureAwait needed — Avalonia tasks resume on the dispatcher
            // by default). When it completes, signal the frame to exit so the
            // outer PushFrame call returns.
            Task<MessageBoxResult> showTask;
            try
            {
                showTask = MessageBoxWindow.Show(owner, message, "Question", MessageBoxMode.YesNo);
            }
            catch (Exception ex)
            {
                // ShowDialog can throw synchronously if e.g. the owner window is
                // already closing. Surface it as a "no" answer rather than
                // crashing the call chain.
                Log.Error("AvaloniaAppServices.ShowQuestion: dialog open failed:", ex.Message);
                return false;
            }

            showTask.ContinueWith(t =>
            {
                // Marshal the result back onto the UI thread so we can mutate
                // 'frame.Continue' safely (DispatcherFrame is owned by the
                // dispatcher that called PushFrame).
                Dispatcher.UIThread.Post(() =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        caught = ExceptionDispatchInfo.Capture(t.Exception.GetBaseException());
                    else if (t.IsCompletedSuccessfully)
                        result = t.Result == MessageBoxResult.Yes;
                    // (cancellation falls through with default 'false')

                    frame.Continue = false;
                }, DispatcherPriority.Send);
            }, TaskScheduler.Default);

            // Nested message-loop pump. PushFrame keeps the UI thread responsive
            // (button clicks, paints, timers) until frame.Continue is set false.
            Dispatcher.UIThread.PushFrame(frame);

            // Rethrow with original stack trace preserved.
            caught?.Throw();

            return result;
        }

        public bool ShowYesNo(string message) => ShowQuestion(message);

        public void RunOnUIThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
        }

        public bool IsMainThread() => Dispatcher.UIThread.CheckAccess();
    }
}
