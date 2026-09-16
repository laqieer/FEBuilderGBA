using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Captures screenshots of a specific window and saves them as PNG artifacts.
    /// </summary>
    public static class ScreenshotHelper
    {
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
        /// Capture an owned live process window with a deterministic filename.
        /// Rejected readiness, ownership or capture failure throws; no screen fallback is used.
        /// </summary>
        public static string CaptureWindowDeterministic(
            Process process, IntPtr hWnd, string name, string? outputDir = null) =>
            CaptureWindow(process, hWnd, name, outputDir ?? OutputDirectory, false,
                DesktopReadiness.Probe, new WindowsCaptureNative());

        /// <summary>
        /// Capture an owned live process window and save a timestamped PNG.
        /// The caller must retain the Process returned by AppRunner.Launch until capture ends.
        /// </summary>
        public static string CaptureWindow(Process process, IntPtr hWnd, string name) =>
            CaptureWindow(process, hWnd, name, OutputDirectory, true,
                DesktopReadiness.Probe, new WindowsCaptureNative());

        internal static string CaptureWindow(Process process, IntPtr hWnd, string name,
            string outputDir, bool timestamp, Func<DesktopReadinessResult> probe,
            IWindowCaptureNative native)
        {
            DesktopReadiness.RequireReady(probe);
            RequireOwnedWindow(process, hWnd, native);

            try
            {
                var (width, height) = native.GetWindowSize(hWnd);
                if (width <= 0 || height <= 0)
                    throw new WindowCaptureException("Window has invalid capture dimensions.");

                string suffix = timestamp ? $"_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}" : "";
                string path = Path.Combine(outputDir, $"{SanitizeFileName(name)}{suffix}.png");
                foreach (uint flags in new uint[] { 2 /* PW_RENDERFULLCONTENT */, 0 })
                {
                    using IWindowCaptureSurface surface = native.CreateSurface(width, height);
                    IntPtr hdc = surface.GetHdc();
                    if (hdc == IntPtr.Zero)
                        throw new WindowCaptureException("Capture surface did not supply an HDC.");
                    bool printed;
                    try
                    {
                        printed = native.PrintWindow(hWnd, hdc, flags);
                    }
                    finally
                    {
                        surface.ReleaseHdc(hdc);
                    }
                    if (!printed || !surface.HasContent())
                        continue;
                    surface.Save(path);
                    return path;
                }
                throw new WindowCaptureException("PrintWindow failed or returned an empty image; no screenshot saved.");
            }
            catch (Exception ex) when (ex is not WindowCaptureException)
            {
                throw new WindowCaptureException("Owned-window capture failed; screenshot is not valid evidence.", ex);
            }
        }

        private static void RequireOwnedWindow(Process process, IntPtr hWnd, IWindowCaptureNative native)
        {
            if (process == null || hWnd == IntPtr.Zero)
                throw new WindowCaptureException("Capture requires a retained Process and a nonzero HWND.");
            CaptureProcessIdentity identity;
            uint owner;
            try
            {
                identity = native.GetProcessIdentity(process);
                if (!identity.IsAlive || identity.ProcessId <= 0 ||
                    identity.HandleProcessId != (uint)identity.ProcessId)
                    throw new WindowCaptureException("Retained process is exited or its identity cannot be verified.");
                owner = native.GetWindowOwner(hWnd);
            }
            catch (Exception ex) when (ex is not WindowCaptureException)
            {
                throw new WindowCaptureException("Cannot verify retained process/window ownership.", ex);
            }
            if (owner == 0 || owner != identity.HandleProcessId)
                throw new WindowCaptureException("HWND is stale or belongs to a different process.");
        }

        private sealed class WindowsCaptureNative : IWindowCaptureNative
        {
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
            [DllImport("user32.dll", EntryPoint = "PrintWindow", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool NativePrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern uint GetProcessId(SafeProcessHandle process);
            [StructLayout(LayoutKind.Sequential)]
            private struct RECT { public int Left, Top, Right, Bottom; }

            public CaptureProcessIdentity GetProcessIdentity(Process process)
            {
                process.Refresh();
                if (process.HasExited)
                    return new(process.Id, false, 0);
                return new(process.Id, true, GetProcessId(process.SafeHandle));
            }
            public uint GetWindowOwner(IntPtr window) =>
                GetWindowThreadProcessId(window, out uint owner) == 0 ? 0 : owner;
            public (int Width, int Height) GetWindowSize(IntPtr window)
            {
                if (!GetWindowRect(window, out RECT rect))
                    throw new WindowCaptureException("GetWindowRect failed.");
                return (checked(rect.Right - rect.Left), checked(rect.Bottom - rect.Top));
            }
            public IWindowCaptureSurface CreateSurface(int width, int height) => new BitmapSurface(width, height);
            public bool PrintWindow(IntPtr window, IntPtr hdc, uint flags) => NativePrintWindow(window, hdc, flags);
        }

        private sealed class BitmapSurface : IWindowCaptureSurface
        {
            private readonly Bitmap bitmap;
            private readonly Graphics graphics;

            public BitmapSurface(int width, int height)
            {
                bitmap = new Bitmap(width, height);
                try
                {
                    graphics = Graphics.FromImage(bitmap);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
            public IntPtr GetHdc() => graphics.GetHdc();
            public void ReleaseHdc(IntPtr hdc) => graphics.ReleaseHdc(hdc);
            public bool HasContent() => ScreenshotHelper.HasContent(bitmap);
            public void Save(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path, ImageFormat.Png);
            }
            public void Dispose()
            {
                try { graphics.Dispose(); }
                finally { bitmap.Dispose(); }
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

    public sealed class WindowCaptureException : InvalidOperationException
    {
        public WindowCaptureException(string message, Exception? inner = null) : base(message, inner) { }
    }

    internal readonly record struct CaptureProcessIdentity(int ProcessId, bool IsAlive, uint HandleProcessId);

    internal interface IWindowCaptureNative
    {
        CaptureProcessIdentity GetProcessIdentity(Process process);
        uint GetWindowOwner(IntPtr window);
        (int Width, int Height) GetWindowSize(IntPtr window);
        IWindowCaptureSurface CreateSurface(int width, int height);
        bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);
    }

    internal interface IWindowCaptureSurface : IDisposable
    {
        IntPtr GetHdc();
        void ReleaseHdc(IntPtr hdc);
        bool HasContent();
        void Save(string path);
    }
}
