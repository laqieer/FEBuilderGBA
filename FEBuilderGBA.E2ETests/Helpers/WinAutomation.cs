using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Lightweight Win32-based UI automation helper for finding windows and controls.
    /// Uses native Win32 APIs to avoid dependency on UIAutomation COM framework.
    /// </summary>
    public static class WinAutomation
    {
        // ------------------------------------------------------------------ P/Invoke
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const uint WM_CLOSE = 0x0010;
        private const uint BM_CLICK = 0x00F5;

        // ------------------------------------------------------------------ Public API

        /// <summary>
        /// Retrieves the window title (caption) for an hWnd using Unicode API.
        /// </summary>
        public static string GetTitle(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd);
            if (len == 0) return string.Empty;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Returns all top-level windows belonging to the given process.
        /// Does NOT filter by visibility — some forms may not be "visible" yet.
        /// </summary>
        public static List<IntPtr> GetProcessWindows(int processId)
        {
            var result = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if ((int)pid == processId) result.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>
        /// Returns the class name of an hWnd.
        /// </summary>
        public static string GetClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// Wait up to <paramref name="timeoutMs"/> ms for a top-level window belonging to
        /// <paramref name="process"/> whose title (from Process.MainWindowTitle or
        /// Win32 GetWindowText) contains <paramref name="titleSubstring"/>.
        ///
        /// Uses Process.MainWindowTitle for the primary check because it correctly
        /// returns Unicode titles even on ANSI-locale machines.
        /// Supplements with Win32 EnumWindows for secondary windows.
        ///
        /// Returns the hWnd, or IntPtr.Zero on timeout.
        /// </summary>
        public static IntPtr WaitForWindow(Process process, string titleSubstring,
                                           int timeoutMs = 20_000, int pollMs = 200)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (process.HasExited) return IntPtr.Zero;

                // Primary: check MainWindowTitle (correct Unicode support)
                try
                {
                    process.Refresh();
                    if (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                        process.MainWindowTitle.Contains(titleSubstring,
                            StringComparison.OrdinalIgnoreCase) &&
                        process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
                catch { }

                // Secondary: enumerate all top-level windows for the process
                foreach (IntPtr hWnd in GetProcessWindows(process.Id))
                {
                    string t = GetTitle(hWnd);
                    if (t.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                        return hWnd;
                }

                Thread.Sleep(pollMs);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Wait for the process's main window to appear (any WinForms top-level window).
        /// Returns the hWnd of the first WinForms-class window, or IntPtr.Zero on timeout.
        /// </summary>
        public static IntPtr WaitForAnyAppWindow(Process process,
                                                  int timeoutMs = 20_000, int pollMs = 200)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (process.HasExited) return IntPtr.Zero;

                // Check MainWindowHandle directly
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
                }
                catch { }

                // Look for any WinForms class window
                foreach (IntPtr hWnd in GetProcessWindows(process.Id))
                {
                    string cls = GetClass(hWnd);
                    if (cls.StartsWith("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase))
                        return hWnd;
                }

                Thread.Sleep(pollMs);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Returns all child windows of <paramref name="parent"/> as a list.
        /// </summary>
        public static List<(IntPtr hWnd, string Title)> GetChildWindows(IntPtr parent)
        {
            var result = new List<(IntPtr, string)>();
            EnumChildWindows(parent, (hWnd, _) =>
            {
                result.Add((hWnd, GetTitle(hWnd)));
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>
        /// Find a child window whose text equals <paramref name="text"/>.
        /// Returns IntPtr.Zero if not found.
        /// </summary>
        public static IntPtr FindChildByText(IntPtr parent, string text)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, (hWnd, _) =>
            {
                if (GetTitle(hWnd) == text) { found = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>
        /// Click a button control by posting BM_CLICK.
        /// Uses PostMessage (asynchronous) rather than SendMessage to avoid
        /// blocking indefinitely when the button handler opens a modal dialog.
        /// </summary>
        public static void ClickButton(IntPtr buttonHWnd)
        {
            PostMessage(buttonHWnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Send WM_CLOSE to the window (same as clicking the X button).
        /// </summary>
        public static void CloseWindow(IntPtr hWnd)
        {
            PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Close all top-level windows belonging to the given process
        /// that are NOT in the <paramref name="keepWindows"/> set.
        /// This is useful for dismissing unexpected modal dialogs
        /// (e.g., file browser dialogs) that may block the main form.
        /// </summary>
        public static void CloseUnexpectedWindows(int processId, HashSet<IntPtr>? keepWindows = null)
        {
            foreach (IntPtr w in GetProcessWindows(processId))
            {
                if (keepWindows != null && keepWindows.Contains(w))
                    continue;
                if (IsWindowVisible(w))
                    PostMessage(w, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
