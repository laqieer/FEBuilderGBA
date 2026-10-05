using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA.Core.Tests;

internal static class CompilerSmokeDiagnostics
{
    internal const int CompileTimeoutMs = 60_000;
    internal const int ProbeTimeoutMs = 10_000;
    internal const int OutputCapChars = 16_384;
    internal const int MaximumDiagnosticChars = 2 * 1024 * 1024;
    const int MaximumArgumentCount = 32;
    const int MaximumIdentityOrArgumentChars = 4096;

    internal delegate ProcessRunResult Runner(
        string executable, IEnumerable<string> arguments, string? directory, int timeoutMs, int outputCapChars);

    internal sealed class Compiler(string requestedExecutable, string versionEvidence, string? launchPath = null)
    {
        internal string RequestedExecutable { get; } = requestedExecutable;
        internal string VersionEvidence { get; } = Limit(versionEvidence);
        internal string? LaunchPath { get; } = launchPath;
        internal bool TerminationOutstanding { get; set; }
    }

    internal static (ProcessRunResult Result, TimeSpan Elapsed) Run(
        string executable, IEnumerable<string> arguments, string directory, int timeoutMs, Runner? runner = null)
    {
        var clock = Stopwatch.StartNew();
        var result = (runner ?? ProcessRunnerCore.Run)(executable, arguments, directory, timeoutMs, OutputCapChars);
        clock.Stop();
        return (result, clock.Elapsed);
    }

    internal static Compiler? Detect(Runner? runner = null, Func<string, string?>? resolve = null)
    {
        foreach (string candidate in new[] { "arm-none-eabi-gcc", "gcc" })
        {
            string[] args = { "--version" };
            string? launchPath = (resolve ?? ResolveInstalledDriver)(candidate);
            if (launchPath == null) continue;
            if (!Path.IsPathFullyQualified(launchPath))
                throw new InvalidOperationException("Compiler resolver did not establish an absolute selected-driver launch path.");
            var compiler = new Compiler(candidate, "", launchPath);
            var call = Run(launchPath, args, Environment.CurrentDirectory, ProbeTimeoutMs, runner);
            if (Classify(call.Result, true) == "success")
                return new Compiler(candidate, Limit(call.Result.Stdout) + Limit(call.Result.Stderr), launchPath);
            throw new InvalidOperationException("Compiler probe failed: " + Describe(
                compiler, args, call.Result, ProbeTimeoutMs, call.Elapsed, true));
        }
        return null;
    }

    static string? ResolveInstalledDriver(string candidate)
    {
        var prefix = new List<string>();
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve compiler: current process executable identity is unavailable.");
        prefix.Add(Path.GetDirectoryName(processPath)!);
        // Match .NET non-shell Process.Start: executable directory, current directory,
        // then PATH; Windows additionally searches the system/Windows directories.
        if (!OperatingSystem.IsWindows() || NeedCurrentDirectoryForExePath(candidate))
            prefix.Add(Environment.CurrentDirectory);
        if (OperatingSystem.IsWindows())
        {
            prefix.Add(Environment.SystemDirectory);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            prefix.Add(Path.Combine(windows, "System"));
            prefix.Add(windows);
        }
        string[] search = ParseSearchPath(Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows());
        return ResolveFromSearch(candidate, prefix, search, OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? null : HasPosixExecuteAccess);
    }

    internal static string[] ParseSearchPath(string? searchPath, bool windows)
    {
        return (searchPath ?? "").Split(windows ? ';' : ':')
            .Where(directory => directory.Length != 0)
            .Select(directory => windows ? directory.Trim('"') : directory)
            .ToArray();
    }

    internal static string? ResolveFromSearch(
        string candidate, IEnumerable<string> prefixDirectories, IEnumerable<string> pathDirectories,
        bool windows, Func<string, bool>? executable = null)
    {
        string name = windows && !Path.HasExtension(candidate) ? candidate + ".exe" : candidate;
        foreach (string directory in prefixDirectories)
        {
            string? found = FindDriver(directory, name);
            if (found != null) return found;
        }
        string? broken = null;
        foreach (string directory in pathDirectories)
        {
            if (directory.Length == 0) continue;
            string? found = FindDriver(directory, name);
            if (found == null) continue;
            if (windows || (executable ?? HasPosixExecuteAccess)(found)) return found;
            broken ??= found;
        }
        if (broken != null)
            throw new InvalidOperationException("Compiler resolution failed: selected search entries lack executable access: "
                + JsonSerializer.Serialize(broken));
        return null;
    }

    internal static string? FindDriver(
        string directory, string name, Func<string, FileAttributes>? getAttributes = null)
    {
        string candidate = Path.GetFullPath(Path.Combine(directory, name));
        FileAttributes attributes;
        try
        {
            attributes = (getAttributes ?? File.GetAttributes)(candidate);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        return (attributes & FileAttributes.Directory) == 0 ? candidate : null;
    }

    static bool HasPosixExecuteAccess(string path)
    {
        if (GetUid() != GetEffectiveUid())
            throw new InvalidOperationException("Cannot safely mirror compiler search under differing real/effective user identities.");
        if (Access(path, 1) == 0) return true;
        int error = Marshal.GetLastPInvokeError();
        if (error == 13) return false; // EACCES: .NET skips non-executable PATH entries.
        throw new Win32Exception(error, "Cannot verify compiler executable access.");
    }

    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
    [DllImport("libc", EntryPoint = "getuid")]
    static extern uint GetUid();
    [DllImport("libc", EntryPoint = "geteuid")]
    static extern uint GetEffectiveUid();
    [DllImport("kernel32.dll", EntryPoint = "NeedCurrentDirectoryForExePathW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool NeedCurrentDirectoryForExePath(string executable);

    internal static string Classify(ProcessRunResult result, bool objectExists)
    {
        if (result.TerminationFailed) return "termination-failure";
        if (result.OutputLimitExceeded) return "output-limit";
        if (result.TimedOut) return "timeout";
        if (result.Cancelled) return "cancelled";
        if (!result.Started) return "start-failure";
        if (!string.IsNullOrEmpty(result.ErrorMessage)) return "runner-error";
        if (result.ExitCode != 0)
            return (result.Stderr ?? "").Contains("frontend command failed due to signal", StringComparison.Ordinal)
                ? "compiler-reported-frontend-signal"
                : "compiler-nonzero";
        return objectExists ? "success" : "missing-object";
    }

    internal static string Describe(
        Compiler compiler, IEnumerable<string> arguments, ProcessRunResult result,
        int timeoutMs, TimeSpan elapsed, bool objectExists, string? source = null)
    {
        bool intervention = result.TimedOut || result.OutputLimitExceeded
            || result.Cancelled || result.TerminationFailed
            || (result.Started && !string.IsNullOrEmpty(result.ErrorMessage));
        byte[]? sourceBytes = source == null ? null : Encoding.UTF8.GetBytes(source);
        string[] argv = arguments.Take(MaximumArgumentCount + 1).ToArray();
        if (argv.Length > MaximumArgumentCount)
            throw new InvalidOperationException("Compiler diagnostic argument count exceeds its explicit reporting budget.");
        string report = JsonSerializer.Serialize(new
        {
            classification = Classify(result, objectExists),
            requestedExecutable = Limit(compiler.RequestedExecutable, MaximumIdentityOrArgumentChars),
            resolvedExecutableIdentity = compiler.LaunchPath == null
                ? "unresolved" : Limit(compiler.LaunchPath, MaximumIdentityOrArgumentChars),
            launchExecutable = Limit(compiler.LaunchPath ?? compiler.RequestedExecutable, MaximumIdentityOrArgumentChars),
            selectedDriverIdentityKind = compiler.LaunchPath == null ? "unresolved" : "absolute-launch-path",
            frontendExecutableIdentity = "unknown",
            argv = argv.Select(argument => Limit(argument, MaximumIdentityOrArgumentChars)).ToArray(),
            versionEvidence = compiler.VersionEvidence,
            maximumDiagnosticChars = MaximumDiagnosticChars,
            maximumReportedArguments = MaximumArgumentCount,
            maximumIdentityOrArgumentChars = MaximumIdentityOrArgumentChars,
            versionEvidenceCapChars = OutputCapChars,
            errorMessageCapChars = OutputCapChars,
            configuredTimeoutMs = timeoutMs,
            perStreamOutputCapChars = OutputCapChars,
            runnerCallElapsedMsIncludingDrainAndCleanup = elapsed.TotalMilliseconds,
            result.Started,
            result.TimedOut,
            result.OutputLimitExceeded,
            result.Cancelled,
            result.TerminationFailed,
            ErrorMessage = Limit(result.ErrorMessage),
            exitCode = result.ExitCode,
            exitCodeKind = intervention ? "synthetic-runner-result" : result.Started ? "driver-exit" : "not-started",
            capturedStdoutChars = (result.Stdout ?? "").Length,
            capturedStderrChars = (result.Stderr ?? "").Length,
            stdout = Limit(result.Stdout),
            stderr = Limit(result.Stderr),
            sourceSha256 = sourceBytes == null ? null : Convert.ToHexString(SHA256.HashData(sourceBytes)),
            sourceUtf8ByteCount = sourceBytes?.Length,
            signalProvenance = "not-observed; historical cause and sender unknown",
        });
        // Budgets include JSON's worst-case six-character escaping expansion.
        if (report.Length > MaximumDiagnosticChars)
            throw new InvalidOperationException("Compiler diagnostic exceeded its explicit overall reporting budget.");
        return report;
    }

    internal static void Compile(
        Compiler compiler, string root, string caseName, string source,
        IEnumerable<string> extraArguments, Runner? runner = null)
    {
        string sourcePath = Path.Combine(root, caseName + ".c");
        string objectPath = Path.Combine(root, caseName + ".o");
        File.WriteAllText(sourcePath, source, new UTF8Encoding(false));
        var args = new List<string> { "-std=gnu11", "-Wall", "-Werror" };
        args.AddRange(extraArguments);
        args.AddRange(new[] { "-c", sourcePath, "-o", objectPath });
        var call = Run(compiler.LaunchPath ?? compiler.RequestedExecutable, args, root, CompileTimeoutMs, runner);
        compiler.TerminationOutstanding |= call.Result.TerminationFailed;
        bool objectExists = File.Exists(objectPath);
        if (Classify(call.Result, objectExists) != "success")
            throw new InvalidOperationException($"[{caseName}] compiler smoke failed: " + Describe(
                compiler, args, call.Result, CompileTimeoutMs, call.Elapsed, objectExists, source));
    }

    internal static void Cleanup(bool primaryFailure, bool terminationOutstanding, Action deleteOwnedRoot, Action<string> log)
    {
        if (terminationOutstanding)
        {
            const string message = "Owned compiler-smoke root retained: process termination is outstanding.";
            if (!primaryFailure) throw new InvalidOperationException(message);
            log(message);
            return;
        }
        try
        {
            deleteOwnedRoot();
        }
        catch (IOException ex) when (primaryFailure)
        {
            log("Compiler-smoke secondary cleanup error: " + ex);
        }
        catch (UnauthorizedAccessException ex) when (primaryFailure)
        {
            log("Compiler-smoke secondary cleanup error: " + ex);
        }
    }

    internal const int OwnedFixtureReportCapChars = 4096;
    internal const int OwnedFixtureStageCapBytes = 64;
    internal const string OwnedFixtureSecondaryKey = "OwnedFixtureSecondary";

    internal enum FixtureMode { Exit, SignalReport, Timeout, Stdout, Stderr }
    internal enum FixtureStage { Ready, WriteStarted, WriteCompleted, SleepStarted, ExitIntent }
    internal enum FixtureStageStatus
    {
        Present, Missing, Oversized, Invalid, IoError, AccessError,
        ReparseRejected, DirectoryRejected, NonQuiescent,
    }

    internal readonly record struct FixtureStageObservation(
        FixtureStage Stage, FixtureStageStatus Status, long? ElapsedMs = null);

    static string StageToken(FixtureStage stage) => stage switch
    {
        FixtureStage.Ready => "ready",
        FixtureStage.WriteStarted => "write-started",
        FixtureStage.WriteCompleted => "write-completed",
        FixtureStage.SleepStarted => "sleep-started",
        FixtureStage.ExitIntent => "exit-intent",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    static string StageStatusToken(FixtureStageStatus status) => status switch
    {
        FixtureStageStatus.Present => "present",
        FixtureStageStatus.Missing => "missing",
        FixtureStageStatus.Oversized => "oversized",
        FixtureStageStatus.Invalid => "invalid",
        FixtureStageStatus.IoError => "io-error",
        FixtureStageStatus.AccessError => "access-error",
        FixtureStageStatus.ReparseRejected => "reparse-rejected",
        FixtureStageStatus.DirectoryRejected => "directory-rejected",
        FixtureStageStatus.NonQuiescent => "non-quiescent",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static FixtureStageObservation ParseOwnedFixtureStage(FixtureStage stage, ReadOnlySpan<byte> bytes)
    {
        string token = StageToken(stage);
        if (bytes.Length > OwnedFixtureStageCapBytes)
            return new(stage, FixtureStageStatus.Oversized);
        int digitCount = bytes.Length - token.Length - 1;
        if (digitCount is < 1 or > 19)
            return new(stage, FixtureStageStatus.Invalid);
        for (int index = 0; index < token.Length; index++)
            if (bytes[index] != token[index])
                return new(stage, FixtureStageStatus.Invalid);
        if (bytes[token.Length] != (byte)':')
            return new(stage, FixtureStageStatus.Invalid);
        long elapsed = 0;
        foreach (byte digit in bytes[(token.Length + 1)..])
        {
            if (digit < (byte)'0' || digit > (byte)'9')
                return new(stage, FixtureStageStatus.Invalid);
            int value = digit - (byte)'0';
            if (elapsed > (long.MaxValue - value) / 10)
                return new(stage, FixtureStageStatus.Invalid);
            elapsed = elapsed * 10 + value;
        }
        return new(stage, FixtureStageStatus.Present, elapsed);
    }

    internal static FixtureStageObservation[] ReadOwnedFixtureStages(
        string ownedRoot, bool terminationOutstanding,
        Func<string, FileAttributes>? getAttributes = null, Func<string, Stream>? openRead = null)
    {
        FixtureStage[] stages = Enum.GetValues<FixtureStage>();
        if (terminationOutstanding)
            return stages.Select(stage => new FixtureStageObservation(stage, FixtureStageStatus.NonQuiescent)).ToArray();
        // Injected operations are for deterministic tests only. Real reads stay relative
        // to a pinned directory handle and never follow the final root/marker link.
        if (getAttributes != null || openRead != null)
            return stages.Select(stage => ObserveOwnedStage(stage, () =>
            {
                string path = Path.Combine(ownedRoot, StageToken(stage) + ".stage");
                var attributes = (getAttributes ?? File.GetAttributes)(path);
                var rejected = RejectedAttributes(attributes);
                if (rejected != null) return new(stage, rejected.Value);
                using Stream stream = (openRead ?? File.OpenRead)(path);
                return ReadOwnedStage(stage, stream);
            })).ToArray();

        SafeFileHandle root;
        try { root = OpenOwnedRoot(ownedRoot); }
        catch (FileNotFoundException) { return StageFailures(stages, FixtureStageStatus.Missing); }
        catch (DirectoryNotFoundException) { return StageFailures(stages, FixtureStageStatus.Missing); }
        catch (OwnedLinkException) { return StageFailures(stages, FixtureStageStatus.ReparseRejected); }
        catch (UnauthorizedAccessException) { return StageFailures(stages, FixtureStageStatus.AccessError); }
        catch (IOException) { return StageFailures(stages, FixtureStageStatus.IoError); }
        using (root)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(root); }
            catch (UnauthorizedAccessException) { return StageFailures(stages, FixtureStageStatus.AccessError); }
            catch (IOException) { return StageFailures(stages, FixtureStageStatus.IoError); }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return StageFailures(stages, FixtureStageStatus.ReparseRejected);
            if ((attributes & FileAttributes.Directory) == 0)
                return StageFailures(stages, FixtureStageStatus.DirectoryRejected);
            return stages.Select(stage => ObserveOwnedStage(stage, () =>
            {
                using SafeFileHandle handle = OpenOwnedMarker(root, StageToken(stage) + ".stage");
                var rejected = RejectedAttributes(File.GetAttributes(handle));
                if (rejected != null) return new(stage, rejected.Value);
                using var stream = new FileStream(handle, FileAccess.Read, 1, false);
                return ReadOwnedStage(stage, stream);
            })).ToArray();
        }
    }

    static FixtureStageObservation[] StageFailures(FixtureStage[] stages, FixtureStageStatus status)
        => stages.Select(stage => new FixtureStageObservation(stage, status)).ToArray();

    static FixtureStageStatus? RejectedAttributes(FileAttributes attributes)
        => (attributes & FileAttributes.ReparsePoint) != 0 ? FixtureStageStatus.ReparseRejected
            : (attributes & FileAttributes.Directory) != 0 ? FixtureStageStatus.DirectoryRejected : null;

    static FixtureStageObservation ObserveOwnedStage(FixtureStage stage, Func<FixtureStageObservation> read)
    {
        try { return read(); }
        catch (FileNotFoundException) { return new(stage, FixtureStageStatus.Missing); }
        catch (DirectoryNotFoundException) { return new(stage, FixtureStageStatus.Missing); }
        catch (OwnedLinkException) { return new(stage, FixtureStageStatus.ReparseRejected); }
        catch (UnauthorizedAccessException) { return new(stage, FixtureStageStatus.AccessError); }
        catch (IOException) { return new(stage, FixtureStageStatus.IoError); }
    }

    static FixtureStageObservation ReadOwnedStage(FixtureStage stage, Stream stream)
    {
        long length = stream.Length;
        if (length > OwnedFixtureStageCapBytes) return new(stage, FixtureStageStatus.Oversized);
        if (length < 0) return new(stage, FixtureStageStatus.Invalid);
        Span<byte> bytes = stackalloc byte[OwnedFixtureStageCapBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes[count..]);
            if (read == 0) break;
            count += read;
        }
        if (count > OwnedFixtureStageCapBytes) return new(stage, FixtureStageStatus.Oversized);
        if (count != length) return new(stage, FixtureStageStatus.Invalid);
        return ParseOwnedFixtureStage(stage, bytes[..count]);
    }

    internal static FixtureStage[] RequiredOwnedFixtureStages(FixtureMode mode, int count, bool terminationOutstanding)
    {
        ValidateOwnedCase(mode, count);
        if (terminationOutstanding || mode == FixtureMode.Timeout) return Array.Empty<FixtureStage>();
        if (mode is FixtureMode.Exit or FixtureMode.SignalReport)
            return new[] { FixtureStage.Ready, FixtureStage.ExitIntent };
        return count == OutputCapChars
            ? new[] { FixtureStage.Ready, FixtureStage.WriteStarted, FixtureStage.WriteCompleted, FixtureStage.ExitIntent }
            : new[] { FixtureStage.Ready, FixtureStage.WriteStarted };
    }

    static void ValidateOwnedCase(FixtureMode mode, int count)
    {
        bool valid = mode switch
        {
            FixtureMode.Stdout or FixtureMode.Stderr => count == OutputCapChars || count == OutputCapChars + 1,
            FixtureMode.Exit or FixtureMode.SignalReport or FixtureMode.Timeout => count == 0,
            _ => false,
        };
        if (!valid) throw new ArgumentException("Invalid owned fixture case.");
    }

    static string ExpectedOwnedClassification(FixtureMode mode, int count) => mode switch
    {
        FixtureMode.Exit => "compiler-nonzero",
        FixtureMode.SignalReport => "compiler-reported-frontend-signal",
        FixtureMode.Timeout => "timeout",
        _ => count == OutputCapChars ? "success" : "output-limit",
    };

    static void ValidateOwnedStages(IReadOnlyList<FixtureStageObservation> stages)
    {
        if (stages.Count != 5) throw new ArgumentException("Owned fixture requires exactly five stage observations.");
        for (int index = 0; index < stages.Count; index++)
        {
            var observation = stages[index];
            if (observation.Stage != (FixtureStage)index || !Enum.IsDefined(observation.Status)
                || observation.ElapsedMs < 0)
                throw new ArgumentException("Invalid owned fixture stage observation.");
        }
    }

    internal static string DescribeOwnedFixture(
        FixtureMode mode, int count, ProcessRunResult result, TimeSpan elapsed,
        IReadOnlyList<FixtureStageObservation> stages)
    {
        ValidateOwnedCase(mode, count);
        ValidateOwnedStages(stages);
        bool intervention = result.TimedOut || result.OutputLimitExceeded || result.Cancelled
            || result.TerminationFailed || (result.Started && !string.IsNullOrEmpty(result.ErrorMessage));
        string errorCategory = result.ErrorMessage switch
        {
            null or "" => "none",
            "Process output capture did not finish." => "capture-incomplete",
            "Process timed out after 1500 ms." or "Process timed out after 10000 ms." => "timeout",
            "Process output exceeded the 16384 character limit." => "output-limit",
            "Process was cancelled." => "cancelled",
            _ => "unrecognized",
        };
        string report = JsonSerializer.Serialize(new
        {
            mode = mode switch
            {
                FixtureMode.Exit => "exit", FixtureMode.SignalReport => "signal-report",
                FixtureMode.Timeout => "timeout", FixtureMode.Stdout => "stdout", _ => "stderr",
            },
            requestedCount = count,
            expectedClassification = ExpectedOwnedClassification(mode, count),
            classification = Classify(result, true),
            configuredTimeoutMs = mode == FixtureMode.Timeout ? 1_500 : 10_000,
            perStreamOutputCapChars = OutputCapChars,
            runnerCallElapsedMsIncludingDrainAndCleanup = elapsed.TotalMilliseconds,
            result.Started, result.TimedOut, result.OutputLimitExceeded, result.Cancelled, result.TerminationFailed,
            exitCode = result.ExitCode,
            exitCodeKind = intervention ? "synthetic-runner-result" : result.Started ? "driver-exit" : "not-started",
            capturedStdoutChars = result.Stdout?.Length ?? 0,
            capturedStderrChars = result.Stderr?.Length ?? 0,
            errorCategory,
            errorMessageChars = result.ErrorMessage?.Length ?? 0,
            stages = stages.Select(stage => new
            {
                stage = StageToken(stage.Stage), status = StageStatusToken(stage.Status),
                childLocalElapsedMs = stage.ElapsedMs,
            }).ToArray(),
        });
        if (report.Length > OwnedFixtureReportCapChars)
            throw new InvalidOperationException("Owned fixture report exceeds its explicit budget.");
        return report;
    }

    internal static void AssertOwnedFixtureContract(
        FixtureMode mode, int count, ProcessRunResult result, IReadOnlyList<FixtureStageObservation> stages)
    {
        ValidateOwnedCase(mode, count);
        ValidateOwnedStages(stages);
        Xunit.Assert.Equal(ExpectedOwnedClassification(mode, count), Classify(result, true));
        Xunit.Assert.True(result.Started);
        Xunit.Assert.False(result.TerminationFailed);
        Xunit.Assert.False(result.Cancelled);
        string stdout = result.Stdout ?? "";
        string stderr = result.Stderr ?? "";
        Xunit.Assert.InRange(stdout.Length, 0, OutputCapChars);
        Xunit.Assert.InRange(stderr.Length, 0, OutputCapChars);
        if (mode == FixtureMode.Stdout) Xunit.Assert.Equal(0, stderr.Length);
        else Xunit.Assert.Equal(0, stdout.Length);
        if (mode is FixtureMode.Stdout or FixtureMode.Stderr)
        {
            Xunit.Assert.Equal(count > OutputCapChars, result.OutputLimitExceeded);
            Xunit.Assert.False(result.TimedOut);
            if (count == OutputCapChars)
                Xunit.Assert.Equal(count, mode == FixtureMode.Stdout ? stdout.Length : stderr.Length);
        }
        else if (mode == FixtureMode.SignalReport)
        {
            Xunit.Assert.True(stderr == "clang frontend command failed due to signal",
                "Owned fixture signal stream does not match its fixed contract.");
            Xunit.Assert.Equal(1, result.ExitCode);
        }
        else
        {
            Xunit.Assert.Equal(0, stderr.Length);
            if (mode == FixtureMode.Timeout) Xunit.Assert.True(result.TimedOut);
            else Xunit.Assert.Equal(42, result.ExitCode);
        }
        foreach (var required in RequiredOwnedFixtureStages(mode, count, result.TerminationFailed))
        {
            Xunit.Assert.Equal(FixtureStageStatus.Present, stages[(int)required].Status);
            Xunit.Assert.True(stages[(int)required].ElapsedMs.HasValue,
                "Owned fixture required stage has no child-local elapsed observation.");
        }
    }

    internal static void EmitOwnedFixture(string report, Exception? primary, Action<string> output)
    {
        if (report.Length > OwnedFixtureReportCapChars)
            throw new ArgumentException("Owned fixture output exceeds its explicit budget.");
        try { output(report); }
        catch (InvalidOperationException) when (primary != null)
        {
            RecordOwnedSecondary(primary, "owned-fixture:output-rejected");
        }
    }

    internal static void CleanupOwnedFixture(
        Exception? primary, bool terminationOutstanding, Action deleteOwnedRoot, Action<string> output)
    {
        string? category = terminationOutstanding ? "owned-fixture:cleanup:retained" : null;
        void Delete()
        {
            try { deleteOwnedRoot(); }
            catch (UnauthorizedAccessException)
            {
                category = "owned-fixture:cleanup:access-error";
                throw;
            }
            catch (IOException)
            {
                category = "owned-fixture:cleanup:io-error";
                throw;
            }
        }
        void Log(string _)
        {
            string message = category ?? throw new InvalidOperationException("Owned cleanup category is unavailable.");
            if (primary != null) RecordOwnedSecondary(primary, message);
            EmitOwnedFixture(message, primary, output);
        }
        Cleanup(primary != null, terminationOutstanding, Delete, Log);
    }

    static void RecordOwnedSecondary(Exception primary, string category)
    {
        string[] allowed =
        {
            "owned-fixture:output-rejected", "owned-fixture:cleanup:retained",
            "owned-fixture:cleanup:access-error", "owned-fixture:cleanup:io-error",
        };
        // Reconstruct from the finite vocabulary, never from arbitrary existing Data text.
        string? existing = primary.Data[OwnedFixtureSecondaryKey] as string;
        if (existing?.Length > OwnedFixtureReportCapChars) existing = null;
        var categories = allowed.Where(value => value == category
            || (existing != null && existing.Split(';').Contains(value, StringComparer.Ordinal)));
        primary.Data[OwnedFixtureSecondaryKey] = string.Join(";", categories);
    }

    sealed class OwnedLinkException : IOException { }

    internal static void DeleteOwnedFixtureRoot(string ownedRoot)
    {
        using SafeFileHandle root = OpenOwnedRoot(ownedRoot, true);
        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            throw new IOException("Owned fixture cleanup rejected the root handle.");
        var names = Enum.GetValues<FixtureStage>()
            .SelectMany(stage => new[] { StageToken(stage) + ".stage", StageToken(stage) + ".stage.tmp" })
            .Append(OperatingSystem.IsWindows() ? "fake.ps1" : "fake.py");
        foreach (string name in names)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using SafeFileHandle file = OpenWindowsOwnedFile(root, name, false, true);
                }
                else
                {
                    bool added = false;
                    try
                    {
                        root.DangerousAddRef(ref added);
                        if (UnlinkAtPosix(root.DangerousGetHandle().ToInt32(), name, 0) != 0)
                            ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
                    }
                    finally { if (added) root.DangerousRelease(); }
                }
            }
            catch (FileNotFoundException) { }
        }
        if (OperatingSystem.IsWindows())
        {
            var disposition = new NativeDisposition { DeleteFile = 1 };
            int status = NtSetInformationFile(root, out _, ref disposition, 1, 13);
            if (status < 0) ThrowOwnedWindowsError(status);
        }
        else
        {
            // Non-recursive rmdir cannot traverse a substituted link or delete unknown
            // children. All file deletion above used the pinned owned directory.
            Directory.Delete(ownedRoot);
        }
    }

    static SafeFileHandle OpenOwnedRoot(string root, bool delete = false)
    {
        if (OperatingSystem.IsWindows())
            return OpenWindowsOwnedFile(null, @"\??\" + Path.GetFullPath(root), true, delete);
        return OpenPosixOwnedFile(-1, Path.GetFullPath(root));
    }

    static SafeFileHandle OpenOwnedMarker(SafeFileHandle root, string name)
    {
        if (OperatingSystem.IsWindows()) return OpenWindowsOwnedFile(root, name, false);
        bool added = false;
        try
        {
            root.DangerousAddRef(ref added);
            return OpenPosixOwnedFile(root.DangerousGetHandle().ToInt32(), name);
        }
        finally { if (added) root.DangerousRelease(); }
    }

    static SafeFileHandle OpenPosixOwnedFile(int directory, string name)
    {
        // O_NOFOLLOW rejects a link in the final component; openat pins the parent.
        // O_NONBLOCK prevents a substituted FIFO from hanging before Length rejects it.
        int flags = OperatingSystem.IsLinux() ? 0x20000 | 0x80000 | 0x800
            : OperatingSystem.IsMacOS() ? 0x100 | 0x1000000 | 0x4
            : throw new PlatformNotSupportedException("Owned fixture no-follow reads are unsupported on this platform.");
        int descriptor = directory == -1 ? OpenPosix(name, flags) : OpenAtPosix(directory, name, flags);
        if (descriptor >= 0) return new SafeFileHandle((IntPtr)descriptor, true);
        ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        throw new InvalidOperationException("Unreachable owned fixture open result.");
    }

    static void ThrowOwnedPosixError(int error)
    {
        if (error == (OperatingSystem.IsMacOS() ? 62 : 40)) throw new OwnedLinkException();
        if (error == 2) throw new FileNotFoundException("Owned fixture file is missing.");
        if (error is 1 or 13) throw new UnauthorizedAccessException("Owned fixture access failed.");
        throw new IOException("Owned fixture handle acquisition failed.");
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    static extern int OpenPosix([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    static extern int OpenAtPosix(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    static extern int UnlinkAtPosix(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeUnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeObjectAttributes
    {
        internal uint Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeIoStatus
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeDisposition
    {
        internal byte DeleteFile;
    }

    static SafeFileHandle OpenWindowsOwnedFile(SafeFileHandle? root, string name, bool directory, bool delete = false)
    {
        int byteCount = checked(name.Length * 2);
        if (byteCount > ushort.MaxValue - 2)
            throw new IOException("Owned fixture filename exceeds the native handle budget.");
        IntPtr buffer = Marshal.StringToHGlobalUni(name);
        IntPtr unicode = IntPtr.Zero;
        bool added = false;
        try
        {
            unicode = Marshal.AllocHGlobal(Marshal.SizeOf<NativeUnicodeString>());
            Marshal.StructureToPtr(new NativeUnicodeString
            {
                Length = (ushort)byteCount, MaximumLength = (ushort)(byteCount + 2), Buffer = buffer,
            }, unicode, false);
            if (root != null) root.DangerousAddRef(ref added);
            var attributes = new NativeObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<NativeObjectAttributes>(),
                RootDirectory = root?.DangerousGetHandle() ?? IntPtr.Zero,
                ObjectName = unicode, Attributes = 0x40,
            };
            // FILE_OPEN_REPARSE_POINT, relative to the pinned root. Exclude
            // FILE_SHARE_DELETE so the root cannot be renamed during the snapshot.
            uint access = delete && !directory ? 0x110080u : delete ? 0x80110000u : 0x80100000u;
            uint options = 0x200000 | 0x20 | (directory ? 1u : 0u);
            if (delete && !directory) options |= 0x1000 | 0x40; // Delete-on-close, non-directory.
            int status = NtCreateFile(out SafeFileHandle handle, access, ref attributes,
                out _, IntPtr.Zero, 0, 3, 1, options, IntPtr.Zero, 0);
            if (status >= 0) return handle;
            handle.Dispose();
            ThrowOwnedWindowsError(status);
            throw new InvalidOperationException("Unreachable owned fixture open result.");
        }
        finally
        {
            if (added) root!.DangerousRelease();
            if (unicode != IntPtr.Zero) Marshal.FreeHGlobal(unicode);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    static extern int NtCreateFile(
        out SafeFileHandle handle, uint access, ref NativeObjectAttributes attributes,
        out NativeIoStatus status, IntPtr allocationSize, uint fileAttributes, uint shareAccess,
        uint disposition, uint options, IntPtr extendedAttributes, uint extendedAttributesLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    static extern int NtSetInformationFile(
        SafeFileHandle handle, out NativeIoStatus status, ref NativeDisposition disposition,
        uint length, uint informationClass);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    static extern uint RtlNtStatusToDosError(int status);

    static void ThrowOwnedWindowsError(int status)
    {
        uint error = RtlNtStatusToDosError(status);
        if (error is 2 or 3) throw new FileNotFoundException("Owned fixture file is missing.");
        if (error == 5) throw new UnauthorizedAccessException("Owned fixture access failed.");
        throw new IOException("Owned fixture handle operation failed.");
    }

    static string Limit(string? value, int maximumChars = OutputCapChars)
    {
        value ??= "";
        return value.Length <= maximumChars ? value : value[..maximumChars];
    }
}
