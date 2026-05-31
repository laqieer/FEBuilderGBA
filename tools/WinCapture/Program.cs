using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class Program
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string title);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static void Main(string[] args)
    {
        string title = args.Length > 0 ? args[0] : "Class Editor";
        string outPath = args.Length > 1 ? args[1] : "capture.png";

        IntPtr hwnd = FindWindow(null, title);
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
