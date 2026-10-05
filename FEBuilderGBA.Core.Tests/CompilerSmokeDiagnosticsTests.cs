using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

public class CompilerSmokeDiagnosticsTests
{
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
        string root = CreateRoot();
        bool outstanding = false;
        bool completed = false;
        var messages = new List<string>();
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
            Assert.Equal(expected, CompilerSmokeDiagnostics.Classify(call.Result, true));
            Assert.False(outstanding);
            Assert.True(call.Result.Stdout.Length <= 16_384);
            Assert.True(call.Result.Stderr.Length <= 16_384);
            if (count == 16_384)
                Assert.Equal(count, mode == "stdout" ? call.Result.Stdout.Length : call.Result.Stderr.Length);
            if (mode == "timeout") Assert.True(call.Result.TimedOut);
            completed = true;
        }
        finally
        {
            CompilerSmokeDiagnostics.Cleanup(!completed, outstanding, () => Directory.Delete(root, true), messages.Add);
        }
        Assert.Empty(messages);
        Assert.False(Directory.Exists(root));
    }

    static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "febuilder-compiler-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    const string PowerShellFixture = """
        param([string]$mode, [int]$count)
        switch ($mode) {
            'exit' { exit 42 }
            'signal-report' { [Console]::Error.Write('clang frontend command failed due to signal'); exit 1 }
            'timeout' { Start-Sleep -Seconds 10 }
            'stdout' { [Console]::Out.Write(('x' * $count)) }
            'stderr' { [Console]::Error.Write(('x' * $count)) }
            default { exit 99 }
        }
        exit 0
        """;

    const string PythonFixture = """
        import sys, time
        mode, count = sys.argv[1], int(sys.argv[2])
        if mode == "exit": sys.exit(42)
        elif mode == "signal-report":
            sys.stderr.write("clang frontend command failed due to signal")
            sys.exit(1)
        elif mode == "timeout": time.sleep(10)
        elif mode == "stdout": sys.stdout.write("x" * count)
        elif mode == "stderr": sys.stderr.write("x" * count)
        else: sys.exit(99)
        """;
}
