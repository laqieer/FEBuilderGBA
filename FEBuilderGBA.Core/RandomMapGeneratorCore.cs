// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// External-process adapter for FEMapCreator random-map generation. This seam never bundles
    /// or downloads FEMapCreator; it only launches a user-selected local executable or DLL.
    /// </summary>
    public static class RandomMapGeneratorCore
    {
        // 120 seconds is long enough for larger random-map generations while still giving the
        // caller a deterministic failure instead of an unbounded hang.
        const int ProcessTimeoutMs = 120_000;
        const int MaximumOutputChars = 1_000_000;
        const int DiagnosticTailChars = 4096;
        const string TempDirectoryPrefix = "FEBuilderGBA-mapgen-";
        const string OutputFileName = "output.mar";

        /// <summary>
        /// Generate a random map through FEMapCreator, parse its temporary <c>.mar</c> file,
        /// and return converted FEBuilder MAR values. Never throws.
        /// </summary>
        public static RandomMapGenerationResult Generate(
            RandomMapGenerationRequest request,
            ProcessRunnerDelegate runner = null)
        {
            runner ??= ProcessRunnerCore.Run;

            if (!TryValidateRequest(request, out string normalizedAssetsDir, out string error))
                return Fail(RandomMapGeneratorErrorCategory.InvalidPath, error);

            string tempDir = "";
            try
            {
                tempDir = CreateTempWorkingDirectory();
                request.OutputMarPath = Path.Combine(tempDir, OutputFileName);

                FEMapCreatorLauncherCore.FEMapCreatorLaunchSpec spec =
                    FEMapCreatorLauncherCore.CreateLaunchSpec(
                        request.FEMapCreatorPath,
                        BuildGenerateArguments(request, normalizedAssetsDir));
                if (!spec.Success)
                    return Fail(spec.ErrorCategory, spec.ErrorMessage);

                ProcessRunResult processResult = runner(
                    spec.Command,
                    spec.Arguments,
                    tempDir,
                    ProcessTimeoutMs,
                    MaximumOutputChars);
                if (!processResult.Started)
                    return FailForNotStarted(spec, processResult);
                if (processResult.TimedOut)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.TimedOut,
                        AppendTerminationFailure("FEMapCreator random-map generation timed out.", processResult),
                        processResult);
                if (processResult.OutputLimitExceeded)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.OutputLimitExceeded,
                        AppendTerminationFailure("FEMapCreator random-map generation exceeded the output capture limit.", processResult),
                        processResult);
                if (processResult.ExitCode != 0)
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.NonZeroExit,
                        "FEMapCreator random-map generation exited with code " + processResult.ExitCode + ".",
                        processResult);
                if (!File.Exists(request.OutputMarPath))
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.OutputMissing,
                        "FEMapCreator reported success but did not produce the expected MAR file: " + request.OutputMarPath,
                        processResult);

                byte[] marBytes = File.ReadAllBytes(request.OutputMarPath);
                if (!RandomMapGeneratorMarParserCore.TryParse(
                    marBytes,
                    request.Width,
                    request.Height,
                    out ushort[] mars,
                    out string parseError))
                {
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.ParseFailed, parseError, processResult);
                }

                return new RandomMapGenerationResult
                {
                    Success = true,
                    ErrorCategory = RandomMapGeneratorErrorCategory.None,
                    ErrorMessage = "Random map generated successfully.",
                    Mars = mars,
                    ExitCode = processResult.ExitCode,
                    StdoutTail = Tail(processResult.Stdout),
                    StderrTail = Tail(processResult.Stderr),
                };
            }
            catch (Exception ex)
            {
                return Fail(RandomMapGeneratorErrorCategory.ParseFailed,
                    "Unable to complete FEMapCreator random-map generation: " + ex.Message);
            }
            finally
            {
                CleanupTempDirectory(tempDir);
            }
        }

        static bool TryValidateRequest(
            RandomMapGenerationRequest request,
            out string normalizedAssetsDir,
            out string error)
        {
            normalizedAssetsDir = "";
            error = "";

            if (request == null)
            {
                error = "Random map generation request is null.";
                return false;
            }
            if (request.Width <= 0 || request.Height <= 0)
            {
                error = $"Random map generation dimensions must be positive; got {request.Width}x{request.Height}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.TilesetName))
            {
                error = "Random map generation tileset name is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.Algorithm))
            {
                error = "Random map generation algorithm is required.";
                return false;
            }

            if (!FEMapCreatorLauncherCore.TryNormalizeAssetsDirectory(
                request.AssetsDir,
                out normalizedAssetsDir,
                out error))
            {
                return false;
            }

            return true;
        }

        static IEnumerable<string> BuildGenerateArguments(
            RandomMapGenerationRequest request,
            string normalizedAssetsDir)
        {
            var args = new List<string>
            {
                "generate",
                "--width",
                request.Width.ToString(),
                "--height",
                request.Height.ToString(),
                "--tileset",
                request.TilesetName,
                "--algorithm",
                request.Algorithm,
                "--seed",
                request.Seed.ToString(),
                "--output",
                request.OutputMarPath,
                "--format",
                "mar",
                "--require-complete",
                "--force",
            };
            if (!string.IsNullOrEmpty(normalizedAssetsDir))
            {
                args.Add("--assets-dir");
                args.Add(normalizedAssetsDir);
            }
            return args;
        }

        static string CreateTempWorkingDirectory()
        {
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                TempDirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        static void CleanupTempDirectory(string tempDir)
        {
            if (string.IsNullOrWhiteSpace(tempDir))
                return;

            Exception lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!Directory.Exists(tempDir))
                        return;
                    Directory.Delete(tempDir, recursive: true);
                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                Thread.Sleep(50);
            }

            if (lastError != null)
                Log.Error($"Random-map temp-directory cleanup failed for '{tempDir}': {lastError}");
        }

        static RandomMapGenerationResult Fail(
            RandomMapGeneratorErrorCategory category,
            string message)
        {
            return new RandomMapGenerationResult
            {
                Success = false,
                ErrorCategory = category,
                ErrorMessage = message ?? "",
            };
        }

        static RandomMapGenerationResult FailFromProcessResult(
            RandomMapGeneratorErrorCategory category,
            string message,
            ProcessRunResult processResult)
        {
            return new RandomMapGenerationResult
            {
                Success = false,
                ErrorCategory = category,
                ErrorMessage = message ?? "",
                ExitCode = processResult.ExitCode,
                StdoutTail = Tail(processResult.Stdout),
                StderrTail = Tail(processResult.Stderr),
            };
        }

        static RandomMapGenerationResult FailForNotStarted(
            FEMapCreatorLauncherCore.FEMapCreatorLaunchSpec spec,
            ProcessRunResult processResult)
        {
            RandomMapGeneratorErrorCategory category = spec.UsesManagedHost
                ? RandomMapGeneratorErrorCategory.HostUnavailable
                : RandomMapGeneratorErrorCategory.ProcessStartFailed;
            string message = spec.UsesManagedHost
                ? "Unable to start the .NET host for FEMapCreator: " + processResult.ErrorMessage
                : "Unable to start FEMapCreator: " + processResult.ErrorMessage;
            return FailFromProcessResult(category, message, processResult);
        }

        static string AppendTerminationFailure(string baseMessage, ProcessRunResult processResult)
        {
            if (!processResult.TerminationFailed)
                return baseMessage;
            return baseMessage + " Process termination cleanup failed.";
        }

        static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            if (text.Length <= DiagnosticTailChars)
                return text;
            return text.Substring(text.Length - DiagnosticTailChars, DiagnosticTailChars);
        }
    }
}
