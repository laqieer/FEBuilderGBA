using System;
using System.IO;
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
    }
}
