using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        string[] path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        IEnumerable<string> search = OperatingSystem.IsWindows()
            ? path.Where(directory => directory.Length != 0).Select(directory => directory.Trim('"'))
            : path.Where(directory => directory.Length != 0);
        return ResolveFromSearch(candidate, prefix, search, OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? null : HasPosixExecuteAccess);
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

    static string Limit(string? value, int maximumChars = OutputCapChars)
    {
        value ??= "";
        return value.Length <= maximumChars ? value : value[..maximumChars];
    }
}
