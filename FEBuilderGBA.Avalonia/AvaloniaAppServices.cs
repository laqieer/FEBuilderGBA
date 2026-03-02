using System;
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
    public class AvaloniaAppServices : IAppServices
    {
        Window? GetMainWindow()
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
                Dispatcher.UIThread.InvokeAsync(() => ShowError(message)).Wait();
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
                Dispatcher.UIThread.InvokeAsync(() => ShowInfo(message)).Wait();
            }
        }

        public bool ShowQuestion(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                var owner = GetMainWindow();
                if (owner != null)
                {
                    var task = MessageBoxWindow.Show(owner, message, "Question", MessageBoxMode.YesNo);
                    task.Wait();
                    return task.Result == MessageBoxResult.Yes;
                }
                return false;
            }
            else
            {
                bool result = false;
                Dispatcher.UIThread.InvokeAsync(() => result = ShowQuestion(message)).Wait();
                return result;
            }
        }

        public bool ShowYesNo(string message) => ShowQuestion(message);

        public void RunOnUIThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.InvokeAsync(action).Wait();
        }

        public bool IsMainThread() => Dispatcher.UIThread.CheckAccess();
    }
}
