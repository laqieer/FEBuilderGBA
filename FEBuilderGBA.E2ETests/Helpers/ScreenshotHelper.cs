using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Captures screenshots of a specific window and saves them as PNG artifacts.
    /// </summary>
    public static class ScreenshotHelper
    {
        private const int MaxCaptureAttempts = 3;
        private const int CaptureRetryDelayMs = 100;
        private const int ErrorInvalidWindowHandle = 1400;

        internal static WindowCaptureException CreateBoundsFailure(IntPtr window, int errorCode)
        {
            string message = $"GetWindowRect failed for HWND=0x{window:X} (Win32 error {errorCode}).";
            return errorCode == ErrorInvalidWindowHandle
                ? new WindowCaptureException(message)
                : new WindowCaptureException(message, new Win32Exception(errorCode));
        }

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
            IWindowCaptureNative native, Action<int>? wait = null)
        {
            wait ??= Thread.Sleep;
            int attempt = 1;
            int? processId = null;
            int width = 0, height = 0;
            bool measured = false;

            try
            {
                string suffix = timestamp ? $"_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}" : "";
                string path = Path.Combine(outputDir, $"{SanitizeFileName(name)}{suffix}.png");
                string transientFailure = "";
                for (; attempt <= MaxCaptureAttempts; attempt++)
                {
                    processId = RequireCaptureAdmission(process, hWnd, probe, native).ProcessId;
                    (width, height) = native.GetWindowSize(hWnd);
                    measured = true;
                    if (width <= 0 || height <= 0)
                    {
                        transientFailure = "Window has nonpositive capture dimensions.";
                    }
                    else
                    {
                        foreach (uint flags in new uint[] { 2 /* PW_RENDERFULLCONTENT */, 0 })
                        {
                            RequireCaptureAdmission(process, hWnd, probe, native);
                            using IWindowCaptureSurface surface = native.CreateSurface(width, height);
                            IntPtr hdc = surface.GetHdc();
                            if (hdc == IntPtr.Zero)
                                throw new WindowCaptureException("Capture surface did not supply an HDC.");
                            bool printed;
                            try
                            {
                                RequireCaptureAdmission(process, hWnd, probe, native);
                                printed = native.PrintWindow(hWnd, hdc, flags);
                            }
                            finally
                            {
                                surface.ReleaseHdc(hdc);
                            }
                            if (!printed)
                            {
                                transientFailure = $"PrintWindow(flags={flags}) returned false.";
                                continue;
                            }
                            if (!surface.HasContent())
                            {
                                transientFailure = $"PrintWindow(flags={flags}) returned uniform RGB content.";
                                continue;
                            }
                            RequireCaptureAdmission(process, hWnd, probe, native);
                            surface.Save(path);
                            return path;
                        }
                    }

                    if (attempt == MaxCaptureAttempts)
                        break;
                    wait(CaptureRetryDelayMs);
                }
                throw new WindowCaptureException($"{transientFailure} No screenshot saved.");
            }
            catch (WindowCaptureException ex)
            {
                throw new WindowCaptureException($"{TargetDescription()} {ex.Message}", ex.InnerException);
            }
            catch (Exception ex) when (ex is not DesktopUnavailableException &&
                ex is InvalidOperationException or ExternalException or IOException or
                    UnauthorizedAccessException or ArgumentException or OverflowException or OutOfMemoryException)
            {
                throw new WindowCaptureException(
                    $"{TargetDescription()} Capture operation failed: {ex.Message} No screenshot saved.", ex);
            }

            string TargetDescription() =>
                $"Capture '{name}', HWND=0x{hWnd:X}, PID={processId?.ToString() ?? "unknown"}, " +
                $"dimensions={(measured ? $"{width}x{height}" : "unknown")}, attempt {attempt}/{MaxCaptureAttempts}:";
        }

        private static CaptureProcessIdentity RequireCaptureAdmission(Process process, IntPtr hWnd,
            Func<DesktopReadinessResult> probe, IWindowCaptureNative native)
        {
            DesktopReadiness.RequireReady(probe);
            return RequireOwnedWindow(process, hWnd, native);
        }

        private static CaptureProcessIdentity RequireOwnedWindow(
            Process process, IntPtr hWnd, IWindowCaptureNative native)
        {
            if (process == null || hWnd == IntPtr.Zero)
                throw new WindowCaptureException("Capture requires a retained Process and a nonzero HWND.");
            CaptureProcessIdentity identity;
            uint windowProcessId;
            try
            {
                identity = native.GetProcessIdentity(process);
                if (!identity.IsAlive || identity.ProcessId <= 0 ||
                    identity.HandleProcessId != (uint)identity.ProcessId)
                    throw new WindowCaptureException("Retained process is exited or its identity cannot be verified.");
                windowProcessId = native.GetWindowProcessId(hWnd);
            }
            catch (Exception ex) when (ex is not WindowCaptureException &&
                ex is InvalidOperationException or ExternalException)
            {
                throw new WindowCaptureException("Cannot verify retained process/window ownership.", ex);
            }
            if (windowProcessId == 0 || windowProcessId != identity.HandleProcessId)
                throw new WindowCaptureException("HWND is stale or belongs to a different process.");
            return identity;
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
            public uint GetWindowProcessId(IntPtr window) =>
                GetWindowThreadProcessId(window, out uint processId) == 0 ? 0 : processId;
            public (int Width, int Height) GetWindowSize(IntPtr window)
            {
                if (!GetWindowRect(window, out RECT rect))
                    throw CreateBoundsFailure(window, Marshal.GetLastWin32Error());
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
        /// Requires RGB variation anywhere in the bitmap; alpha alone is not rendered content.
        /// </summary>
        internal static bool HasContent(Bitmap bmp)
        {
            ArgumentNullException.ThrowIfNull(bmp);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[checked(bmp.Width * 4)];
                Marshal.Copy(data.Scan0, row, 0, row.Length);
                byte blue = row[0], green = row[1], red = row[2];
                for (int y = 0; y < bmp.Height; y++)
                {
                    if (y > 0)
                        Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        if (row[x] != blue || row[x + 1] != green || row[x + 2] != red)
                            return true;
                    }
                }
                return false;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
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
        uint GetWindowProcessId(IntPtr window);
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
