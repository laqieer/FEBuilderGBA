// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FEBuilderGBA;
using FEBuilderGBA.CLI;
using CliProgram = FEBuilderGBA.CLI.Program;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    public sealed class CliPlaytestTests : IDisposable
    {
        private const string PassJson =
            "{\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"pass\"}";
        private readonly string _root;
        private readonly string _rom;
        private readonly string _scenario;

        public CliPlaytestTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "febuildergba-playtest-cli-" + Guid.NewGuid().ToString("N"));
            string package = Path.Combine(
                _root,
                "gba-playtest",
                "febuildergba_playtest");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "__main__.py"), "");
            File.WriteAllText(
                Path.Combine(_root, "gba-playtest", "scenario.schema.json"),
                "{}");
            _rom = Path.Combine(_root, "test rom.gba");
            _scenario = Path.Combine(_root, "test scenario.json");
            File.WriteAllBytes(_rom, new byte[0x200]);
            File.WriteAllText(_scenario, "{}");
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

        private PlaytestOperations Operations(
            Func<string, IReadOnlyList<string>, string, int, ProcessRunResult> run,
            string configuredPython = null,
            bool isWindows = true,
            string baseDirectory = null)
        {
            return new PlaytestOperations
            {
                BaseDirectory = baseDirectory ?? _root,
                IsWindows = isWindows,
                GetEnvironmentVariable = name =>
                    name == "FEBUILDERGBA_PLAYTEST_PYTHON" ? configuredPython : null,
                RunProcess = run,
            };
        }

        private static ProcessRunResult Result(
            int exitCode,
            string stdout,
            string stderr = "")
        {
            return new ProcessRunResult
            {
                Started = true,
                TimedOut = false,
                ExitCode = exitCode,
                Stdout = stdout,
                Stderr = stderr,
                ErrorMessage = "",
            };
        }

        private static (int Code, string Stdout, string Stderr) Run(
            string[] rawArgs,
            PlaytestOperations operations)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = CliProgram.RunPlaytest(
                CliProgram.ParseArgs(rawArgs),
                rawArgs,
                operations,
                stdout,
                stderr);
            return (code, stdout.ToString(), stderr.ToString());
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

        private string[] RunArgs(params string[] extra)
        {
            var args = new List<string>
            {
                "--playtest",
                "--rom=" + _rom,
                "--scenario=" + _scenario,
            };
            args.AddRange(extra);
            return args.ToArray();
        }

        [Fact]
        public void ExplicitPython_WinsAndUsesStructuralArguments()
        {
            string command = null;
            IReadOnlyList<string> arguments = null;
            string workingDirectory = null;
            int timeout = 0;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    command = cmd;
                    arguments = args.ToArray();
                    workingDirectory = cwd;
                    timeout = timeoutMs;
                    return Result(0, PassJson);
                },
                configuredPython: "environment-python");

            var result = Run(
                RunArgs(
                    "--python=explicit-python",
                    "--timeout=1234",
                    "--artifact-dir=" + _root),
                operations);

            Assert.Equal(0, result.Code);
            Assert.Equal("explicit-python", command);
            Assert.Equal(Path.Combine(_root, "gba-playtest"), workingDirectory);
            Assert.Equal(1234, timeout);
            Assert.Equal(
                new[]
                {
                    "-m",
                    "febuildergba_playtest",
                    "--rom",
                    Path.GetFullPath(_rom),
                    "--scenario",
                    Path.GetFullPath(_scenario),
                    "--artifact-dir",
                    Path.GetFullPath(_root),
                },
                arguments);
        }

        [Fact]
        public void EnvironmentPython_WinsOverCandidateList()
        {
            var commands = new List<string>();
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    commands.Add(cmd);
                    return Result(0, PassJson);
                },
                configuredPython: "configured-python");

            var result = Run(RunArgs(), operations);

            Assert.Equal(0, result.Code);
            Assert.Equal(new[] { "configured-python" }, commands);
        }

        [Fact]
        public void CandidateList_FallsBackOnlyWhenInterpreterDidNotStart()
        {
            var commands = new List<string>();
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    commands.Add(cmd);
                    return cmd == "python"
                        ? ProcessRunResult.NotStarted("missing")
                        : Result(0, PassJson);
                },
                isWindows: true);

            var result = Run(RunArgs(), operations);

            Assert.Equal(0, result.Code);
            Assert.Equal(new[] { "python", "python3" }, commands);
        }

        [Fact]
        public void Check_UsesModuleEntryPointWithoutRunArguments()
        {
            IReadOnlyList<string> arguments = null;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    arguments = args.ToArray();
                    return Result(
                        0,
                        "{\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"check_ok\"}");
                });

            var result = Run(
                new[] { "--playtest", "--check", "--python=python" },
                operations);

            Assert.Equal(0, result.Code);
            Assert.Equal(
                new[] { "-m", "febuildergba_playtest", "--check" },
                arguments);
        }

        [Fact]
        public void Check_RejectsRunArgumentsBeforeLaunching()
        {
            bool launched = false;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                });

            var result = Run(
                new[] { "--playtest", "--check", "--rom=" + _rom },
                operations);
            JsonElement json = ParseSingleResult(result.Stdout);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Equal("harness_error", json.GetProperty("status").GetString());
            Assert.Contains("--check cannot be combined", json.GetProperty("note").GetString());
        }

        [Fact]
        public void DuplicateOption_IsRejectedBeforeLaunching()
        {
            bool launched = false;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                });

            var result = Run(
                new[]
                {
                    "--playtest",
                    "--rom=" + _rom,
                    "--rom=" + _rom,
                    "--scenario=" + _scenario,
                },
                operations);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Contains("duplicate option", result.Stdout);
        }

        [Fact]
        public void MissingRunnerFiles_ReturnsStableHarnessError()
        {
            string emptyRoot = Path.Combine(_root, "empty");
            Directory.CreateDirectory(emptyRoot);
            bool launched = false;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                },
                baseDirectory: emptyRoot);

            var result = Run(RunArgs(), operations);
            JsonElement json = ParseSingleResult(result.Stdout);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Equal(1, json.GetProperty("resultSchemaVersion").GetInt32());
            Assert.Equal(1, json.GetProperty("exitCode").GetInt32());
            Assert.Equal("harness_error", json.GetProperty("status").GetString());
        }

        [Fact]
        public void MissingRom_IsRejectedBeforeLaunching()
        {
            File.Delete(_rom);
            bool launched = false;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                });

            var result = Run(RunArgs(), operations);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Contains("existing --rom file", result.Stdout);
        }

        [Fact]
        public void MissingArtifactDirectory_IsRejectedBeforeLaunching()
        {
            bool launched = false;
            string missing = Path.Combine(_root, "missing-artifacts");
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                });

            var result = Run(
                RunArgs("--artifact-dir=" + missing),
                operations);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Contains("existing directory", result.Stdout);
        }

        [Theory]
        [InlineData("999")]
        [InlineData("3600001")]
        [InlineData("not-a-number")]
        public void InvalidTimeout_IsRejectedBeforeLaunching(string timeout)
        {
            bool launched = false;
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                {
                    launched = true;
                    return Result(0, PassJson);
                });

            var result = Run(RunArgs("--timeout=" + timeout), operations);

            Assert.Equal(1, result.Code);
            Assert.False(launched);
            Assert.Contains("--timeout must be an integer", result.Stdout);
        }

        [Fact]
        public void StartFailure_SynthesizesJsonAndWritesRequestedOutput()
        {
            string outPath = Path.Combine(_root, "result.json");
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                    ProcessRunResult.NotStarted("secret executable path"));

            var result = Run(
                RunArgs("--python=missing-python", "--out=" + outPath),
                operations);
            JsonElement json = ParseSingleResult(result.Stdout);

            Assert.Equal(1, result.Code);
            Assert.Equal("harness_error", json.GetProperty("status").GetString());
            Assert.Contains("could not be started", json.GetProperty("note").GetString());
            Assert.Equal(result.Stdout, File.ReadAllText(outPath));
            Assert.DoesNotContain("secret executable path", result.Stdout);
        }

        [Fact]
        public void Timeout_SynthesizesHarnessError()
        {
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) => new ProcessRunResult
                {
                    Started = true,
                    TimedOut = true,
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = "",
                    ErrorMessage = "timed out",
                });

            var result = Run(RunArgs("--python=python"), operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("exceeded the process timeout", result.Stdout);
        }

        [Fact]
        public void NativeCrashExit_SynthesizesHarnessError()
        {
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) => Result(
                    unchecked((int)0xC0000005),
                    ""));

            var result = Run(RunArgs("--python=python"), operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("invalid result document", result.Stdout);
            Assert.Contains("\"status\":\"harness_error\"", result.Stdout);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("{\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"unknown\"}")]
        [InlineData("{\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"pass\"}\n{}")]
        [InlineData("{\"exitCode\":0,\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"pass\"}")]
        public void MalformedRunnerOutput_SynthesizesHarnessError(string runnerOutput)
        {
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) => Result(0, runnerOutput));

            var result = Run(RunArgs("--python=python"), operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("invalid result document", result.Stdout);
            if (runnerOutput.Length > 0)
                Assert.DoesNotContain(runnerOutput, result.Stdout);
        }

        [Fact]
        public void ProcessExitMustMatchDocumentExit()
        {
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) => Result(
                    1,
                    "{\"exitCode\":0,\"resultSchemaVersion\":1,\"status\":\"pass\"}"));

            var result = Run(RunArgs("--python=python"), operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("invalid result document", result.Stdout);
        }

        [Theory]
        [InlineData(0, "pass")]
        [InlineData(0, "check_ok")]
        [InlineData(1, "dependency_error")]
        [InlineData(1, "harness_error")]
        [InlineData(2, "assertion_failed")]
        [InlineData(2, "crash")]
        [InlineData(2, "softlock")]
        public void ValidRunnerResult_PreservesJsonAndExitCode(int exitCode, string status)
        {
            string json =
                $"{{\"exitCode\":{exitCode},\"resultSchemaVersion\":1,\"status\":\"{status}\"}}";
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) => Result(exitCode, json));

            string[] args = status == "check_ok"
                ? new[] { "--playtest", "--check", "--python=python" }
                : RunArgs("--python=python");
            var result = Run(args, operations);

            Assert.Equal(exitCode, result.Code);
            Assert.Equal(json + "\n", result.Stdout);
        }

        [Fact]
        public void RunnerDiagnostics_AreRelayedOnlyToStderr()
        {
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                    Result(0, PassJson, "native diagnostic"));

            var result = Run(RunArgs("--python=python"), operations);

            Assert.Equal(0, result.Code);
            Assert.Equal(PassJson + "\n", result.Stdout);
            Assert.Equal("native diagnostic\n", result.Stderr);
        }

        [Fact]
        public void OutputCollision_DoesNotOverwriteScenarioOnPreflightFailure()
        {
            string original = File.ReadAllText(_scenario);
            File.Delete(_rom);
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                    throw new InvalidOperationException("must not launch"));

            var result = Run(
                RunArgs("--out=" + _scenario),
                operations);

            Assert.Equal(1, result.Code);
            Assert.Contains("cannot overwrite", result.Stdout);
            Assert.Equal(original, File.ReadAllText(_scenario));
        }

        [Fact]
        public void UnwritableOutput_ReplacesOriginalErrorWithOutputFailure()
        {
            string directoryTarget = Path.Combine(_root, "result-dir");
            Directory.CreateDirectory(directoryTarget);
            var operations = Operations(
                (cmd, args, cwd, timeoutMs) =>
                    ProcessRunResult.NotStarted("missing"));

            var result = Run(
                RunArgs("--python=missing", "--out=" + directoryTarget),
                operations);
            JsonElement json = ParseSingleResult(result.Stdout);

            Assert.Equal(1, result.Code);
            Assert.Equal(
                "cannot write the playtest result output",
                json.GetProperty("note").GetString());
            Assert.Contains("target is a directory", result.Stderr);
        }
    }
}
