// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #2050: shared support for ProcessRunnerCore process-tree tests. Both the strict
    /// single-launch theory (<see cref="ProcessRunnerProcessTreeTests"/>) and the concurrent
    /// contention scenarios (<see cref="ProcessRunnerContentionTests"/>) spawn the exact same
    /// "leader process spawns a detached long-running descendant, atomically publishes both
    /// identities, then exits" fixture and need the exact same PID-safe readiness/cleanup
    /// plumbing. Centralizing it here means individual test methods only assert outcomes.
    ///
    /// Wire contract (see docs/ENGINEERING-NOTES.md #1993 / #2050):
    /// - The leader script writes an atomic `leader.pid` payload, then spawns a detached
    ///   long-running descendant (self-watchdog <see cref="ChildWatchdogSeconds"/> seconds, so a
    ///   containment bug can never hang a CI host forever), sleeps
    ///   <see cref="IdentitySettleSeconds"/> seconds to let the descendant's process identity
    ///   settle in the OS, then captures the readiness timestamp and atomically publishes
    ///   `child.ready` before exiting. Both payloads are `&lt;pid&gt;|&lt;unixMs&gt;`.
    /// - Windows (PowerShell 5.1): each payload is written with `[IO.File]::WriteAllText` to a
    ///   `&lt;file&gt;.tmp` sibling, then published via the two-argument `[IO.File]::Move`
    ///   (fails rather than silently overwriting a stale file). If `Move` throws, the helper
    ///   re-reads the destination and accepts a completed-then-threw publish when the final
    ///   payload is already complete; retries (bounded 3×50 ms) happen only while the temp
    ///   source still exists, to absorb transient sharing violations.
    /// - POSIX (`/usr/bin/python3`): the script's <c>ArgumentList[0]</c> basename is exactly
    ///   `process_worker.py` (required by <c>ProcessRunnerCore.Run</c>'s POSIX process-group
    ///   containment gate) and it calls <c>os.setsid()</c> before creating the descendant. Each
    ///   payload is written to a `.tmp` sibling then published via `os.replace` (atomic rename).
    /// </summary>
    internal static class ProcessRunnerScenarioSupport
    {
        /// <summary>ProcessRunnerCore.Run timeoutMs used by every scenario.</summary>
        internal const int ScenarioTimeoutMs = 30_000;

        /// <summary>ProcessRunnerCore.Run maximumOutputChars used by every scenario.</summary>
        internal const int OutputCapChars = 4096;

        /// <summary>Readiness-file poll cadence; never coarser than this.</summary>
        internal const int ReadinessPollMs = 25;

        /// <summary>
        /// The descendant's own safety-net lifetime: if containment never terminates it, it
        /// exits on its own instead of hanging a CI host indefinitely.
        /// </summary>
        internal const int ChildWatchdogSeconds = 330;

        /// <summary>
        /// How long the leader sleeps after capturing the descendant's PID/timestamp but
        /// before publishing `child.ready`, so the descendant's OS-level identity has settled.
        /// </summary>
        internal const int IdentitySettleSeconds = 2;

        /// <summary>Maximum total time spent polling for descendant death.</summary>
        internal const int DescendantDeathPollSeconds = 5;

        /// <summary>
        /// Clock-sanity ceiling for a descendant-death observation relative to the atomically
        /// recorded child-readiness timestamp. The much tighter
        /// <see cref="DescendantDeathPollSeconds"/> poll is what prevents the 330-second
        /// watchdog from satisfying the containment oracle; this wider bound rejects stale
        /// payloads or implausible wall-clock jumps.
        /// </summary>
        internal const int ReadinessAcceptanceWindowMs = 120_000;

        /// <summary>On-failure cleanup: how long to wait for the Run(...) task after cancelling.</summary>
        internal const int CancelGraceMs = 25_000;

        /// <summary>On-failure cleanup: how long to wait for a best-effort kill to take effect.</summary>
        internal const int FinalKillWaitMs = 5_000;

        /// <summary>
        /// Best-effort-kill start-time acceptance tolerance (Windows): PowerShell's own
        /// `[DateTimeOffset]::UtcNow` reading and the OS's actual process-creation timestamp can
        /// disagree by a small amount; this bounds how much earlier than the scenario's own
        /// start a live process's StartTime may be and still be considered "ours".
        /// </summary>
        internal const int WindowsStartTimeToleranceMs = 250;

        /// <summary>Same tolerance as <see cref="WindowsStartTimeToleranceMs"/>, but for POSIX.</summary>
        internal const int PosixStartTimeToleranceMs = 2_000;

        /// <summary>
        /// The exact ProcessRunnerCore.cs message emitted when the dedicated LongRunning
        /// output-capture tasks could not be joined within their bounded wait under real
        /// scheduler/OS process-thread pressure — a legitimate, documented outcome under
        /// concurrent contention, never under a single isolated launch.
        /// </summary>
        internal const string CaptureIncompleteErrorMessage =
            "Process output capture did not finish.";

        /// <summary>An atomically-published `&lt;pid&gt;|&lt;unixMs&gt;` identity record.</summary>
        internal readonly struct ProcessIdentity
        {
            public int Pid { get; }
            public long RecordedUnixMs { get; }

            public ProcessIdentity(int pid, long recordedUnixMs)
            {
                Pid = pid;
                RecordedUnixMs = recordedUnixMs;
            }
        }

        /// <summary>
        /// A synchronous <see cref="ProcessRunnerCore.Run"/> result plus the UTC captured
        /// immediately inside the dedicated LongRunning wrapper task once that synchronous call
        /// returned.
        /// </summary>
        internal readonly struct RunCompletion
        {
            public ProcessRunResult Result { get; }
            public DateTimeOffset CompletionUtc { get; }

            public RunCompletion(ProcessRunResult result, DateTimeOffset completionUtc)
            {
                Result = result;
                CompletionUtc = completionUtc;
            }
        }

        /// <summary>
        /// A unique, disposable scenario root directory. Callers must ensure the scenario's
        /// Run(...) task has actually finished (normally or via <see cref="BestEffortCleanup"/>)
        /// before disposing, or hand ownership to the retained-resource lease via
        /// <see cref="RetainLiveResources"/> — never delete the root while that task might still
        /// be reading or writing under it.
        /// </summary>
        internal sealed class ScenarioRoot : IDisposable
        {
            public string Path { get; }

            public ScenarioRoot(string prefix)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"{prefix}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                        Directory.Delete(Path, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort: a slow-to-exit descendant may still hold a handle briefly.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static readonly object RetainedScenarioLock = new object();
        private static readonly List<RetainedScenarioLease> RetainedScenarioLeases =
            new List<RetainedScenarioLease>();

        private sealed class RetainedScenarioLease
        {
            private readonly string _scenarioLabel;
            private readonly ScenarioRoot _root;
            private readonly CancellationTokenSource _cts;
            private readonly Task _runTask;
            private readonly Action<string> _diagnosticsLog;
            private int _released;

            internal RetainedScenarioLease(
                string scenarioLabel,
                ScenarioRoot root,
                CancellationTokenSource cts,
                Task runTask,
                Action<string> diagnosticsLog)
            {
                _scenarioLabel = scenarioLabel;
                _root = root;
                _cts = cts;
                _runTask = runTask;
                _diagnosticsLog = diagnosticsLog;
            }

            internal void Activate()
            {
                lock (RetainedScenarioLock)
                {
                    RetainedScenarioLeases.Add(this);
                }

                SafeDiagnosticsLog(
                    _diagnosticsLog,
                    $"Retaining live scenario resources for {_scenarioLabel}: "
                    + $"Run task is still live (status={_runTask.Status}, root='{_root.Path}').");

                _runTask.ContinueWith(
                    _ => ReleaseAfterQuiesced(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            private void ReleaseAfterQuiesced()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;

                SafeDiagnosticsLog(
                    _diagnosticsLog,
                    $"Releasing retained scenario resources for {_scenarioLabel}: "
                    + $"Run task quiesced (status={_runTask.Status}, root='{_root.Path}').");

                try
                {
                    _cts.Dispose();
                }
                catch (Exception ex)
                {
                    SafeDiagnosticsLog(
                        _diagnosticsLog,
                        $"Disposing retained CTS for {_scenarioLabel} failed: "
                        + $"{ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    _root.Dispose();
                }
                catch (Exception ex)
                {
                    SafeDiagnosticsLog(
                        _diagnosticsLog,
                        $"Disposing retained root for {_scenarioLabel} failed: "
                        + $"{ex.GetType().Name}: {ex.Message}");
                }

                lock (RetainedScenarioLock)
                {
                    RetainedScenarioLeases.Remove(this);
                }
            }
        }

        internal static void RetainLiveResources(
            string scenarioLabel,
            ScenarioRoot root,
            CancellationTokenSource cts,
            Task runTask,
            Action<string> diagnosticsLog)
        {
            new RetainedScenarioLease(
                scenarioLabel,
                root,
                cts,
                runTask,
                diagnosticsLog).Activate();
        }

        internal static Task<RunCompletion> StartRunWithCompletion(
            string command,
            string[] args,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                () =>
                {
                    ProcessRunResult result = ProcessRunnerCore.Run(
                        command,
                        args,
                        workingDirectory,
                        ScenarioTimeoutMs,
                        OutputCapChars,
                        cancellationToken);
                    return new RunCompletion(result, DateTimeOffset.UtcNow);
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Build the leader+descendant fixture under <paramref name="root"/> and return the
        /// (command, args) pair ready to hand to <c>ProcessRunnerCore.Run</c>.
        /// </summary>
        internal static (string Command, string[] Args) BuildDescendantScenarioScript(
            string root,
            string leaderFinalPath,
            string childReadyFinalPath)
        {
            if (OperatingSystem.IsWindows())
            {
                string scriptPath = Path.Combine(root, "process_worker.ps1");
                string script = string.Join("\n", new[]
                {
                    "$ErrorActionPreference = 'Stop'",
                    "function Test-IdentityPayload([string]$Path, [string]$ExpectedPayload) {",
                    "    try {",
                    "        return [IO.File]::Exists($Path) -and ([IO.File]::ReadAllText($Path) -ceq $ExpectedPayload)",
                    "    }",
                    "    catch {",
                    "        return $false",
                    "    }",
                    "}",
                    "function Write-AtomicIdentity([string]$FinalPath, [string]$Payload) {",
                    "    $TempPath = \"$FinalPath.tmp\"",
                    "    [IO.File]::WriteAllText($TempPath, $Payload)",
                    "    for ($i = 0; $i -lt 3; $i++) {",
                    "        try { [IO.File]::Move($TempPath, $FinalPath); return $true }",
                    "        catch {",
                    "            if (Test-IdentityPayload $FinalPath $Payload) { return $true }",
                    "            if (-not [IO.File]::Exists($TempPath)) { return $false }",
                    "            Start-Sleep -Milliseconds 50",
                    "        }",
                    "    }",
                    "    return (Test-IdentityPayload $FinalPath $Payload)",
                    "}",
                    "$leaderNow = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()",
                    "if (-not (Write-AtomicIdentity "
                        + PowerShellStringLiteral(leaderFinalPath)
                        + " \"$PID|$leaderNow\")) { exit 97 }",
                    "$child = Start-Process -FilePath 'powershell.exe' -ArgumentList @("
                        + "'-NoProfile','-Command','Start-Sleep -Seconds "
                        + ChildWatchdogSeconds.ToString(CultureInfo.InvariantCulture)
                        + "') -NoNewWindow -PassThru",
                    "Start-Sleep -Seconds "
                        + IdentitySettleSeconds.ToString(CultureInfo.InvariantCulture),
                    "$childNow = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()",
                    "if (-not (Write-AtomicIdentity "
                        + PowerShellStringLiteral(childReadyFinalPath)
                        + " \"$($child.Id)|$childNow\")) { exit 98 }",
                    "",
                });
                File.WriteAllText(scriptPath, script);
                return (
                    "powershell.exe",
                    new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath });
            }

            string pyScriptPath = Path.Combine(root, "process_worker.py");
            string pyScript = string.Join("\n", new[]
            {
                "import os,subprocess,sys,time",
                "def _atomic_write(path,payload):",
                "    tmp=path+'.tmp'",
                "    with open(tmp,'w') as fh:",
                "        fh.write(payload)",
                "    os.replace(tmp,path)",
                "os.setsid()",
                "leader_now=int(time.time()*1000)",
                "_atomic_write("
                    + PythonStringLiteral(leaderFinalPath)
                    + ",'%d|%d'%(os.getpid(),leader_now))",
                "child=subprocess.Popen([sys.executable,'-c',"
                    + "'import time;time.sleep("
                    + ChildWatchdogSeconds.ToString(CultureInfo.InvariantCulture)
                    + ")'])",
                "time.sleep(" + IdentitySettleSeconds.ToString(CultureInfo.InvariantCulture) + ")",
                "child_now=int(time.time()*1000)",
                "_atomic_write("
                    + PythonStringLiteral(childReadyFinalPath)
                    + ",'%d|%d'%(child.pid,child_now))",
                "",
            });
            File.WriteAllText(pyScriptPath, pyScript);
            return ("/usr/bin/python3", new[] { pyScriptPath });
        }

        private static string PowerShellStringLiteral(string value) =>
            "'" + value.Replace("'", "''") + "'";

        private static string PythonStringLiteral(string value) =>
            "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        /// <summary>Parse a `&lt;pid&gt;|&lt;unixMs&gt;` payload. Null on any malformed content.</summary>
        internal static ProcessIdentity? TryParseIdentity(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;
            string[] parts = payload.Split('|');
            if (parts.Length != 2)
                return null;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
                return null;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixMs))
                return null;
            if (pid <= 0)
                return null;
            return new ProcessIdentity(pid, unixMs);
        }

        /// <summary>
        /// Poll for a readiness file at ≤<see cref="ReadinessPollMs"/> ms intervals until it
        /// exists and parses as a complete identity payload, or <paramref name="maxWait"/>
        /// elapses.
        /// </summary>
        internal static ProcessIdentity? PollForIdentity(
            string path,
            TimeSpan maxWait,
            Task? runTask = null)
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                ProcessIdentity? identity = ReadFinalIdentity(path);
                if (identity != null)
                    return identity;
                if (runTask?.IsCompleted == true)
                    return ReadFinalIdentity(path);
                if (stopwatch.Elapsed >= maxWait)
                    return null;
                Thread.Sleep(ReadinessPollMs);
            }
        }

        /// <summary>Final authoritative re-read after the run completed; must be a complete payload.</summary>
        internal static ProcessIdentity? ReadFinalIdentity(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                return TryParseIdentity(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static bool IsExpectedProcessName(string name)
        {
            return name.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("python", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecordedProcessAlive(
            ProcessIdentity identity,
            DateTimeOffset scenarioStartUtc)
        {
            try
            {
                using Process process = Process.GetProcessById(identity.Pid);
                if (process.HasExited)
                    return false;
                if (!IsExpectedProcessName(process.ProcessName))
                    return false;

                int toleranceMs = OperatingSystem.IsWindows()
                    ? WindowsStartTimeToleranceMs
                    : PosixStartTimeToleranceMs;
                long windowStartMs =
                    scenarioStartUtc.ToUnixTimeMilliseconds() - toleranceMs;
                long windowEndMs = identity.RecordedUnixMs + toleranceMs;
                long startUnixMs =
                    new DateTimeOffset(process.StartTime.ToUniversalTime())
                        .ToUnixTimeMilliseconds();
                if (startUnixMs > windowEndMs)
                    return false; // PID was reused by a newer process.
                if (startUnixMs < windowStartMs)
                    return true; // Identity is implausibly old; fail closed.
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
        }

        internal static long ComputeCleanupMs(
            ProcessIdentity childIdentity,
            DateTimeOffset completionUtc)
        {
            return (long)(
                completionUtc
                - DateTimeOffset.FromUnixTimeMilliseconds(childIdentity.RecordedUnixMs))
                .TotalMilliseconds;
        }

        /// <summary>
        /// Poll (≤<see cref="DescendantDeathPollSeconds"/> s total) for the descendant identified
        /// by <paramref name="identity"/> to die, then accept it as containment evidence only if
        /// death was observed within <see cref="ReadinessAcceptanceWindowMs"/> of
        /// <paramref name="readinessRecordedAtUtc"/> (the atomically-recorded child-readiness
        /// timestamp) — see <see cref="ReadinessAcceptanceWindowMs"/> for why a later death is
        /// rejected rather than accepted.
        /// </summary>
        internal static bool DescendantDiedWithinAcceptedWindow(
            ProcessIdentity identity,
            DateTimeOffset readinessRecordedAtUtc,
            DateTimeOffset scenarioStartUtc,
            out TimeSpan observedAfterReadiness)
        {
            var pollStopwatch = Stopwatch.StartNew();
            bool alive = IsRecordedProcessAlive(identity, scenarioStartUtc);
            while (alive && pollStopwatch.Elapsed < TimeSpan.FromSeconds(DescendantDeathPollSeconds))
            {
                Thread.Sleep(10);
                alive = IsRecordedProcessAlive(identity, scenarioStartUtc);
            }
            observedAfterReadiness = DateTimeOffset.UtcNow - readinessRecordedAtUtc;
            return !alive
                && observedAfterReadiness < TimeSpan.FromMilliseconds(ReadinessAcceptanceWindowMs);
        }

        /// <summary>
        /// On-failure cleanup only: cancel the scenario's own <paramref name="cts"/>, wait
        /// <see cref="CancelGraceMs"/> ms for <paramref name="runTask"/>, best-effort-kill the
        /// recorded leader/child identities, then wait a final <see cref="FinalKillWaitMs"/> ms
        /// for <paramref name="runTask"/>. Kills are attempted ONLY when the PID still exists,
        /// its process name (OrdinalIgnoreCase) starts with "powershell" or "python", and its
        /// actual live <c>Process.StartTime</c> (UTC, as unix ms) falls inside
        /// [<paramref name="scenarioStartUtc"/> − tolerance, identity.RecordedUnixMs]. This
        /// bounded window is what prevents a PID-reuse false-kill of an unrelated process that
        /// happened to be assigned the same PID after ours already exited. Returns true only
        /// when the Run task is quiesced; callers must retain CTS/root/task if false. Never
        /// throws; cleanup-only exceptions are logged via <paramref name="diagnosticsLog"/>,
        /// never allowed to replace/mask the caller's primary failure.
        /// </summary>
        internal static bool BestEffortCleanup(
            CancellationTokenSource cts,
            Task? runTask,
            ProcessIdentity? leaderIdentity,
            ProcessIdentity? childIdentity,
            DateTimeOffset scenarioStartUtc,
            Action<string> diagnosticsLog)
        {
            bool runQuiesced = runTask == null || runTask.IsCompleted;
            try
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                bool quiescedAfterCancel = WaitForRunTaskQuiescence(
                    runTask,
                    CancelGraceMs,
                    "cancel-grace wait",
                    diagnosticsLog);

                TryBestEffortKill(leaderIdentity, scenarioStartUtc, diagnosticsLog);
                TryBestEffortKill(childIdentity, scenarioStartUtc, diagnosticsLog);

                bool quiescedAfterKill = WaitForRunTaskQuiescence(
                    runTask,
                    FinalKillWaitMs,
                    "post-kill wait",
                    diagnosticsLog);

                runQuiesced = quiescedAfterCancel || quiescedAfterKill;
                if (!runQuiesced)
                {
                    SafeDiagnosticsLog(
                        diagnosticsLog,
                        "Run task remained live after cancellation, PID-safe kills, and the "
                        + $"final {FinalKillWaitMs} ms wait; retaining CTS/root/task.");
                }
            }
            catch (Exception ex)
            {
                // Cleanup must never replace the primary test failure with one of its own.
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"BestEffortCleanup itself failed: {ex.GetType().Name}: {ex.Message}");
                runQuiesced = runQuiesced || (runTask == null || runTask.IsCompleted);
            }
            return runQuiesced;
        }

        private static bool WaitForRunTaskQuiescence(
            Task? runTask,
            int waitMs,
            string phase,
            Action<string> diagnosticsLog)
        {
            if (runTask == null)
                return true;

            try
            {
                return runTask.Wait(TimeSpan.FromMilliseconds(waitMs));
            }
            catch (AggregateException ex)
            {
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"Scenario Run task completed during {phase} with status {runTask.Status}: "
                    + ex.Flatten().Message);
                return true;
            }
        }

        private static void SafeDiagnosticsLog(Action<string> diagnosticsLog, string message)
        {
            try
            {
                diagnosticsLog(message);
            }
            catch
            {
                // Never let diagnostic logging mask the primary outcome.
            }
        }

        private static void TryBestEffortKill(
            ProcessIdentity? identity,
            DateTimeOffset scenarioStartUtc,
            Action<string> diagnosticsLog)
        {
            if (identity == null)
                return;
            ProcessIdentity id = identity.Value;
            int toleranceMs = OperatingSystem.IsWindows()
                ? WindowsStartTimeToleranceMs
                : PosixStartTimeToleranceMs;
            long windowStartMs = scenarioStartUtc.ToUnixTimeMilliseconds() - toleranceMs;
            long windowEndMs = id.RecordedUnixMs;
            try
            {
                using Process process = Process.GetProcessById(id.Pid);
                string name = process.ProcessName;
                bool nameMatches = IsExpectedProcessName(name);
                if (!nameMatches)
                {
                    SafeDiagnosticsLog(
                        diagnosticsLog,
                        $"Refusing best-effort kill of pid {id.Pid}: process name '{name}' does "
                        + "not match the expected powershell/python prefix (likely PID reuse).");
                    return;
                }
                long startUnixMs =
                    new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                if (startUnixMs < windowStartMs || startUnixMs > windowEndMs)
                {
                    SafeDiagnosticsLog(
                        diagnosticsLog,
                        $"Refusing best-effort kill of pid {id.Pid}: start time {startUnixMs} "
                        + $"outside accepted window [{windowStartMs},{windowEndMs}] (likely PID reuse).");
                    return;
                }
                process.Kill(entireProcessTree: true);
                process.WaitForExit(FinalKillWaitMs);
            }
            catch (ArgumentException ex)
            {
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"Best-effort kill of pid {id.Pid} failed closed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"Best-effort kill of pid {id.Pid} failed closed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"Best-effort kill of pid {id.Pid} failed closed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (PlatformNotSupportedException ex)
            {
                SafeDiagnosticsLog(
                    diagnosticsLog,
                    $"Best-effort kill of pid {id.Pid} failed closed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Emit one grep-able PROCESS_RUNNER_METRIC line per scenario iteration.</summary>
        internal static void EmitMetric(
            ITestOutputHelper output,
            string scenario,
            int iteration,
            long readinessMs,
            long? cleanupMs,
            long totalMs,
            string outcome)
        {
            string cleanupValue = cleanupMs?.ToString(CultureInfo.InvariantCulture)
                ?? "unknown";
            try
            {
                output.WriteLine(
                    "PROCESS_RUNNER_METRIC "
                    + $"scenario={scenario} iteration={iteration} "
                    + $"readinessMs={readinessMs} cleanupMs={cleanupValue} totalMs={totalMs} "
                    + $"outcome={outcome}");
            }
            catch (InvalidOperationException)
            {
                // A retained background task may quiesce after xUnit closes its output sink.
            }
        }
    }
}
