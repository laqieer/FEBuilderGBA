using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Finds and launches the FEBuilderGBA.exe for black-box E2E tests.
    /// </summary>
    public static class AppRunner
    {
        /// <summary>
        /// Returns the path to FEBuilderGBA.exe, checking (in order):
        ///   1. FEBUILDERGBA_EXE environment variable (CI/CD injection)
        ///   2. Release build relative to the solution root
        ///   3. Debug build relative to the solution root
        /// Throws InvalidOperationException if the exe cannot be found.
        /// </summary>
        public static string FindExePath()
        {
            // 1. Explicit env var override (set by CI workflow)
            string? envPath = Environment.GetEnvironmentVariable("FEBUILDERGBA_EXE");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                return envPath;

            // 2. Walk up from the test assembly to find the solution root, then locate the exe
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);

            // Traverse up looking for FEBuilderGBA.sln
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    // Try Release x86 first, then Debug
                    string[] candidates =
                    {
                        // msbuild via .sln (CI) outputs to bin/Release/ (Release|Any CPU)
                        Path.Combine(dir, "FEBuilderGBA", "bin", "Release",        "FEBuilderGBA.exe"),
                        // msbuild/dotnet build with explicit -p:Platform=x86 → bin/x86/Release/
                        Path.Combine(dir, "FEBuilderGBA", "bin", "x86", "Release", "FEBuilderGBA.exe"),
                        Path.Combine(dir, "FEBuilderGBA", "bin", "Debug",          "FEBuilderGBA.exe"),
                        Path.Combine(dir, "FEBuilderGBA", "bin", "x86", "Debug",   "FEBuilderGBA.exe"),
                    };
                    // Pick the most recently built existing candidate so the latest fix is used
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
                    if (newest != null) return newest;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException(
                "FEBuilderGBA.exe not found. " +
                "Set FEBUILDERGBA_EXE env var or build the project first.");
        }

        /// <summary>
        /// Run the exe with the given arguments, capturing stdout + stderr.
        /// Returns (exitCode, stdout, stderr).
        /// The process is killed after <paramref name="timeoutMs"/> ms.
        /// </summary>
        public static (int ExitCode, string Stdout, string Stderr) Run(
            string exePath, string args, int timeoutMs = 15_000)
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
            }
            else
            {
                // After WaitForExit(int) returns, async output handlers may not have
                // finished reading buffered data yet.  Call the no-param overload to
                // flush all pending OutputDataReceived / ErrorDataReceived events.
                // See: https://learn.microsoft.com/dotnet/api/system.diagnostics.process.waitforexit
                p.WaitForExit();
            }

            return (p.ExitCode, sb_out.ToString(), sb_err.ToString());
        }

        /// <summary>
        /// Returns the path to FEBuilderGBA.CLI executable, checking (in order):
        ///   1. FEBUILDERGBA_CLI_EXE environment variable
        ///   2. Release build relative to the solution root
        ///   3. Debug build relative to the solution root
        /// Throws InvalidOperationException if the exe cannot be found.
        /// </summary>
        public static string FindCliExePath()
        {
            string? envPath = Environment.GetEnvironmentVariable("FEBUILDERGBA_CLI_EXE");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                return envPath;

            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);

            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string[] candidates =
                    {
                        Path.Combine(dir, "FEBuilderGBA.CLI", "bin", "Release", "net9.0", "FEBuilderGBA.CLI.exe"),
                        Path.Combine(dir, "FEBuilderGBA.CLI", "bin", "Debug",   "net9.0", "FEBuilderGBA.CLI.exe"),
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
                    if (newest != null) return newest;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException(
                "FEBuilderGBA.CLI.exe not found. " +
                "Set FEBUILDERGBA_CLI_EXE env var or build the CLI project first.");
        }

        /// <summary>
        /// Launch the exe and return the Process (still running).
        /// The caller is responsible for killing it.
        /// UseShellExecute=true so WinForms apps get a proper window station / desktop context.
        /// </summary>
        public static Process Launch(string exePath, string args = "")
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute  = true,   // WinForms requires shell execution context for windows
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            if (!string.IsNullOrEmpty(args))
                psi.Arguments = args;

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();
            return p;
        }
    }
}
