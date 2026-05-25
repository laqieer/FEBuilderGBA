using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    // Use the Unicode variants so titles with CJK / Hangul characters resolve.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static IntPtr FindByPartialTitle(string substring)
    {
        // Guard against empty substring: every window title contains "",
        // so an empty-arg invocation would match an unrelated visible window
        // (Copilot bot review #458). Treat empty as an unfindable target.
        if (string.IsNullOrEmpty(substring))
            return IntPtr.Zero;
        IntPtr result = IntPtr.Zero;
        EnumWindowsProc proc = (hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (!string.IsNullOrEmpty(title) && title.Contains(substring, StringComparison.Ordinal))
            {
                result = hWnd;
                return false; // stop
            }
            return true;
        };
        EnumWindows(proc, IntPtr.Zero);
        return result;
    }

    static void Main(string[] args)
    {
        // Become DPI-aware so GetWindowRect returns PHYSICAL pixel coords and
        // our Bitmap matches the actual rendered window size. Without this,
        // a 200%-scaled Avalonia window reports 1280x950 logical to a
        // DPI-unaware caller and PrintWindow renders the full 2560x1900
        // physical content into a 1280x950 Bitmap, cutting off ~half of the
        // content. (#501 — discovered while capturing the action-anime
        // editor on a 200% scaled monitor.)
        SetProcessDPIAware();

        // Reject empty args[0] up-front so the user sees a clear error rather
        // than the FindByPartialTitle fallback silently matching a random
        // visible window (Copilot bot review #458).
        if (args.Length > 0 && string.IsNullOrEmpty(args[0]))
        {
            Console.Error.WriteLine("Usage: WinCapture <window title> [output.png] — title must be non-empty.");
            Environment.Exit(2);
        }
        string title = args.Length > 0 ? args[0] : "Class Editor";
        string outPath = args.Length > 1 ? args[1] : "capture.png";

        // Try exact match first (handles ASCII-only titles fast); fall back to
        // visible-window enumeration with substring match (handles Unicode
        // titles + partial matches like "Editor" finding "Class Editor").
        IntPtr hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero)
        {
            hwnd = FindByPartialTitle(title);
        }
        if (hwnd == IntPtr.Zero) { Console.Error.WriteLine($"Window '{title}' not found"); Environment.Exit(1); }

        GetWindowRect(hwnd, out RECT rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        Console.WriteLine($"Window: {w}x{h}");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
            g.ReleaseHdc(hdc);
        }
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"Saved: {outPath}");
    }
}
