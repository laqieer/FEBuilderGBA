using System.Text;
using Xunit;
using Xunit.Sdk;
using FixtureMode = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.FixtureMode;
using FixtureStage = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.FixtureStage;
using FixtureStageStatus = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.FixtureStageStatus;
using FixtureStageObservation = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.FixtureStageObservation;
using OwnedDirectoryIdentity = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.OwnedDirectoryIdentity;
using OwnedEntryType = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.OwnedEntryType;
using OwnedCleanupCheckpoint = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.OwnedCleanupCheckpoint;
using OwnedFixtureDirectory = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.OwnedFixtureDirectory;
using OwnedIdentityMismatchException = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.OwnedIdentityMismatchException;
using IOwnedDirectoryHandle = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.IOwnedDirectoryHandle;
using IOwnedDirectoryOperations = FEBuilderGBA.Core.Tests.CompilerSmokeDiagnostics.IOwnedDirectoryOperations;

namespace FEBuilderGBA.Core.Tests;

public class CompilerSmokeDiagnosticsTests
{
    readonly Xunit.Abstractions.ITestOutputHelper output;

    public CompilerSmokeDiagnosticsTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData(":first::second:", false)]
    [InlineData(";\"first\";;\"second\";", true)]
    public void Resolver_DotNetPathParsingContractIsIndependentlyTestable(string searchPath, bool windows)
    {
        Assert.Equal(new[] { "first", "second" },
            CompilerSmokeDiagnostics.ParseSearchPath(searchPath, windows));
        Assert.Empty(CompilerSmokeDiagnostics.ParseSearchPath(null, windows));
        Assert.Empty(CompilerSmokeDiagnostics.ParseSearchPath("", windows));
    }

    [Fact]
    public void Resolver_PosixPreservesDotNetPrefixesAndSkipsEmptyPathEntries()
    {
        string root = CreateRoot();
        try
        {
            string processDirectory = Path.Combine(root, "process");
            string currentDirectory = Path.Combine(root, "current");
            string pathDirectory = Path.Combine(root, "path");
            foreach (string directory in new[] { processDirectory, currentDirectory, pathDirectory })
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "driver"), "not executed");
            }
            Assert.Equal(Path.Combine(processDirectory, "driver"), CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { processDirectory, currentDirectory }, new[] { "", pathDirectory, "" }, false, _ => true));
            File.Delete(Path.Combine(processDirectory, "driver"));
            Assert.Equal(Path.Combine(currentDirectory, "driver"), CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { processDirectory, currentDirectory }, new[] { "", pathDirectory, "" }, false, _ => true));
            File.Delete(Path.Combine(currentDirectory, "driver"));
            Assert.Equal(Path.Combine(pathDirectory, "driver"), CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { processDirectory, currentDirectory }, new[] { "", pathDirectory, "" }, false, _ => true));
            Assert.Null(CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { "", "" }, false, _ => true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolver_KnownCandidateLookupDoesNotRequireDirectoryListing()
    {
        var method = typeof(CompilerSmokeDiagnostics).GetMethod("FindDriver",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        byte[] body = method.GetMethodBody()!.GetILAsByteArray()!;
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields()
            .Where(field => field.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(field => (System.Reflection.Emit.OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        for (int position = 0; position < body.Length;)
        {
            ushort code = body[position++];
            if (code == 0xfe) code = (ushort)(0xfe00 | body[position++]);
            var opcode = opcodes[code];
            if (opcode.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(body, position));
                Assert.False(called?.DeclaringType == typeof(Directory)
                    && called.Name == nameof(Directory.GetFileSystemEntries),
                    "Known-filename compiler search must not require directory listing access.");
                Assert.False(called?.DeclaringType == typeof(File) && called.Name == nameof(File.Exists),
                    "Permission/I/O errors must not be silently converted to absence.");
            }
            position += opcode.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or
                System.Reflection.Emit.OperandType.ShortInlineI or
                System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineI8 or
                System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, position),
                _ => 4,
            };
        }
    }

    [Fact]
    public void Resolver_PreciseMetadataWorksWithoutListAccessAndPreservesErrors()
    {
        string directory = Path.GetFullPath("non-listable-fixture");
        string candidate = Path.Combine(directory, "driver.exe");
        int calls = 0;
        Assert.Equal(candidate, CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe", path =>
        {
            calls++;
            Assert.Equal(candidate, path);
            return FileAttributes.Normal;
        }));
        Assert.Equal(1, calls);
        Assert.Null(CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe",
            _ => throw new FileNotFoundException("candidate absent")));
        Assert.Null(CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe",
            _ => throw new DirectoryNotFoundException("stale search directory")));
        var denied = new UnauthorizedAccessException("actual candidate access denied");
        Assert.Same(denied, Assert.Throws<UnauthorizedAccessException>(() =>
            CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe", _ => throw denied)));
        var ioError = new IOException("actual candidate metadata failure");
        Assert.Same(ioError, Assert.Throws<IOException>(() =>
            CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe", _ => throw ioError)));
        Assert.Null(CompilerSmokeDiagnostics.FindDriver(directory, "driver.exe", _ => FileAttributes.Directory));
    }

    [SkippableFact]
    public void Resolver_WindowsKnownNameLookupIsCaseInsensitive()
    {
        Skip.If(!OperatingSystem.IsWindows(), "Windows filename case behavior fixture.");
        string root = CreateRoot();
        try
        {
            string driver = Path.Combine(root, "DRIVER.EXE");
            File.WriteAllText(driver, "not executed");
            Assert.Equal(Path.Combine(root, "driver.exe"), CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { root }, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryReport_PreservesBoundedCompilerStreams(bool signalReport)
    {
        string root = CreateRoot();
        try
        {
            string stdout = "\"stdout\"\n" + new string('x', 20_000);
            string stderr = (signalReport ? "clang frontend command failed due to signal\n" : "")
                + "primary compiler diagnostic\n" + new string('y', 20_000);
            var failure = Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Compile(
                new("fake", "version"), root, "case", "private generated source",
                Array.Empty<string>(), (_, _, _, _, _) => new ProcessRunResult
                {
                    Started = true, ExitCode = 42, Stdout = stdout, Stderr = stderr, ErrorMessage = "",
                }));
            using var json = System.Text.Json.JsonDocument.Parse(failure.Message[failure.Message.IndexOf('{')..]);
            Assert.Equal(stdout[..16_384], json.RootElement.GetProperty("stdout").GetString());
            Assert.Equal(stderr[..16_384], json.RootElement.GetProperty("stderr").GetString());
            Assert.Equal(42, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.DoesNotContain("private generated source", failure.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolver_ReturnsAbsoluteDriverAndSkipsMissingDirectories()
    {
        string root = CreateRoot();
        try
        {
            string driver = Path.Combine(root, "driver");
            File.WriteAllText(driver, "not executed");
            string? resolved = CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { Path.Combine(root, "missing"), root }, false, _ => true);
            Assert.NotNull(resolved);
            Assert.Equal(driver, resolved);
            Assert.True(Path.IsPathFullyQualified(resolved));
            Assert.Null(CompilerSmokeDiagnostics.ResolveFromSearch(
                "missing-driver", new[] { root }, Array.Empty<string>(), false, _ => true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolver_PosixPrefixPreservesBrokenCurrentDirectorySelectionButPathRequiresExecuteAccess()
    {
        string root = CreateRoot();
        try
        {
            string prefix = Path.Combine(root, "current");
            string path = Path.Combine(root, "path");
            Directory.CreateDirectory(prefix);
            Directory.CreateDirectory(path);
            string prefixDriver = Path.Combine(prefix, "driver");
            string pathDriver = Path.Combine(path, "driver");
            File.WriteAllText(prefixDriver, "not executable");
            File.WriteAllText(pathDriver, "not executed");
            Assert.Equal(prefixDriver, CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { prefix }, new[] { path }, false, _ => false));
            Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { prefix, path }, false, _ => false));
            Assert.Equal(pathDriver, CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { prefix, path }, false, p => p == pathDriver));
            Assert.Throws<UnauthorizedAccessException>(() => CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", Array.Empty<string>(), new[] { path }, false,
                _ => throw new UnauthorizedAccessException("cannot verify execute access")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Resolver_WindowsUsesNativeExeSuffixNotShellExtensionsAndPreservesSearchOrder()
    {
        string root = CreateRoot();
        try
        {
            string first = Path.Combine(root, "first");
            string second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "driver.cmd"), "not executed");
            string driver = Path.Combine(second, "driver.exe");
            File.WriteAllText(driver, "not executed");
            Assert.Equal(driver, CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { first }, new[] { second }, true));
            File.WriteAllText(Path.Combine(first, "driver.exe"), "not executed");
            Assert.Equal(Path.Combine(first, "driver.exe"), CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { first }, new[] { second }, true));
            Assert.Equal(driver, CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver.exe", Array.Empty<string>(), new[] { second }, true));
            Assert.Throws<DirectoryNotFoundException>(() => File.GetAttributes(Path.Combine(driver, "driver.exe")));
            Assert.Null(CompilerSmokeDiagnostics.ResolveFromSearch(
                "driver", new[] { driver }, Array.Empty<string>(), true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SelectedDriver_PathIsReusedForProbeCompileAndReportedIdentity()
    {
        string root = CreateRoot();
        try
        {
            string driver = Path.Combine(root, "driver");
            File.WriteAllText(driver, "not executed");
            var requests = new List<string>();
            var compiler = CompilerSmokeDiagnostics.Detect(
                (executable, _, _, _, _) => { requests.Add(executable); return Success("driver version"); },
                _ => driver);
            Assert.NotNull(compiler);
            Assert.Equal("arm-none-eabi-gcc", compiler.RequestedExecutable);
            Assert.Equal(driver, compiler.LaunchPath);
            CompilerSmokeDiagnostics.Compile(compiler, root, "case", "private source", Array.Empty<string>(),
                (executable, _, _, _, _) =>
                {
                    requests.Add(executable);
                    File.WriteAllBytes(Path.Combine(root, "case.o"), new byte[] { 1 });
                    return Success();
                });
            Assert.Equal(new[] { driver, driver }, requests);
            using var json = System.Text.Json.JsonDocument.Parse(CompilerSmokeDiagnostics.Describe(
                compiler, Array.Empty<string>(), Success(), 60_000, TimeSpan.Zero, true));
            Assert.Equal(driver, json.RootElement.GetProperty("resolvedExecutableIdentity").GetString());
            Assert.Equal(driver, json.RootElement.GetProperty("launchExecutable").GetString());
            Assert.Equal("unknown", json.RootElement.GetProperty("frontendExecutableIdentity").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Probe_ResolutionErrorsAndNonAbsoluteClaimsAreExplicitWithoutExecutionOrFallback()
    {
        int calls = 0;
        ProcessRunResult Run(string _, IEnumerable<string> args, string? dir, int timeout, int cap)
        {
            calls++;
            return Success();
        }
        Assert.Throws<UnauthorizedAccessException>(() => CompilerSmokeDiagnostics.Detect(
            Run, _ => throw new UnauthorizedAccessException("resolver denied")));
        Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Detect(Run, _ => "relative-driver"));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0, 1, "", true, "compiler-nonzero")]
    [InlineData(0, 1, "clang frontend command failed due to signal", true, "compiler-reported-frontend-signal")]
    [InlineData(1, -1, "", true, "start-failure")]
    [InlineData(2, -1, "", true, "timeout")]
    [InlineData(4, -1, "", true, "output-limit")]
    [InlineData(8, -1, "", true, "cancelled")]
    [InlineData(16, -1, "", true, "termination-failure")]
    [InlineData(32, -1, "", true, "runner-error")]
    [InlineData(0, 0, "", false, "missing-object")]
    [InlineData(0, 0, "", true, "success")]
    [InlineData(30, -1, "clang frontend command failed due to signal", true, "termination-failure")]
    [InlineData(2, -1, "clang frontend command failed due to signal", true, "timeout")]
    [InlineData(32, 0, "", true, "runner-error")]
    [InlineData(4, -1, "clang frontend command failed due to signal", true, "output-limit")]
    [InlineData(8, -1, "clang frontend command failed due to signal", true, "cancelled")]
    public void Classification_DistinguishesFailureEvidence(
        int flags, int exit, string stderr, bool objectExists, string expected)
    {
        var result = new ProcessRunResult
        {
            Started = (flags & 1) == 0,
            TimedOut = (flags & 2) != 0,
            OutputLimitExceeded = (flags & 4) != 0,
            Cancelled = (flags & 8) != 0,
            TerminationFailed = (flags & 16) != 0,
            ErrorMessage = (flags & 32) != 0 ? "Process output capture did not finish." : "",
            ExitCode = exit,
            Stdout = "",
            Stderr = stderr,
        };
        Assert.Equal(expected, CompilerSmokeDiagnostics.Classify(result, objectExists));
    }

    static ProcessRunResult Success(string stdout = "") => new()
    {
        Started = true, ExitCode = 0, Stdout = stdout, Stderr = "", ErrorMessage = "",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Probe_PreservesPreferenceAndConfiguredBounds(bool armAbsent)
    {
        var calls = new List<string>();
        var resolutions = new List<string>();
        var selected = CompilerSmokeDiagnostics.Detect(
            (executable, args, directory, timeout, cap) =>
            {
                calls.Add(executable);
                Assert.Equal(new[] { "--version" }, args);
                Assert.Equal(10_000, timeout);
                Assert.Equal(16_384, cap);
                return Success("version evidence");
            },
            name =>
            {
                resolutions.Add(name);
                return armAbsent && name == "arm-none-eabi-gcc" ? null : Path.GetFullPath(name);
            });
        Assert.NotNull(selected);
        Assert.Equal(armAbsent ? "gcc" : "arm-none-eabi-gcc", selected.RequestedExecutable);
        Assert.Equal(new[] { Path.GetFullPath(armAbsent ? "gcc" : "arm-none-eabi-gcc") }, calls);
        Assert.Equal(armAbsent ? new[] { "arm-none-eabi-gcc", "gcc" } : new[] { "arm-none-eabi-gcc" }, resolutions);
        Assert.Equal("version evidence", selected.VersionEvidence);
    }

    [Fact]
    public void Probe_OnlyPositiveAbsenceMaySkip()
    {
        Assert.Null(CompilerSmokeDiagnostics.Detect(
            (_, _, _, _, _) => throw new InvalidOperationException("absent compiler must not launch"), _ => null));
        int calls = 0;
        var failure = Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Detect(
            (_, _, _, _, _) => { calls++; return ProcessRunResult.NotStarted("permission denied"); },
            _ => Path.GetFullPath("driver")));
        Assert.Contains("start-failure", failure.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Probe_UnrelatedStartFailureCannotBecomeAbsenceSkip()
    {
        Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Detect(
            (_, _, _, _, _) => ProcessRunResult.NotStarted("Working directory does not exist"), _ => Path.GetFullPath("driver")));
    }

    [Fact]
    public void Absence_UsesExactMetadataAndTreatsBrokenFileAsPresent()
    {
        string root = CreateRoot();
        try
        {
            string missing = Path.Combine(root, "missing-directory");
            Assert.Null(CompilerSmokeDiagnostics.ResolveFromSearch("fake-compiler", new[] { missing, root },
                Array.Empty<string>(), OperatingSystem.IsWindows()));
            string executable = Path.Combine(root, OperatingSystem.IsWindows() ? "fake-compiler.exe" : "fake-compiler");
            File.WriteAllText(executable, "not executable");
            Assert.Equal(executable, CompilerSmokeDiagnostics.ResolveFromSearch("fake-compiler", new[] { root },
                Array.Empty<string>(), OperatingSystem.IsWindows()));
            string candidateName = OperatingSystem.IsWindows() ? "fake-compiler.exe" : "fake-compiler";
            Assert.Throws<DirectoryNotFoundException>(() => File.GetAttributes(Path.Combine(executable, candidateName)));
            Assert.Null(CompilerSmokeDiagnostics.ResolveFromSearch("fake-compiler", new[] { executable },
                Array.Empty<string>(), OperatingSystem.IsWindows()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("overflow")]
    [InlineData("nonzero")]
    [InlineData("capture")]
    [InlineData("termination")]
    public void Probe_BrokenOrNoisyCompilerFailsWithoutFallback(string mode)
    {
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Detect(
            (_, _, _, _, _) =>
            {
                calls++;
                return new ProcessRunResult
                {
                    Started = true, ExitCode = -1, Stdout = "", Stderr = "",
                    TimedOut = mode == "timeout", OutputLimitExceeded = mode == "overflow",
                    TerminationFailed = mode == "termination",
                    ErrorMessage = mode == "capture" ? "capture failed" : "",
                };
            }, _ => Path.GetFullPath("driver")));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Probe_PrimaryFailureKeepsStreamsAndResolvedLaunchPath()
    {
        string driver = Path.GetFullPath("fake-driver");
        var failure = Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Detect(
            (_, _, _, _, _) => new ProcessRunResult
            {
                Started = true, ExitCode = -1, TimedOut = true,
                ErrorMessage = "primary timeout", Stdout = "version stdout",
                Stderr = "clang frontend command failed due to signal\nunderlying diagnostic",
            }, _ => driver));
        using var json = System.Text.Json.JsonDocument.Parse(failure.Message[failure.Message.IndexOf('{')..]);
        Assert.Equal("timeout", json.RootElement.GetProperty("classification").GetString());
        Assert.Equal("primary timeout", json.RootElement.GetProperty("ErrorMessage").GetString());
        Assert.Equal("version stdout", json.RootElement.GetProperty("stdout").GetString());
        Assert.Contains("underlying diagnostic", json.RootElement.GetProperty("stderr").GetString()!);
        Assert.Equal(driver, json.RootElement.GetProperty("launchExecutable").GetString());
    }

    [Fact]
    public void Report_IsStructuredBoundedAndTruthfulWithoutAddingSourceContents()
    {
        string source = "sensitive generated source \u03bb";
        var result = new ProcessRunResult
        {
            Started = true, TimedOut = true, OutputLimitExceeded = true,
            Cancelled = true, TerminationFailed = true, ExitCode = -1,
            ErrorMessage = "runner error", Stdout = "compiler stdout", Stderr = "compiler stderr",
        };
        string report = CompilerSmokeDiagnostics.Describe(
            new("gcc", new string('v', 20_000)), new[] { "space arg", "\"quoted\"\narg" },
            result, 60_000, TimeSpan.FromMilliseconds(123), false, source);
        using var json = System.Text.Json.JsonDocument.Parse(report);
        var root = json.RootElement;
        Assert.Equal("termination-failure", root.GetProperty("classification").GetString());
        Assert.Equal("synthetic-runner-result", root.GetProperty("exitCodeKind").GetString());
        Assert.Equal(-1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("unresolved", root.GetProperty("resolvedExecutableIdentity").GetString());
        Assert.Equal("space arg", root.GetProperty("argv")[0].GetString());
        Assert.Equal(60_000, root.GetProperty("configuredTimeoutMs").GetInt32());
        Assert.Equal(16_384, root.GetProperty("perStreamOutputCapChars").GetInt32());
        Assert.Equal(123, root.GetProperty("runnerCallElapsedMsIncludingDrainAndCleanup").GetDouble());
        foreach (string flag in new[] { "Started", "TimedOut", "OutputLimitExceeded", "Cancelled", "TerminationFailed" })
            Assert.True(root.GetProperty(flag).GetBoolean());
        Assert.Equal("runner error", root.GetProperty("ErrorMessage").GetString());
        Assert.Equal(result.Stdout.Length, root.GetProperty("capturedStdoutChars").GetInt32());
        Assert.Equal(result.Stderr.Length, root.GetProperty("capturedStderrChars").GetInt32());
        Assert.Equal(16_384, root.GetProperty("versionEvidence").GetString()!.Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(source), root.GetProperty("sourceUtf8ByteCount").GetInt32());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            root.GetProperty("sourceSha256").GetString());
        Assert.DoesNotContain(source, report);
        Assert.DoesNotContain("emitted", report);
        Assert.DoesNotContain("truncated", report);
    }

    [Fact]
    public void Report_OverallBoundIncludesWorstCaseJsonEscapingAndSeparateVersionBudget()
    {
        string payload = new('\u0001', 20_000);
        var result = new ProcessRunResult
        {
            Started = true, ExitCode = 42, Stdout = payload, Stderr = payload, ErrorMessage = payload,
        };
        string report = CompilerSmokeDiagnostics.Describe(new(payload, payload),
            Enumerable.Repeat(payload, 32), result, 60_000, TimeSpan.Zero, false);
        using var json = System.Text.Json.JsonDocument.Parse(report);
        Assert.Equal(16_384, json.RootElement.GetProperty("stdout").GetString()!.Length);
        Assert.Equal(16_384, json.RootElement.GetProperty("stderr").GetString()!.Length);
        Assert.Equal(16_384, json.RootElement.GetProperty("versionEvidence").GetString()!.Length);
        Assert.True(report.Length <= CompilerSmokeDiagnostics.MaximumDiagnosticChars);
        Assert.Equal(CompilerSmokeDiagnostics.MaximumDiagnosticChars,
            json.RootElement.GetProperty("maximumDiagnosticChars").GetInt32());
        Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Describe(
            new("gcc", ""), Enumerable.Repeat("arg", 33), Success(), 60_000, TimeSpan.Zero, true));
    }

    [Theory]
    [InlineData(0, true, "driver-exit")]
    [InlineData(42, true, "driver-exit")]
    [InlineData(-1, false, "not-started")]
    public void Report_ExitCodeLabelDoesNotInventIntervention(int exit, bool started, string expected)
    {
        var result = new ProcessRunResult { Started = started, ExitCode = exit, Stdout = "", Stderr = "", ErrorMessage = "" };
        using var json = System.Text.Json.JsonDocument.Parse(CompilerSmokeDiagnostics.Describe(
            new("gcc", ""), Array.Empty<string>(), result, 60_000, TimeSpan.Zero, true));
        Assert.Equal(expected, json.RootElement.GetProperty("exitCodeKind").GetString());
    }

    [Fact]
    public void RunnerCall_UsesMonotonicElapsedAndCompileBounds()
    {
        var call = CompilerSmokeDiagnostics.Run(
            "fake", new[] { "literal argument" }, ".", 60_000,
            (command, args, directory, timeout, cap) =>
            {
                Assert.Equal("fake", command);
                Assert.Equal(new[] { "literal argument" }, args);
                Assert.Equal(".", directory);
                Assert.Equal(60_000, timeout);
                Assert.Equal(16_384, cap);
                Thread.Sleep(20);
                return Success();
            });
        Assert.True(call.Elapsed >= TimeSpan.FromMilliseconds(15));
        Assert.Equal(0, call.Result.ExitCode);
    }

    [Fact]
    public void Compile_PreservesFlagsSourceEncodingAndObjectAssertions()
    {
        string root = CreateRoot();
        try
        {
            var compiler = new CompilerSmokeDiagnostics.Compiler("fake", "version");
            string source = "generated \u03bb";
            CompilerSmokeDiagnostics.Compile(compiler, root, "case", source,
                new[] { "-Dlinux=1", "-Dunix=1", "-DlinuxCount=1", "-DFEBuilder_MacroNames=1" },
                (_, args, _, timeout, cap) =>
                {
                    Assert.Equal(60_000, timeout);
                    Assert.Equal(16_384, cap);
                    Assert.Equal(new[] { "-std=gnu11", "-Wall", "-Werror", "-Dlinux=1", "-Dunix=1",
                        "-DlinuxCount=1", "-DFEBuilder_MacroNames=1", "-c", Path.Combine(root, "case.c"),
                        "-o", Path.Combine(root, "case.o") }, args);
                    Assert.Equal(Encoding.UTF8.GetBytes(source), File.ReadAllBytes(Path.Combine(root, "case.c")));
                    File.WriteAllBytes(Path.Combine(root, "case.o"), new byte[] { 1 });
                    return Success();
                });
            File.Delete(Path.Combine(root, "case.o"));
            var missing = Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Compile(
                compiler, root, "case", source, Array.Empty<string>(), (_, _, _, _, _) => Success()));
            Assert.Contains("missing-object", missing.Message);
            Assert.DoesNotContain(source, missing.Message);
            Assert.Throws<InvalidOperationException>(() => CompilerSmokeDiagnostics.Compile(
                compiler, root, "case", source, Array.Empty<string>(),
                (_, _, _, _, _) => new ProcessRunResult { Started = true, ExitCode = -1, TerminationFailed = true }));
            Assert.True(compiler.TerminationOutstanding);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_ErrorIsVisibleAndPreservesPrimary(bool unauthorized)
    {
        var primary = new InvalidOperationException("primary compile failure");
        var messages = new List<string>();
        void FailCleanup()
        {
            if (unauthorized) throw new UnauthorizedAccessException("cleanup denied");
            throw new IOException("cleanup failed");
        }
        var observed = Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            try { throw primary; }
            finally { CompilerSmokeDiagnostics.Cleanup(true, false, FailCleanup, messages.Add); }
        }));
        Assert.Same(primary, observed);
        Assert.Contains(nameof(Cleanup_ErrorIsVisibleAndPreservesPrimary), observed.StackTrace!);
        Assert.Single(messages);
        Assert.Contains("secondary", messages[0]);
        if (unauthorized)
            Assert.Throws<UnauthorizedAccessException>(() => CompilerSmokeDiagnostics.Cleanup(false, false, FailCleanup, messages.Add));
        else
            Assert.Throws<IOException>(() => CompilerSmokeDiagnostics.Cleanup(false, false, FailCleanup, messages.Add));
    }

    [Fact]
    public void Cleanup_RetainsRootWhileTerminationIsOutstanding()
    {
        bool deleted = false;
        var messages = new List<string>();
        CompilerSmokeDiagnostics.Cleanup(true, true, () => deleted = true, messages.Add);
        Assert.False(deleted);
        Assert.Contains("retained", Assert.Single(messages));
        Assert.Throws<InvalidOperationException>(() =>
            CompilerSmokeDiagnostics.Cleanup(false, true, () => deleted = true, messages.Add));
        Assert.False(deleted);
    }

    [Theory]
    [InlineData("Ready", "ready")]
    [InlineData("WriteStarted", "write-started")]
    [InlineData("WriteCompleted", "write-completed")]
    [InlineData("SleepStarted", "sleep-started")]
    [InlineData("ExitIntent", "exit-intent")]
    public void OwnedFixture_StageSchemaRoundTripsExactTokens(string stageName, string token)
    {
        var stage = Enum.Parse<FixtureStage>(stageName);
        Assert.Equal(new FixtureStageObservation(stage, FixtureStageStatus.Present, 123),
            CompilerSmokeDiagnostics.ParseOwnedFixtureStage(stage, Encoding.ASCII.GetBytes(token + ":123")));
        Assert.Equal(new FixtureStageObservation(stage, FixtureStageStatus.Present, long.MaxValue),
            CompilerSmokeDiagnostics.ParseOwnedFixtureStage(stage, Encoding.ASCII.GetBytes(token + ":9223372036854775807")));
        Assert.Equal(new FixtureStageObservation(stage, FixtureStageStatus.Present, 0),
            CompilerSmokeDiagnostics.ParseOwnedFixtureStage(stage, Encoding.ASCII.GetBytes(token + ":0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ready:")]
    [InlineData("ready:-1")]
    [InlineData("ready:+1")]
    [InlineData("ready:9223372036854775808")]
    [InlineData("ready:00000000000000000000")]
    [InlineData("ready:1\n")]
    [InlineData(" ready:1")]
    [InlineData("ready: 1")]
    [InlineData("ready:1:2")]
    [InlineData("Ready:1")]
    [InlineData("exit-intent:1")]
    [InlineData("\uFEFFready:1")]
    [InlineData("ready:\u0661")]
    [InlineData("ready:1\0")]
    public void OwnedFixture_StageSchemaRejectsInvalidPayloadWithoutEcho(string payload)
    {
        var observation = CompilerSmokeDiagnostics.ParseOwnedFixtureStage(
            FixtureStage.Ready, Encoding.UTF8.GetBytes(payload));
        Assert.Equal(new FixtureStageObservation(FixtureStage.Ready, FixtureStageStatus.Invalid), observation);
    }

    [Theory]
    [InlineData(64, "Invalid")]
    [InlineData(65, "Oversized")]
    public void OwnedFixture_StageSizeBoundPrecedesGrammar(int size, string status)
    {
        var observation = CompilerSmokeDiagnostics.ParseOwnedFixtureStage(
            FixtureStage.Ready, Enumerable.Repeat((byte)'x', size).ToArray());
        Assert.Equal(new FixtureStageObservation(FixtureStage.Ready, Enum.Parse<FixtureStageStatus>(status)), observation);
    }

    [Fact]
    public void OwnedFixture_ReaderUsesOnlyExactFinalNamesAndReportsMissing()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "ready.stage"), "ready:12");
            File.WriteAllText(Path.Combine(root, "ready.stage.tmp"), "SECRET_TEMP_PAYLOAD");
            File.WriteAllText(Path.Combine(root, "unrelated.stage"), "SECRET_UNRELATED_PAYLOAD");
            var metadataPaths = new List<string>();
            var openedPaths = new List<string>();
            var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages(root, false,
                path => { metadataPaths.Add(path); return File.GetAttributes(path); },
                path => { openedPaths.Add(path); return File.OpenRead(path); });
            Assert.Equal(OwnedStageNames.Select(name => Path.Combine(root, name)), metadataPaths);
            Assert.Equal(new[] { Path.Combine(root, "ready.stage") }, openedPaths);
            Assert.Equal(5, stages.Length);
            Assert.Equal(new FixtureStageObservation(FixtureStage.Ready, FixtureStageStatus.Present, 12), stages[0]);
            Assert.All(stages.Skip(1), stage => Assert.Equal(FixtureStageStatus.Missing, stage.Status));
        }
        finally { Directory.Delete(root, true); }
    }

    [SkippableFact]
    public void OwnedFixture_NativeReaderRejectsRootAndMarkerLinks()
    {
        using var fixture = new OwnedOwnershipNativeFixture();
        string root = fixture.AnchorPath;
        string other = Path.Combine(root, "other");
        fixture.Directory(other);
        string privateFile = Path.Combine(other, "private.txt");
        fixture.Write(other, "private.txt", "SECRET_LINK_TARGET");
        string linkedRoot = Path.Combine(root, "linked-root");
        string linkedMarker = Path.Combine(root, "ready.stage");
        Assert.All(CompilerSmokeDiagnostics.ReadOwnedFixtureStages(root, false),
            stage => Assert.Equal(FixtureStageStatus.Missing, stage.Status));
        try
        {
            fixture.Link(linkedRoot, other);
            fixture.FileLink(linkedMarker, privateFile);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Skip.If(true, "Windows symbolic-link creation privilege is unavailable; no privilege change requested.");
        }
        var rootStages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages(linkedRoot, false);
        Assert.All(rootStages, stage => Assert.Equal(FixtureStageStatus.ReparseRejected, stage.Status));
        var markerStages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages(root, false);
        Assert.Equal(FixtureStageStatus.ReparseRejected, markerStages[0].Status);
        Assert.All(markerStages.Skip(1), stage => Assert.Equal(FixtureStageStatus.Missing, stage.Status));
        Assert.Equal("SECRET_LINK_TARGET", File.ReadAllText(privateFile));
        Assert.Equal(other, new DirectoryInfo(linkedRoot).LinkTarget);
        Assert.Equal(privateFile, new FileInfo(linkedMarker).LinkTarget);
    }

    [Fact]
    public void OwnedFixture_NativeReaderReadsRegularMarkersBeforeLinkChecks()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        string root = fixture.AnchorPath;
        string marker = Path.Combine(root, "ready.stage");
        fixture.Write(root, "ready.stage", "ready:12");
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages(root, false);
        Assert.Equal(new FixtureStageObservation(FixtureStage.Ready, FixtureStageStatus.Present, 12), stages[0]);
        Assert.All(stages.Skip(1), stage => Assert.Equal(FixtureStageStatus.Missing, stage.Status));
        Assert.Equal("ready:12", File.ReadAllText(marker));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("directory-missing")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("directory")]
    [InlineData("reparse")]
    public void OwnedFixture_ReaderDistinguishesMetadataFailuresAndRejectsUnsafeFiles(string failure)
    {
        bool opened = false;
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", false,
            _ => failure switch
            {
                "missing" => throw new FileNotFoundException("SECRET_PATH"),
                "directory-missing" => throw new DirectoryNotFoundException("SECRET_PATH"),
                "io" => throw new IOException("SECRET_IO"),
                "access" => throw new UnauthorizedAccessException("SECRET_ACCESS"),
                "directory" => FileAttributes.Directory,
                "reparse" => FileAttributes.ReparsePoint,
                _ => throw new InvalidOperationException(),
            },
            _ => { opened = true; throw new InvalidOperationException("Unsafe file must not open."); });
        var expected = failure switch
        {
            "missing" or "directory-missing" => FixtureStageStatus.Missing,
            "io" => FixtureStageStatus.IoError,
            "access" => FixtureStageStatus.AccessError,
            "directory" => FixtureStageStatus.DirectoryRejected,
            "reparse" => FixtureStageStatus.ReparseRejected,
            _ => throw new InvalidOperationException(),
        };
        Assert.False(opened);
        Assert.Equal(5, stages.Length);
        Assert.All(stages, stage => Assert.Equal(expected, stage.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedFixture_ReaderReportsOpenErrorsWithoutPayload(bool access)
    {
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", false,
            _ => FileAttributes.Normal,
            _ => access ? throw new UnauthorizedAccessException("SECRET_PATH")
                : throw new IOException("SECRET_PATH"));
        Assert.All(stages, stage => Assert.Equal(
            access ? FixtureStageStatus.AccessError : FixtureStageStatus.IoError, stage.Status));
    }

    [Fact]
    public void OwnedFixture_ReaderPropagatesUnexpectedErrors()
    {
        var unexpected = new InvalidOperationException("unexpected");
        Assert.Same(unexpected, Assert.Throws<InvalidOperationException>(() =>
            CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", false, _ => throw unexpected)));
    }

    [Theory]
    [InlineData(65, 65, "Oversized", 0)]
    [InlineData(7, 65, "Oversized", 65)]
    [InlineData(8, 7, "Invalid", 7)]
    [InlineData(7, 7, "Present", 7)]
    public void OwnedFixture_ReaderBoundsReadsAndDetectsGrowthOrTruncation(
        long advertisedLength, int actualLength, string expected, int maximumBytesRead)
    {
        var streams = new List<OwnedStageReadStream>();
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", false,
            _ => FileAttributes.Normal, _ =>
            {
                var stream = new OwnedStageReadStream(advertisedLength, actualLength);
                streams.Add(stream);
                return stream;
            });
        for (int index = 0; index < stages.Length; index++)
        {
            var status = expected == "Present" && index != 0
                ? FixtureStageStatus.Invalid : Enum.Parse<FixtureStageStatus>(expected);
            Assert.Equal(status, stages[index].Status);
        }
        Assert.Equal(5, streams.Count);
        Assert.All(streams, stream =>
        {
            Assert.InRange(stream.BytesRead, 0, maximumBytesRead);
            Assert.True(stream.Disposed);
            Assert.True(stream.LengthWasChecked);
            Assert.True(stream.LengthCheckedBeforeRead);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedFixture_ReaderReportsReadErrorsAndDisposesHandles(bool access)
    {
        var streams = new List<OwnedStageReadStream>();
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", false,
            _ => FileAttributes.Normal, _ =>
            {
                Exception error = access ? new UnauthorizedAccessException("SECRET_READ_PATH")
                    : new IOException("SECRET_READ_PATH");
                var stream = new OwnedStageReadStream(7, 7, error);
                streams.Add(stream);
                return stream;
            });
        Assert.Equal(5, streams.Count);
        Assert.All(streams, stream => Assert.True(stream.Disposed));
        Assert.All(stages, stage => Assert.Equal(
            access ? FixtureStageStatus.AccessError : FixtureStageStatus.IoError, stage.Status));
    }

    [Fact]
    public void OwnedFixture_OutstandingTerminationDoesNotReadAndMarksEveryStageNonQuiescent()
    {
        var stages = CompilerSmokeDiagnostics.ReadOwnedFixtureStages("owned-root", true,
            _ => throw new InvalidOperationException("Must not inspect a live fixture."),
            _ => throw new InvalidOperationException("Must not read a live fixture."));
        Assert.Equal(Enum.GetValues<FixtureStage>(), stages.Select(stage => stage.Stage));
        Assert.All(stages, stage => Assert.Equal(FixtureStageStatus.NonQuiescent, stage.Status));
    }

    [Theory]
    [InlineData("Stdout", 16_384, "Ready,WriteStarted,WriteCompleted,ExitIntent")]
    [InlineData("Stderr", 16_384, "Ready,WriteStarted,WriteCompleted,ExitIntent")]
    [InlineData("Stdout", 16_385, "Ready,WriteStarted")]
    [InlineData("Stderr", 16_385, "Ready,WriteStarted")]
    [InlineData("Exit", 0, "Ready,ExitIntent")]
    [InlineData("SignalReport", 0, "Ready,ExitIntent")]
    [InlineData("Timeout", 0, "")]
    public void OwnedFixture_RequiredStageMatrixIsExact(string mode, int count, string required)
    {
        var expected = required.Length == 0 ? Array.Empty<string>() : required.Split(',');
        Assert.Equal(expected, CompilerSmokeDiagnostics.RequiredOwnedFixtureStages(
            Enum.Parse<FixtureMode>(mode), count, false).Select(stage => stage.ToString()));
        Assert.Empty(CompilerSmokeDiagnostics.RequiredOwnedFixtureStages(Enum.Parse<FixtureMode>(mode), count, true));
    }

    [Theory]
    [InlineData("Stdout", 16_384, "success", 10_000)]
    [InlineData("Stderr", 16_384, "success", 10_000)]
    [InlineData("Stdout", 16_385, "output-limit", 10_000)]
    [InlineData("Stderr", 16_385, "output-limit", 10_000)]
    [InlineData("Exit", 0, "compiler-nonzero", 10_000)]
    [InlineData("SignalReport", 0, "compiler-reported-frontend-signal", 10_000)]
    [InlineData("Timeout", 0, "timeout", 1_500)]
    public void OwnedFixture_ReportAndContractPreserveSevenCases(
        string modeName, int count, string expected, int timeout)
    {
        var mode = Enum.Parse<FixtureMode>(modeName);
        var result = OwnedResult(mode, count);
        var stages = PresentOwnedStages();
        CompilerSmokeDiagnostics.AssertOwnedFixtureContract(mode, count, result, stages);
        string report = CompilerSmokeDiagnostics.DescribeOwnedFixture(mode, count, result,
            TimeSpan.FromMilliseconds(123), stages);
        Assert.InRange(report.Length, 1, 4096);
        using var json = System.Text.Json.JsonDocument.Parse(report);
        var body = json.RootElement;
        Assert.Equal(expected, body.GetProperty("expectedClassification").GetString());
        Assert.Equal(expected, body.GetProperty("classification").GetString());
        Assert.Equal(timeout, body.GetProperty("configuredTimeoutMs").GetInt32());
        Assert.Equal(16_384, body.GetProperty("perStreamOutputCapChars").GetInt32());
        Assert.Equal(count, body.GetProperty("requestedCount").GetInt32());
        Assert.Equal(123, body.GetProperty("runnerCallElapsedMsIncludingDrainAndCleanup").GetDouble());
        Assert.Equal(result.Stdout.Length, body.GetProperty("capturedStdoutChars").GetInt32());
        Assert.Equal(result.Stderr.Length, body.GetProperty("capturedStderrChars").GetInt32());
        Assert.Equal(5, body.GetProperty("stages").GetArrayLength());
    }

    [Fact]
    public void OwnedFixture_ReportIsBoundedPrivateAndUsesClosedStageStatuses()
    {
        const string secret = "SECRET_PATH_ARGV_SOURCE_STREAM_FILE_EXCEPTION";
        var result = new ProcessRunResult
        {
            Started = true, ExitCode = -1, TimedOut = true, OutputLimitExceeded = true,
            Cancelled = true, TerminationFailed = true,
            Stdout = secret + new string('\u0001', 20_000), Stderr = secret, ErrorMessage = secret,
        };
        foreach (var status in Enum.GetValues<FixtureStageStatus>())
        {
            var stages = Enum.GetValues<FixtureStage>()
                .Select(stage => new FixtureStageObservation(stage, status)).ToArray();
            string report = CompilerSmokeDiagnostics.DescribeOwnedFixture(
                FixtureMode.Stdout, 16_384, result, TimeSpan.FromMilliseconds(123), stages);
            Assert.InRange(report.Length, 1, 4096);
            Assert.DoesNotContain(secret, report);
            using var json = System.Text.Json.JsonDocument.Parse(report);
            var body = json.RootElement;
            Assert.Equal("termination-failure", body.GetProperty("classification").GetString());
            Assert.Equal("unrecognized", body.GetProperty("errorCategory").GetString());
            Assert.Equal(secret.Length, body.GetProperty("errorMessageChars").GetInt32());
            Assert.Equal("synthetic-runner-result", body.GetProperty("exitCodeKind").GetString());
            foreach (string flag in new[] { "Started", "TimedOut", "OutputLimitExceeded", "Cancelled", "TerminationFailed" })
                Assert.True(body.GetProperty(flag).GetBoolean());
            string expectedStatus = status switch
            {
                FixtureStageStatus.IoError => "io-error",
                FixtureStageStatus.AccessError => "access-error",
                FixtureStageStatus.ReparseRejected => "reparse-rejected",
                FixtureStageStatus.DirectoryRejected => "directory-rejected",
                FixtureStageStatus.NonQuiescent => "non-quiescent",
                _ => status.ToString().ToLowerInvariant(),
            };
            Assert.All(body.GetProperty("stages").EnumerateArray(),
                stage => Assert.Equal(expectedStatus, stage.GetProperty("status").GetString()));
            foreach (string forbidden in new[] { "argv", "source", "stdout", "stderr", "path", "exception", "ErrorMessage" })
                Assert.False(body.TryGetProperty(forbidden, out _));
        }
    }

    [Theory]
    [InlineData(1, "start-failure")]
    [InlineData(2, "timeout")]
    [InlineData(4, "output-limit")]
    [InlineData(8, "cancelled")]
    [InlineData(16, "termination-failure")]
    [InlineData(32, "runner-error")]
    [InlineData(30, "termination-failure")]
    public void OwnedFixture_ReportKeepsResultClassificationPrecedence(int flags, string expected)
    {
        var result = new ProcessRunResult
        {
            Started = (flags & 1) == 0, TimedOut = (flags & 2) != 0,
            OutputLimitExceeded = (flags & 4) != 0, Cancelled = (flags & 8) != 0,
            TerminationFailed = (flags & 16) != 0, ExitCode = -1,
            ErrorMessage = (flags & 32) != 0 ? "Process output capture did not finish." : "",
            Stdout = "", Stderr = "",
        };
        using var json = System.Text.Json.JsonDocument.Parse(CompilerSmokeDiagnostics.DescribeOwnedFixture(
            FixtureMode.Exit, 0, result, TimeSpan.Zero, PresentOwnedStages()));
        Assert.Equal(expected, json.RootElement.GetProperty("classification").GetString());
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("Process output capture did not finish.", "capture-incomplete")]
    [InlineData("SECRET_ERROR", "unrecognized")]
    public void OwnedFixture_ReportCategorizesErrorsWithoutRawText(string? error, string expected)
    {
        var result = new ProcessRunResult { Started = true, ExitCode = 0, ErrorMessage = error!, Stdout = null!, Stderr = null! };
        using var json = System.Text.Json.JsonDocument.Parse(CompilerSmokeDiagnostics.DescribeOwnedFixture(
            FixtureMode.Stdout, 16_384, result, TimeSpan.Zero, PresentOwnedStages()));
        Assert.Equal(expected, json.RootElement.GetProperty("errorCategory").GetString());
        Assert.Equal(error?.Length ?? 0, json.RootElement.GetProperty("errorMessageChars").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("capturedStdoutChars").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("capturedStderrChars").GetInt32());
        Assert.DoesNotContain("SECRET_ERROR", json.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("Stdout", 16_384)]
    [InlineData("Stderr", 16_384)]
    [InlineData("Stdout", 16_385)]
    [InlineData("Stderr", 16_385)]
    [InlineData("Exit", 0)]
    [InlineData("Timeout", 0)]
    [InlineData("SignalReport", 0)]
    public void OwnedFixture_StreamContractRejectsUnusedStreamPayload(string modeName, int count)
    {
        var mode = Enum.Parse<FixtureMode>(modeName);
        var valid = OwnedResult(mode, count);
        var bad = new ProcessRunResult
        {
            Started = valid.Started, ExitCode = valid.ExitCode, TimedOut = valid.TimedOut,
            OutputLimitExceeded = valid.OutputLimitExceeded, ErrorMessage = valid.ErrorMessage,
            Stdout = mode == FixtureMode.Stdout ? valid.Stdout : "SECRET_UNUSED_STREAM",
            Stderr = mode == FixtureMode.Stdout ? "SECRET_UNUSED_STREAM" : valid.Stderr,
        };
        Assert.ThrowsAny<XunitException>(() =>
            CompilerSmokeDiagnostics.AssertOwnedFixtureContract(mode, count, bad, PresentOwnedStages()));
    }

    [Theory]
    [InlineData("Stdout", 16_384, 16_383, false)]
    [InlineData("Stderr", 16_384, 16_385, false)]
    [InlineData("Stdout", 16_385, 16_384, false)]
    [InlineData("Stderr", 16_385, 16_385, true)]
    public void OwnedFixture_StreamContractRejectsWrongLengthOrOverflow(
        string modeName, int count, int capturedLength, bool overflow)
    {
        var mode = Enum.Parse<FixtureMode>(modeName);
        var bad = new ProcessRunResult
        {
            Started = true, ExitCode = overflow ? -1 : 0, OutputLimitExceeded = overflow, ErrorMessage = "",
            Stdout = mode == FixtureMode.Stdout ? new string('x', capturedLength) : "",
            Stderr = mode == FixtureMode.Stderr ? new string('x', capturedLength) : "",
        };
        Assert.ThrowsAny<XunitException>(() =>
            CompilerSmokeDiagnostics.AssertOwnedFixtureContract(mode, count, bad, PresentOwnedStages()));
    }

    [Fact]
    public void OwnedFixture_SignalContractRejectsIncorrectSignalPayload()
    {
        var bad = new ProcessRunResult
        {
            Started = true, ExitCode = 1, Stdout = "",
            Stderr = "clang frontend command failed due to signalEXTRA", ErrorMessage = "",
        };
        Assert.ThrowsAny<XunitException>(() =>
            CompilerSmokeDiagnostics.AssertOwnedFixtureContract(FixtureMode.SignalReport, 0, bad, PresentOwnedStages()));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Invalid")]
    [InlineData("IoError")]
    [InlineData("AccessError")]
    public void OwnedFixture_RequiredStageErrorsFailNormalCompletion(string status)
    {
        var stages = PresentOwnedStages();
        stages[0] = new(FixtureStage.Ready, Enum.Parse<FixtureStageStatus>(status));
        Assert.ThrowsAny<XunitException>(() => CompilerSmokeDiagnostics.AssertOwnedFixtureContract(
            FixtureMode.Stdout, 16_384, OwnedResult(FixtureMode.Stdout, 16_384), stages));
    }

    [Theory]
    [InlineData("Stdout", 16_385)]
    [InlineData("Stderr", 16_385)]
    [InlineData("Timeout", 0)]
    public void OwnedFixture_KilledCasesAllowMissingLaterStages(string modeName, int count)
    {
        var mode = Enum.Parse<FixtureMode>(modeName);
        var stages = Enum.GetValues<FixtureStage>()
            .Select(stage => new FixtureStageObservation(stage, FixtureStageStatus.Missing)).ToArray();
        if (mode != FixtureMode.Timeout)
        {
            stages[0] = new(FixtureStage.Ready, FixtureStageStatus.Present, 0);
            stages[1] = new(FixtureStage.WriteStarted, FixtureStageStatus.Present, 1);
        }
        CompilerSmokeDiagnostics.AssertOwnedFixtureContract(mode, count, OwnedResult(mode, count), stages);
    }

    [Fact]
    public void OwnedFixture_OutstandingTerminationStillFailsContract()
    {
        var result = new ProcessRunResult
        {
            Started = true, ExitCode = -1, TimedOut = true, TerminationFailed = true,
            Stdout = "", Stderr = "", ErrorMessage = "",
        };
        var stages = Enum.GetValues<FixtureStage>()
            .Select(stage => new FixtureStageObservation(stage, FixtureStageStatus.NonQuiescent)).ToArray();
        Assert.ThrowsAny<XunitException>(() => CompilerSmokeDiagnostics.AssertOwnedFixtureContract(
            FixtureMode.Timeout, 0, result, stages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedFixture_FailingAssertionKeepsPrimaryStackAndVisibleCleanupError(bool access)
    {
        XunitException? primary = null;
        var messages = new List<string>();
        var observed = Assert.ThrowsAny<XunitException>(() =>
        {
            try
            {
                CompilerSmokeDiagnostics.EmitOwnedFixture("{\"classification\":\"timeout\"}", null, messages.Add);
                FailOwnedFixtureAssertion();
            }
            catch (XunitException ex) { primary = ex; throw; }
            finally
            {
                CompilerSmokeDiagnostics.CleanupOwnedFixture(primary, false,
                    () => { if (access) throw new UnauthorizedAccessException("SECRET_PATH");
                        throw new IOException("SECRET_PATH"); }, messages.Add);
            }
        });
        Assert.Same(primary, observed);
        Assert.Contains(nameof(FailOwnedFixtureAssertion), observed.StackTrace!);
        Assert.Equal(2, messages.Count);
        Assert.Contains("classification", messages[0]);
        Assert.Equal(access ? "owned-fixture:cleanup:access-error" : "owned-fixture:cleanup:io-error", messages[1]);
        Assert.DoesNotContain("SECRET_PATH", string.Join("", messages));
    }

    [Fact]
    public void OwnedFixture_OutputRejectionAttachesOnlyBoundedSecondaryCategories()
    {
        XunitException? primary = null;
        var observed = Assert.ThrowsAny<XunitException>(() =>
        {
            try { FailOwnedFixtureAssertion(); }
            catch (XunitException ex) { primary = ex; throw; }
            finally
            {
                void Reject(string _) => throw new InvalidOperationException("SECRET_OUTPUT_EXCEPTION");
                CompilerSmokeDiagnostics.EmitOwnedFixture("{\"classification\":\"timeout\"}", primary, Reject);
                CompilerSmokeDiagnostics.CleanupOwnedFixture(primary, false,
                    () => throw new IOException("SECRET_CLEANUP_EXCEPTION"), Reject);
            }
        });
        Assert.Same(primary, observed);
        Assert.Contains(nameof(FailOwnedFixtureAssertion), observed.StackTrace!);
        string secondary = Assert.IsType<string>(observed.Data[CompilerSmokeDiagnostics.OwnedFixtureSecondaryKey]);
        Assert.InRange(secondary.Length, 1, 4096);
        Assert.Contains("output-rejected", secondary);
        Assert.Contains("cleanup:io-error", secondary);
        Assert.DoesNotContain("SECRET", secondary);
        Assert.Single(observed.Data.Keys.Cast<object>());
    }

    [Fact]
    public void OwnedFixture_OutputRejectionWithoutPrimaryPropagatesSameError()
    {
        var rejected = new InvalidOperationException("output closed");
        Assert.Same(rejected, Assert.Throws<InvalidOperationException>(() =>
            CompilerSmokeDiagnostics.EmitOwnedFixture("{}", null, _ => throw rejected)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedFixture_CleanupOnlyErrorsPropagateSameException(bool access)
    {
        Exception failure = access ? new UnauthorizedAccessException("SECRET_PATH") : new IOException("SECRET_PATH");
        var messages = new List<string>();
        Exception? observed = Record.Exception(() => CompilerSmokeDiagnostics.CleanupOwnedFixture(
            null, false, () => throw failure, messages.Add));
        Assert.Same(failure, observed);
        Assert.DoesNotContain("SECRET_PATH", string.Join("", messages));
    }

    [Fact]
    public void OwnedFixture_CleanupDeletesOnlyCompletedOwnedRootAndRetainsOutstandingRoot()
    {
        bool deleted = false;
        var messages = new List<string>();
        CompilerSmokeDiagnostics.CleanupOwnedFixture(null, false, () => deleted = true, messages.Add);
        Assert.True(deleted);
        deleted = false;
        CompilerSmokeDiagnostics.CleanupOwnedFixture(new XunitException("primary"), true, () => deleted = true, messages.Add);
        Assert.False(deleted);
        Assert.Equal("owned-fixture:cleanup:retained", Assert.Single(messages));
        Assert.Throws<InvalidOperationException>(() =>
            CompilerSmokeDiagnostics.CleanupOwnedFixture(null, true, () => deleted = true, messages.Add));
        Assert.False(deleted);
    }

    [Fact]
    public void OwnedFixture_NativeCleanupDeletesFixedEntriesWithoutTraversingUnknownChildren()
    {
        using var owned = OperatingSystem.IsWindows() ? null
            : CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(OwnedTempAnchor);
        string root = owned?.RootPath ?? CreateRoot();
        void Delete() { if (owned != null) owned.Delete(); else CompilerSmokeDiagnostics.DeleteOwnedFixtureRoot(root); }
        string script = OperatingSystem.IsWindows() ? "fake.ps1" : "fake.py";
        string unknownDirectory = Path.Combine(root, "unknown");
        string unknownFile = Path.Combine(unknownDirectory, "private.txt");
        try
        {
            File.WriteAllText(Path.Combine(root, script), "not executed");
            foreach (string name in OwnedStageNames)
            {
                File.WriteAllText(Path.Combine(root, name), "owned marker");
                File.WriteAllText(Path.Combine(root, name + ".tmp"), "owned temporary marker");
            }
            Directory.CreateDirectory(unknownDirectory);
            File.WriteAllText(unknownFile, "SECRET_UNKNOWN_CHILD");
            Assert.ThrowsAny<IOException>(Delete);
            Assert.True(File.Exists(unknownFile));
            Assert.Equal("SECRET_UNKNOWN_CHILD", File.ReadAllText(unknownFile));
            Assert.False(File.Exists(Path.Combine(root, script)));
            foreach (string name in OwnedStageNames)
            {
                Assert.False(File.Exists(Path.Combine(root, name)));
                Assert.False(File.Exists(Path.Combine(root, name + ".tmp")));
            }
            File.Delete(unknownFile);
            Directory.Delete(unknownDirectory);
            Delete();
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                File.Delete(unknownFile);
                if (Directory.Exists(unknownDirectory)) Directory.Delete(unknownDirectory, false);
                Delete();
            }
        }
    }

    [Theory]
    [InlineData("device")]
    [InlineData("inode")]
    [InlineData("type")]
    [InlineData("owner")]
    [InlineData("missing")]
    public void OwnedOwnership_AbstractRootMismatchPrecedesEveryLeafMutation(string changed)
    {
        var model = new OwnedOwnershipModel();
        model.RootEntry = ChangedIdentity(model.RootIdentity, changed);
        Assert.Throws<OwnedIdentityMismatchException>(() => model.Lifetime.Delete());
        Assert.Empty(model.Mutations);
        Assert.Equal("owned payload", model.Leaves["fake.py"]);
        Assert.False(model.Root.IsClosed);
    }

    [Theory]
    [InlineData("anchor")]
    [InlineData("envelope")]
    public void OwnedOwnership_AbstractParentMismatchFailsClosed(string changed)
    {
        var model = new OwnedOwnershipModel();
        if (changed == "anchor") model.AnchorIdentity = model.AnchorIdentity with { Inode = 999 };
        else model.EnvelopeEntry = model.EnvelopeIdentity with { Inode = 999 };
        Assert.Throws<OwnedIdentityMismatchException>(() => model.Lifetime.Delete());
        Assert.Empty(model.Mutations);
    }

    [Theory]
    [InlineData("anchor", "device")]
    [InlineData("anchor", "inode")]
    [InlineData("anchor", "type")]
    [InlineData("anchor", "owner")]
    [InlineData("envelope", "device")]
    [InlineData("envelope", "type")]
    [InlineData("envelope", "owner")]
    [InlineData("envelope", "missing")]
    public void OwnedOwnership_AbstractParentIdentityUsesEveryField(string parent, string field)
    {
        using var lifetime = new OwnedOwnershipModel().Lifetime;
        var model = Assert.IsType<OwnedOwnershipModel>(lifetime.Operations);
        if (parent == "anchor") model.AnchorIdentity = ChangedIdentity(model.AnchorIdentity, field)!.Value;
        else model.EnvelopeEntry = ChangedIdentity(model.EnvelopeIdentity, field);
        Assert.Throws<OwnedIdentityMismatchException>(() => lifetime.Delete());
        Assert.Empty(model.Mutations);
        Assert.Equal("owned payload", model.Leaves["fake.py"]);
    }

    [Fact]
    public void OwnedOwnership_AbstractIdentityIsRecheckedBeforeEachLeafMutation()
    {
        var model = new OwnedOwnershipModel();
        using var lifetime = model.Lifetime;
        model.Leaves.Add("ready.stage", "second owned payload");
        model.AfterLeafDeletion = _ => model.RootEntry = null;
        Assert.Throws<OwnedIdentityMismatchException>(() => lifetime.Delete());
        Assert.Equal(new[] { "delete:fake.py" }, model.Mutations);
        Assert.Equal("second owned payload", model.Leaves["ready.stage"]);
    }

    [Theory]
    [InlineData("BeforeLeafMutation")]
    [InlineData("BeforeFinalRootRemoval")]
    [InlineData("BeforeFinalEnvelopeRemoval")]
    public void OwnedOwnership_AbstractCheckpointRunsBeforeIdentityValidation(string checkpointName)
    {
        var point = Enum.Parse<OwnedCleanupCheckpoint>(checkpointName);
        var model = new OwnedOwnershipModel();
        int calls = 0;
        Assert.Throws<OwnedIdentityMismatchException>(() => model.Lifetime.Delete(checkpoint =>
        {
            Assert.False(model.Anchor.IsClosed || model.Envelope.IsClosed || model.Root.IsClosed);
            if (checkpoint != point) return;
            calls++;
            if (point == OwnedCleanupCheckpoint.BeforeFinalEnvelopeRemoval)
                model.EnvelopeEntry = model.EnvelopeIdentity with { Inode = 999 };
            else model.RootEntry = model.RootIdentity with { Inode = 999 };
        }));
        Assert.Equal(1, calls);
        if (point == OwnedCleanupCheckpoint.BeforeLeafMutation) Assert.Empty(model.Mutations);
        Assert.DoesNotContain("remove:envelope", model.Mutations);
        if (point != OwnedCleanupCheckpoint.BeforeFinalEnvelopeRemoval)
            Assert.DoesNotContain("remove:root", model.Mutations);
    }

    [Fact]
    public void OwnedOwnership_AbstractUnknownChildrenAreNeverTraversed()
    {
        var model = new OwnedOwnershipModel();
        model.Leaves.Add("unknown-directory", "private nested sentinel");
        Assert.ThrowsAny<IOException>(() => model.Lifetime.Delete());
        Assert.Equal("private nested sentinel", model.Leaves["unknown-directory"]);
        Assert.DoesNotContain("delete:unknown-directory", model.Mutations);
        Assert.DoesNotContain("remove:root", model.Mutations);
    }

    [Fact]
    public void OwnedOwnership_AbstractOrdinaryTeardownUsesRetainedParentsAndKeepsPinsUntilDisposal()
    {
        var model = new OwnedOwnershipModel();
        model.Lifetime.Delete();
        Assert.Equal(new[] { "delete:fake.py", "remove:root", "remove:envelope" }, model.Mutations);
        Assert.False(model.Anchor.IsClosed || model.Envelope.IsClosed || model.Root.IsClosed);
        model.Lifetime.Dispose();
        model.Lifetime.Dispose();
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.Equal(1, handle.DisposeCalls));
        Assert.Throws<ObjectDisposedException>(() => model.Lifetime.Delete());
    }

    [Fact]
    public void OwnedOwnership_AbstractDisposalOnlyClosesPinsAndIsIdempotent()
    {
        var model = new OwnedOwnershipModel();
        model.Lifetime.Dispose();
        model.Lifetime.Dispose();
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.Equal(1, handle.DisposeCalls));
        Assert.Empty(model.Mutations);
        Assert.Equal(model.RootIdentity, model.RootEntry);
        Assert.Equal(model.EnvelopeIdentity, model.EnvelopeEntry);
        Assert.Equal("owned payload", model.Leaves["fake.py"]);
        Assert.Throws<ObjectDisposedException>(() => model.Lifetime.Delete());
    }

    [Fact]
    public void OwnedOwnership_AbstractOutstandingTerminationWithoutPrimaryIsExplicitAndClosesPins()
    {
        var model = new OwnedOwnershipModel();
        var messages = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => model.Lifetime.Cleanup(null, true, messages.Add));
        Assert.Equal("Owned compiler-smoke root retained: process termination is outstanding.", error.Message);
        Assert.Empty(model.Mutations);
        Assert.Equal("owned payload", model.Leaves["fake.py"]);
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.Equal(1, handle.DisposeCalls));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("mismatch")]
    [InlineData("access")]
    [InlineData("io")]
    [InlineData("retained")]
    public void OwnedOwnership_AbstractCleanupClosesEveryPinOnEveryExit(string outcome)
    {
        var model = new OwnedOwnershipModel();
        if (outcome == "mismatch") model.RootEntry = null;
        if (outcome == "access") model.Failure = new UnauthorizedAccessException("SECRET_PATH");
        if (outcome == "io") model.Failure = new IOException("SECRET_PATH");
        var primary = new XunitException("primary");
        var messages = new List<string>();
        model.Lifetime.Cleanup(primary, outcome == "retained", messages.Add);
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.Equal(1, handle.DisposeCalls));
        if (outcome == "success") Assert.Empty(messages);
        else
        {
            string category = outcome switch
            {
                "mismatch" => "identity-mismatch", "access" => "access-error",
                "io" => "io-error", _ => "retained",
            };
            Assert.Equal("owned-fixture:cleanup:" + category, Assert.Single(messages));
            Assert.Empty(model.Mutations);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_AbstractMismatchPreservesPrimaryStackAndCappedPrivateSecondary(bool rejectOutput)
    {
        var model = new OwnedOwnershipModel { RootEntry = null };
        var messages = new List<string>();
        XunitException? primary = null;
        var observed = Assert.ThrowsAny<XunitException>(() =>
        {
            try { FailOwnedFixtureAssertion(); }
            catch (XunitException ex)
            {
                primary = ex;
                ex.Data[CompilerSmokeDiagnostics.OwnedFixtureSecondaryKey] = "SECRET_PATH" + new string('x', 5000);
                throw;
            }
            finally
            {
                model.Lifetime.Cleanup(primary, false, message =>
                {
                    messages.Add(message);
                    if (rejectOutput) throw new InvalidOperationException("SECRET_OUTPUT");
                });
            }
        });
        Assert.Same(primary, observed);
        Assert.Contains(nameof(FailOwnedFixtureAssertion), observed.StackTrace!);
        Assert.Equal("owned-fixture:cleanup:identity-mismatch", Assert.Single(messages));
        string secondary = Assert.IsType<string>(observed.Data[CompilerSmokeDiagnostics.OwnedFixtureSecondaryKey]);
        Assert.Equal(rejectOutput
            ? "owned-fixture:output-rejected;owned-fixture:cleanup:identity-mismatch"
            : "owned-fixture:cleanup:identity-mismatch", secondary);
        Assert.InRange(secondary.Length, 1, CompilerSmokeDiagnostics.OwnedFixtureReportCapChars);
        Assert.DoesNotContain("SECRET", secondary);
        Assert.Single(observed.Data.Keys.Cast<object>());
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public void OwnedOwnership_AbstractCleanupOnlyMismatchIsTypedAndPrivate()
    {
        var model = new OwnedOwnershipModel { RootEntry = null };
        var messages = new List<string>();
        var error = Assert.Throws<OwnedIdentityMismatchException>(() =>
            model.Lifetime.Cleanup(null, false, messages.Add));
        Assert.Equal("owned-fixture:cleanup:identity-mismatch", error.Message);
        Assert.Empty(messages);
        Assert.Empty(model.Mutations);
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.True(handle.IsClosed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_ExistingCleanupMustNotCollapseIdentityMismatchIntoIoError(bool rejectOutput)
    {
        XunitException? primary = null;
        var messages = new List<string>();
        var observed = Assert.ThrowsAny<XunitException>(() =>
        {
            try { FailOwnedFixtureAssertion(); }
            catch (XunitException ex)
            {
                primary = ex;
                ex.Data[CompilerSmokeDiagnostics.OwnedFixtureSecondaryKey] = "SECRET_PATH" + new string('x', 5000);
                throw;
            }
            finally
            {
                CompilerSmokeDiagnostics.CleanupOwnedFixture(primary, false,
                    () => throw new OwnedIdentityMismatchException(), message =>
                    {
                        messages.Add(message);
                        if (rejectOutput) throw new InvalidOperationException("SECRET_OUTPUT");
                    });
            }
        });
        Assert.Same(primary, observed);
        Assert.Contains(nameof(FailOwnedFixtureAssertion), observed.StackTrace!);
        Assert.Equal("owned-fixture:cleanup:identity-mismatch", Assert.Single(messages));
        string secondary = Assert.IsType<string>(observed.Data[CompilerSmokeDiagnostics.OwnedFixtureSecondaryKey]);
        Assert.Equal(rejectOutput
            ? "owned-fixture:output-rejected;owned-fixture:cleanup:identity-mismatch"
            : "owned-fixture:cleanup:identity-mismatch", secondary);
        Assert.InRange(secondary.Length, 1, CompilerSmokeDiagnostics.OwnedFixtureReportCapChars);
        Assert.DoesNotContain("SECRET", secondary);
        Assert.Single(observed.Data.Keys.Cast<object>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OwnedOwnership_PosixRootReplacementRetainsExactOriginalAndUnrelatedLeaves(
        bool beforeFinal, bool populated)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return; // Native cases only; abstract cases above run on Windows.
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        string root = owned.RootPath;
        string moved = root + ".moved";
        fixture.Write(root, "fake.py", "original owned payload");
        bool replaced = false;
        OwnedDirectoryIdentity replacement = default;
        void Replace()
        {
            fixture.Move(root, moved);
            fixture.Directory(root);
            if (populated)
            {
                fixture.Write(root, "sentinel", "unrelated sentinel");
                fixture.Write(root, "fake.py", "unrelated allowlisted payload");
                fixture.Write(root, "ready.stage", "unrelated allowlisted marker");
                fixture.Write(root, "ready.stage.tmp", "unrelated allowlisted temporary");
            }
            replacement = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(root);
            Assert.NotEqual(owned.RootIdentity, replacement);
            replaced = true;
        }
        if (!beforeFinal) Replace();
        var error = Assert.Throws<OwnedIdentityMismatchException>(() => owned.Delete(checkpoint =>
        {
            if (beforeFinal && checkpoint == OwnedCleanupCheckpoint.BeforeFinalRootRemoval) Replace();
        }));
        Assert.True(replaced);
        Assert.Equal("owned-fixture:cleanup:identity-mismatch", error.Message);
        Assert.Equal(replacement, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(root));
        Assert.Equal(owned.RootIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(moved));
        if (populated)
        {
            Assert.Equal("unrelated sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
            Assert.Equal("unrelated allowlisted payload", File.ReadAllText(Path.Combine(root, "fake.py")));
            Assert.Equal("unrelated allowlisted marker", File.ReadAllText(Path.Combine(root, "ready.stage")));
            Assert.Equal("unrelated allowlisted temporary", File.ReadAllText(Path.Combine(root, "ready.stage.tmp")));
            Assert.Equal(4, Directory.GetFileSystemEntries(root).Length);
        }
        else Assert.Empty(Directory.GetFileSystemEntries(root));
        if (!beforeFinal)
            Assert.Equal("original owned payload", File.ReadAllText(Path.Combine(moved, "fake.py")));
        else Assert.False(File.Exists(Path.Combine(moved, "fake.py")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_PosixEnvelopeReplacementIsNotAdopted(bool beforeFinal)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        string envelope = owned.EnvelopePath;
        string moved = envelope + ".moved";
        fixture.Write(owned.RootPath, "fake.py", "original owned payload");
        OwnedDirectoryIdentity replacement = default;
        bool replaced = false;
        void Replace()
        {
            fixture.Move(envelope, moved);
            fixture.Directory(envelope);
            fixture.Write(envelope, "sentinel", "unrelated envelope");
            replacement = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(envelope);
            replaced = true;
        }
        if (!beforeFinal) Replace();
        Assert.Throws<OwnedIdentityMismatchException>(() => owned.Delete(checkpoint =>
        {
            if (beforeFinal && checkpoint == OwnedCleanupCheckpoint.BeforeFinalEnvelopeRemoval) Replace();
        }));
        Assert.True(replaced);
        Assert.Equal(replacement, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(envelope));
        Assert.Equal(owned.EnvelopeIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(moved));
        Assert.Equal("unrelated envelope", File.ReadAllText(Path.Combine(envelope, "sentinel")));
        if (!beforeFinal)
            Assert.Equal("original owned payload", File.ReadAllText(Path.Combine(moved, OwnedFixtureDirectory.RootName, "fake.py")));
    }

    [Theory]
    [InlineData("link")]
    [InlineData("file")]
    [InlineData("missing")]
    public void OwnedOwnership_PosixNoFollowRejectsReplacementBeforeLeaves(string replacement)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        fixture.Write(owned.RootPath, "fake.py", "original owned payload");
        string moved = owned.RootPath + ".moved";
        fixture.Move(owned.RootPath, moved);
        if (replacement == "link")
        {
            string target = Path.Combine(fixture.AnchorPath, "target");
            fixture.Directory(target);
            fixture.Write(target, "fake.py", "external target sentinel");
            fixture.Link(owned.RootPath, target);
        }
        else if (replacement == "file") fixture.Write(owned.EnvelopePath, OwnedFixtureDirectory.RootName, "replacement file");
        Assert.Throws<OwnedIdentityMismatchException>(() => owned.Delete());
        Assert.Equal("original owned payload", File.ReadAllText(Path.Combine(moved, "fake.py")));
        if (replacement == "link")
        {
            Assert.Equal(OwnedEntryType.SymbolicLink,
                CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath).Type);
            Assert.Equal("external target sentinel", File.ReadAllText(Path.Combine(fixture.AnchorPath, "target", "fake.py")));
        }
        else if (replacement == "file") Assert.Equal("replacement file", File.ReadAllText(owned.RootPath));
        else Assert.False(Path.Exists(owned.RootPath));
    }

    [Fact]
    public void OwnedOwnership_PosixPinnedParentSurvivesAncestorRenameWithoutTouchingReplacement()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        fixture.Write(owned.RootPath, "fake.py", "owned payload");
        string movedAnchor = fixture.AnchorPath + ".moved";
        fixture.Move(fixture.AnchorPath, movedAnchor);
        fixture.Directory(fixture.AnchorPath);
        fixture.Directory(owned.EnvelopePath);
        fixture.Directory(owned.RootPath);
        fixture.Write(owned.RootPath, "fake.py", "unrelated ancestor payload");
        var replacement = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath);
        owned.Delete();
        Assert.Equal(replacement, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath));
        Assert.Equal("unrelated ancestor payload", File.ReadAllText(Path.Combine(owned.RootPath, "fake.py")));
        Assert.False(Directory.Exists(Path.Combine(movedAnchor, owned.EnvelopeName)));
        Assert.True(Directory.Exists(movedAnchor));
    }

    [Fact]
    public void OwnedOwnership_PosixOrdinaryTeardownRemovesRootAndPrivateEnvelopeOnly()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(owned.EnvelopePath));
        Assert.Equal(OwnedEntryType.Directory, owned.RootIdentity.Type);
        Assert.Equal(owned.AnchorIdentity.Owner, owned.EnvelopeIdentity.Owner);
        Assert.Equal(owned.EnvelopeIdentity.Owner, owned.RootIdentity.Owner);
        Assert.Equal(owned.AnchorIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(fixture.AnchorPath));
        Assert.Equal(owned.EnvelopeIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.EnvelopePath));
        Assert.Equal(owned.RootIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath));
        fixture.Write(owned.RootPath, "fake.py", "not executed");
        foreach (string name in OwnedStageNames)
        {
            fixture.Write(owned.RootPath, name, "owned marker");
            fixture.Write(owned.RootPath, name + ".tmp", "owned temporary");
        }
        owned.Cleanup(null, false, _ => Assert.Fail("Successful cleanup must not report an error."));
        Assert.False(Path.Exists(owned.RootPath));
        Assert.False(Path.Exists(owned.EnvelopePath));
        Assert.True(Directory.Exists(fixture.AnchorPath));
        Assert.All(new[] { owned.Anchor, owned.Envelope, owned.Root }, handle => Assert.True(handle.IsClosed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_PosixUnknownChildrenRemainExactAndUntraversed(bool directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        string parent = directory ? Path.Combine(owned.RootPath, "unknown") : owned.RootPath;
        if (directory) fixture.Directory(parent);
        fixture.Write(parent, "sentinel", "unknown child payload");
        var rootIdentity = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath);
        Assert.ThrowsAny<IOException>(() => owned.Delete());
        Assert.Equal(rootIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath));
        Assert.Equal("unknown child payload", File.ReadAllText(Path.Combine(parent, "sentinel")));
        Assert.True(Directory.Exists(owned.EnvelopePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_PosixAllowlistedDirectoryIsNeverTraversed(bool populated)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        string child = Path.Combine(owned.RootPath, "ready.stage");
        fixture.Directory(child);
        if (populated) fixture.Write(child, "sentinel", "unrelated nested payload");
        var identity = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(child);
        // Darwin unlinkat(..., 0) rejects directories with EPERM; Linux uses EISDIR.
        if (OperatingSystem.IsMacOS())
        {
            var error = Assert.Throws<UnauthorizedAccessException>(() => owned.Delete());
            Assert.Equal("Owned fixture access failed.", error.Message);
        }
        else
        {
            var error = Assert.Throws<IOException>(() => owned.Delete());
            Assert.Equal("Owned fixture handle acquisition failed.", error.Message);
        }
        Assert.Equal(identity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(child));
        Assert.False(owned.RootRemoved);
        Assert.False(owned.EnvelopeRemoved);
        if (populated) Assert.Equal("unrelated nested payload", File.ReadAllText(Path.Combine(child, "sentinel")));
        else Assert.Empty(Directory.GetFileSystemEntries(child));
    }

    [Fact]
    public void OwnedOwnership_PosixUnprotectedAnchorFailsBeforeCreation()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        fixture.Write(fixture.AnchorPath, "sentinel", "unrelated anchor payload");
        File.SetUnixFileMode(fixture.AnchorPath, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
        Assert.Throws<OwnedIdentityMismatchException>(() =>
            CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(fixture.AnchorPath));
        Assert.Equal(new[] { Path.Combine(fixture.AnchorPath, "sentinel") },
            Directory.GetFileSystemEntries(fixture.AnchorPath));
        Assert.Equal("unrelated anchor payload", File.ReadAllText(Path.Combine(fixture.AnchorPath, "sentinel")));
    }

    [Fact]
    public void OwnedOwnership_PosixUnprotectedAncestorCannotPublishChildPaths()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        string parent = Path.Combine(fixture.AnchorPath, "unprotected");
        fixture.Directory(parent);
        string anchor = Path.Combine(parent, "private");
        fixture.Directory(anchor);
        fixture.Write(anchor, "sentinel", "unrelated private payload");
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        Assert.Throws<OwnedIdentityMismatchException>(() =>
            CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(anchor));
        Assert.Equal(new[] { Path.Combine(anchor, "sentinel") }, Directory.GetFileSystemEntries(anchor));
        Assert.Equal("unrelated private payload", File.ReadAllText(Path.Combine(anchor, "sentinel")));
    }

    [Theory]
    [InlineData("anchor")]
    [InlineData("envelope")]
    [InlineData("root")]
    public void OwnedOwnership_PosixWeakenedPrivacyFailsBeforeLeafMutation(string component)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        fixture.Write(owned.RootPath, "fake.py", "original owned payload");
        string path = component switch
        {
            "anchor" => fixture.AnchorPath, "envelope" => owned.EnvelopePath, _ => owned.RootPath,
        };
        var identity = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(path);
        var error = Assert.Throws<OwnedIdentityMismatchException>(() => owned.Delete(checkpoint =>
        {
            if (checkpoint == OwnedCleanupCheckpoint.BeforeLeafMutation)
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
                }
                else
                {
                    throw new PlatformNotSupportedException("Native ownership tests require Linux or macOS.");
                }
            }
        }));
        Assert.Equal("owned-fixture:cleanup:identity-mismatch", error.Message);
        Assert.Equal(identity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(path));
        Assert.Equal("original owned payload", File.ReadAllText(Path.Combine(owned.RootPath, "fake.py")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_PosixAcquisitionRejectsFinalAndIntermediateAnchorLinks(bool intermediate)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        string target = Path.Combine(fixture.AnchorPath, "private");
        fixture.Directory(target);
        fixture.Write(target, "sentinel", "unrelated target payload");
        string alias = Path.Combine(fixture.AnchorPath, "alias");
        fixture.Link(alias, intermediate ? fixture.AnchorPath : target);
        Assert.ThrowsAny<IOException>(() => CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(
            intermediate ? Path.Combine(alias, "private") : alias));
        Assert.Equal(new[] { Path.Combine(target, "sentinel") }, Directory.GetFileSystemEntries(target));
        Assert.Equal("unrelated target payload", File.ReadAllText(Path.Combine(target, "sentinel")));
    }

    [Fact]
    public void OwnedOwnership_PosixAcquisitionFailureRemovesOnlyCreatedEntriesAndClosesPins()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        fixture.Write(fixture.AnchorPath, "sentinel", "unrelated anchor payload");
        var anchorIdentity = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(fixture.AnchorPath);
        OwnedFixtureDirectory? acquired = null;
        var failure = new IOException("Intentional acquisition failure.");
        Assert.Same(failure, Assert.Throws<IOException>(() => fixture.Create(owned =>
        {
            acquired = owned;
            throw failure;
        })));
        Assert.NotNull(acquired);
        Assert.All(new[] { acquired.Anchor, acquired.Envelope, acquired.Root }, handle => Assert.True(handle.IsClosed));
        Assert.Throws<ObjectDisposedException>(() => acquired.Delete());
        Assert.False(Path.Exists(acquired.RootPath));
        Assert.False(Path.Exists(acquired.EnvelopePath));
        Assert.Equal(anchorIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(fixture.AnchorPath));
        Assert.Equal("unrelated anchor payload", File.ReadAllText(Path.Combine(fixture.AnchorPath, "sentinel")));
        Assert.Equal(new[] { Path.Combine(fixture.AnchorPath, "sentinel") },
            Directory.GetFileSystemEntries(fixture.AnchorPath));
    }

    [Fact]
    public void OwnedOwnership_PosixAcquisitionFailureNeverDeletesAReplacement()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        OwnedFixtureDirectory? acquired = null;
        OwnedDirectoryIdentity replacement = default;
        string moved = "";
        var error = Assert.Throws<AggregateException>(() => fixture.Create(owned =>
        {
            acquired = owned;
            fixture.Write(owned.RootPath, "fake.py", "original owned payload");
            moved = owned.RootPath + ".moved";
            fixture.Move(owned.RootPath, moved);
            fixture.Directory(owned.RootPath);
            fixture.Write(owned.RootPath, "fake.py", "unrelated replacement payload");
            replacement = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(owned.RootPath);
        }));
        Assert.StartsWith("owned-fixture:acquisition:retained", error.Message);
        Assert.All(error.InnerExceptions, exception => Assert.IsType<OwnedIdentityMismatchException>(exception));
        Assert.NotNull(acquired);
        Assert.All(new[] { acquired.Anchor, acquired.Envelope, acquired.Root }, handle => Assert.True(handle.IsClosed));
        Assert.Throws<ObjectDisposedException>(() => acquired.Delete());
        Assert.Equal(acquired.RootIdentity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(moved));
        Assert.Equal(replacement, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(acquired.RootPath));
        Assert.Equal("original owned payload", File.ReadAllText(Path.Combine(moved, "fake.py")));
        Assert.Equal("unrelated replacement payload", File.ReadAllText(Path.Combine(acquired.RootPath, "fake.py")));
    }

    [Fact]
    public void OwnedOwnership_PosixStageReaderRejectsRootReplacement()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        fixture.Write(owned.RootPath, "ready.stage", "ready:7");
        string moved = owned.RootPath + ".moved";
        fixture.Move(owned.RootPath, moved);
        fixture.Directory(owned.RootPath);
        fixture.Write(owned.RootPath, "ready.stage", "ready:99");
        Assert.Throws<OwnedIdentityMismatchException>(() => owned.ReadStages(false));
        Assert.All(owned.ReadStages(true), stage => Assert.Equal(FixtureStageStatus.NonQuiescent, stage.Status));
        Assert.Equal("ready:7", File.ReadAllText(Path.Combine(moved, "ready.stage")));
        Assert.Equal("ready:99", File.ReadAllText(Path.Combine(owned.RootPath, "ready.stage")));
    }

    [Fact]
    public void OwnedOwnership_PosixStageReaderRemainsPinnedAfterAncestorRename()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        var owned = fixture.Create();
        fixture.Write(owned.RootPath, "ready.stage", "ready:7");
        fixture.Move(fixture.AnchorPath, fixture.AnchorPath + ".moved");
        fixture.Directory(fixture.AnchorPath);
        fixture.Directory(owned.EnvelopePath);
        fixture.Directory(owned.RootPath);
        fixture.Write(owned.RootPath, "ready.stage", "ready:99");
        Assert.Equal(new FixtureStageObservation(FixtureStage.Ready, FixtureStageStatus.Present, 7),
            owned.ReadStages(false)[0]);
        Assert.Equal("ready:99", File.ReadAllText(Path.Combine(owned.RootPath, "ready.stage")));
        owned.Delete();
        Assert.Equal("ready:99", File.ReadAllText(Path.Combine(owned.RootPath, "ready.stage")));
    }

    [Fact]
    public void OwnedOwnership_PosixLegacyPathCleanupFailsClosedWithoutMutation()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var fixture = new OwnedOwnershipNativeFixture();
        fixture.Write(fixture.AnchorPath, "fake.py", "unrelated legacy payload");
        var identity = CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(fixture.AnchorPath);
        Assert.Throws<PlatformNotSupportedException>(() =>
            CompilerSmokeDiagnostics.DeleteOwnedFixtureRoot(fixture.AnchorPath));
        Assert.Equal(identity, CompilerSmokeDiagnostics.ReadOwnedDirectoryIdentityForTest(fixture.AnchorPath));
        Assert.Equal("unrelated legacy payload", File.ReadAllText(Path.Combine(fixture.AnchorPath, "fake.py")));
    }

    static OwnedDirectoryIdentity? ChangedIdentity(OwnedDirectoryIdentity identity, string field) => field switch
    {
        "device" => identity with { Device = identity.Device + 1 },
        "inode" => identity with { Inode = identity.Inode + 1 },
        "type" => identity with { Type = OwnedEntryType.RegularFile },
        "owner" => identity with { Owner = identity.Owner + 1 },
        "missing" => null,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OwnedOwnership_FixtureDisposeAfterRecordedParentRemoval(bool partial, bool moved)
    {
        using var fixture = new OwnedOwnershipNativeFixture();
        string envelope = Path.Combine(fixture.AnchorPath, "envelope");
        string root = Path.Combine(envelope, OwnedFixtureDirectory.RootName);
        System.IO.Directory.CreateDirectory(root);
        var model = new OwnedOwnershipModel(root, envelope);
        fixture.Track(model.Lifetime);
        fixture.Write(root, "fake.py", "owned payload");
        if (moved)
        {
            string movedAnchor = fixture.AnchorPath + ".moved";
            fixture.Move(fixture.AnchorPath, movedAnchor);
            envelope = Path.Combine(movedAnchor, "envelope");
            root = Path.Combine(envelope, OwnedFixtureDirectory.RootName);
            fixture.Directory(fixture.AnchorPath);
            fixture.Write(fixture.AnchorPath, "sentinel", "unrelated ancestor");
        }
        model.AfterLeafDeletion = name => File.Delete(Path.Combine(root, name));
        model.RemoveDirectoryEntry = name =>
            System.IO.Directory.Delete(name == OwnedFixtureDirectory.RootName ? root : envelope, false);
        string? replacementEnvelope = null;
        if (partial)
        {
            Assert.Throws<OwnedIdentityMismatchException>(() => model.Lifetime.Delete(checkpoint =>
            {
                if (checkpoint != OwnedCleanupCheckpoint.BeforeFinalEnvelopeRemoval) return;
                replacementEnvelope = envelope;
                fixture.Move(envelope, envelope + ".moved");
                envelope += ".moved";
                root = Path.Combine(envelope, OwnedFixtureDirectory.RootName);
                fixture.Directory(replacementEnvelope);
                fixture.Write(replacementEnvelope, "sentinel", "unrelated envelope");
                model.EnvelopeEntry = ChangedIdentity(model.EnvelopeIdentity, "inode");
            }));
            Assert.Equal("unrelated envelope", File.ReadAllText(Path.Combine(replacementEnvelope!, "sentinel")));
        }
        else model.Lifetime.Delete();
        Assert.True(model.Lifetime.RootRemoved);
        Assert.Equal(!partial, model.Lifetime.EnvelopeRemoved);
        Assert.Throws<DirectoryNotFoundException>(() => System.IO.Directory.Delete(root, false));
        if (!partial)
            Assert.Throws<DirectoryNotFoundException>(() => System.IO.Directory.Delete(envelope, false));
        if (moved)
            Assert.Equal("unrelated ancestor", File.ReadAllText(Path.Combine(fixture.AnchorPath, "sentinel")));
        fixture.Dispose();
        fixture.Dispose();
        Assert.All(new[] { model.Anchor, model.Envelope, model.Root }, handle => Assert.Equal(1, handle.DisposeCalls));
        Assert.False(Path.Exists(fixture.AnchorPath));
        Assert.False(Path.Exists(envelope));
        if (replacementEnvelope != null) Assert.False(Path.Exists(replacementEnvelope));
        if (moved) Assert.False(Path.Exists(fixture.AnchorPath + ".moved"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOwnership_FixtureDisposeRejectsUnrecordedMissingParent(bool file)
    {
        using var fixture = new OwnedOwnershipNativeFixture();
        string parent = Path.Combine(fixture.AnchorPath, "unrecorded");
        fixture.Directory(parent);
        if (file)
        {
            fixture.Write(parent, "sentinel", "test payload");
            File.Delete(Path.Combine(parent, "sentinel"));
        }
        System.IO.Directory.Delete(parent, false);
        Assert.Throws<DirectoryNotFoundException>(() => fixture.Dispose());
        System.IO.Directory.CreateDirectory(parent);
        fixture.Dispose();
        fixture.Dispose();
    }

    [Fact]
    public void OwnedOwnership_FixtureDisposePropagatesUnexpectedIo()
    {
        using var fixture = new OwnedOwnershipNativeFixture();
        string parent = Path.Combine(fixture.AnchorPath, "retained");
        fixture.Directory(parent);
        string sentinel = Path.Combine(parent, "sentinel");
        File.WriteAllText(sentinel, "unexpected child");
        Assert.ThrowsAny<IOException>(() => fixture.Dispose());
        Assert.Equal("unexpected child", File.ReadAllText(sentinel));
        File.Delete(sentinel);
        fixture.Dispose();
        fixture.Dispose();
    }

    sealed class OwnedOwnershipHandle : IOwnedDirectoryHandle
    {
        public bool IsClosed => DisposeCalls != 0;
        internal int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }

    sealed class OwnedOwnershipModel : IOwnedDirectoryOperations
    {
        internal readonly OwnedOwnershipHandle Anchor = new(), Envelope = new(), Root = new();
        internal OwnedDirectoryIdentity AnchorIdentity = new(1, 10, OwnedEntryType.Directory, 1000);
        internal readonly OwnedDirectoryIdentity EnvelopeIdentity = new(1, 11, OwnedEntryType.Directory, 1000);
        internal readonly OwnedDirectoryIdentity RootIdentity = new(1, 12, OwnedEntryType.Directory, 1000);
        internal OwnedDirectoryIdentity? EnvelopeEntry, RootEntry;
        internal readonly Dictionary<string, string> Leaves = new() { ["fake.py"] = "owned payload" };
        internal readonly List<string> Mutations = new();
        internal Exception? Failure;
        internal Action<string>? AfterLeafDeletion;
        internal Action<string>? RemoveDirectoryEntry;
        internal readonly OwnedFixtureDirectory Lifetime;

        internal OwnedOwnershipModel(
            string rootPath = "ABSTRACT_PRIVATE_PATH", string envelopePath = "ABSTRACT_ENVELOPE_PATH")
        {
            EnvelopeEntry = EnvelopeIdentity;
            RootEntry = RootIdentity;
            Lifetime = new(rootPath, envelopePath, "envelope", this,
                Anchor, AnchorIdentity, Envelope, EnvelopeIdentity, Root, RootIdentity);
        }

        public OwnedDirectoryIdentity ReadIdentity(IOwnedDirectoryHandle handle)
        {
            CheckPins();
            if (ReferenceEquals(handle, Anchor)) return AnchorIdentity;
            if (ReferenceEquals(handle, Envelope)) return EnvelopeIdentity;
            Assert.Same(Root, handle);
            return RootIdentity;
        }

        public OwnedDirectoryIdentity? QueryChildNoFollow(IOwnedDirectoryHandle parent, string name)
        {
            CheckPins();
            if (ReferenceEquals(parent, Anchor))
            {
                Assert.Equal("envelope", name);
                return EnvelopeEntry;
            }
            Assert.Same(Envelope, parent);
            Assert.Equal(OwnedFixtureDirectory.RootName, name);
            return RootEntry;
        }

        public IReadOnlyList<string> ReadChildNames(IOwnedDirectoryHandle root)
        {
            CheckPins();
            Assert.Same(Root, root);
            return Leaves.Keys.ToArray();
        }

        public void DeleteLeaf(IOwnedDirectoryHandle root, string name)
        {
            CheckPins();
            Assert.Same(Root, root);
            Assert.Contains(name, OwnedStageNames.SelectMany(stage => new[] { stage, stage + ".tmp" }).Append("fake.py"));
            if (!Leaves.ContainsKey(name)) throw new FileNotFoundException("Absent fixed leaf.");
            Mutations.Add("delete:" + name);
            Leaves.Remove(name);
            AfterLeafDeletion?.Invoke(name);
        }

        public void RemoveDirectory(IOwnedDirectoryHandle parent, string name)
        {
            CheckPins();
            if (ReferenceEquals(parent, Envelope))
            {
                Assert.Equal(OwnedFixtureDirectory.RootName, name);
                Assert.Empty(Leaves);
                RemoveDirectoryEntry?.Invoke(name);
                RootEntry = null;
            }
            else
            {
                Assert.Same(Anchor, parent);
                Assert.Equal("envelope", name);
                Assert.Null(RootEntry);
                RemoveDirectoryEntry?.Invoke(name);
                EnvelopeEntry = null;
            }
            Mutations.Add("remove:" + name);
        }

        void CheckPins()
        {
            Assert.False(Anchor.IsClosed || Envelope.IsClosed || Root.IsClosed);
            if (Failure != null) throw Failure;
        }
    }

    sealed class OwnedOwnershipNativeFixture : IDisposable
    {
        internal string AnchorPath { get; } = Path.Combine(OwnedTempAnchor, "febuilder-owned-red-" + Guid.NewGuid().ToString("N"));
        readonly List<string> directories = new();
        readonly List<string> files = new();
        OwnedFixtureDirectory? owned;
        string ownedRoot = "", ownedEnvelope = "";

        internal OwnedOwnershipNativeFixture()
        {
            Directory(AnchorPath);
        }

        internal OwnedFixtureDirectory Create(Action<OwnedFixtureDirectory>? beforePublication = null)
        {
            return CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(AnchorPath, acquired =>
            {
                Track(acquired);
                beforePublication?.Invoke(acquired);
            });
        }

        internal void Track(OwnedFixtureDirectory acquired)
        {
            owned = acquired;
            ownedEnvelope = acquired.EnvelopePath;
            ownedRoot = acquired.RootPath;
            directories.Add(ownedEnvelope);
            directories.Add(ownedRoot);
        }

        internal void Directory(string path)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                System.IO.Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else
            {
                System.IO.Directory.CreateDirectory(path);
            }
            directories.Add(path);
        }

        internal void Write(string parent, string name, string content)
        {
            string path = Path.Combine(parent, name);
            files.Add(path);
            File.WriteAllText(path, content);
        }

        internal void Link(string path, string target)
        {
            System.IO.Directory.CreateSymbolicLink(path, target);
            directories.Add(path);
        }

        internal void FileLink(string path, string target)
        {
            File.CreateSymbolicLink(path, target);
            files.Add(path);
        }

        internal void Move(string source, string destination)
        {
            System.IO.Directory.Move(source, destination);
            string Rebase(string path) => path == source ? destination
                : path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    ? Path.Combine(destination, Path.GetRelativePath(source, path)) : path;
            for (int index = 0; index < files.Count; index++) files[index] = Rebase(files[index]);
            for (int index = 0; index < directories.Count; index++) directories[index] = Rebase(directories[index]);
            ownedRoot = Rebase(ownedRoot);
            ownedEnvelope = Rebase(ownedEnvelope);
        }

        public void Dispose()
        {
            try { owned?.Dispose(); }
            finally
            {
                // Only exact test-created entries; never enumerate for deletion or recurse.
                for (int index = files.Count - 1; index >= 0; index--)
                {
                    try { File.Delete(files[index]); }
                    catch (DirectoryNotFoundException) when (owned?.RootRemoved == true
                        && Path.GetDirectoryName(files[index]) == ownedRoot) { }
                    files.RemoveAt(index);
                }
                for (int index = directories.Count - 1; index >= 0; index--)
                {
                    try { System.IO.Directory.Delete(directories[index], false); }
                    catch (DirectoryNotFoundException) when (
                        (owned?.RootRemoved == true && directories[index] == ownedRoot)
                        || (owned?.EnvelopeRemoved == true && directories[index] == ownedEnvelope)) { }
                    directories.RemoveAt(index);
                }
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void FailOwnedFixtureAssertion() => Assert.Equal("success", "timeout");

    static readonly string[] OwnedStageNames =
        { "ready.stage", "write-started.stage", "write-completed.stage", "sleep-started.stage", "exit-intent.stage" };

    static FixtureStageObservation[] PresentOwnedStages() => Enum.GetValues<FixtureStage>()
        .Select(stage => new FixtureStageObservation(stage, FixtureStageStatus.Present, 1)).ToArray();

    static ProcessRunResult OwnedResult(FixtureMode mode, int count) => new()
    {
        Started = true,
        ExitCode = mode == FixtureMode.Exit ? 42 : mode == FixtureMode.SignalReport ? 1
            : mode == FixtureMode.Timeout || count > 16_384 ? -1 : 0,
        TimedOut = mode == FixtureMode.Timeout,
        OutputLimitExceeded = count > 16_384,
        Stdout = mode == FixtureMode.Stdout ? new string('x', Math.Min(count, 16_384)) : "",
        Stderr = mode == FixtureMode.SignalReport ? "clang frontend command failed due to signal"
            : mode == FixtureMode.Stderr ? new string('x', Math.Min(count, 16_384)) : "",
        ErrorMessage = "",
    };

    sealed class OwnedStageReadStream(long advertisedLength, int actualLength, Exception? readError = null) : Stream
    {
        bool lengthChecked;
        public bool LengthWasChecked => lengthChecked;
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public bool LengthCheckedBeforeRead { get; private set; } = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get { lengthChecked = true; return advertisedLength; } }
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            LengthCheckedBeforeRead &= lengthChecked;
            Assert.True(lengthChecked, "Length must be checked before payload reads.");
            Assert.InRange(buffer.Length, 0, 65);
            if (readError != null) throw readError;
            int count = Math.Min(buffer.Length, actualLength - BytesRead);
            ReadOnlySpan<byte> payload = "ready:1"u8;
            for (int index = 0; index < count; index++)
                buffer[index] = BytesRead + index < payload.Length ? payload[BytesRead + index] : (byte)'x';
            BytesRead += count;
            Assert.InRange(BytesRead, 0, 65);
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [SkippableTheory]
    [InlineData("exit", 0, "compiler-nonzero")]
    [InlineData("signal-report", 0, "compiler-reported-frontend-signal")]
    [InlineData("timeout", 0, "timeout")]
    [InlineData("stdout", 16_384, "success")]
    [InlineData("stderr", 16_384, "success")]
    [InlineData("stdout", 16_385, "output-limit")]
    [InlineData("stderr", 16_385, "output-limit")]
    public void OwnedFakeProcess_ExercisesBoundariesWithoutRealCompiler(string mode, int count, string expected)
    {
        string executable = OperatingSystem.IsWindows() ? "powershell.exe" : "/usr/bin/python3";
        Skip.If(!OperatingSystem.IsWindows() && !File.Exists(executable), "Owned fake process requires installed python3.");
        using var owned = OperatingSystem.IsWindows() ? null
            : CompilerSmokeDiagnostics.CreateOwnedFixtureDirectory(OwnedTempAnchor);
        string root = owned?.RootPath ?? CreateRoot();
        bool outstanding = false;
        Exception? primary = null;
        try
        {
            string script = Path.Combine(root, OperatingSystem.IsWindows() ? "fake.ps1" : "fake.py");
            File.WriteAllText(script, OperatingSystem.IsWindows() ? PowerShellFixture : PythonFixture, new UTF8Encoding(false));
            string[] args = OperatingSystem.IsWindows()
                ? new[] { "-NoProfile", "-NonInteractive", "-File", script, mode, count.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : new[] { script, mode, count.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            int timeout = mode == "timeout" ? 1_500 : 10_000;
            var call = CompilerSmokeDiagnostics.Run(executable, args, root, timeout);
            outstanding = call.Result.TerminationFailed;
            var fixtureMode = mode switch
            {
                "exit" => FixtureMode.Exit, "signal-report" => FixtureMode.SignalReport,
                "timeout" => FixtureMode.Timeout, "stdout" => FixtureMode.Stdout,
                "stderr" => FixtureMode.Stderr,
                _ => throw new ArgumentException("Invalid owned fixture mode."),
            };
            var stages = owned != null ? owned.ReadStages(outstanding)
                : CompilerSmokeDiagnostics.ReadOwnedFixtureStages(root, outstanding);
            CompilerSmokeDiagnostics.EmitOwnedFixture(CompilerSmokeDiagnostics.DescribeOwnedFixture(
                fixtureMode, count, call.Result, call.Elapsed, stages), null, output.WriteLine);
            Assert.Equal(expected, CompilerSmokeDiagnostics.Classify(call.Result, true));
            CompilerSmokeDiagnostics.AssertOwnedFixtureContract(fixtureMode, count, call.Result, stages);
        }
        catch (XunitException ex) { primary = ex; throw; }
        catch (IOException ex) { primary = ex; throw; }
        catch (UnauthorizedAccessException ex) { primary = ex; throw; }
        catch (InvalidOperationException ex) { primary = ex; throw; }
        catch (ArgumentException ex) { primary = ex; throw; }
        catch (NotSupportedException ex) { primary = ex; throw; }
        finally
        {
            if (owned != null) owned.Cleanup(primary, outstanding, output.WriteLine);
            else CompilerSmokeDiagnostics.CleanupOwnedFixture(primary, outstanding,
                () => CompilerSmokeDiagnostics.DeleteOwnedFixtureRoot(root), output.WriteLine);
        }
        Assert.False(Directory.Exists(root));
    }

    static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "febuilder-compiler-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static string OwnedTempAnchor => OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();

    const string PowerShellFixture = """
        param([string]$mode, [int]$count)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        function Publish-Stage([string]$stage) {
            $final = [IO.Path]::Combine($PSScriptRoot, $stage + '.stage')
            $temporary = $final + '.tmp'
            $elapsed = $clock.ElapsedMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)
            $bytes = [Text.Encoding]::ASCII.GetBytes($stage + ':' + $elapsed)
            try {
                $file = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $file.Write($bytes, 0, $bytes.Length) }
                finally { $file.Dispose() }
                [IO.File]::Move($temporary, $final)
            }
            catch [IO.IOException] { }
            catch [UnauthorizedAccessException] { }
        }
        Publish-Stage 'ready'
        switch ($mode) {
            'exit' { Publish-Stage 'exit-intent'; exit 42 }
            'signal-report' {
                [Console]::Error.Write('clang frontend command failed due to signal')
                Publish-Stage 'exit-intent'
                exit 1
            }
            'timeout' { Publish-Stage 'sleep-started'; Start-Sleep -Seconds 10 }
            'stdout' {
                Publish-Stage 'write-started'
                [Console]::Out.Write(('x' * $count))
                Publish-Stage 'write-completed'
            }
            'stderr' {
                Publish-Stage 'write-started'
                [Console]::Error.Write(('x' * $count))
                Publish-Stage 'write-completed'
            }
            default { exit 99 }
        }
        Publish-Stage 'exit-intent'
        exit 0
        """;

    const string PythonFixture = """
        import os, sys, time
        started = time.monotonic_ns()
        def publish_stage(stage):
            final = os.path.join(os.path.dirname(__file__), stage + ".stage")
            elapsed = (time.monotonic_ns() - started) // 1000000
            payload = (stage + ":" + str(elapsed)).encode("ascii")
            try:
                with open(final + ".tmp", "xb") as marker:
                    marker.write(payload)
                os.rename(final + ".tmp", final)
            except OSError:
                pass
        mode, count = sys.argv[1], int(sys.argv[2])
        publish_stage("ready")
        if mode == "exit":
            publish_stage("exit-intent")
            sys.exit(42)
        elif mode == "signal-report":
            sys.stderr.write("clang frontend command failed due to signal")
            publish_stage("exit-intent")
            sys.exit(1)
        elif mode == "timeout":
            publish_stage("sleep-started")
            time.sleep(10)
        elif mode == "stdout":
            publish_stage("write-started")
            sys.stdout.write("x" * count)
            publish_stage("write-completed")
        elif mode == "stderr":
            publish_stage("write-started")
            sys.stderr.write("x" * count)
            publish_stage("write-completed")
        else: sys.exit(99)
        publish_stage("exit-intent")
        """;
}
