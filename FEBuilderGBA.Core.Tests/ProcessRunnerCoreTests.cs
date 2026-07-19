using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ProcessRunnerCoreTests
    {
        // Per-OS helper: get a shell + script prefix that executes a shell command string.
        // On Windows: cmd /c "<script>"; on Linux/macOS: /bin/sh -c "<script>"
        private static (string shell, string argPrefix) GetShell()
        {
            if (OperatingSystem.IsWindows())
                return ("cmd", "/c");
            return ("/bin/sh", "-c");
        }

        [Fact]
        public void Run_CapturesStdout()
        {
            var (shell, argPrefix) = GetShell();
            string script = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
            var result = ProcessRunnerCore.Run(shell, new[] { argPrefix, script }, null, 30_000);

            Assert.True(result.Started, $"Process did not start. ErrorMessage: {result.ErrorMessage}");
            Assert.False(result.TimedOut);
            Assert.True(
                result.ExitCode == 0,
                $"Expected exit 0, got {result.ExitCode}. ErrorMessage: {result.ErrorMessage}");
            Assert.Contains("hello", result.Stdout);
        }

        [Fact]
        public void Run_CapturesStderrOnly()
        {
            var (shell, argPrefix) = GetShell();
            // Write to stderr only
            string script = OperatingSystem.IsWindows()
                ? "echo erronly 1>&2"
                : "echo erronly >&2";
            var result = ProcessRunnerCore.Run(shell, new[] { argPrefix, script }, null, 30_000);

            Assert.True(result.Started, $"Process did not start. ErrorMessage: {result.ErrorMessage}");
            Assert.Contains("erronly", result.Stderr);
        }

        [Fact]
        public void Run_NonZeroExitCode()
        {
            var (shell, argPrefix) = GetShell();
            string script = OperatingSystem.IsWindows() ? "exit 42" : "exit 42";
            var result = ProcessRunnerCore.Run(shell, new[] { argPrefix, script }, null, 30_000);

            Assert.True(result.Started);
            Assert.NotEqual(0, result.ExitCode);
        }

        [Fact]
        public void Run_MissingExecutable_DoesNotThrow_ReturnsFalse()
        {
            var result = ProcessRunnerCore.Run(
                "this_executable_does_not_exist_febuildergba_1134_test",
                Array.Empty<string>(), null, 30_000);

            Assert.False(result.Started);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        [Fact]
        public void Run_EmptyCommand_DoesNotThrow_ReturnsFalse()
        {
            var result = ProcessRunnerCore.Run("", Array.Empty<string>(), null, 30_000);
            Assert.False(result.Started);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        [Fact]
        public void Run_WhitespaceCommand_DoesNotThrow_ReturnsFalse()
        {
            var result = ProcessRunnerCore.Run("   ", Array.Empty<string>(), null, 30_000);
            Assert.False(result.Started);
        }

        [Fact]
        public void Run_Timeout_ReturnsTimedOut()
        {
            var (shell, argPrefix) = GetShell();
            // Sleep for 10 seconds but timeout after 1 second
            string script = OperatingSystem.IsWindows() ? "ping 127.0.0.1 -n 10 >nul" : "sleep 10";
            var result = ProcessRunnerCore.Run(shell, new[] { argPrefix, script }, null, 1_000);

            Assert.True(result.Started, $"Process did not start. ErrorMessage: {result.ErrorMessage}");
            Assert.True(result.TimedOut, "Expected TimedOut=true");
            Assert.False(result.TerminationFailed);
        }

        [Fact]
        public void Run_MissingWorkingDir_ReturnsNotStarted_DoesNotRun()
        {
            // Security: when a non-empty working dir is supplied but does not
            // exist, the command must NOT run (no silent fallback to the host
            // process current directory). #1134 review finding 3.
            var (shell, argPrefix) = GetShell();
            string script = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
            string missingDir = Path.Combine(
                Path.GetTempPath(),
                $"febuildergba_missing_wd_{Guid.NewGuid():N}");
            Assert.False(Directory.Exists(missingDir));

            var result = ProcessRunnerCore.Run(
                shell, new[] { argPrefix, script }, missingDir, 30_000);

            // Process did NOT start...
            Assert.False(result.Started);
            Assert.False(result.TimedOut);
            // ...exit code is the not-started sentinel, and no stdout was produced.
            Assert.Equal(-1, result.ExitCode);
            Assert.Equal("", result.Stdout);
            Assert.DoesNotContain("hello", result.Stdout);
            Assert.Contains(missingDir, result.ErrorMessage);
        }

        [Fact]
        public void Run_LargeOutput_NoDeadlock()
        {
            var (shell, argPrefix) = GetShell();
            // Generate > 64KB output
            string script = OperatingSystem.IsWindows()
                ? "for /L %i in (1,1,2000) do @echo XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
                : "for i in $(seq 1 2000); do echo XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX; done";
            var result = ProcessRunnerCore.Run(shell, new[] { argPrefix, script }, null, 30_000);

            Assert.True(result.Started);
            Assert.True(result.Stdout.Length > 64 * 1024,
                $"Expected >64KB output, got {result.Stdout.Length} bytes");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Run_BoundedOutput_KillsAndCapsEitherStream(bool useStderr)
        {
            var (shell, argPrefix) = GetShell();
            string redirect = useStderr ? " 1>&2" : "";
            string script = OperatingSystem.IsWindows()
                ? "for /L %i in (1,1,2000) do @echo XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
                    + redirect
                : "for i in $(seq 1 2000); do echo XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
                    + redirect + "; done";

            var result = ProcessRunnerCore.Run(
                shell,
                new[] { argPrefix, script },
                null,
                30_000,
                1024);

            Assert.True(result.Started);
            Assert.False(result.TimedOut);
            Assert.True(result.OutputLimitExceeded);
            Assert.False(result.TerminationFailed);
            Assert.Equal(-1, result.ExitCode);
            Assert.True(result.Stdout.Length <= 1024);
            Assert.True(result.Stderr.Length <= 1024);
            Assert.Contains("output exceeded", result.ErrorMessage);
        }

        [Fact]
        public void RetryTermination_IsBoundedAndReturnsEarly()
        {
            int failedAttempts = 0;
            bool failed = ProcessRunnerCore.RetryTermination(
                () =>
                {
                    failedAttempts++;
                    return false;
                },
                maximumAttempts: 3,
                backoffMs: 0);
            Assert.False(failed);
            Assert.Equal(3, failedAttempts);

            int successAttempts = 0;
            bool succeeded = ProcessRunnerCore.RetryTermination(
                () => ++successAttempts == 2,
                maximumAttempts: 3,
                backoffMs: 0);
            Assert.True(succeeded);
            Assert.Equal(2, successAttempts);
        }

        [Fact]
        public void PosixUnobservedTermination_Success_IsIdempotentAndUsesOnlySigKill()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;
            int observedPid = 0;
            int observedSignal = 0;

            bool first = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    calls++;
                    observedPid = pid;
                    observedSignal = signal;
                    return 0;
                },
                () => 0,
                ref state);
            bool second = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    calls++;
                    return 0;
                },
                () => 0,
                ref state);

            Assert.True(first);
            Assert.True(second);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Succeeded,
                state);
            Assert.Equal(1, calls);
            Assert.Equal(-123, observedPid);
            Assert.Equal(ProcessRunnerCore.PosixSigKill, observedSignal);
        }

        [Fact]
        public void PosixUnobservedTermination_Esrch_IsAlreadyTerminated()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;

            bool terminated =
                ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                    123,
                    (pid, signal) =>
                    {
                        calls++;
                        return -1;
                    },
                    () => ProcessRunnerCore.PosixEsrch,
                    ref state);

            Assert.True(terminated);
            Assert.Equal(1, calls);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Succeeded,
                state);
        }

        [Fact]
        public void PosixUnobservedTermination_EintrRetriesWithinBound()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;

            bool terminated =
                ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                    123,
                    (pid, signal) =>
                    {
                        calls++;
                        return calls == 3 ? 0 : -1;
                    },
                    () => ProcessRunnerCore.PosixEintr,
                    ref state);

            Assert.True(terminated);
            Assert.Equal(3, calls);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Succeeded,
                state);
        }

        [Theory]
        [InlineData(ProcessRunnerCore.PosixEperm)]
        [InlineData(5)]
        public void PosixUnobservedTermination_NonRetryableErrorFailsClosed(
            int error)
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;

            bool first = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    calls++;
                    return -1;
                },
                () => error,
                ref state);
            bool second = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    calls++;
                    return 0;
                },
                () => 0,
                ref state);

            Assert.False(first);
            Assert.False(second);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Failed,
                state);
            Assert.Equal(1, calls);
            Assert.True(
                ProcessRunnerCore.MergeTerminationFailure(
                    terminationFailed: false,
                    containmentTerminated: first));
        }

        [Fact]
        public void PosixUnobservedTermination_EintrExhaustionFailsClosed()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;

            bool terminated =
                ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                    123,
                    (pid, signal) =>
                    {
                        calls++;
                        return -1;
                    },
                    () => ProcessRunnerCore.PosixEintr,
                    ref state,
                    maximumAttempts: 3);

            Assert.False(terminated);
            Assert.Equal(3, calls);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Failed,
                state);
        }

        [Fact]
        public void PosixUnobservedTermination_NativeExceptionConsumesOneShot()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;
            int calls = 0;

            Assert.Throws<InvalidOperationException>(() =>
                ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                    123,
                    (pid, signal) =>
                    {
                        calls++;
                        throw new InvalidOperationException("native failure");
                    },
                    () => 0,
                    ref state));

            bool second = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    calls++;
                    return 0;
                },
                () => 0,
                ref state);

            Assert.False(second);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Failed,
                state);
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        public void MergeTerminationFailure_PreservesAnyFailure(
            bool existingFailure,
            bool containmentTerminated,
            bool expected)
        {
            Assert.Equal(
                expected,
                ProcessRunnerCore.MergeTerminationFailure(
                    existingFailure,
                    containmentTerminated));
        }

        [Fact]
        public void PrepareUnobservedPosixFallback_LiveLeaderConsumesOneShot()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;

            bool prepared = ProcessRunnerCore.PrepareUnobservedPosixFallback(
                leaderExited: false,
                ref state);
            int signalCalls = 0;
            bool delayed = ProcessRunnerCore.TryTerminateUnobservedPosixGroup(
                123,
                (pid, signal) =>
                {
                    signalCalls++;
                    return 0;
                },
                () => 0,
                ref state);

            Assert.False(prepared);
            Assert.False(delayed);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.Failed,
                state);
            Assert.Equal(0, signalCalls);
        }

        [Fact]
        public void PrepareUnobservedPosixFallback_ExitedLeaderKeepsOneShotAvailable()
        {
            var state =
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted;

            bool prepared = ProcessRunnerCore.PrepareUnobservedPosixFallback(
                leaderExited: true,
                ref state);

            Assert.True(prepared);
            Assert.Equal(
                ProcessRunnerCore.PosixUnobservedTerminationState.NotAttempted,
                state);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Run_ParentExitStillTerminatesDescendantHoldingPipes(
            int iteration)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"febuildergba_process_tree_{iteration}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            string pidPath = Path.Combine(root, "child.pid");
            string scriptPath;
            string command;
            string[] args;
            if (OperatingSystem.IsWindows())
            {
                scriptPath = Path.Combine(root, "spawn-child.ps1");
                string quotedPid = pidPath.Replace("'", "''");
                File.WriteAllText(
                    scriptPath,
                    "$child = Start-Process -FilePath powershell.exe "
                    + "-ArgumentList '-NoProfile','-Command',"
                    + "'Start-Sleep -Seconds 30' -NoNewWindow -PassThru\n"
                    + $"[IO.File]::WriteAllText('{quotedPid}', [string]$child.Id)\n");
                command = "powershell.exe";
                args = new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                };
            }
            else
            {
                command = "/usr/bin/python3";
                if (!File.Exists(command))
                {
                    Directory.Delete(root, recursive: true);
                    return;
                }
                string pythonPidPath = "'"
                    + pidPath.Replace("\\", "\\\\").Replace("'", "\\'")
                    + "'";
                scriptPath = Path.Combine(root, "process_worker.py");
                File.WriteAllText(
                    scriptPath,
                    "import os,pathlib,subprocess,sys\n"
                    + "os.setsid()\n"
                    + "p=subprocess.Popen([sys.executable,'-c',"
                    + "'import time;time.sleep(30)'])\n"
                    + $"pathlib.Path({pythonPidPath}).write_text(str(p.pid))\n");
                args = new[] { scriptPath };
            }

            var stopwatch = Stopwatch.StartNew();
            ProcessRunResult result = ProcessRunnerCore.Run(
                command,
                args,
                root,
                30_000,
                1024);
            stopwatch.Stop();

            Assert.True(result.Started, result.ErrorMessage);
            Assert.False(result.TimedOut, result.ErrorMessage);
            Assert.False(result.OutputLimitExceeded, result.ErrorMessage);
            Assert.False(result.TerminationFailed, result.ErrorMessage);
            Assert.True(
                result.ExitCode == 0,
                $"Expected exit 0, got {result.ExitCode}. "
                + $"ErrorMessage: {result.ErrorMessage}");
            Assert.Equal("", result.ErrorMessage);
            Assert.True(File.Exists(pidPath), "Parent did not record child PID.");
            int childPid = int.Parse(File.ReadAllText(pidPath));
            bool alive = true;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    using Process child = Process.GetProcessById(childPid);
                    alive = !child.HasExited;
                }
                catch (ArgumentException)
                {
                    alive = false;
                }
                if (!alive)
                    break;
                Thread.Sleep(10);
            }
            Assert.False(alive, "Descendant outlived ProcessRunnerCore.");
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Process tree cleanup took {stopwatch.Elapsed}.");

            Directory.Delete(root, recursive: true);
        }
    }
}
