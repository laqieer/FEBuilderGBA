using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Comprehensive E2E tests for all FEBuilderGBA.CLI command-line arguments.
    /// Tests that don't need a ROM verify error messages and exit codes.
    /// Tests that need a ROM use SkippableTheory with RomLocator.
    /// </summary>
    public class CliArgsE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        private string TempFile(string ext = ".tmp")
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_test_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        // ================================================================ --help / -h

        [Fact]
        public void Help_ExitsZero()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("Usage:", stdout);
        }

        [Fact]
        public void Help_ShortFlag_ExitsZero()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "-h", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("Usage:", stdout);
        }

        [Fact]
        public void Help_ListsAllPrimaryCommands()
        {
            var (_, stdout, _) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            Assert.Contains("--version", stdout);
            Assert.Contains("--makeups", stdout);
            Assert.Contains("--applyups", stdout);
            Assert.Contains("--lint", stdout);
            Assert.Contains("--disasm", stdout);
            Assert.Contains("--decreasecolor", stdout);
            Assert.Contains("--pointercalc", stdout);
            Assert.Contains("--rebuild", stdout);
            Assert.Contains("--songexchange", stdout);
            Assert.Contains("--convertmap1picture", stdout);
            Assert.Contains("--translate", stdout);
            Assert.Contains("--translate-roundtrip", stdout);
            Assert.Contains("--lastrom", stdout);
            Assert.Contains("--force-detail", stdout);
            Assert.Contains("--translate_batch", stdout);
            Assert.Contains("--test", stdout);
            Assert.Contains("--testonly", stdout);
        }

        [Fact]
        public void NoArgs_PrintsHelpAndExitsZero()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("Usage:", stdout);
        }

        // ================================================================ --version

        [Fact]
        public void Version_ExitsZeroAndContainsInfo()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--version", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("Version", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Copyright", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GPLv3", stdout);
        }

        // ================================================================ --force-detail

        [Fact]
        public void ForceDetail_ExitsZero()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--force-detail", timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.Contains("--force-detail", stdout);
        }

        // ================================================================ --test / --testonly

        [Fact]
        public void Test_WithoutRom_RunsDiagnostics()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--test", timeoutMs: 30_000);
            // Should run config/env test at minimum
            Assert.Contains("Self-test", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TestOnly_WithoutRom_RunsAndExits()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--testonly", timeoutMs: 30_000);
            Assert.Contains("testonly", stdout, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================ --makeups (error paths)

        [Fact]
        public void Makeups_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--makeups=out.ups", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void Makeups_MissingFromrom_Errors()
        {
            var rom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe, $"--makeups=out.ups --rom=\"{rom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--fromrom", stderr);
        }

        [Fact]
        public void Makeups_MissingPath_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--makeups", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--makeups", stderr);
        }

        [Fact]
        public void Makeups_ValidArgs_CreatesUps()
        {
            var original = TempFile(".gba");
            var modified = TempFile(".gba");
            var upsOut = TempFile(".ups");

            // Create two small files that differ
            byte[] origData = new byte[256];
            byte[] modData = new byte[256];
            modData[0] = 0xFF;
            File.WriteAllBytes(original, origData);
            File.WriteAllBytes(modified, modData);

            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--makeups=\"{upsOut}\" --rom=\"{modified}\" --fromrom=\"{original}\"",
                timeoutMs: 15_000);
            Assert.Equal(0, code);
            Assert.True(File.Exists(upsOut), "UPS file should be created");
            Assert.Contains("UPS patch created", stdout);
        }

        // ================================================================ --applyups (error paths)

        [Fact]
        public void ApplyUps_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--applyups=out.gba", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void ApplyUps_MissingPatch_Errors()
        {
            var rom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe, $"--applyups=out.gba --rom=\"{rom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--patch", stderr);
        }

        [Fact]
        public void ApplyUps_RoundTrip_Works()
        {
            var original = TempFile(".gba");
            var modified = TempFile(".gba");
            var upsFile = TempFile(".ups");
            var applied = TempFile(".gba");

            byte[] origData = new byte[256];
            byte[] modData = new byte[256];
            modData[10] = 0xAB;
            File.WriteAllBytes(original, origData);
            File.WriteAllBytes(modified, modData);

            // Create UPS
            var (code1, _, _) = AppRunner.Run(CliExe,
                $"--makeups=\"{upsFile}\" --rom=\"{modified}\" --fromrom=\"{original}\"",
                timeoutMs: 15_000);
            Assert.Equal(0, code1);

            // Apply UPS
            var (code2, stdout, _) = AppRunner.Run(CliExe,
                $"--applyups=\"{applied}\" --rom=\"{original}\" --patch=\"{upsFile}\"",
                timeoutMs: 15_000);
            Assert.Equal(0, code2);
            Assert.Contains("UPS patch applied", stdout);
            Assert.True(File.Exists(applied));

            // Verify round-trip
            byte[] appliedData = File.ReadAllBytes(applied);
            Assert.Equal(modData.Length, appliedData.Length);
            Assert.Equal(modData[10], appliedData[10]);
        }

        // ================================================================ --lint (error paths)

        [Fact]
        public void Lint_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--lint", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        // ================================================================ --disasm (error paths)

        [Fact]
        public void Disasm_MissingPath_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--disasm", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--disasm", stderr);
        }

        [Fact]
        public void Disasm_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--disasm=out.asm", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        // ================================================================ --decreasecolor (error paths + success)

        [Fact]
        public void DecreaseColor_MissingIn_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--decreasecolor --out=out.png", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--in", stderr);
        }

        [Fact]
        public void DecreaseColor_MissingOut_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--decreasecolor --in=in.png", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--out", stderr);
        }

        [Fact]
        public void DecreaseColor_MissingInputFile_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--decreasecolor --in=nonexistent.png --out=out.png --paletteno=16",
                timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("not found", stderr, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================ --pointercalc (error paths)

        [Fact]
        public void PointerCalc_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--pointercalc", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void PointerCalc_MissingTarget_Errors()
        {
            var rom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--pointercalc --rom=\"{rom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--target", stderr);
        }

        [Fact]
        public void PointerCalc_MissingAddress_Errors()
        {
            var rom = TempFile(".gba");
            var target = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            File.WriteAllBytes(target, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--pointercalc --rom=\"{rom}\" --target=\"{target}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--address", stderr);
        }

        // ================================================================ --rebuild (error paths)

        [Fact]
        public void Rebuild_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--rebuild", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void Rebuild_MissingFromrom_Errors()
        {
            var rom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--rebuild --rom=\"{rom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--fromrom", stderr);
        }

        // ================================================================ --songexchange (error paths)

        [Fact]
        public void SongExchange_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--songexchange", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void SongExchange_MissingFromrom_Errors()
        {
            var rom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--songexchange --rom=\"{rom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--fromrom", stderr);
        }

        [Fact]
        public void SongExchange_MissingFromsong_Errors()
        {
            var rom = TempFile(".gba");
            var fromrom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            File.WriteAllBytes(fromrom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--songexchange --rom=\"{rom}\" --fromrom=\"{fromrom}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--fromsong", stderr);
        }

        [Fact]
        public void SongExchange_MissingTosong_Errors()
        {
            var rom = TempFile(".gba");
            var fromrom = TempFile(".gba");
            File.WriteAllBytes(rom, new byte[256]);
            File.WriteAllBytes(fromrom, new byte[256]);
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--songexchange --rom=\"{rom}\" --fromrom=\"{fromrom}\" --fromsong=0x1A", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--tosong", stderr);
        }

        // ================================================================ --convertmap1picture (error paths)

        [Fact]
        public void ConvertMap1Picture_MissingIn_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--convertmap1picture", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--in", stderr);
        }

        [Fact]
        public void ConvertMap1Picture_MissingOutput_Errors()
        {
            var input = TempFile(".png");
            File.WriteAllBytes(input, new byte[1]); // dummy
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{input}\"", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--outImg", stderr);
        }

        // ================================================================ --translate (error paths)

        [Fact]
        public void Translate_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--translate", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        // ================================================================ --translate-roundtrip (error paths)

        [Fact]
        public void TranslateRoundTrip_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--translate-roundtrip", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void TranslateRoundTrip_NonexistentRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--translate-roundtrip --rom=/nonexistent/rom.gba", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("not found", stderr, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================ --lastrom (error path)

        [Fact]
        public void Lastrom_WithoutConfig_Errors()
        {
            // Without a valid config, --lastrom should give an error
            var (code, _, stderr) = AppRunner.Run(CliExe, "--lastrom", timeoutMs: 15_000);
            // Should fail because no last ROM is configured in this test environment
            Assert.NotEqual(0, code);
        }

        // ================================================================ --translate_batch (error paths)

        [Fact]
        public void TranslateBatch_MissingRom_Errors()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--translate_batch", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("--rom", stderr);
        }

        // ================================================================ Unknown command

        [Fact]
        public void UnknownCommand_ExitsNonZero()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe, "--bogus-command", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            Assert.Contains("not yet supported", stderr, StringComparison.OrdinalIgnoreCase);
        }
    }
}
