// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    internal sealed class PlaytestOperations
    {
        internal string BaseDirectory { get; init; } = "";
        internal bool IsWindows { get; init; } = OperatingSystem.IsWindows();
        internal Func<string, string> GetEnvironmentVariable { get; init; }
            = Environment.GetEnvironmentVariable;
        internal Func<string, IReadOnlyList<string>, string, int, ProcessRunResult> RunProcess
            { get; init; }
            = (command, args, workingDirectory, timeoutMs) =>
                ProcessRunnerCore.Run(command, args, workingDirectory, timeoutMs);
    }

    static partial class Program
    {
        internal const int PlaytestDefaultTimeoutMs = 600_000;
        internal const int PlaytestMinimumTimeoutMs = 1_000;
        internal const int PlaytestMaximumTimeoutMs = 3_600_000;
        internal const int PlaytestMaximumResultChars = 1_048_576;

        private static readonly IReadOnlyDictionary<string, int> PlaytestStatusExitCodes =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["pass"] = 0,
                ["check_ok"] = 0,
                ["scenario_error"] = 1,
                ["dependency_error"] = 1,
                ["harness_error"] = 1,
                ["check_failed"] = 1,
                ["rom_mismatch"] = 2,
                ["assertion_failed"] = 2,
                ["crash"] = 2,
                ["softlock"] = 2,
            };

        static int RunPlaytest(Dictionary<string, string> argsDic)
        {
            var operations = new PlaytestOperations
            {
                BaseDirectory = CoreState.BaseDirectory,
            };
            return RunPlaytest(argsDic, RawArgs, operations, Console.Out, Console.Error);
        }

        internal static int RunPlaytest(
            Dictionary<string, string> argsDic,
            IReadOnlyList<string> rawArgs,
            PlaytestOperations operations,
            TextWriter stdout,
            TextWriter stderr)
        {
            if (argsDic == null || operations == null || stdout == null || stderr == null)
                return 1;

            if (!TryValidatePlaytestArgs(rawArgs, out string argumentError))
                return EmitPlaytestError(argumentError, null, stdout, stderr);

            bool check = argsDic.ContainsKey("--check");
            string outPath = null;
            if (argsDic.TryGetValue("--out", out string outValue))
            {
                if (!TryGetFullPath(outValue, out outPath))
                    return EmitPlaytestError("invalid --out path", null, stdout, stderr);
            }

            string romPath = null;
            string scenarioPath = null;
            string artifactDirectory = null;
            int timeoutMs = PlaytestDefaultTimeoutMs;

            if (check)
            {
                if (argsDic.ContainsKey("--rom")
                    || argsDic.ContainsKey("--scenario")
                    || argsDic.ContainsKey("--out")
                    || argsDic.ContainsKey("--artifact-dir")
                    || argsDic.ContainsKey("--timeout"))
                {
                    return EmitPlaytestError(
                        "--check cannot be combined with --rom/--scenario/--out/--artifact-dir/--timeout",
                        null,
                        stdout,
                        stderr);
                }
            }
            else
            {
                if (!TryRequiredPath(argsDic, "--rom", out romPath))
                    return EmitPlaytestError("--playtest requires --rom=<file>", outPath, stdout, stderr);
                if (!TryRequiredPath(argsDic, "--scenario", out scenarioPath))
                    return EmitPlaytestError("--playtest requires --scenario=<file>", outPath, stdout, stderr);

                if (outPath != null
                    && (PathsEqual(outPath, romPath) || PathsEqual(outPath, scenarioPath)))
                {
                    return EmitPlaytestError(
                        "--out cannot overwrite the ROM or scenario",
                        null,
                        stdout,
                        stderr);
                }

                if (!File.Exists(romPath))
                    return EmitPlaytestError("--playtest requires an existing --rom file", outPath, stdout, stderr);
                if (!File.Exists(scenarioPath))
                    return EmitPlaytestError("--playtest requires an existing --scenario file", outPath, stdout, stderr);

                if (argsDic.TryGetValue("--artifact-dir", out string artifactValue))
                {
                    if (!TryGetFullPath(artifactValue, out artifactDirectory)
                        || !Directory.Exists(artifactDirectory))
                    {
                        return EmitPlaytestError(
                            "--artifact-dir must name an existing directory",
                            outPath,
                            stdout,
                            stderr);
                    }
                }

                if (argsDic.TryGetValue("--timeout", out string timeoutValue)
                    && (!int.TryParse(timeoutValue, out timeoutMs)
                        || timeoutMs < PlaytestMinimumTimeoutMs
                        || timeoutMs > PlaytestMaximumTimeoutMs))
                {
                    return EmitPlaytestError(
                        $"--timeout must be an integer from {PlaytestMinimumTimeoutMs} through {PlaytestMaximumTimeoutMs}",
                        outPath,
                        stdout,
                        stderr);
                }

            }

            string runnerRoot = Path.Combine(operations.BaseDirectory ?? "", "gba-playtest");
            string runnerMain = Path.Combine(
                runnerRoot,
                "febuildergba_playtest",
                "__main__.py");
            string schemaPath = Path.Combine(runnerRoot, "scenario.schema.json");
            if (!File.Exists(runnerMain) || !File.Exists(schemaPath))
            {
                return EmitPlaytestError(
                    "playtest runner files are missing from the CLI installation",
                    outPath,
                    stdout,
                    stderr);
            }

            if (!TryGetInterpreterCandidates(argsDic, operations, out List<string> interpreters,
                    out bool allowFallback, out string interpreterError))
            {
                return EmitPlaytestError(interpreterError, outPath, stdout, stderr);
            }

            var runnerArgs = new List<string>
            {
                "-m",
                "febuildergba_playtest",
            };
            if (check)
            {
                runnerArgs.Add("--check");
            }
            else
            {
                runnerArgs.Add("--rom");
                runnerArgs.Add(romPath);
                runnerArgs.Add("--scenario");
                runnerArgs.Add(scenarioPath);
                if (outPath != null)
                {
                    runnerArgs.Add("--out");
                    runnerArgs.Add(outPath);
                }
                if (artifactDirectory != null)
                {
                    runnerArgs.Add("--artifact-dir");
                    runnerArgs.Add(artifactDirectory);
                }
            }

            ProcessRunResult run = ProcessRunResult.NotStarted("No interpreter candidate started.");
            foreach (string interpreter in interpreters)
            {
                run = operations.RunProcess(interpreter, runnerArgs, runnerRoot, timeoutMs);
                if (run.Started || !allowFallback)
                    break;
            }

            RelayPlaytestDiagnostics(run.Stderr, stderr);
            if (!run.Started)
            {
                return EmitPlaytestError(
                    "the Python interpreter could not be started",
                    outPath,
                    stdout,
                    stderr);
            }
            if (run.TimedOut)
            {
                return EmitPlaytestError(
                    "the playtest runner exceeded the process timeout",
                    outPath,
                    stdout,
                    stderr);
            }
            if (!TryValidatePlaytestResult(run.Stdout, run.ExitCode, out string resultJson))
            {
                return EmitPlaytestError(
                    "the playtest runner returned an invalid result document",
                    outPath,
                    stdout,
                    stderr);
            }

            stdout.Write(resultJson);
            stdout.Write('\n');
            return run.ExitCode;
        }

        private static bool TryValidatePlaytestArgs(
            IReadOnlyList<string> rawArgs,
            out string error)
        {
            error = "";
            if (rawArgs == null)
                return true;

            var flagOptions = new HashSet<string>(StringComparer.Ordinal)
            {
                "--playtest",
                "--check",
            };
            var valueOptions = new HashSet<string>(StringComparer.Ordinal)
            {
                "--rom",
                "--scenario",
                "--out",
                "--artifact-dir",
                "--python",
                "--timeout",
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < rawArgs.Count; i++)
            {
                string token = rawArgs[i] ?? "";
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "unexpected positional argument for --playtest";
                    return false;
                }

                int equals = token.IndexOf('=');
                string name = equals >= 0 ? token.Substring(0, equals) : token;
                string inlineValue = equals >= 0 ? token.Substring(equals + 1) : null;
                if (!flagOptions.Contains(name) && !valueOptions.Contains(name))
                {
                    error = "unsupported option for --playtest";
                    return false;
                }
                if (!seen.Add(name))
                {
                    error = "duplicate option for --playtest";
                    return false;
                }
                if (flagOptions.Contains(name))
                {
                    if (inlineValue != null)
                    {
                        error = "flag option cannot have a value";
                        return false;
                    }
                    continue;
                }

                if (inlineValue == null)
                {
                    if (i + 1 >= rawArgs.Count
                        || (rawArgs[i + 1] ?? "").StartsWith("-", StringComparison.Ordinal))
                    {
                        error = "missing value for --playtest option";
                        return false;
                    }
                    inlineValue = rawArgs[++i];
                }
                if (string.IsNullOrEmpty(inlineValue))
                {
                    error = "empty value for --playtest option";
                    return false;
                }
            }

            return seen.Contains("--playtest");
        }

        private static bool TryRequiredPath(
            Dictionary<string, string> argsDic,
            string option,
            out string fullPath)
        {
            fullPath = null;
            return argsDic.TryGetValue(option, out string value)
                && TryGetFullPath(value, out fullPath);
        }

        private static bool TryGetFullPath(string value, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            try
            {
                fullPath = Path.GetFullPath(value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (SecurityException)
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (left == null || right == null)
                return false;
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(left),
                Path.TrimEndingDirectorySeparator(right),
                comparison);
        }

        private static bool TryGetInterpreterCandidates(
            Dictionary<string, string> argsDic,
            PlaytestOperations operations,
            out List<string> interpreters,
            out bool allowFallback,
            out string error)
        {
            interpreters = new List<string>();
            allowFallback = false;
            error = "";

            if (argsDic.TryGetValue("--python", out string explicitPython))
            {
                if (!TryNormalizeExecutable(explicitPython, out string normalized))
                {
                    error = "--python requires a non-empty executable";
                    return false;
                }
                interpreters.Add(normalized);
                return true;
            }

            string configured = operations.GetEnvironmentVariable(
                "FEBUILDERGBA_PLAYTEST_PYTHON");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!TryNormalizeExecutable(configured, out string normalized))
                {
                    error = "FEBUILDERGBA_PLAYTEST_PYTHON is invalid";
                    return false;
                }
                interpreters.Add(normalized);
                return true;
            }

            allowFallback = true;
            if (operations.IsWindows)
            {
                interpreters.Add("python");
                interpreters.Add("python3");
            }
            else
            {
                interpreters.Add("python3");
                interpreters.Add("python");
            }
            return true;
        }

        private static bool TryNormalizeExecutable(string value, out string executable)
        {
            executable = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (Path.IsPathRooted(value)
                || value.IndexOf(Path.DirectorySeparatorChar) >= 0
                || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return TryGetFullPath(value, out executable);
            }

            executable = value;
            return true;
        }

        private static bool TryValidatePlaytestResult(
            string stdout,
            int processExitCode,
            out string resultJson)
        {
            resultJson = null;
            if (string.IsNullOrWhiteSpace(stdout)
                || stdout.Length > PlaytestMaximumResultChars
                || processExitCode < 0
                || processExitCode > 2)
            {
                return false;
            }

            string trimmed = stdout.Trim();
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    trimmed,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 64,
                    });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        return false;
                }

                if (!root.TryGetProperty("resultSchemaVersion", out JsonElement schema)
                    || schema.ValueKind != JsonValueKind.Number
                    || !schema.TryGetInt32(out int schemaVersion)
                    || schemaVersion != 1
                    || !root.TryGetProperty("status", out JsonElement statusElement)
                    || statusElement.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("exitCode", out JsonElement exitElement)
                    || exitElement.ValueKind != JsonValueKind.Number
                    || !exitElement.TryGetInt32(out int documentExitCode))
                {
                    return false;
                }

                string status = statusElement.GetString();
                if (!PlaytestStatusExitCodes.TryGetValue(status ?? "", out int expectedExit)
                    || expectedExit != documentExitCode
                    || documentExitCode != processExitCode)
                {
                    return false;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            resultJson = trimmed;
            return true;
        }

        private static int EmitPlaytestError(
            string note,
            string outPath,
            TextWriter stdout,
            TextWriter stderr)
        {
            string json = BuildPlaytestErrorJson(note);
            if (outPath != null && !TryWritePlaytestResult(outPath, json, out string writeError))
            {
                stderr.WriteLine(writeError);
                json = BuildPlaytestErrorJson("cannot write the playtest result output");
            }
            stdout.Write(json);
            stdout.Write('\n');
            return 1;
        }

        private static string BuildPlaytestErrorJson(string note)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = false,
            }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("exitCode", 1);
                writer.WriteString("note", SanitizePlaytestNote(note));
                writer.WriteNumber("resultSchemaVersion", 1);
                writer.WriteString("status", "harness_error");
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string SanitizePlaytestNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return "playtest harness error";

            var builder = new StringBuilder(Math.Min(note.Length, 200));
            bool previousWhitespace = false;
            foreach (char ch in note)
            {
                if (builder.Length >= 200)
                    break;
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWhitespace && builder.Length > 0)
                        builder.Append(' ');
                    previousWhitespace = true;
                    continue;
                }
                if (char.IsControl(ch))
                    continue;
                builder.Append(ch);
                previousWhitespace = false;
            }
            return builder.ToString().Trim();
        }

        private static bool TryWritePlaytestResult(
            string outPath,
            string json,
            out string error)
        {
            error = "";
            string tempPath = null;
            try
            {
                string directory = Path.GetDirectoryName(outPath);
                if (string.IsNullOrEmpty(directory))
                    directory = Directory.GetCurrentDirectory();
                if (!Directory.Exists(directory))
                {
                    error = "Playtest result directory does not exist.";
                    return false;
                }
                if (Directory.Exists(outPath))
                {
                    error = "Playtest result target is a directory.";
                    return false;
                }
                if (File.Exists(outPath)
                    && (File.GetAttributes(outPath) & FileAttributes.ReparsePoint) != 0)
                {
                    error = "Playtest result target cannot be a link or reparse point.";
                    return false;
                }

                tempPath = Path.Combine(
                    directory,
                    "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(tempPath, json + "\n", new UTF8Encoding(false));
                File.Move(tempPath, outPath, true);
                tempPath = null;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied while writing the playtest result.";
                return false;
            }
            catch (IOException)
            {
                error = "I/O error while writing the playtest result.";
                return false;
            }
            catch (ArgumentException)
            {
                error = "Invalid playtest result path.";
                return false;
            }
            catch (NotSupportedException)
            {
                error = "Unsupported playtest result path.";
                return false;
            }
            catch (SecurityException)
            {
                error = "Permission denied while writing the playtest result.";
                return false;
            }
            finally
            {
                if (tempPath != null)
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private static void RelayPlaytestDiagnostics(string diagnostics, TextWriter stderr)
        {
            if (string.IsNullOrEmpty(diagnostics))
                return;
            stderr.Write(diagnostics);
            if (!diagnostics.EndsWith("\n", StringComparison.Ordinal))
                stderr.Write('\n');
        }
    }
}
