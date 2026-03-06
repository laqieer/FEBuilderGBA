using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs ROM-based CLI commands (lint, disasm, translate, rebuild) and persists
    /// stdout/stderr/exitCode to .log files for regression tracking.
    /// </summary>
    public class CliOutputLogRomPart1Tests : IDisposable
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
        public void Log_Lint(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--lint --rom=\"{romPath}\"", timeoutMs: 120_000);
            SaveLog($"CLI_lint_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Disasm(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var outFile = TempFile(".asm");
            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--disasm=\"{outFile}\" --rom=\"{romPath}\"", timeoutMs: 120_000);
            SaveLog($"CLI_disasm_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Translate(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var outFile = TempFile(".txt");
            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--translate --rom=\"{romPath}\" --out=\"{outFile}\"", timeoutMs: 120_000);
            SaveLog($"CLI_translate_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Rebuild(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var tempRom = TempFile(".gba");
            File.Copy(romPath!, tempRom);
            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--rebuild --rom=\"{tempRom}\"", timeoutMs: 600_000);
            SaveLog($"CLI_rebuild_{romName}.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }
    }
}
