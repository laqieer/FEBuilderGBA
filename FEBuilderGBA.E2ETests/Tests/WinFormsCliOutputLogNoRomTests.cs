using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs every no-ROM WinForms CLI command and persists stdout/stderr/exitCode to .log files.
    /// These logs serve as reference artifacts in CI for regression tracking.
    /// Note: no-args and unknown commands may timeout (WinForms tries to open GUI),
    /// but we still log whatever output is produced before the timeout kill.
    /// </summary>
    public class WinFormsCliOutputLogNoRomTests
    {
        private static readonly string ExePath = AppRunner.FindExePath();

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
        public void Log_Version()
        {
            var (code, stdout, stderr) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            SaveLog("WinForms_version.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_NoArgs()
        {
            // WinForms with no args will try to open the GUI — will be killed after timeout.
            // We still capture whatever output is produced.
            var (code, stdout, stderr) = AppRunner.Run(ExePath, "", timeoutMs: 10_000);
            SaveLog("WinForms_noargs.log", code, stdout, stderr);
            // No assertion on output — GUI startup may produce nothing on stdout/stderr
        }

        [Fact]
        public void Log_BogusCommand()
        {
            // Unknown command will likely trigger GUI startup — killed after timeout.
            var (code, stdout, stderr) = AppRunner.Run(ExePath, "--bogus-command", timeoutMs: 10_000);
            SaveLog("WinForms_bogus-command.log", code, stdout, stderr);
            // No assertion on output — GUI startup may produce nothing on stdout/stderr
        }
    }
}
