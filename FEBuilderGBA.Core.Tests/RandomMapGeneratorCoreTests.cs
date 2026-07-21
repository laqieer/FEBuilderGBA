using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class RandomMapGeneratorCoreTests
    {
        sealed class RecordedCall
        {
            public string Command = "";
            public List<string> Arguments = new List<string>();
            public string WorkingDirectory = "";
            public int TimeoutMs;
            public int MaximumOutputChars;
        }

        [Fact]
        public void Generate_UsesNativeExecutableAndExpectedArguments()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = new RandomMapGenerationRequest
                {
                    Width = 15,
                    Height = 10,
                    TilesetName = "Grassland",
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = 42,
                    FEMapCreatorPath = femapCreatorPath,
                };

                var call = new RecordedCall();
                bool sawCall = false;
                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        sawCall = true;
                        call = Record(command, args, workingDir, timeoutMs, maximumOutputChars);
                        WriteRawMar(FindArgumentValue(call.Arguments, "--output"),
                            request.Width * request.Height,
                            rawValueFactory: _ => 0);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "ok",
                            Stderr = "",
                        };
                    });

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(sawCall);
                Assert.Equal(femapCreatorPath, call.Command);
                Assert.Equal(120000, call.TimeoutMs);
                Assert.Equal(1000000, call.MaximumOutputChars);
                Assert.Equal(new[]
                {
                    "generate",
                    "--width", "15",
                    "--height", "10",
                    "--tileset", "Grassland",
                    "--algorithm", RandomMapGeneratorAlgorithms.Default,
                    "--seed", "42",
                    "--output", FindArgumentValue(call.Arguments, "--output"),
                    "--format", "mar",
                    "--require-complete",
                    "--force",
                }, call.Arguments);
                Assert.DoesNotContain("--json", call.Arguments);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_UsesExecutableUnixProgramWithDottedName()
        {
            if (OperatingSystem.IsWindows())
                return;

            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FE_Map_Creator.Cli");
                var request = CreateValidRequest(femapCreatorPath);
                int calls = 0;

                RandomMapGenerationResult executableResult = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        calls++;
                        Assert.Equal(femapCreatorPath, command);
                        WriteRawMar(FindArgumentValue(args, "--output"), 4, _ => 0);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "",
                            Stderr = "",
                        };
                    });

                Assert.True(executableResult.Success, executableResult.ErrorMessage);
                Assert.Equal(1, calls);

                File.SetUnixFileMode(
                    femapCreatorPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.OtherExecute);
                RandomMapGenerationResult nonExecutableResult = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        calls++;
                        return ProcessRunResult.NotStarted("must not run");
                    });

                Assert.False(nonExecutableResult.Success);
                Assert.Equal(
                    RandomMapGeneratorErrorCategory.InvalidPath,
                    nonExecutableResult.ErrorCategory);
                Assert.Contains(
                    "not executable",
                    nonExecutableResult.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, calls);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_AppendsAssetsDirAfterForce()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsDir = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsDir);
                var request = new RandomMapGenerationRequest
                {
                    Width = 2,
                    Height = 2,
                    TilesetName = "Grassland",
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = 42,
                    FEMapCreatorPath = femapCreatorPath,
                    AssetsDir = assetsDir,
                };

                var call = new RecordedCall();
                bool sawCall = false;
                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        sawCall = true;
                        call = Record(command, args, workingDir, timeoutMs, maximumOutputChars);
                        WriteRawMar(FindArgumentValue(call.Arguments, "--output"), 4, _ => 0);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "",
                            Stderr = "",
                        };
                    });

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(sawCall);
                Assert.Equal(new[]
                {
                    "generate",
                    "--width", "2",
                    "--height", "2",
                    "--tileset", "Grassland",
                    "--algorithm", RandomMapGeneratorAlgorithms.Default,
                    "--seed", "42",
                    "--output", FindArgumentValue(call.Arguments, "--output"),
                    "--format", "mar",
                    "--require-complete",
                    "--force",
                    "--assets-dir", assetsDir,
                }, call.Arguments);
                Assert.DoesNotContain("--json", call.Arguments);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_RejectsUrlRelativeBareMissingAndUnsupportedPaths()
        {
            RandomMapGenerationResult urlResult = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
            {
                Width = 1,
                Height = 1,
                TilesetName = "Grassland",
                Algorithm = RandomMapGeneratorAlgorithms.Default,
                Seed = 1,
                FEMapCreatorPath = "https://example.com/femapcreator.exe",
            });
            Assert.False(urlResult.Success);
            Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, urlResult.ErrorCategory);

            RandomMapGenerationResult fileUriResult = RandomMapGeneratorCore.Generate(
                new RandomMapGenerationRequest
                {
                    Width = 1,
                    Height = 1,
                    TilesetName = "Grassland",
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = 1,
                    FEMapCreatorPath = "file:///tmp/femapcreator.exe",
                });
            Assert.False(fileUriResult.Success);
            Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, fileUriResult.ErrorCategory);

            foreach (string notFullyQualified in new[] { @"C:tool.exe", @"\tool.exe" })
            {
                RandomMapGenerationResult rootedButRelative = RandomMapGeneratorCore.Generate(
                    new RandomMapGenerationRequest
                    {
                        Width = 1,
                        Height = 1,
                        TilesetName = "Grassland",
                        Algorithm = RandomMapGeneratorAlgorithms.Default,
                        Seed = 1,
                        FEMapCreatorPath = notFullyQualified,
                    });
                Assert.False(rootedButRelative.Success);
                Assert.Equal(
                    RandomMapGeneratorErrorCategory.InvalidPath,
                    rootedButRelative.ErrorCategory);
            }

            RandomMapGenerationResult relativeResult = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
            {
                Width = 1,
                Height = 1,
                TilesetName = "Grassland",
                Algorithm = RandomMapGeneratorAlgorithms.Default,
                Seed = 1,
                FEMapCreatorPath = "tools\\femapcreator.exe",
            });
            Assert.False(relativeResult.Success);
            Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, relativeResult.ErrorCategory);

            RandomMapGenerationResult bareResult = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
            {
                Width = 1,
                Height = 1,
                TilesetName = "Grassland",
                Algorithm = RandomMapGeneratorAlgorithms.Default,
                Seed = 1,
                FEMapCreatorPath = "femapcreator.exe",
            });
            Assert.False(bareResult.Success);
            Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, bareResult.ErrorCategory);

            string tempRoot = CreateTempDirectory();
            try
            {
                string missingPath = Path.Combine(tempRoot, "missing.exe");
                RandomMapGenerationResult missingResult = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
                {
                    Width = 1,
                    Height = 1,
                    TilesetName = "Grassland",
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = 1,
                    FEMapCreatorPath = missingPath,
                });
                Assert.False(missingResult.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, missingResult.ErrorCategory);

                string unsupportedPath = CreateEmptyFile(tempRoot, "femapcreator.py");
                RandomMapGenerationResult unsupportedResult = RandomMapGeneratorCore.Generate(new RandomMapGenerationRequest
                {
                    Width = 1,
                    Height = 1,
                    TilesetName = "Grassland",
                    Algorithm = RandomMapGeneratorAlgorithms.Default,
                    Seed = 1,
                    FEMapCreatorPath = unsupportedPath,
                });
                Assert.False(unsupportedResult.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, unsupportedResult.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_RejectsUnsupportedAlgorithmAndOversizedDimensionsBeforeSpawn()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                int calls = 0;
                ProcessRunnerDelegate runner = (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                {
                    calls++;
                    return ProcessRunResult.NotStarted("must not run");
                };

                RandomMapGenerationResult badAlgorithm = RandomMapGeneratorCore.Generate(
                    new RandomMapGenerationRequest
                    {
                        Width = 1,
                        Height = 1,
                        TilesetName = "Grassland",
                        Algorithm = "cellular",
                        Seed = 1,
                        FEMapCreatorPath = femapCreatorPath,
                    },
                    runner);
                Assert.False(badAlgorithm.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, badAlgorithm.ErrorCategory);
                Assert.Contains("experimental", badAlgorithm.ErrorMessage);

                RandomMapGenerationResult oversized = RandomMapGeneratorCore.Generate(
                    new RandomMapGenerationRequest
                    {
                        Width = RandomMapGeneratorCore.MaximumDimension + 1,
                        Height = 1,
                        TilesetName = "Grassland",
                        Algorithm = RandomMapGeneratorAlgorithms.Default,
                        Seed = 1,
                        FEMapCreatorPath = femapCreatorPath,
                    },
                    runner);
                Assert.False(oversized.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.InvalidPath, oversized.ErrorCategory);
                Assert.Equal(0, calls);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_DeletesTempDirectoryAfterSuccess()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                string observedWorkingDirectory = "";

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        observedWorkingDirectory = workingDir;
                        Assert.StartsWith("FEBuilderGBA-mapgen-", Path.GetFileName(workingDir), StringComparison.Ordinal);
                        Assert.True(Directory.Exists(workingDir));
                        File.WriteAllText(Path.Combine(workingDir, "marker.txt"), "marker");
                        WriteRawMar(FindArgumentValue(args, "--output"), 4, _ => 0);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "",
                            Stderr = "",
                        };
                    });

                Assert.True(result.Success, result.ErrorMessage);
                Assert.False(string.IsNullOrWhiteSpace(observedWorkingDirectory));
                Assert.False(Directory.Exists(observedWorkingDirectory));
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_DeletesTempDirectoryAfterFailure()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                string observedWorkingDirectory = "";

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        observedWorkingDirectory = workingDir;
                        Assert.StartsWith("FEBuilderGBA-mapgen-", Path.GetFileName(workingDir), StringComparison.Ordinal);
                        File.WriteAllText(Path.Combine(workingDir, "marker.txt"), "marker");
                        return ProcessRunResult.NotStarted("boom");
                    });

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.ProcessStartFailed, result.ErrorCategory);
                Assert.False(Directory.Exists(observedWorkingDirectory));
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_MapsProcessFailureCategories()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);

                RandomMapGenerationResult notStarted = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                        ProcessRunResult.NotStarted("start failed"));
                Assert.Equal(RandomMapGeneratorErrorCategory.ProcessStartFailed, notStarted.ErrorCategory);

                RandomMapGenerationResult timedOut = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        TimedOut = true,
                        ExitCode = -1,
                        Stdout = "",
                        Stderr = "",
                    });
                Assert.Equal(RandomMapGeneratorErrorCategory.TimedOut, timedOut.ErrorCategory);

                RandomMapGenerationResult outputLimit = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        OutputLimitExceeded = true,
                        ExitCode = -1,
                        Stdout = "",
                        Stderr = "",
                    });
                Assert.Equal(RandomMapGeneratorErrorCategory.OutputLimitExceeded, outputLimit.ErrorCategory);

                RandomMapGenerationResult nonZero = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 9,
                        Stdout = "",
                        Stderr = "generation detail",
                    });
                Assert.Equal(RandomMapGeneratorErrorCategory.NonZeroExit, nonZero.ErrorCategory);
                Assert.Contains("generation detail", nonZero.ErrorMessage);

                RandomMapGenerationResult captureFailure = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = -1,
                        Stdout = "",
                        Stderr = "",
                        ErrorMessage = "process output capture failed",
                    });
                Assert.Equal(
                    RandomMapGeneratorErrorCategory.NonZeroExit,
                    captureFailure.ErrorCategory);
                Assert.Contains(
                    "process output capture failed",
                    captureFailure.ErrorMessage);

                RandomMapGenerationResult missingOutput = RandomMapGeneratorCore.Generate(
                    CreateValidRequest(femapCreatorPath),
                    (command, args, workingDir, timeoutMs, maximumOutputChars) => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = "",
                        Stderr = "",
                    });
                Assert.Equal(RandomMapGeneratorErrorCategory.OutputMissing, missingOutput.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_ReturnsParseFailedForMalformedMar()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        File.WriteAllBytes(FindArgumentValue(args, "--output"), new byte[] { 1, 2, 3, 4, 5, 6 });
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "",
                            Stderr = "",
                        };
                    });

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.ParseFailed, result.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_RejectsMarWithTrailingData()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        File.WriteAllBytes(
                            FindArgumentValue(args, "--output"),
                            new byte[request.Width * request.Height * 2 + 1]);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "",
                            Stderr = "",
                        };
                    });

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.ParseFailed, result.ErrorCategory);
                Assert.Contains("length mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Parse_ConvertsGoldenVectorsThroughChipsetIndexPipeline()
        {
            byte[] raw = BuildRawMarBytes(new short[] { 0, 32, 992, 1024, 32736 });

            bool ok = RandomMapGeneratorMarParserCore.TryParse(raw, 5, 1, out ushort[] mars, out string error);

            Assert.True(ok, error);
            Assert.Equal(new ushort[] { 0, 4, 124, 128, 4092 }, mars);
        }

        [Fact]
        public void Parse_RejectsNegativeValue()
        {
            byte[] raw = BuildRawMarBytes(new short[] { -32 });

            bool ok = RandomMapGeneratorMarParserCore.TryParse(raw, 1, 1, out ushort[] _, out string error);

            Assert.False(ok);
            Assert.Contains("negative", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_RejectsNonMultipleOf32()
        {
            byte[] raw = BuildRawMarBytes(new short[] { 31 });

            bool ok = RandomMapGeneratorMarParserCore.TryParse(raw, 1, 1, out ushort[] _, out string error);

            Assert.False(ok);
            Assert.Contains("divisible by 32", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_RejectsIndexOutsideConfiguredChipsetCount()
        {
            byte[] raw = BuildRawMarBytes(new short[] { 256 });

            bool ok = RandomMapGeneratorMarParserCore.TryParse(raw, 1, 1, 8, out ushort[] _, out string error);

            Assert.False(ok);
            Assert.Contains("chipset index 8", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_RejectsWrongLength()
        {
            byte[] raw = BuildRawMarBytes(new short[] { 0, 32, 64 });

            bool ok = RandomMapGeneratorMarParserCore.TryParse(raw, 1, 1, out ushort[] _, out string error);

            Assert.False(ok);
            Assert.Contains("length mismatch", error, StringComparison.OrdinalIgnoreCase);
        }

        // #1978 Slice 3 review finding #1: an already-signalled token must short-circuit before
        // any process launch, and never surface a success even if a fake runner returns one.
        [Fact]
        public void Generate_PreCancelledToken_ReturnsCancelledWithoutLaunching()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                bool sawCall = false;
                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        sawCall = true;
                        return new ProcessRunResult { Started = true, ExitCode = 0 };
                    },
                    cts.Token);

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.Cancelled, result.ErrorCategory);
                Assert.False(sawCall, "Generate must not launch the process once the token is already cancelled.");
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_CancelledAfterSetupBeforeRunner_DoesNotLaunch()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                using var cts = new CancellationTokenSource();
                bool sawCall = false;

                RandomMapGenerationResult result = RandomMapGeneratorCore.GenerateWithPreLaunchHook(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        sawCall = true;
                        return new ProcessRunResult { Started = true, ExitCode = 0 };
                    },
                    cts.Token,
                    cancellableRunner: null,
                    beforeProcessLaunch: cts.Cancel);

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.Cancelled, result.ErrorCategory);
                Assert.False(sawCall, "Generate must re-check cancellation after setup and immediately before runner invocation.");
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        // A late-arriving successful result from a custom (non-cancellable) fake runner must
        // still be discarded once the token is observed cancelled immediately afterwards.
        [Fact]
        public void Generate_TokenCancelledAfterSyncRunnerReturnsSuccess_DiscardsLateResult()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                using var cts = new CancellationTokenSource();

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    (command, args, workingDir, timeoutMs, maximumOutputChars) =>
                    {
                        WriteRawMar(FindArgumentValue(args, "--output"), request.Width * request.Height, _ => 0);
                        // Simulate cancellation being requested while the (fake) external
                        // process was running, observed only once the call returns.
                        cts.Cancel();
                        return new ProcessRunResult { Started = true, ExitCode = 0, Stdout = "ok" };
                    },
                    cts.Token);

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.Cancelled, result.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        // The default production path (no runner/cancellableRunner override) must route through
        // the cancellation-aware ProcessRunnerCore.Run overload automatically.
        [Fact]
        public void Generate_CancellableRunnerOverride_ReportsCancelledFromProcessResult()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                bool sawCancellableCall = false;

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    runner: null,
                    cancellationToken: default,
                    cancellableRunner: (command, args, workingDir, timeoutMs, maximumOutputChars, token) =>
                    {
                        sawCancellableCall = true;
                        return new ProcessRunResult { Started = true, Cancelled = true, ExitCode = -1 };
                    });

                Assert.True(sawCancellableCall);
                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.Cancelled, result.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Generate_CancellableRunnerThrowsAssociatedCancellation_ReportsCancelled()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var request = CreateValidRequest(femapCreatorPath);
                using var cts = new CancellationTokenSource();

                RandomMapGenerationResult result = RandomMapGeneratorCore.Generate(
                    request,
                    runner: null,
                    cancellationToken: cts.Token,
                    cancellableRunner: (command, args, workingDir, timeoutMs, maximumOutputChars, token) =>
                    {
                        cts.Cancel();
                        throw new OperationCanceledException(token);
                    });

                Assert.False(result.Success);
                Assert.Equal(RandomMapGeneratorErrorCategory.Cancelled, result.ErrorCategory);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        static RandomMapGenerationRequest CreateValidRequest(string femapCreatorPath)
        {
            return new RandomMapGenerationRequest
            {
                Width = 2,
                Height = 2,
                TilesetName = "Grassland",
                Algorithm = RandomMapGeneratorAlgorithms.Default,
                Seed = 42,
                FEMapCreatorPath = femapCreatorPath,
            };
        }

        static RecordedCall Record(
            string command,
            IEnumerable<string> args,
            string workingDir,
            int timeoutMs,
            int maximumOutputChars)
        {
            var call = new RecordedCall
            {
                Command = command,
                WorkingDirectory = workingDir,
                TimeoutMs = timeoutMs,
                MaximumOutputChars = maximumOutputChars,
            };
            call.Arguments.AddRange(args);
            return call;
        }

        static string FindArgumentValue(IEnumerable<string> args, string name)
        {
            string previous = "";
            foreach (string arg in args)
            {
                if (previous == name)
                    return arg;
                previous = arg;
            }
            return "";
        }

        static void WriteRawMar(string outputPath, int entryCount, Func<int, short> rawValueFactory)
        {
            var bytes = new byte[entryCount * 2];
            for (int i = 0; i < entryCount; i++)
            {
                short rawValue = rawValueFactory(i);
                bytes[i * 2] = (byte)(rawValue & 0xFF);
                bytes[i * 2 + 1] = (byte)((rawValue >> 8) & 0xFF);
            }
            File.WriteAllBytes(outputPath, bytes);
        }

        static byte[] BuildRawMarBytes(IReadOnlyList<short> rawValues)
        {
            var bytes = new byte[rawValues.Count * 2];
            for (int i = 0; i < rawValues.Count; i++)
            {
                short rawValue = rawValues[i];
                bytes[i * 2] = (byte)(rawValue & 0xFF);
                bytes[i * 2 + 1] = (byte)((rawValue >> 8) & 0xFF);
            }
            return bytes;
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "febuildergba-randommap-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateEmptyFile(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "");
            if (!OperatingSystem.IsWindows()
                && !fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                File.SetUnixFileMode(
                    path,
                    File.GetUnixFileMode(path)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
            }
            return path;
        }

        static void DeleteDirectoryIfPresent(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
