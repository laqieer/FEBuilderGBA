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
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // PW_RENDERFULLCONTENT = 2: captures even DWM-composed content
        private const uint PW_RENDERFULLCONTENT = 2;

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

                using var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                    g.ReleaseHdc(hdc);
                }

                string safeName = SanitizeFileName(name);
                string path = Path.Combine(dir, $"{safeName}.png");

                bmp.Save(path, ImageFormat.Png);
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Capture a screenshot of <paramref name="hWnd"/> and save it as PNG.
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

                using var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                    g.ReleaseHdc(hdc);
                }

                string safeName = string.Join("_",
                    name.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(OutputDirectory,
                    $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");

                bmp.Save(path, ImageFormat.Png);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
