using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Abstraction layer for platform-dependent services (UI dialogs, clipboard, etc.).
    /// WinForms, Avalonia, and CLI each provide their own implementation.
    /// </summary>
    public interface IAppServices
    {
        void ShowError(string message);
        void ShowInfo(string message);
        bool ShowQuestion(string message);
        bool ShowYesNo(string message);
        void RunOnUIThread(Action action);
        bool IsMainThread();
    }

    /// <summary>
    /// Minimal no-op implementation for headless / CLI scenarios.
    /// </summary>
    public class HeadlessAppServices : IAppServices
    {
        public void ShowError(string message) => Console.Error.WriteLine("[ERROR] " + message);
        public void ShowInfo(string message) => Console.WriteLine("[INFO] " + message);
        public bool ShowQuestion(string message) { Console.WriteLine("[QUESTION] " + message); return false; }
        public bool ShowYesNo(string message) { Console.WriteLine("[QUESTION] " + message); return false; }
        public void RunOnUIThread(Action action) => action();
        public bool IsMainThread() => true;
    }
}
