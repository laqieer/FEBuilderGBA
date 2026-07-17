// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text.Json;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public sealed class CliPlaytestE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly string _root;
        private readonly string _rom;
        private readonly string _scenario;

        public CliPlaytestE2ETests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "FEBuilderGBA playtest " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _rom = Path.Combine(_root, "synthetic test rom.gba");
            _scenario = Path.Combine(_root, "scenario with spaces.json");
            File.WriteAllBytes(_rom, new byte[0x200]);
            File.WriteAllText(
                _scenario,
                "{\"schemaVersion\":1,\"runFrames\":1,\"assertions\":[]}");
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch
            {
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static JsonElement ParseSingleResult(string stdout)
        {
            string[] lines = stdout.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            using JsonDocument document = JsonDocument.Parse(lines[0]);
            return document.RootElement.Clone();
        }

        [Fact]
        public void Help_ListsPlaytestCommand()
        {
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--help",
                timeoutMs: 15_000);

            Assert.Equal(0, code);
            Assert.Contains("--playtest", stdout);
            Assert.Contains("--scenario=<path>", stdout);
        }

        [Fact]
        public void MissingRom_EmitsOneMachineReadableHarnessError()
        {
            string missingRom = Path.Combine(_root, "missing.gba");
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--playtest --rom=" + Quote(missingRom)
                    + " --scenario=" + Quote(_scenario),
                timeoutMs: 15_000);
            JsonElement result = ParseSingleResult(stdout);

            Assert.Equal(1, code);
            Assert.Equal(1, result.GetProperty("resultSchemaVersion").GetInt32());
            Assert.Equal("harness_error", result.GetProperty("status").GetString());
            Assert.Contains(
                "existing --rom file",
                result.GetProperty("note").GetString());
        }

        [Fact]
        public void CheckWithRunArguments_IsRejectedBeforePythonLaunch()
        {
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--playtest --check --rom=" + Quote(_rom),
                timeoutMs: 15_000);
            JsonElement result = ParseSingleResult(stdout);

            Assert.Equal(1, code);
            Assert.Equal("harness_error", result.GetProperty("status").GetString());
            Assert.Contains(
                "--check cannot be combined",
                result.GetProperty("note").GetString());
        }

        [Fact]
        public void MissingExplicitPython_UsesShippedRunnerThenSynthesizesError()
        {
            string missingPython = Path.Combine(_root, "missing python.exe");
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--playtest --check --python=" + Quote(missingPython),
                timeoutMs: 15_000);
            JsonElement result = ParseSingleResult(stdout);

            Assert.Equal(1, code);
            Assert.Equal("harness_error", result.GetProperty("status").GetString());
            Assert.Contains(
                "could not be started",
                result.GetProperty("note").GetString());
            Assert.DoesNotContain("runner files are missing", stdout);
        }

        [Fact]
        public void StartFailure_WritesSameMachineReadableResultToOutPath()
        {
            string missingPython = Path.Combine(_root, "missing python.exe");
            string outPath = Path.Combine(_root, "result with spaces.json");
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--playtest --rom=" + Quote(_rom)
                    + " --scenario=" + Quote(_scenario)
                    + " --python=" + Quote(missingPython)
                    + " --out=" + Quote(outPath),
                timeoutMs: 15_000);
            JsonElement stdoutResult = ParseSingleResult(stdout);

            Assert.Equal(1, code);
            Assert.True(File.Exists(outPath));
            using JsonDocument persisted = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal(
                stdoutResult.GetRawText(),
                persisted.RootElement.GetRawText());
            Assert.Equal(
                "harness_error",
                persisted.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public void InvalidTimeout_IsRejectedBeforePythonLaunch()
        {
            var (code, stdout, _) = AppRunner.Run(
                CliExe,
                "--playtest --rom=" + Quote(_rom)
                    + " --scenario=" + Quote(_scenario)
                    + " --timeout=999",
                timeoutMs: 15_000);
            JsonElement result = ParseSingleResult(stdout);

            Assert.Equal(1, code);
            Assert.Contains(
                "--timeout must be an integer",
                result.GetProperty("note").GetString());
        }
    }
}
