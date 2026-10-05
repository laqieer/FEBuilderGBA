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

    internal enum OwnedEntryType { Directory, RegularFile, SymbolicLink, Other }
    internal readonly record struct OwnedDirectoryIdentity(
        ulong Device, ulong Inode, OwnedEntryType Type, uint Owner);
    internal enum OwnedCleanupCheckpoint { BeforeLeafMutation, BeforeFinalRootRemoval, BeforeFinalEnvelopeRemoval }

    internal interface IOwnedDirectoryHandle : IDisposable
    {
        bool IsClosed { get; }
    }

    internal interface IOwnedDirectoryOperations
    {
        OwnedDirectoryIdentity ReadIdentity(IOwnedDirectoryHandle handle);
        OwnedDirectoryIdentity? QueryChildNoFollow(IOwnedDirectoryHandle parent, string name);
        IReadOnlyList<string> ReadChildNames(IOwnedDirectoryHandle root);
        void DeleteLeaf(IOwnedDirectoryHandle root, string name);
        void RemoveDirectory(IOwnedDirectoryHandle parent, string name);
    }

    internal sealed class OwnedIdentityMismatchException() : IOException("owned-fixture:cleanup:identity-mismatch") { }

    // Identity checks and unlinkat are not atomic. The protected namespace and trusted,
    // quiescent payloads exclude other users, not arbitrary active same-UID mutation.
    internal sealed class OwnedFixtureDirectory(
        string rootPath, string envelopePath, string envelopeName,
        IOwnedDirectoryOperations operations,
        IOwnedDirectoryHandle anchor, OwnedDirectoryIdentity anchorIdentity,
        IOwnedDirectoryHandle envelope, OwnedDirectoryIdentity envelopeIdentity,
        IOwnedDirectoryHandle root, OwnedDirectoryIdentity rootIdentity) : IDisposable
    {
        internal const string RootName = "root";
        internal string RootPath { get; } = rootPath;
        internal string EnvelopePath { get; } = envelopePath;
        internal string EnvelopeName { get; } = envelopeName;
        internal IOwnedDirectoryOperations Operations { get; } = operations;
        internal IOwnedDirectoryHandle Anchor { get; } = anchor;
        internal OwnedDirectoryIdentity AnchorIdentity { get; } = anchorIdentity;
        internal IOwnedDirectoryHandle Envelope { get; } = envelope;
        internal OwnedDirectoryIdentity EnvelopeIdentity { get; } = envelopeIdentity;
        internal IOwnedDirectoryHandle Root { get; } = root;
        internal OwnedDirectoryIdentity RootIdentity { get; } = rootIdentity;

        readonly object lifecycleLock = new();
        bool disposed;
        bool deleted;

        internal void Validate(bool includeRoot = true)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Operations.ReadIdentity(Anchor) != AnchorIdentity
                || Operations.ReadIdentity(Envelope) != EnvelopeIdentity
                || Operations.QueryChildNoFollow(Anchor, EnvelopeName) != EnvelopeIdentity
                || (includeRoot && (Operations.ReadIdentity(Root) != RootIdentity
                    || Operations.QueryChildNoFollow(Envelope, RootName) != RootIdentity)))
                throw new OwnedIdentityMismatchException();
        }

        internal void Delete(Action<OwnedCleanupCheckpoint>? checkpoint = null)
        {
            lock (lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (deleted) return;
                checkpoint?.Invoke(OwnedCleanupCheckpoint.BeforeLeafMutation);
                Validate();
                // Enumerate only the retained directory; unknown children are never traversed.
                // Known leaves are removed even if an unknown child prevents final rmdir.
                bool unknownChild = false;
                foreach (string name in Operations.ReadChildNames(Root))
                {
                    if (!OwnedLeafNames.Contains(name, StringComparer.Ordinal))
                    {
                        unknownChild = true;
                        continue;
                    }
                    Validate();
                    try { Operations.DeleteLeaf(Root, name); }
                    catch (FileNotFoundException) { }
                }
                if (unknownChild) throw new IOException("Owned fixture cleanup rejected unknown children.");
                checkpoint?.Invoke(OwnedCleanupCheckpoint.BeforeFinalRootRemoval);
                Validate();
                Operations.RemoveDirectory(Envelope, RootName);
                checkpoint?.Invoke(OwnedCleanupCheckpoint.BeforeFinalEnvelopeRemoval);
                Validate(false);
                Operations.RemoveDirectory(Anchor, EnvelopeName);
                deleted = true;
            }
        }

        internal void Cleanup(
            Exception? primary, bool terminationOutstanding, Action<string> output,
            Action<OwnedCleanupCheckpoint>? checkpoint = null)
        {
            lock (lifecycleLock)
            {
                try { CleanupOwnedFixture(primary, terminationOutstanding, () => Delete(checkpoint), output); }
                finally { Dispose(); }
            }
        }

        internal FixtureStageObservation[] ReadStages(bool terminationOutstanding)
        {
            lock (lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                FixtureStage[] stages = Enum.GetValues<FixtureStage>();
                if (terminationOutstanding) return StageFailures(stages, FixtureStageStatus.NonQuiescent);
                Validate();
                return ReadPinnedOwnedStages(((NativeOwnedDirectoryHandle)Root).Handle, stages);
            }
        }

        public void Dispose()
        {
            lock (lifecycleLock)
            {
                if (disposed) return;
                disposed = true;
                try { Root.Dispose(); }
                finally
                {
                    try { Envelope.Dispose(); }
                    finally { Anchor.Dispose(); }
                }
            }
        }
    }

    internal static OwnedFixtureDirectory CreateOwnedFixtureDirectory(
        string trustedAnchor, Action<OwnedFixtureDirectory>? beforePublication = null)
    {
        RequireOwnedPosixAbi();
        string anchorPath = Path.GetFullPath(trustedAnchor);
        var operations = new NativeOwnedDirectoryOperations();
        NativeOwnedDirectoryHandle? anchor = null, envelope = null, root = null;
        OwnedFixtureDirectory? lifetime = null;
        OwnedDirectoryIdentity? anchorIdentity = null, envelopeIdentity = null, rootIdentity = null;
        bool transferred = false;
        bool envelopeCreated = false, rootCreated = false;
        string envelopeName = "febuilder-owned-" + Guid.NewGuid().ToString("N");
        try
        {
            anchor = OpenPosixDirectoryPath(anchorPath, true);
            anchor.Protection = DirectoryProtection.Anchor;
            anchorIdentity = operations.ReadIdentity(anchor);
            CreatePosixDirectory(anchor, envelopeName);
            envelopeCreated = true;
            envelopeIdentity = operations.QueryChildNoFollow(anchor, envelopeName)
                ?? throw new OwnedIdentityMismatchException();
            envelope = OpenPosixDirectory(anchor.Descriptor, envelopeName);
            envelope.Protection = DirectoryProtection.Private;
            if (operations.ReadIdentity(envelope) != envelopeIdentity)
                throw new OwnedIdentityMismatchException();
            CreatePosixDirectory(envelope, OwnedFixtureDirectory.RootName);
            rootCreated = true;
            rootIdentity = operations.QueryChildNoFollow(envelope, OwnedFixtureDirectory.RootName)
                ?? throw new OwnedIdentityMismatchException();
            root = OpenPosixDirectory(envelope.Descriptor, OwnedFixtureDirectory.RootName);
            root.Protection = DirectoryProtection.Private;
            if (operations.ReadIdentity(root) != rootIdentity)
                throw new OwnedIdentityMismatchException();
            string envelopePath = Path.Combine(anchorPath, envelopeName);
            lifetime = new OwnedFixtureDirectory(Path.Combine(envelopePath, OwnedFixtureDirectory.RootName),
                envelopePath, envelopeName, operations, anchor, anchorIdentity.Value,
                envelope, envelopeIdentity.Value, root, rootIdentity.Value);
            beforePublication?.Invoke(lifetime);
            lifetime.Validate();
            transferred = true;
            return lifetime;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Only entries created by this acquisition, with captured no-follow identities.
            // If identity cannot be established, retain rather than adopt a pathname.
            try
            {
                if ((envelopeCreated && envelopeIdentity == null) || (rootCreated && rootIdentity == null))
                    throw new OwnedIdentityMismatchException();
                if (envelope != null && rootIdentity != null)
                {
                    if (operations.ReadIdentity(anchor!) != anchorIdentity
                        || operations.QueryChildNoFollow(anchor!, envelopeName) != envelopeIdentity
                        || ReadPosixMetadata(envelope.Descriptor, null).Identity != envelopeIdentity
                        || (root != null && ReadPosixMetadata(root.Descriptor, null).Identity != rootIdentity)
                        || operations.QueryChildNoFollow(envelope, OwnedFixtureDirectory.RootName) != rootIdentity)
                        throw new OwnedIdentityMismatchException();
                    operations.RemoveDirectory(envelope, OwnedFixtureDirectory.RootName);
                }
                if (anchor != null && envelopeIdentity != null)
                {
                    if (operations.ReadIdentity(anchor) != anchorIdentity
                        || (envelope != null && ReadPosixMetadata(envelope.Descriptor, null).Identity != envelopeIdentity)
                        || operations.QueryChildNoFollow(anchor, envelopeName) != envelopeIdentity)
                        throw new OwnedIdentityMismatchException();
                    operations.RemoveDirectory(anchor, envelopeName);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new AggregateException("owned-fixture:acquisition:retained", failure, cleanup);
            }
            throw;
        }
        finally
        {
            // Ownership transfers only after the complete acquisition succeeds.
            if (!transferred)
            {
                if (lifetime != null) lifetime.Dispose();
                else
                {
                    try { root?.Dispose(); }
                    finally
                    {
                        try { envelope?.Dispose(); }
                        finally { anchor?.Dispose(); }
                    }
                }
            }
        }
    }

    internal static OwnedDirectoryIdentity ReadOwnedDirectoryIdentityForTest(string path)
    {
        RequireOwnedPosixAbi();
        string fullPath = Path.GetFullPath(path);
        using var parent = OpenPosixDirectoryPath(Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Owned fixture identity parent is unavailable."));
        return ReadPosixMetadata(parent.Descriptor, Path.GetFileName(fullPath)).Identity;
    }

    static readonly string[] OwnedLeafNames = Enum.GetValues<FixtureStage>()
        .SelectMany(stage => new[] { StageToken(stage) + ".stage", StageToken(stage) + ".stage.tmp" })
        .Append("fake.py").ToArray();

    enum DirectoryProtection { None, Anchor, Private }

    sealed class NativeOwnedDirectoryHandle(SafeFileHandle handle) : IOwnedDirectoryHandle
    {
        internal SafeFileHandle Handle { get; } = handle;
        internal DirectoryProtection Protection { get; set; }
        internal int Descriptor
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsClosed, this);
                return Handle.DangerousGetHandle().ToInt32();
            }
        }
        public bool IsClosed => Handle.IsClosed;
        public void Dispose() => Handle.Dispose();
    }

    readonly record struct PosixMetadata(OwnedDirectoryIdentity Identity, ushort Mode);

    sealed class NativeOwnedDirectoryOperations : IOwnedDirectoryOperations
    {
        public OwnedDirectoryIdentity ReadIdentity(IOwnedDirectoryHandle handle)
        {
            var native = (NativeOwnedDirectoryHandle)handle;
            var metadata = ReadPosixMetadata(native.Descriptor, null);
            if (native.Protection != DirectoryProtection.None)
            {
                VerifyProtection(metadata, native.Protection);
                VerifyNoDarwinAcl(native.Descriptor);
            }
            return metadata.Identity;
        }

        public OwnedDirectoryIdentity? QueryChildNoFollow(IOwnedDirectoryHandle parent, string name)
        {
            try { return ReadPosixMetadata(((NativeOwnedDirectoryHandle)parent).Descriptor, name).Identity; }
            catch (FileNotFoundException) { return null; }
        }

        public IReadOnlyList<string> ReadChildNames(IOwnedDirectoryHandle root)
            => ReadPosixChildNames((NativeOwnedDirectoryHandle)root);

        public void DeleteLeaf(IOwnedDirectoryHandle root, string name)
        {
            if (UnlinkAtPosix(((NativeOwnedDirectoryHandle)root).Descriptor, name, 0) != 0)
                ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        }

        public void RemoveDirectory(IOwnedDirectoryHandle parent, string name)
        {
            if (UnlinkAtPosix(((NativeOwnedDirectoryHandle)parent).Descriptor, name,
                OperatingSystem.IsLinux() ? 0x200 : 0x80) != 0)
                ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        }
    }

    static void RequireOwnedPosixAbi()
    {
        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64
                && RuntimeInformation.ProcessArchitecture != Architecture.Arm64))
            throw new PlatformNotSupportedException("Owned fixture native ABI is unsupported.");
        if (GetUid() != GetEffectiveUid())
            throw new PlatformNotSupportedException("Owned fixture differing user identities are unsupported.");
    }

    static void VerifyProtection(PosixMetadata metadata, DirectoryProtection protection)
    {
        uint uid = GetEffectiveUid();
        bool privateDirectory = metadata.Identity.Owner == uid && (metadata.Mode & 0xfff) == 0x1c0;
        bool stickyAnchor = protection == DirectoryProtection.Anchor
            && (metadata.Identity.Owner == 0 || metadata.Identity.Owner == uid)
            && (metadata.Mode & 0x200) != 0 && (metadata.Mode & 0x1c0) == 0x1c0;
        if (metadata.Identity.Type != OwnedEntryType.Directory || (!privateDirectory && !stickyAnchor))
            throw new OwnedIdentityMismatchException();
    }

    static NativeOwnedDirectoryHandle OpenPosixDirectoryPath(string path, bool protectPublication = false)
    {
        RequireOwnedPosixAbi();
        var current = OpenPosixDirectory(-1, "/");
        try
        {
            if (protectPublication) VerifyPublicationParent(current);
            foreach (string component in Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = OpenPosixDirectory(current.Descriptor, component);
                current.Dispose();
                current = next;
                if (protectPublication) VerifyPublicationParent(current);
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    static void VerifyPublicationParent(NativeOwnedDirectoryHandle directory)
    {
        var metadata = ReadPosixMetadata(directory.Descriptor, null);
        if (metadata.Identity.Type != OwnedEntryType.Directory
            || (metadata.Identity.Owner != 0 && metadata.Identity.Owner != GetEffectiveUid())
            || ((metadata.Mode & 0x12) != 0 && (metadata.Mode & 0x200) == 0))
            throw new OwnedIdentityMismatchException();
        VerifyNoDarwinAcl(directory.Descriptor);
    }

    static void VerifyNoDarwinAcl(int descriptor)
    {
        if (!OperatingSystem.IsMacOS()) return;
        // Darwin extended ACLs can grant rights independently of 0700/sticky mode bits.
        // Reject them, rather than assuming that a mode-only check proves privacy.
        IntPtr acl;
        try { acl = GetDarwinAcl(descriptor, 0x100); } // ACL_TYPE_EXTENDED, Libc/include/sys/acl.h.
        catch (EntryPointNotFoundException)
        {
            throw new PlatformNotSupportedException("Owned fixture native protection API is unavailable.");
        }
        if (acl == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == 2) return; // No FILESEC_ACL property on the verified, open descriptor.
            ThrowOwnedPosixError(error);
        }
        try
        {
            if (GetDarwinAclEntry(acl, 0, out _) == 0) throw new OwnedIdentityMismatchException();
            int error = Marshal.GetLastPInvokeError();
            // Apple's acl_get_entry reports EINVAL for FIRST_ENTRY on an empty ACL.
            if (error != 22) ThrowOwnedPosixError(error);
        }
        finally
        {
            if (FreeDarwinAcl(acl) != 0) ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        }
    }

    static NativeOwnedDirectoryHandle OpenPosixDirectory(int parent, string name)
    {
        int flags = OperatingSystem.IsLinux() ? 0x10000 | 0x20000 | 0x80000
            : 0x100000 | 0x100 | 0x1000000;
        int descriptor = parent == -1 ? OpenPosix(name, flags) : OpenAtPosix(parent, name, flags);
        if (descriptor < 0) ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        return new(new SafeFileHandle((IntPtr)descriptor, true));
    }

    static void CreatePosixDirectory(NativeOwnedDirectoryHandle parent, string name)
    {
        if (MkdirAtPosix(parent.Descriptor, name, 0x1c0) != 0)
            ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
    }

    // Linux UAPI statx is fixed-width, 256 bytes, independent of libc struct stat.
    // Darwin LP64 __DARWIN_STRUCT_STAT64 is 144 bytes on x86_64 and arm64.
    // References: linux/include/uapi/linux/stat.h; xnu/bsd/sys/stat.h.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    struct LinuxStatx
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(20)] internal uint Owner;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    struct DarwinStat
    {
        [FieldOffset(0)] internal uint Device;
        [FieldOffset(4)] internal ushort Mode;
        [FieldOffset(8)] internal ulong Inode;
        [FieldOffset(16)] internal uint Owner;
    }

    static PosixMetadata ReadPosixMetadata(int parent, string? name)
    {
        RequireOwnedPosixAbi();
        ulong device, inode;
        uint owner;
        ushort mode;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                const uint required = 0x10b; // TYPE | MODE | UID | INO; device is unconditional.
                if (StatxPosix(parent, name ?? "", name == null ? 0x1000 : 0x100,
                    required, out var stat) != 0)
                    ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
                if ((stat.Mask & required) != required)
                    throw new PlatformNotSupportedException("Owned fixture native identity fields are unavailable.");
                device = ((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor;
                inode = stat.Inode;
                owner = stat.Owner;
                mode = stat.Mode;
            }
            else
            {
                bool x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
                DarwinStat stat;
                int result = name == null
                    ? (x64 ? FstatDarwinInode64(parent, out stat) : FstatDarwin(parent, out stat))
                    : (x64 ? FstatAtDarwinInode64(parent, name, out stat, 0x20)
                        : FstatAtDarwin(parent, name, out stat, 0x20));
                if (result != 0) ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
                device = stat.Device;
                inode = stat.Inode;
                owner = stat.Owner;
                mode = stat.Mode;
            }
        }
        catch (EntryPointNotFoundException)
        {
            throw new PlatformNotSupportedException("Owned fixture native identity API is unavailable.");
        }
        var type = (mode & 0xf000) switch
        {
            0x4000 => OwnedEntryType.Directory, 0x8000 => OwnedEntryType.RegularFile,
            0xa000 => OwnedEntryType.SymbolicLink, _ => OwnedEntryType.Other,
        };
        return new(new(device, inode, type, owner), mode);
    }

    static IReadOnlyList<string> ReadPosixChildNames(NativeOwnedDirectoryHandle root)
    {
        // A separate open file description keeps enumeration offsets off the lifetime pin.
        using var scan = OpenPosixDirectory(root.Descriptor, ".");
        bool darwinInode64 = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
        IntPtr directory = darwinInode64 ? FdOpenDirDarwinInode64(scan.Descriptor) : FdOpenDirPosix(scan.Descriptor);
        if (directory == IntPtr.Zero)
            ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        scan.Handle.SetHandleAsInvalid(); // fdopendir owns the descriptor on success.
        try
        {
            var names = new List<string>();
            while (true)
            {
                // .NET clears errno before, and captures it after, SetLastError P/Invokes.
                IntPtr entry = darwinInode64 ? ReadDirDarwinInode64(directory) : ReadDirPosix(directory);
                if (entry == IntPtr.Zero)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != 0) ThrowOwnedPosixError(error);
                    return names;
                }
                // glibc/musl LP64 dirent: name@19; Darwin INODE64 dirent: name@21.
                int offset = OperatingSystem.IsLinux() ? 19 : 21;
                int recordLength = (ushort)Marshal.ReadInt16(entry, 16);
                int maximum = OperatingSystem.IsLinux() ? 256 : 1024;
                if (recordLength <= offset || recordLength > offset + maximum + 8)
                    throw new IOException("Owned fixture directory record is invalid.");
                int length = 0;
                while (length < Math.Min(maximum, recordLength - offset)
                    && Marshal.ReadByte(entry, offset + length) != 0) length++;
                if (length == Math.Min(maximum, recordLength - offset))
                    throw new IOException("Owned fixture directory name is invalid.");
                string name = Marshal.PtrToStringUTF8(IntPtr.Add(entry, offset), length)!;
                if (name is "." or "..") continue;
                names.Add(name);
            }
        }
        finally
        {
            if (CloseDirPosix(directory) != 0)
                ThrowOwnedPosixError(Marshal.GetLastPInvokeError());
        }
    }

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    static extern int MkdirAtPosix(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    static extern int StatxPosix(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    static extern int FstatDarwinInode64(int descriptor, out DarwinStat stat);
    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    static extern int FstatDarwin(int descriptor, out DarwinStat stat);
    [DllImport("libc", EntryPoint = "fstatat$INODE64", SetLastError = true)]
    static extern int FstatAtDarwinInode64(int descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out DarwinStat stat, int flags);
    [DllImport("libc", EntryPoint = "fstatat", SetLastError = true)]
    static extern int FstatAtDarwin(int descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out DarwinStat stat, int flags);
    [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
    static extern IntPtr FdOpenDirPosix(int descriptor);
    [DllImport("libc", EntryPoint = "fdopendir$INODE64", SetLastError = true)]
    static extern IntPtr FdOpenDirDarwinInode64(int descriptor);
    [DllImport("libc", EntryPoint = "readdir", SetLastError = true)]
    static extern IntPtr ReadDirPosix(IntPtr directory);
    [DllImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)]
    static extern IntPtr ReadDirDarwinInode64(IntPtr directory);
    [DllImport("libc", EntryPoint = "closedir", SetLastError = true)]
    static extern int CloseDirPosix(IntPtr directory);
    [DllImport("libc", EntryPoint = "acl_get_fd_np", SetLastError = true)]
    static extern IntPtr GetDarwinAcl(int descriptor, int type);
    [DllImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)]
    static extern int GetDarwinAclEntry(IntPtr acl, int entryId, out IntPtr entry);
    [DllImport("libc", EntryPoint = "acl_free", SetLastError = true)]
    static extern int FreeDarwinAcl(IntPtr acl);

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
            return ReadPinnedOwnedStages(root, stages);
        }
    }

    static FixtureStageObservation[] ReadPinnedOwnedStages(SafeFileHandle root, FixtureStage[] stages)
        => stages.Select(stage => ObserveOwnedStage(stage, () =>
        {
            using SafeFileHandle handle = OpenOwnedMarker(root, StageToken(stage) + ".stage");
            var rejected = RejectedAttributes(File.GetAttributes(handle));
            if (rejected != null) return new(stage, rejected.Value);
            using var stream = new FileStream(handle, FileAccess.Read, 1, false);
            return ReadOwnedStage(stage, stream);
        })).ToArray();

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
            catch (OwnedIdentityMismatchException)
            {
                category = "owned-fixture:cleanup:identity-mismatch";
                throw;
            }
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
            "owned-fixture:cleanup:identity-mismatch",
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
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Owned fixture POSIX cleanup requires creation-time ownership.");
        using SafeFileHandle root = OpenOwnedRoot(ownedRoot, true);
        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            throw new IOException("Owned fixture cleanup rejected the root handle.");
        foreach (string name in OwnedLeafNames.Where(name => name != "fake.py").Append("fake.ps1"))
        {
            try
            {
                using SafeFileHandle file = OpenWindowsOwnedFile(root, name, false, true);
            }
            catch (FileNotFoundException) { }
        }
        var disposition = new NativeDisposition { DeleteFile = 1 };
        int status = NtSetInformationFile(root, out _, ref disposition, 1, 13);
        if (status < 0) ThrowOwnedWindowsError(status);
    }

    static SafeFileHandle OpenOwnedRoot(string root, bool delete = false)
    {
        if (OperatingSystem.IsWindows())
            return OpenWindowsOwnedFile(null, @"\??\" + Path.GetFullPath(root), true, delete);
        string fullPath = Path.GetFullPath(root);
        using var parent = OpenPosixDirectoryPath(Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Owned fixture root parent is unavailable."));
        return OpenPosixOwnedFile(parent.Descriptor, Path.GetFileName(fullPath));
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
        if (error == (OperatingSystem.IsMacOS() ? 78 : 38))
            throw new PlatformNotSupportedException("Owned fixture native operation is unavailable.");
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
