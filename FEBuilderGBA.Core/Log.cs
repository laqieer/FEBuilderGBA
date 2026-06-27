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

        // #1637: real string.Format overloads.
        // The plain Debug/Notify/Error(params string[]) sink only does string.Join(" ", args),
        // so a call like Error("... {0}", ex.Message) recorded the LITERAL "{0}" token
        // instead of substituting it. These *F overloads do an actual string.Format so the
        // {N} placeholders are substituted. Call sites that pass a {N}-bearing format string
        // plus arguments must use these (the LogFormatPlaceholderTests regression guard enforces it).
        [Conditional("DEBUG")]
        public static void DebugF(string fmt, params object[] args)
        {
            writeString("D:", FormatSafe(fmt, args));
        }

        public static void NotifyF(string fmt, params object[] args)
        {
            writeString("N:", FormatSafe(fmt, args));
        }

        public static void ErrorF(string fmt, params object[] args)
        {
            writeString("E:", FormatSafe(fmt, args));
        }

        /// <summary>
        /// #1637: string.Format that can never throw from a logging call. A malformed
        /// format string (bad/extra placeholder) falls back to the raw format string —
        /// with every <c>{N}</c> token stripped so the literal-placeholder bug can never
        /// be reintroduced via the fallback path — followed by the joined arguments, so
        /// logging stays diagnostic, never fatal and never polluted with <c>{N}</c>.
        /// </summary>
        private static string FormatSafe(string fmt, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return StripPlaceholders(fmt ?? string.Empty);
            }
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt ?? string.Empty, args);
            }
            catch (FormatException)
            {
                return FormatFallback(fmt, args);
            }
            catch (ArgumentNullException)
            {
                return FormatFallback(fmt, args);
            }
        }

        private static string FormatFallback(string fmt, object[] args)
        {
            return StripPlaceholders(fmt ?? string.Empty)
                + " " + string.Join(" ", System.Array.ConvertAll(args, a => a?.ToString() ?? string.Empty));
        }

        /// <summary>
        /// Replaces every <c>{N}</c> indexed placeholder (e.g. <c>{0}</c>, <c>{1,5:X}</c>) with a
        /// neutral <c>{}</c> token so a malformed-format fallback never records a literal
        /// numbered placeholder in the log. Used only on the error path.
        /// </summary>
        private static string StripPlaceholders(string fmt)
        {
            if (string.IsNullOrEmpty(fmt) || fmt.IndexOf('{') < 0)
            {
                return fmt;
            }
            return System.Text.RegularExpressions.Regex.Replace(fmt, @"\{\d+(?:[,:][^}]*)?\}", "{}");
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
