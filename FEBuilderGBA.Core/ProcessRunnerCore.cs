// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

        /// <summary>True when stdout or stderr exceeded the configured capture limit.</summary>
        public bool OutputLimitExceeded { get; init; }

        /// <summary>True when synchronous termination failed and a lifetime reaper took ownership.</summary>
        public bool TerminationFailed { get; init; }

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
            OutputLimitExceeded = false,
            TerminationFailed = false,
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
        internal const int TerminationReaperAttempts = 3;
        internal const int TerminationReaperBackoffMs = 1000;

        private static readonly object RetainedProcessLock = new object();
        private static readonly HashSet<Process> RetainedProcesses =
            new HashSet<Process>();

        private interface IProcessContainment : IDisposable
        {
            bool Terminate();
        }

        private sealed class NoopProcessContainment : IProcessContainment
        {
            internal static readonly NoopProcessContainment Instance =
                new NoopProcessContainment();

            public bool Terminate() => true;
            public void Dispose()
            {
            }
        }

        private sealed class WindowsJobContainment : IProcessContainment
        {
            private const uint JobObjectLimitKillOnJobClose = 0x00002000;
            private const int JobObjectExtendedLimitInformation = 9;
            private IntPtr _job;

            [StructLayout(LayoutKind.Sequential)]
            private struct JobObjectBasicLimitInformation
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IoCounters
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JobObjectExtendedLimitInfo
            {
                public JobObjectBasicLimitInformation BasicLimitInformation;
                public IoCounters IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateJobObject(
                IntPtr jobAttributes,
                string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetInformationJobObject(
                IntPtr job,
                int infoType,
                IntPtr info,
                uint infoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(
                IntPtr job,
                IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool TerminateJobObject(
                IntPtr job,
                uint exitCode);

            [DllImport("kernel32.dll")]
            private static extern bool CloseHandle(IntPtr handle);

            internal WindowsJobContainment(Process process)
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(
                        Marshal.GetLastWin32Error());
                try
                {
                    var info = new JobObjectExtendedLimitInfo();
                    info.BasicLimitInformation.LimitFlags =
                        JobObjectLimitKillOnJobClose;
                    int size = Marshal.SizeOf<JobObjectExtendedLimitInfo>();
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, buffer, false);
                        if (!SetInformationJobObject(
                                _job,
                                JobObjectExtendedLimitInformation,
                                buffer,
                                (uint)size))
                        {
                            throw new System.ComponentModel.Win32Exception(
                                Marshal.GetLastWin32Error());
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }

                    if (!AssignProcessToJobObject(
                            _job,
                            process.SafeHandle.DangerousGetHandle()))
                    {
                        if (TryWaitForExit(process, 0))
                        {
                            Dispose();
                            return;
                        }
                        throw new System.ComponentModel.Win32Exception(
                            Marshal.GetLastWin32Error());
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public bool Terminate()
            {
                return _job == IntPtr.Zero
                    || TerminateJobObject(_job, 1);
            }

            public void Dispose()
            {
                if (_job == IntPtr.Zero)
                    return;
                CloseHandle(_job);
                _job = IntPtr.Zero;
            }
        }

        private sealed class PosixProcessGroupContainment : IProcessContainment
        {
            private const int SigKill = 9;
            private readonly Process _process;
            private readonly int _processGroup;
            private bool _active;

            [DllImport("libc", SetLastError = true)]
            private static extern int kill(int pid, int signal);

            [DllImport("libc", SetLastError = true)]
            private static extern int getpgid(int pid);

            internal PosixProcessGroupContainment(Process process)
            {
                _process = process;
                _processGroup = process.Id;
                TryActivate(maximumAttempts: 1000);
            }

            private bool TryActivate(int maximumAttempts)
            {
                if (_active)
                    return true;
                for (int attempt = 0; attempt < maximumAttempts; attempt++)
                {
                    if (getpgid(_processGroup) == _processGroup)
                    {
                        _active = true;
                        return true;
                    }
                    if (TryWaitForExit(_process, 0))
                        return false;
                    Thread.Sleep(1);
                }
                return false;
            }

            public bool Terminate()
            {
                if (!TryActivate(maximumAttempts: 1000))
                    return TryWaitForExit(_process, 0);
                if (kill(-_processGroup, SigKill) == 0)
                    return true;
                int error = Marshal.GetLastWin32Error();
                return error == 3;
            }

            public void Dispose()
            {
                Terminate();
                _active = false;
            }
        }

        private static IProcessContainment CreateContainment(
            Process process,
            bool requirePosixProcessGroup)
        {
            if (OperatingSystem.IsWindows())
                return new WindowsJobContainment(process);
            if (requirePosixProcessGroup
                && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            {
                return new PosixProcessGroupContainment(process);
            }
            return NoopProcessContainment.Instance;
        }

        private sealed class BoundedTextCapture
        {
            private readonly int _maximumChars;
            private readonly StringBuilder _builder = new StringBuilder();

            internal BoundedTextCapture(int maximumChars)
            {
                _maximumChars = maximumChars;
            }

            internal bool Append(char[] buffer, int count)
            {
                if (_maximumChars <= 0)
                {
                    _builder.Append(buffer, 0, count);
                    return false;
                }

                int remaining = Math.Max(0, _maximumChars - _builder.Length);
                if (remaining > 0)
                    _builder.Append(buffer, 0, Math.Min(remaining, count));
                return count > remaining;
            }

            public override string ToString()
            {
                return _builder.ToString();
            }
        }

        private static Exception DrainStream(
            TextReader reader,
            BoundedTextCapture capture,
            Action outputLimitReached)
        {
            try
            {
                var buffer = new char[4096];
                while (true)
                {
                    int count = reader.Read(buffer, 0, buffer.Length);
                    if (count == 0)
                        return null;
                    if (capture.Append(buffer, count))
                        outputLimitReached();
                }
            }
            catch (IOException ex)
            {
                return ex;
            }
            catch (ObjectDisposedException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static bool TryWaitForExit(Process process, int milliseconds)
        {
            try
            {
                return process.HasExited || process.WaitForExit(milliseconds);
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static bool TryTerminateProcess(
            Process process,
            IProcessContainment containment)
        {
            bool treeTerminated = containment?.Terminate() ?? true;
            if (TryWaitForExit(process, 0))
                return treeTerminated;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            if (TryWaitForExit(process, 5000))
                return treeTerminated;

            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            return TryWaitForExit(process, 5000) && treeTerminated;
        }

        private static void RetainProcessForTermination(
            Process process,
            IProcessContainment containment)
        {
            lock (RetainedProcessLock)
            {
                if (!RetainedProcesses.Add(process))
                    return;
            }

            var reaper = new Thread(() =>
            {
                try
                {
                    RetryTermination(
                        () => TryTerminateProcess(process, containment),
                        TerminationReaperAttempts,
                        TerminationReaperBackoffMs);
                }
                finally
                {
                    lock (RetainedProcessLock)
                    {
                        RetainedProcesses.Remove(process);
                    }
                    containment?.Dispose();
                    process.Dispose();
                }
            })
            {
                IsBackground = false,
                Name = "FEBuilderGBA process reaper",
            };
            reaper.Start();
        }

        internal static bool RetryTermination(
            Func<bool> tryTerminate,
            int maximumAttempts,
            int backoffMs)
        {
            if (tryTerminate == null || maximumAttempts <= 0)
                return false;
            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                if (tryTerminate())
                    return true;
                if (attempt + 1 < maximumAttempts && backoffMs > 0)
                    Thread.Sleep(backoffMs);
            }
            return false;
        }

        /// <summary>
        /// Run an external process, capturing stdout + stderr without a size limit.
        /// Never throws.
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
            return Run(command, args, workingDir, timeoutMs, 0);
        }

        /// <summary>
        /// Run an external process with a hard per-stream capture limit. The
        /// process tree is terminated as soon as stdout or stderr exceeds the
        /// limit. Never throws.
        /// </summary>
        /// <param name="maximumOutputChars">
        /// Maximum characters captured independently from stdout and stderr;
        /// ≤ 0 means unlimited.
        /// </param>
        public static ProcessRunResult Run(
            string command,
            IEnumerable<string> args,
            string workingDir,
            int timeoutMs,
            int maximumOutputChars)
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

                var proc = new Process { StartInfo = psi };
                IProcessContainment containment = null;
                bool processRetained = false;
                bool started = false;
                try
                {
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

                    try
                    {
                        bool requirePosixProcessGroup =
                            !OperatingSystem.IsWindows()
                            && psi.ArgumentList.Count > 0
                            && string.Equals(
                                Path.GetFileName(psi.ArgumentList[0]),
                                "process_worker.py",
                                StringComparison.Ordinal);
                        containment = CreateContainment(
                            proc,
                            requirePosixProcessGroup);
                    }
                    catch (Exception ex)
                    {
                        TryTerminateProcess(
                            proc,
                            NoopProcessContainment.Instance);
                        return new ProcessRunResult
                        {
                            Started = true,
                            TimedOut = false,
                            OutputLimitExceeded = false,
                            TerminationFailed = false,
                            ExitCode = -1,
                            Stdout = "",
                            Stderr = "",
                            ErrorMessage =
                                "Failed to establish process-tree containment: "
                                + ex.GetType().Name + ".",
                        };
                    }

                    var stdout = new BoundedTextCapture(maximumOutputChars);
                    var stderr = new BoundedTextCapture(maximumOutputChars);
                    int outputLimitSignal = 0;
                    Action outputLimitReached = () =>
                        Interlocked.Exchange(ref outputLimitSignal, 1);
                    Task<Exception> stdoutTask = Task.Factory.StartNew(
                        () => DrainStream(
                            proc.StandardOutput,
                            stdout,
                            outputLimitReached),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    Task<Exception> stderrTask = Task.Factory.StartNew(
                        () => DrainStream(
                            proc.StandardError,
                            stderr,
                            outputLimitReached),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);

                    bool timedOut = false;
                    bool outputLimitExceeded = false;
                    bool terminationFailed = false;
                    var stopwatch = Stopwatch.StartNew();
                    while (true)
                    {
                        if (Volatile.Read(ref outputLimitSignal) != 0)
                        {
                            outputLimitExceeded = true;
                            terminationFailed = !TryTerminateProcess(
                                proc,
                                containment);
                            break;
                        }

                        int remaining = timeout - (int)Math.Min(
                            int.MaxValue,
                            stopwatch.ElapsedMilliseconds);
                        if (remaining <= 0)
                        {
                            timedOut = true;
                            terminationFailed = !TryTerminateProcess(
                                proc,
                                containment);
                            break;
                        }

                        if (proc.WaitForExit(Math.Min(50, remaining)))
                            break;
                    }

                    if (terminationFailed)
                    {
                        RetainProcessForTermination(proc, containment);
                        containment = null;
                        processRetained = true;
                    }
                    else
                    {
                        proc.WaitForExit();
                        containment?.Terminate();
                    }

                    bool streamsDrained = Task.WaitAll(
                        new[] { stdoutTask, stderrTask },
                        5000);
                    if (!streamsDrained && !processRetained)
                    {
                        TryTerminateProcess(proc, containment);
                        streamsDrained = Task.WaitAll(
                            new[] { stdoutTask, stderrTask },
                            5000);
                    }
                    Exception stdoutError = streamsDrained
                        ? stdoutTask.Result
                        : null;
                    Exception stderrError = streamsDrained
                        ? stderrTask.Result
                        : null;
                    outputLimitExceeded |= Volatile.Read(ref outputLimitSignal) != 0;
                    bool streamReadFailed = stdoutError != null
                        || stderrError != null;
                    bool captureFailed = !streamsDrained
                        || (!timedOut
                            && !outputLimitExceeded
                            && !terminationFailed
                            && streamReadFailed);
                    string capturedStdout = streamsDrained ? stdout.ToString() : "";
                    string capturedStderr = streamsDrained ? stderr.ToString() : "";

                    string errorMessage = "";
                    if (outputLimitExceeded)
                    {
                        errorMessage =
                            $"Process output exceeded the {maximumOutputChars} character limit.";
                    }
                    else if (timedOut)
                    {
                        errorMessage = $"Process timed out after {timeout} ms.";
                    }
                    else if (captureFailed)
                    {
                        Exception captureError = stdoutError ?? stderrError;
                        errorMessage = captureError == null
                            ? "Process output capture did not finish."
                            : $"Process output capture failed: {captureError.GetType().Name}.";
                    }
                    if (terminationFailed)
                    {
                        errorMessage +=
                            " Process termination did not complete; "
                            + "a bounded foreground lifetime reaper took control.";
                    }
                    return new ProcessRunResult
                    {
                        Started = true,
                        TimedOut = timedOut,
                        OutputLimitExceeded = outputLimitExceeded,
                        TerminationFailed = terminationFailed,
                        ExitCode = timedOut || outputLimitExceeded
                            || terminationFailed || captureFailed
                            ? -1
                            : proc.ExitCode,
                        Stdout = capturedStdout,
                        Stderr = capturedStderr,
                        ErrorMessage = errorMessage,
                    };
                }
                finally
                {
                    if (!processRetained
                        && started
                        && !TryTerminateProcess(proc, containment))
                    {
                        RetainProcessForTermination(proc, containment);
                        containment = null;
                        processRetained = true;
                    }
                    if (!processRetained)
                    {
                        containment?.Dispose();
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                return ProcessRunResult.NotStarted($"Unexpected error running '{command}': {ex.Message}");
            }
        }
    }
}
