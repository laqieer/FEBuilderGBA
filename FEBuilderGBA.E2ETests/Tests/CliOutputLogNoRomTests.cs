using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs every no-ROM CLI command and persists stdout/stderr/exitCode to .log files.
    /// These logs serve as reference artifacts in CI for regression tracking.
    /// </summary>
    public class CliOutputLogNoRomTests
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();

        private static void SaveLog(string fileName, int exitCode, string stdout, string stderr)
        {
            string dir = Environment.GetEnvironmentVariable("FEBUILDERGBA_CLI_LOG_DIR")
                ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "cli-logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path,
                $"=== {fileName} ===\r\nTimestamp: {DateTime.UtcNow:O}\r\nExitCode: {exitCode}\r\n\r\n--- STDOUT ---\r\n{stdout}\r\n--- STDERR ---\r\n{stderr}\r\n");
        }

        [Fact]
        public void Log_Help()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            SaveLog("CLI_help.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_Help_Short()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "-h", timeoutMs: 15_000);
            SaveLog("CLI_h.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_Version()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--version", timeoutMs: 15_000);
            SaveLog("CLI_version.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_ForceDetail()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--force-detail", timeoutMs: 15_000);
            SaveLog("CLI_force-detail.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_Test()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--test", timeoutMs: 30_000);
            SaveLog("CLI_test.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_TestOnly()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--testonly", timeoutMs: 30_000);
            SaveLog("CLI_testonly.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_NoArgs()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "", timeoutMs: 15_000);
            SaveLog("CLI_noargs.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_BogusCommand()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe, "--bogus-command", timeoutMs: 15_000);
            SaveLog("CLI_bogus-command.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }
    }
}
