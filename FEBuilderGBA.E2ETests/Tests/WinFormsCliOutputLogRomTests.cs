using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs ROM-based WinForms CLI commands and persists stdout/stderr/exitCode to .log files.
    /// Uses the old WinForms exe (FEBuilderGBA.exe) which uses space-separated --rom args.
    ///
    /// NOTE: FEBuilderGBA.exe is a WinExe (GUI app), not a console app. Many CLI commands
    /// may produce no stdout/stderr output, or may hang by opening a GUI dialog.
    /// These tests only assert that the log file was saved — they do NOT assert on output
    /// content, since the WinForms exe's console behaviour is unreliable.
    /// </summary>
    public class WinFormsCliOutputLogRomTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
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

        // ------------------------------------------------------------------ --lint

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Lint(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{romPath}\" --lint", timeoutMs: 120_000);
            SaveLog($"WinForms_lint_{romName}.log", code, stdout, stderr);
            // WinExe may produce no console output — just save the log
        }

        // ------------------------------------------------------------------ --rebuild
        // Skipped: WinForms --rebuild takes ~10 min per ROM.
        // Already covered by CliOutputLogRomPart1Tests.Log_Rebuild (CLI exe, <1s).

        // ------------------------------------------------------------------ --makeups

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Makeups(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var tempRom = TempFile(".gba");
            File.Copy(romPath!, tempRom);
            var upsOut = Path.ChangeExtension(tempRom, ".ups");
            _tempFiles.Add(upsOut);

            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{tempRom}\" --makeups=\"{upsOut}\"", timeoutMs: 60_000);
            SaveLog($"WinForms_makeups_{romName}.log", code, stdout, stderr);
        }

        // ------------------------------------------------------------------ --disasm

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Disasm(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var outFile = TempFile(".asm");
            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{romPath}\" --disasm=\"{outFile}\"", timeoutMs: 120_000);
            SaveLog($"WinForms_disasm_{romName}.log", code, stdout, stderr);
        }

        // ------------------------------------------------------------------ --translate

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_Translate(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var outFile = TempFile(".txt");
            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{romPath}\" --translate --out=\"{outFile}\"", timeoutMs: 120_000);
            SaveLog($"WinForms_translate_{romName}.log", code, stdout, stderr);
        }

        // ------------------------------------------------------------------ --pointercalc

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_PointerCalc(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var targetRom = TempFile(".gba");
            File.Copy(romPath!, targetRom);

            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{romPath}\" --pointercalc --target=\"{targetRom}\" --address=0x100",
                timeoutMs: 120_000);
            SaveLog($"WinForms_pointercalc_{romName}.log", code, stdout, stderr);
        }

        // ------------------------------------------------------------------ --songexchange

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Log_SongExchange(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            var destRom = TempFile(".gba");
            File.Copy(romPath!, destRom);

            var (code, stdout, stderr) = AppRunner.Run(
                ExePath, $"--rom \"{destRom}\" --songexchange --fromrom=\"{romPath}\" --fromsong=0x1 --tosong=0x1",
                timeoutMs: 120_000);
            SaveLog($"WinForms_songexchange_{romName}.log", code, stdout, stderr);
        }

        // ------------------------------------------------------------------ --decreasecolor
        // NOTE: Removed. The WinForms exe opens a GUI dialog for --decreasecolor without
        // a ROM, hanging indefinitely. Use the cross-platform CLI tests for this command.

        // ------------------------------------------------------------------ --convertmap1picture
        // NOTE: Removed. Same as --decreasecolor — WinForms exe opens a GUI dialog.
    }
}
