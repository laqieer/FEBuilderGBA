using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Diagnostic tests — always pass, just log window info for debugging.
    /// </summary>
    public class DiagnosticTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private readonly ITestOutputHelper _output;
        private Process? _process;

        public DiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            if (_process != null)
            {
                try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
                try { _process.WaitForExit(3_000); } catch { }
                _process.Dispose();
                _process = null;
            }
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int cap);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int cap);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private string WinTitle(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, 512);
            return sb.ToString();
        }
        private string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            return sb.ToString();
        }

        [Fact]
        public void Diagnose_AppWindowsAfterLaunch()
        {
            _output.WriteLine($"Exe path: {ExePath}");
            _process = AppRunner.Launch(ExePath);
            _output.WriteLine($"CLR PID: {_process.Id}");

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(1_000);
                if (_process.HasExited)
                {
                    _output.WriteLine($"[{i}s] Process exited with code {_process.ExitCode}");
                    break;
                }

                // Refresh process to get MainWindowTitle
                _process.Refresh();
                _output.WriteLine($"[{i}s] MainWindowTitle='{_process.MainWindowTitle}' " +
                                  $"MainWindowHandle={_process.MainWindowHandle}");

                var wins = new List<(IntPtr, bool, uint, string, string)>();
                EnumWindows((h, _) =>
                {
                    bool vis = IsWindowVisible(h);
                    GetWindowThreadProcessId(h, out uint pid);
                    wins.Add((h, vis, pid, WinTitle(h), ClassName(h)));
                    return true;
                }, IntPtr.Zero);

                var ours = wins.FindAll(x => (int)x.Item3 == _process.Id);
                _output.WriteLine($"  Windows for PID: {ours.Count}");
                foreach (var w in ours)
                {
                    // Print title as hex to see actual character codes
                    string titleHex = BitConverter.ToString(
                        Encoding.Unicode.GetBytes(w.Item4)).Replace("-", " ");
                    _output.WriteLine($"  h={w.Item1} vis={w.Item2} " +
                        $"title='{w.Item4}' titleLen={w.Item4.Length} titleHex=[{titleHex}]");
                }
            }
        }
    }
}
