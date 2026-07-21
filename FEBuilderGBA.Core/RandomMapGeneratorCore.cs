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
        public const int MinimumDimension = 1;
        public const int MaximumDimension = 64;

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
        /// <param name="cancellationToken">
        /// Checked immediately before launching and immediately after the process call
        /// returns/finishes, so a signalled token always yields
        /// <see cref="RandomMapGeneratorErrorCategory.Cancelled"/> and never a stale success.
        /// When <paramref name="cancellableRunner"/> is not supplied and the caller also did not
        /// override <paramref name="runner"/>, this call automatically uses the cancellation-aware
        /// <see cref="ProcessRunnerCore.Run(string, System.Collections.Generic.IEnumerable{string}, string, int, int, CancellationToken)"/>
        /// overload, which owns and terminates its own external process on cancellation instead
        /// of merely abandoning an awaited Task.
        /// </param>
        /// <param name="cancellableRunner">
        /// Optional cancellation-aware process-runner override for tests. Takes precedence over
        /// <paramref name="runner"/> when both are supplied.
        /// </param>
        public static RandomMapGenerationResult Generate(
            RandomMapGenerationRequest request,
            ProcessRunnerDelegate runner = null,
            CancellationToken cancellationToken = default,
            ProcessRunnerCancellableDelegate cancellableRunner = null)
        {
            return GenerateCore(
                request,
                runner,
                cancellationToken,
                cancellableRunner,
                beforeProcessLaunch: null);
        }

        internal static RandomMapGenerationResult GenerateWithPreLaunchHook(
            RandomMapGenerationRequest request,
            ProcessRunnerDelegate runner,
            CancellationToken cancellationToken,
            ProcessRunnerCancellableDelegate cancellableRunner,
            Action beforeProcessLaunch)
        {
            return GenerateCore(
                request,
                runner,
                cancellationToken,
                cancellableRunner,
                beforeProcessLaunch);
        }

        static RandomMapGenerationResult GenerateCore(
            RandomMapGenerationRequest request,
            ProcessRunnerDelegate runner,
            CancellationToken cancellationToken,
            ProcessRunnerCancellableDelegate cancellableRunner,
            Action beforeProcessLaunch)
        {
            if (cancellationToken.IsCancellationRequested)
                return Fail(RandomMapGeneratorErrorCategory.Cancelled, "Random map generation was cancelled.");

            if (cancellableRunner == null && runner == null)
                cancellableRunner = ProcessRunnerCore.Run;

            if (!TryValidateRequest(
                request,
                out string normalizedAssetsDir,
                out string normalizedAlgorithm,
                out string error))
                return Fail(RandomMapGeneratorErrorCategory.InvalidPath, error);

            string tempDir = "";
            try
            {
                tempDir = CreateTempWorkingDirectory();
                string outputMarPath = Path.Combine(tempDir, OutputFileName);

                FEMapCreatorLauncherCore.FEMapCreatorLaunchSpec spec =
                    FEMapCreatorLauncherCore.CreateLaunchSpec(
                        request.FEMapCreatorPath,
                        BuildGenerateArguments(
                            request,
                            normalizedAssetsDir,
                            normalizedAlgorithm,
                            outputMarPath));
                if (!spec.Success)
                    return Fail(spec.ErrorCategory, spec.ErrorMessage);

                beforeProcessLaunch?.Invoke();
                if (cancellationToken.IsCancellationRequested)
                    return Fail(RandomMapGeneratorErrorCategory.Cancelled, "Random map generation was cancelled.");

                ProcessRunResult processResult;
                if (cancellableRunner != null)
                {
                    processResult = cancellableRunner(
                        spec.Command,
                        spec.Arguments,
                        tempDir,
                        ProcessTimeoutMs,
                        MaximumOutputChars,
                        cancellationToken);
                }
                else
                {
                    processResult = runner(
                        spec.Command,
                        spec.Arguments,
                        tempDir,
                        ProcessTimeoutMs,
                        MaximumOutputChars);
                }
                if (processResult.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    return Fail(RandomMapGeneratorErrorCategory.Cancelled,
                        "Random map generation was cancelled; the result was discarded.");
                }
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
                        FEMapCreatorProcessDiagnosticCore.AppendNonZeroExitDetail(
                            "FEMapCreator random-map generation exited with code "
                                + processResult.ExitCode + ".",
                            processResult),
                        processResult);
                if (!File.Exists(outputMarPath))
                    return FailFromProcessResult(RandomMapGeneratorErrorCategory.OutputMissing,
                        "FEMapCreator reported success but did not produce the expected MAR file: " + outputMarPath,
                        processResult);

                int expectedLength = request.Width * request.Height * 2;
                if (!TryReadExactMar(
                    outputMarPath,
                    expectedLength,
                    out byte[] marBytes,
                    out string readError))
                {
                    return FailFromProcessResult(
                        RandomMapGeneratorErrorCategory.ParseFailed,
                        readError,
                        processResult);
                }

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
            catch (OperationCanceledException ex) when (
                cancellationToken.IsCancellationRequested ||
                (cancellationToken.CanBeCanceled && ex.CancellationToken == cancellationToken))
            {
                return Fail(RandomMapGeneratorErrorCategory.Cancelled,
                    "Random map generation was cancelled.");
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

        static bool TryReadExactMar(
            string outputMarPath,
            int expectedLength,
            out byte[] marBytes,
            out string error)
        {
            marBytes = Array.Empty<byte>();
            error = "";

            using var stream = new FileStream(
                outputMarPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            long initialLength = stream.Length;
            if (initialLength != expectedLength)
            {
                error =
                    $"Generated MAR length mismatch: expected {expectedLength} bytes but got {initialLength}.";
                return false;
            }

            var buffer = new byte[expectedLength];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    error =
                        $"Generated MAR length mismatch: expected {expectedLength} bytes but reached EOF after {offset}.";
                    return false;
                }
                offset += read;
            }

            if (stream.ReadByte() != -1)
            {
                error =
                    $"Generated MAR length mismatch: expected {expectedLength} bytes but the file contains trailing data.";
                return false;
            }

            marBytes = buffer;
            return true;
        }

        static bool TryValidateRequest(
            RandomMapGenerationRequest request,
            out string normalizedAssetsDir,
            out string normalizedAlgorithm,
            out string error)
        {
            normalizedAssetsDir = "";
            normalizedAlgorithm = "";
            error = "";

            if (request == null)
            {
                error = "Random map generation request is null.";
                return false;
            }
            if (request.Width < MinimumDimension || request.Width > MaximumDimension
                || request.Height < MinimumDimension || request.Height > MaximumDimension)
            {
                error = $"Random map generation dimensions must be in the range {MinimumDimension}..{MaximumDimension}; got {request.Width}x{request.Height}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.TilesetName))
            {
                error = "Random map generation tileset name is required.";
                return false;
            }
            if (!RandomMapGeneratorAlgorithms.TryNormalize(
                request.Algorithm, out normalizedAlgorithm))
            {
                error = "Random map generation algorithm must be one of: "
                    + string.Join(", ", RandomMapGeneratorAlgorithms.All) + ".";
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
            string normalizedAssetsDir,
            string normalizedAlgorithm,
            string outputMarPath)
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
                normalizedAlgorithm,
                "--seed",
                request.Seed.ToString(),
                "--output",
                outputMarPath,
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
