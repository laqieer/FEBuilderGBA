using System;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    /// <summary>
    /// IAppServices implementation for CLI with colored stderr/stdout.
    /// </summary>
    public class CliAppServices : IAppServices
    {
        public void ShowError(string message)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("[ERROR] " + message);
            Console.ForegroundColor = prev;
        }

        public void ShowInfo(string message)
        {
            Console.WriteLine("[INFO] " + message);
        }

        public bool ShowQuestion(string message)
        {
            Console.Write("[QUESTION] " + message + " (y/N): ");
            string input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
            return input == "y" || input == "yes";
        }

        public bool ShowYesNo(string message)
        {
            Console.Write("[QUESTION] " + message + " (y/N): ");
            string input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
            return input == "y" || input == "yes";
        }

        public void RunOnUIThread(Action action) => action();
        public bool IsMainThread() => true;
    }
}
