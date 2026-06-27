using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System.Collections;
using System.Diagnostics;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    class Log
    {
        public static event EventHandler UpdateEvent;
        public static StringBuilder NonWriteStringB = new StringBuilder();
        [Conditional("DEBUG")] 
        public static void Debug(
              params string[] args)
        {
            writeString("D:", args);
        }

        public static void Notify(
            params string[] args)
        {
            writeString("N:",  args);
        }
        public static void Error(
              params string[] args)
        {
            writeString("E:", args);
        }

        // #1637: real string.Format overloads.
        // The plain Debug/Notify/Error(params string[]) sink only does string.Join(" ", args),
        // so a call like Error("... {0}", ex.Message) recorded the LITERAL "{0}" token
        // instead of substituting it. These *F overloads do an actual string.Format so the
        // {N} placeholders are substituted. (Mirrors FEBuilderGBA.Core/Log.cs; this WinForms
        // Log class shadows the Core one for the WinForms assembly.)
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
        /// format string falls back to the raw format string — with every {N} token
        /// stripped so the literal-placeholder bug can never be reintroduced via the
        /// fallback — followed by the joined arguments.
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
                + " " + string.Join(" ", Array.ConvertAll(args, a => a?.ToString() ?? string.Empty));
        }

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

            if (UpdateEvent != null)
            {
                UpdateEvent(null, EventArgs.Empty);
            }
        }
        static string GetLogFilename()
        {
            return System.IO.Path.Combine(Program.BaseDirectory ?? ".", "config","log", "log.txt");
        }
        static string GetOldLogFilename(int num)
        {
            return System.IO.Path.Combine(Program.BaseDirectory ?? ".", "config","log","log."+num+".txt.7z");
        }
        private static Object thisLock = new Object();  
        public static void SyncLog()
        {
            lock (thisLock)
            {
                string fullfilename = GetLogFilename();
                try
                {
                    File.AppendAllText(fullfilename, NonWriteStringB.ToString());
                    NonWriteStringB.Clear();

                    if (U.GetFileSize(fullfilename) >= 1024 * 256)
                    {
                        Rotate(fullfilename);
                    }
                }
                catch (Exception)
                {
                    //どうすることもできない
                }
            }
        }

        const int GEN_COUNT = 30;
        static void Rotate(string current)
        {
            {
                string oldlog2 = GetOldLogFilename(GEN_COUNT);
                if (File.Exists(oldlog2))
                {
                    File.Delete(oldlog2);
                }
            }
            for (int i = GEN_COUNT; i > 0; i--)
            {
                string oldlog1 = GetOldLogFilename(i - 1);
                string oldlog2 = GetOldLogFilename(i);

                if (File.Exists(oldlog1))
                {
                    File.Move(oldlog1, oldlog2);
                }
            }
            {
                string oldlog2 = GetOldLogFilename(0);
                ArchSevenZip.Compress(oldlog2, current);
                File.Delete(current);
            }
        }
        static string[] LogToLines()
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
                    lines = new string[]{};
                }
            }
            return lines;
        }

        public static String LogToString(int showLines = 100)
        {
            string[] lines = LogToLines();
            if (lines.Length < showLines)
            {
                return string.Join("\r\n", lines);
            }
            else
            {
                return string.Join("\r\n", lines, lines.Length - showLines, showLines);
            }
        }
        public static void LogToListBox(ListBox LogList)
        {
            string[] lines = LogToLines();
            int length = lines.Length;
            int limit = Math.Min(100, length);

            LogList.BeginUpdate();
            LogList.Items.Clear();

            for (int i = 0; i < limit; i++)
            {
                LogList.Items.Add(lines[length - (limit  - i)]);
            }
            LogList.EndUpdate();
        }

        public static void ToFile(string filename)
        {
            U.WriteAllText(filename, LogToString());
            U.SelectFileByExplorer(filename);
        }
        public static void OpenLogDir()
        {
            U.SelectFileByExplorer(GetLogFilename());
        }

        public static void TouchLogDirectory()
        {
            string dir = Path.Combine(Program.BaseDirectory ?? ".", "config", "log");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    };
}
