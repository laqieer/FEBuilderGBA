using System;

namespace FEBuilderGBA.Avalonia
{
    /// <summary>
    /// IAppServices implementation for Avalonia UI.
    /// </summary>
    public class AvaloniaAppServices : IAppServices
    {
        public void ShowError(string message)
        {
            Console.Error.WriteLine("[ERROR] " + message);
        }

        public void ShowInfo(string message)
        {
            Console.WriteLine("[INFO] " + message);
        }

        public bool ShowQuestion(string message)
        {
            Console.WriteLine("[QUESTION] " + message);
            return false;
        }

        public bool ShowYesNo(string message)
        {
            Console.WriteLine("[QUESTION] " + message);
            return false;
        }

        public void RunOnUIThread(Action action)
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                action();
            else
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action).Wait();
        }

        public bool IsMainThread()
        {
            return global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
        }
    }
}
