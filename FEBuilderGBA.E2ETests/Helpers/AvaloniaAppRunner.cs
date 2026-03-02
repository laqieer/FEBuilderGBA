using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Finds and launches the FEBuilderGBA.Avalonia executable for E2E tests.
    /// </summary>
    public static class AvaloniaAppRunner
    {
        /// <summary>
        /// Returns the path to FEBuilderGBA.Avalonia.exe, checking (in order):
        ///   1. AVALONIA_EXE environment variable
        ///   2. Build output relative to the solution root
        /// Returns null if not found.
        /// </summary>
        public static string? FindExePath()
        {
            // 1. Explicit env var override
            string? envPath = Environment.GetEnvironmentVariable("AVALONIA_EXE");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                return envPath;

            // 2. Walk up from the test assembly to find the solution root
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);

            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string[] candidates =
                    {
                        Path.Combine(dir, "FEBuilderGBA.Avalonia", "bin", "Release", "net9.0", "FEBuilderGBA.Avalonia.exe"),
                        Path.Combine(dir, "FEBuilderGBA.Avalonia", "bin", "Debug",   "net9.0", "FEBuilderGBA.Avalonia.exe"),
                        Path.Combine(dir, "FEBuilderGBA.Avalonia", "bin", "Release", "net9.0", "win-x64", "FEBuilderGBA.Avalonia.exe"),
                        Path.Combine(dir, "FEBuilderGBA.Avalonia", "bin", "Debug",   "net9.0", "win-x64", "FEBuilderGBA.Avalonia.exe"),
                    };

                    string? newest = null;
                    DateTime newestTime = DateTime.MinValue;
                    foreach (string c in candidates)
                    {
                        if (File.Exists(c))
                        {
                            DateTime t = File.GetLastWriteTimeUtc(c);
                            if (t > newestTime) { newestTime = t; newest = c; }
                        }
                    }
                    return newest;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Run the Avalonia exe with the given arguments, capturing stdout + stderr.
        /// Returns (exitCode, stdout, stderr).
        /// </summary>
        public static (int ExitCode, string Stdout, string Stderr) Run(
            string exePath, string args, int timeoutMs = 30_000)
        {
            var sb_out = new StringBuilder();
            var sb_err = new StringBuilder();

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = Path.GetDirectoryName(exePath)!,
            };

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) sb_out.AppendLine(e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb_err.AppendLine(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            bool finished = p.WaitForExit(timeoutMs);
            if (!finished)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                p.WaitForExit(2_000);
                return (-1, sb_out.ToString(), sb_err.ToString() + "\n[TIMEOUT]");
            }
            else
            {
                p.WaitForExit();
            }

            return (p.ExitCode, sb_out.ToString(), sb_err.ToString());
        }
    }
}
