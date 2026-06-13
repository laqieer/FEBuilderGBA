// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace FEBuilderGBA
{
    /// <summary>Result of a <see cref="ProcessRunnerCore.Run"/> call.</summary>
    public readonly struct ProcessRunResult
    {
        /// <summary>True when the process started successfully.</summary>
        public bool Started { get; init; }

        /// <summary>True when the process was killed due to timeout.</summary>
        public bool TimedOut { get; init; }

        /// <summary>Process exit code (0 on success). Meaningful only when Started is true.</summary>
        public int ExitCode { get; init; }

        /// <summary>Captured standard output.</summary>
        public string Stdout { get; init; }

        /// <summary>Captured standard error.</summary>
        public string Stderr { get; init; }

        /// <summary>Human-readable error message when Started is false.</summary>
        public string ErrorMessage { get; init; }

        /// <summary>Create a NotStarted result with the given error message.</summary>
        public static ProcessRunResult NotStarted(string message) => new ProcessRunResult
        {
            Started = false,
            TimedOut = false,
            ExitCode = -1,
            Stdout = "",
            Stderr = "",
            ErrorMessage = message ?? ""
        };
    }

    /// <summary>
    /// Cross-platform process runner. Never throws — all faults are returned as
    /// <see cref="ProcessRunResult"/> values. Thread-safe: each call creates an
    /// independent <see cref="Process"/> instance.
    /// </summary>
    public static class ProcessRunnerCore
    {
        /// <summary>Default timeout when timeoutMs is zero or negative (10 minutes).</summary>
        public const int DefaultTimeoutMs = 600_000;

        /// <summary>
        /// Run an external process, capturing stdout + stderr. Never throws.
        /// </summary>
        /// <param name="command">Executable name or path. Empty/whitespace → NotStarted, no launch.</param>
        /// <param name="args">Arguments passed structurally (no shell quoting/injection).</param>
        /// <param name="workingDir">Working directory; null/empty → use current directory. A non-null/non-empty value that does NOT exist → NotStarted, no launch (security: never run elsewhere than promised).</param>
        /// <param name="timeoutMs">Timeout in milliseconds; ≤ 0 → DefaultTimeoutMs.</param>
        public static ProcessRunResult Run(
            string command,
            IEnumerable<string> args,
            string workingDir,
            int timeoutMs)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command))
                    return ProcessRunResult.NotStarted("Command is empty or whitespace.");

                int timeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;

                // Security: when a working directory is explicitly supplied but
                // does NOT exist, REFUSE to run rather than silently falling
                // back to the host process current directory. The opt-in
                // confirmation tells the user the command runs in the project
                // root, so running it elsewhere would violate that contract.
                if (!string.IsNullOrEmpty(workingDir)
                    && !System.IO.Directory.Exists(workingDir))
                {
                    return ProcessRunResult.NotStarted(
                        $"Working directory does not exist: {workingDir}");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                if (!string.IsNullOrEmpty(workingDir))
                {
                    psi.WorkingDirectory = workingDir;
                }

                if (args != null)
                {
                    foreach (string arg in args)
                        psi.ArgumentList.Add(arg ?? "");
                }

                var stdoutSb = new StringBuilder();
                var stderrSb = new StringBuilder();
                var stdoutLock = new object();
                var stderrLock = new object();

                using var proc = new Process { StartInfo = psi };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (stdoutLock)
                        {
                            stdoutSb.AppendLine(e.Data);
                        }
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (stderrLock)
                        {
                            stderrSb.AppendLine(e.Data);
                        }
                    }
                };

                bool started;
                try
                {
                    started = proc.Start();
                }
                catch (Exception ex)
                {
                    return ProcessRunResult.NotStarted($"Failed to start '{command}': {ex.Message}");
                }

                if (!started)
                    return ProcessRunResult.NotStarted($"Process.Start returned false for '{command}'.");

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                bool exited = proc.WaitForExit(timeout);
                bool timedOut = false;

                if (!exited)
                {
                    timedOut = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    // Wait for drain after kill
                    try { proc.WaitForExit(5000); } catch { }
                }
                else
                {
                    // Ensure async reads are fully drained
                    proc.WaitForExit();
                }

                return new ProcessRunResult
                {
                    Started = true,
                    TimedOut = timedOut,
                    ExitCode = timedOut ? -1 : proc.ExitCode,
                    Stdout = stdoutSb.ToString(),
                    Stderr = stderrSb.ToString(),
                    ErrorMessage = timedOut ? $"Process timed out after {timeout} ms." : "",
                };
            }
            catch (Exception ex)
            {
                return ProcessRunResult.NotStarted($"Unexpected error running '{command}': {ex.Message}");
            }
        }
    }
}
