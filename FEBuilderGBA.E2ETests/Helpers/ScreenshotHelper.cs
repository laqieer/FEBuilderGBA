using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Captures screenshots of a specific window and saves them as PNG artifacts.
    /// </summary>
    public static class ScreenshotHelper
    {
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // PW_RENDERFULLCONTENT = 2: captures even DWM-composed content
        private const uint PW_RENDERFULLCONTENT = 2;
        private const uint SRCCOPY = 0x00CC0020;

        /// <summary>
        /// Directory where screenshots are saved.
        /// Defaults to a "screenshots" folder beside the test assembly.
        /// Override with FEBUILDERGBA_SCREENSHOT_DIR env var.
        /// </summary>
        public static string OutputDirectory { get; } = GetOutputDir();

        private static string GetOutputDir()
        {
            string? envDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(envDir)) return envDir;

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                            ?? Directory.GetCurrentDirectory();
            return Path.Combine(asmDir, "screenshots");
        }

        /// <summary>
        /// Sanitize a name for use as a filename by replacing invalid chars with underscore.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }

        /// <summary>
        /// Capture a screenshot with a deterministic filename (no timestamp suffix).
        /// Useful for predictable filenames in automated screenshot tests.
        /// Returns the saved file path, or null if the capture fails.
        /// </summary>
        public static string? CaptureWindowDeterministic(IntPtr hWnd, string name, string? outputDir = null)
        {
            try
            {
                string dir = outputDir ?? OutputDirectory;
                Directory.CreateDirectory(dir);

                SetForegroundWindow(hWnd);
                System.Threading.Thread.Sleep(200);

                if (!GetWindowRect(hWnd, out RECT r)) return null;
                int w = r.Right  - r.Left;
                int h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return null;

                Bitmap? bmp = TryPrintWindow(hWnd, w, h, PW_RENDERFULLCONTENT)
                           ?? TryPrintWindow(hWnd, w, h, 0)
                           ?? TryBitBlt(r, w, h);

                if (bmp == null) return null;

                string safeName = SanitizeFileName(name);
                string path = Path.Combine(dir, $"{safeName}.png");

                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Capture a screenshot of <paramref name="hWnd"/> and save it as PNG.
        /// Tries multiple capture strategies: PrintWindow with PW_RENDERFULLCONTENT,
        /// PrintWindow with flag=0, and BitBlt from screen DC as fallback.
        /// Returns the saved file path, or null if the capture fails.
        /// </summary>
        public static string? CaptureWindow(IntPtr hWnd, string name)
        {
            try
            {
                Directory.CreateDirectory(OutputDirectory);

                SetForegroundWindow(hWnd);
                System.Threading.Thread.Sleep(200); // let paint settle

                if (!GetWindowRect(hWnd, out RECT r)) return null;
                int w = r.Right  - r.Left;
                int h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return null;

                Bitmap? bmp = TryPrintWindow(hWnd, w, h, PW_RENDERFULLCONTENT)
                           ?? TryPrintWindow(hWnd, w, h, 0)
                           ?? TryBitBlt(r, w, h);

                if (bmp == null) return null;

                string safeName = string.Join("_",
                    name.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(OutputDirectory,
                    $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");

                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap? TryPrintWindow(IntPtr hWnd, int w, int h, uint flags)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hWnd, hdc, flags);
                g.ReleaseHdc(hdc);
            }
            if (HasContent(bmp)) return bmp;
            bmp.Dispose();
            return null;
        }

        private static Bitmap? TryBitBlt(RECT r, int w, int h)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;
            try
            {
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    BitBlt(hdc, 0, 0, w, h, screenDc, r.Left, r.Top, SRCCOPY);
                    g.ReleaseHdc(hdc);
                }
                if (HasContent(bmp)) return bmp;
                bmp.Dispose();
                return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>
        /// Quick check: sample a few pixels to see if the bitmap has real content
        /// (not all-black or all-same-color).
        /// </summary>
        private static bool HasContent(Bitmap bmp)
        {
            if (bmp.Width < 2 || bmp.Height < 2) return false;
            var firstPixel = bmp.GetPixel(0, 0);
            // Sample corners and center
            var samples = new[]
            {
                bmp.GetPixel(bmp.Width / 2, bmp.Height / 2),
                bmp.GetPixel(bmp.Width - 1, 0),
                bmp.GetPixel(0, bmp.Height - 1),
                bmp.GetPixel(bmp.Width - 1, bmp.Height - 1),
            };
            foreach (var s in samples)
            {
                if (s != firstPixel) return true;
            }
            return false;
        }
    }
}
