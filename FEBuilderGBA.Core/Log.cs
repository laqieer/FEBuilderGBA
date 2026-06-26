using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core logging — writes to debug output and an in-memory buffer.
    /// Platform-specific features (file rotation, UI display) stay in WinForms.
    /// </summary>
    internal class Log
    {
        public static event EventHandler UpdateEvent;
        public static StringBuilder NonWriteStringB = new StringBuilder();
        private static readonly object thisLock = new object();

        [Conditional("DEBUG")]
        public static void Debug(params string[] args)
        {
            writeString("D:", args);
        }

        public static void Notify(params string[] args)
        {
            writeString("N:", args);
        }

        public static void Error(params string[] args)
        {
            writeString("E:", args);
        }

        private static void writeString(string type, params string[] args)
        {
            string str = string.Join(" ", args);
            lock (thisLock)
            {
                NonWriteStringB.AppendLine(str);
            }
            System.Diagnostics.Debug.WriteLine(str);

            if (NonWriteStringB.Length >= 2048)
            {
                SyncLog();
            }

            UpdateEvent?.Invoke(null, EventArgs.Empty);
        }

        static string GetLogFilename()
        {
            // #1124: CoreState.BaseDirectory is the exe dir on desktop and Context.FilesDir (app-private) on Android (#1123), so the log is already redirected to app-private storage on Android.
            return Path.Combine(CoreState.BaseDirectory ?? ".", "config", "log", "log.txt");
        }

        /// <summary>
        /// #1467: Public accessor for the on-disk log file path
        /// (<c>config/log/log.txt</c> under <see cref="CoreState.BaseDirectory"/>).
        /// Lets platform UIs (Avalonia Log Viewer) offer Save / Open-log-folder
        /// without owning the path-resolution logic. The WinForms <c>Log.ToFile</c>
        /// / <c>Log.OpenLogDir</c> use the same path.
        /// </summary>
        public static string GetLogFilePath()
        {
            return GetLogFilename();
        }

        public static void SyncLog()
        {
            lock (thisLock)
            {
                string fullfilename = GetLogFilename();
                try
                {
                    string dir = Path.GetDirectoryName(fullfilename);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(fullfilename, NonWriteStringB.ToString());
                    NonWriteStringB.Clear();
                }
                catch (Exception)
                {
                    //どうすることもできない
                }
            }
        }

        public static string LogToString(int showLines = 100)
        {
            SyncLog();
            string[] lines;
            lock (thisLock)
            {
                try
                {
                    string fullfilename = GetLogFilename();
                    lines = File.ReadAllLines(fullfilename);
                }
                catch (Exception)
                {
                    lines = new string[] { };
                }
            }
            if (lines.Length < showLines)
            {
                return string.Join("\r\n", lines);
            }
            else
            {
                return string.Join("\r\n", lines, lines.Length - showLines, showLines);
            }
        }

        public static void TouchLogDirectory()
        {
            string dir = Path.Combine(CoreState.BaseDirectory ?? ".", "config", "log");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
