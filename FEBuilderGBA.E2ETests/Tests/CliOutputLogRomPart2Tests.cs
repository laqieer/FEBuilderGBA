using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs ROM-based CLI commands (makeups, applyups, pointercalc, songexchange) and
    /// persists stdout/stderr/exitCode to .log files for regression tracking.
    /// </summary>
    public class CliOutputLogRomPart2Tests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();

        private string TempFile(string ext = ".tmp")
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_log_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        private static void SaveLog(string fileName, int exitCode, string stdout, string stderr)
        {
            string dir = Environment.GetEnvironmentVariable("FEBUILDERGBA_CLI_LOG_DIR")
                ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "cli-logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path,
                $"=== {fileName} ===\r\nTimestamp: {DateTime.UtcNow:O}\r\nExitCode: {exitCode}\r\n\r\n--- STDOUT ---\r\n{stdout}\r\n--- STDERR ---\r\n{stderr}\r\n");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Makeups(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var tempRom = TempFile(".gba");
            File.Copy(romPath!, tempRom);
            var upsOut = TempFile(".ups");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--makeups=\"{upsOut}\" --rom=\"{tempRom}\" --fromrom=\"{romPath}\"",
                timeoutMs: 60_000);
            SaveLog($"CLI_makeups_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Log_ApplyUps(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            // Create a UPS patch first, then apply it
            var tempRom = TempFile(".gba");
            File.Copy(romPath!, tempRom);
            var upsFile = TempFile(".ups");
            var appliedOut = TempFile(".gba");

            // Make a trivial UPS (identical ROM → minimal patch)
            AppRunner.Run(CliExe,
                $"--makeups=\"{upsFile}\" --rom=\"{tempRom}\" --fromrom=\"{romPath}\"",
                timeoutMs: 60_000);

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--applyups=\"{appliedOut}\" --rom=\"{romPath}\" --patch=\"{upsFile}\"",
                timeoutMs: 120_000);
            SaveLog($"CLI_applyups_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Log_PointerCalc(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var targetRom = TempFile(".gba");
            File.Copy(romPath!, targetRom);

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--pointercalc --rom=\"{romPath}\" --target=\"{targetRom}\" --address=0x100",
                timeoutMs: 120_000);
            SaveLog($"CLI_pointercalc_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.RepresentativeRoms), MemberType = typeof(RomLocator))]
        public void Log_SongExchange(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var destRom = TempFile(".gba");
            File.Copy(romPath!, destRom);

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--songexchange --rom=\"{destRom}\" --fromrom=\"{romPath}\" --fromsong=0x1 --tosong=0x1",
                timeoutMs: 120_000);
            SaveLog($"CLI_songexchange_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }
    }
}
