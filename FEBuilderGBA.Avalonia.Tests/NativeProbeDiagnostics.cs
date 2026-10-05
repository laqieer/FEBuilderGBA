using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA.Avalonia.Tests;

internal static class NativeProbeDiagnostics
{
    internal const int MaximumRecords = 12, MaximumRecordBytes = 512, MaximumBytes = 8_192;
    internal const string TokenVariable = "FEBUILDER_NATIVE_PROBE_TOKEN";
    internal const string DirectoryVariable = "FEBUILDER_NATIVE_PROBE_DIRECTORY";
    internal const string OwnerVariable = "FEBUILDER_NATIVE_PROBE_OWNER_PID";
    static readonly object Sync = new();
    static readonly string[] Stages =
        ["managed-entry", "fixture-start", "fixture-end", "probe-entry", "acquire-start", "acquire-end", "publish-start", "publish-end"];
    static Activation? active;
    internal sealed record Activation(Session? Session, string? Error);

    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            active = Activate(Environment.GetEnvironmentVariable(TokenVariable),
                Environment.GetEnvironmentVariable(DirectoryVariable), Environment.GetEnvironmentVariable(OwnerVariable));
            Mark("managed-entry");
        }
        catch (Exception ex)
        {
            active = new(null, "diagnostic-init-error: " + Error(ex));
            Emit(active.Error!);
        }
    }

    internal static Activation Activate(string? token, string? directory, string? owner)
    {
        if (token == null && directory == null && owner == null) return new(null, null);
        try
        {
            ValidateToken(token);
            if (!int.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
                throw new InvalidDataException("Invalid diagnostic owner PID.");
            ValidateDirectory(directory!, token!);
            var session = new Session(directory!, token!, pid);
            session.ValidateHeader();
            return new(session, null);
        }
        catch (Exception ex)
        {
            return new(null, "diagnostic-init-error: " + Error(ex));
        }
    }

    internal static void Mark(string stage)
    {
        if (active == null) return;
        if (active.Error != null)
        {
            Emit(active.Error);
            return;
        }
        if (active.Session is { } session) Mark(session, stage, Emit);
    }

    internal static void Mark(Session session, string stage, Action<string> emit)
    {
        try { session.Record(stage); }
        catch (Exception ex)
        {
            string error = "diagnostic-stage-error: " + Error(ex);
            // Never throw from instrumentation or obscure the fixture/probe's original exception.
            try { session.RetainError(error); }
            catch (Exception secondary) { error += "; retention-error: " + Error(secondary); }
            emit(error);
        }
    }

    internal static IDisposable? FixtureStages()
    {
        if (active?.Session == null && active?.Error == null) return null;
        Mark("fixture-start");
        return new FixtureMarker();
    }

    sealed class FixtureMarker : IDisposable
    {
        public void Dispose() => Mark("fixture-end");
    }

    static void Emit(string error)
    {
        try { Console.Error.WriteLine(error); }
        catch (Exception ex) { active = new(active?.Session, error + "; stderr-error: " + Error(ex)); }
    }

    internal static void ValidateLeaseRoot(string root) =>
        ValidateDirectory(Path.Combine(Path.GetFullPath(root), "native-probe-" + Guid.Empty.ToString("N")),
            Guid.Empty.ToString("N"), allowMissingLeaf: true);

    internal static Session Prepare(string leaseRoot, string token, int owner)
    {
        ValidateToken(token);
        if (owner <= 0) throw new InvalidDataException("Invalid diagnostic owner PID.");
        ValidateLeaseRoot(leaseRoot);
        // Keep diagnostic files outside the lease fixture's mutation/snapshot assertions.
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "native-probe-" + token);
        ValidateDirectory(directory, token, allowMissingLeaf: true);
        Directory.CreateDirectory(directory);
        ValidateDirectory(directory, token);
        var session = new Session(directory, token, owner);
        using var file = session.Open(session.StagePath, FileMode.CreateNew);
        byte[] header = Encoding.UTF8.GetBytes(token + "|" + owner.ToString(CultureInfo.InvariantCulture) + "\n");
        file.Write(header);
        return session;
    }

    static void ValidateToken(string? token)
    {
        if (token == null || !Guid.TryParseExact(token, "N", out Guid parsed) || parsed.ToString("N") != token)
            throw new InvalidDataException("Invalid diagnostic launch token.");
    }

    static void ValidateDirectory(string directory, string token, bool allowMissingLeaf = false)
    {
        string basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        string full = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(basePath + Path.DirectorySeparatorChar, comparison) ||
            !string.Equals(Path.GetFileName(full), "native-probe-" + token, StringComparison.Ordinal))
            throw new InvalidDataException("Diagnostic root is not token-owned below assembly TestResults.");
        // Check every existing ancestor, including the assembly directory, rather than just the leaf.
        for (string? path = full; path != null; path = Path.GetDirectoryName(path))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                    throw new InvalidDataException("Diagnostic path redirect/non-directory rejected.");
                if (!ProjectionFileSystemSafety.TryValidateDirectory(path, out _))
                    throw new InvalidDataException("Diagnostic directory inode type rejected.");
            }
            catch (DirectoryNotFoundException) when (allowMissingLeaf) { }
            catch (FileNotFoundException) when (allowMissingLeaf) { }
        }
    }

    internal static void ConfigureTrace(ProcessStartInfo start)
    {
        foreach (string prefix in new[] { "DOTNET_HOST", "COREHOST" })
        {
            start.Environment.Remove(prefix + "_TRACEFILE");
            start.Environment[prefix + "_TRACE"] = "1";
            start.Environment[prefix + "_TRACE_VERBOSITY"] = "4";
        }
    }

    internal static string Error(Exception ex) => ex.GetType().Name + "; hresult=" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle OpenWindows(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    static extern int OpenUnix([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    static extern int LockUnix(int descriptor, int operation);

    internal static FileStreamOptions UnixCreationOptions()
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unix diagnostic creation options unavailable on Windows.");
        return new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 512,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
    }

    static FileStream OpenWritableNoFollow(string path, FileMode mode, FileSystemEntryIdentity? previous)
    {
        if (mode is not (FileMode.Open or FileMode.CreateNew))
            throw new InvalidDataException("Unsupported diagnostic open mode.");
        SafeFileHandle handle;
        FileStream? created = null;
        if (OperatingSystem.IsWindows())
        {
            // No sharing, no truncation, and open the reparse entry itself rather than its target.
            handle = OpenWindows(path, 0xC0000000, 0, IntPtr.Zero,
                mode == FileMode.CreateNew ? 1u : 3u, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException("Diagnostic writable no-follow acquisition failed; native-error=" + error);
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (mode == FileMode.CreateNew)
            {
                // Runtime O_CREAT|O_EXCL rejects every existing leaf, including dangling links.
                // It also supplies the creation mode through the platform-correct native ABI.
                created = new FileStream(path, UnixCreationOptions());
                handle = created.SafeFileHandle;
            }
            else
            {
                bool mac = OperatingSystem.IsMacOS();
                // O_RDWR | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC; never O_CREAT or O_TRUNC.
                int flags = 2 | (mac ? 0x100 | 0x4 | 0x1000000 : 0x20000 | 0x800 | 0x80000);
                int descriptor = OpenUnix(path, flags);
                if (descriptor < 0)
                    throw new IOException("Diagnostic writable no-follow acquisition failed; native-error=" +
                        Marshal.GetLastPInvokeError());
                handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            }
        }
        else throw new PlatformNotSupportedException("Diagnostic writable no-follow acquisition unavailable.");
        try
        {
            var opened = ProjectionFileSystemSafety.InspectOpenedRegularFile(handle, "diagnostic file", true);
            if (previous is { } identity && !identity.Equals(opened.Identity))
                throw new InvalidDataException("Diagnostic file identity changed.");
            if (!OperatingSystem.IsWindows() && LockUnix(handle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
                throw new IOException("Diagnostic exclusive lock failed; native-error=" + Marshal.GetLastPInvokeError());
            return created ?? new FileStream(handle, FileAccess.ReadWrite, 512, isAsync: false);
        }
        catch
        {
            if (created != null) created.Dispose();
            else handle.Dispose();
            throw;
        }
    }

    internal static string ExceptionText(Exception ex, string root, int limit) =>
        Redact(ex.GetType().Name + ": " + Prefix(ex.Message, limit) + "\n" + Prefix(ex.StackTrace ?? "<stack unavailable>", limit), root, limit);

    static string Prefix(string value, int length) => value[..Math.Min(value.Length, length)];

    internal static string Redact(string text, string root, int limit)
    {
        // Bound before regex allocation; redact absolute paths even when capture ended mid-path.
        int bound = Math.Min(limit, 40_000);
        bool truncated = text.Length > bound;
        text = Prefix(text, bound);
        // Conservatively remove the remainder of a path-bearing line, including spaces/partial suffixes.
        text = Regex.Replace(text, @"(?:[A-Za-z]:[\\/]|\\\\|/|(?<![A-Za-z0-9])[A-Za-z]:$)[^\r\n]*", "<owned-path>",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (string path in new[] { root, typeof(NativeProbeDiagnostics).Assembly.Location,
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        {
            if (path.Length == 0) continue;
            text = text.Replace(path, "<owned-path>", StringComparison.OrdinalIgnoreCase)
                .Replace(path.Replace('\\', '/'), "<owned-path>", StringComparison.OrdinalIgnoreCase);
        }
        return text + (truncated ? "\n[diagnostics truncated]" : "");
    }

    internal sealed class Session(string directory, string token, int owner)
    {
        internal Action<string>? BeforeWritableOpen;
        internal Action<FileStream>? BeforeBoundedRead;
        internal string DirectoryPath => directory;
        internal string StagePath => Path.Combine(directory, "native-probe-stages");
        string ErrorPath => Path.Combine(directory, "native-probe-errors");
        string Header => token + "|" + owner.ToString(CultureInfo.InvariantCulture);

        internal FileStream Open(string path, FileMode mode)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    ValidateDirectory(directory, token);
                    FileSystemEntryIdentity? identity = null;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                            throw new InvalidDataException("Diagnostic file redirect/non-regular file rejected.");
                        using var observed = ProjectionFileSystemSafety.OpenRegularFileForRead(path);
                        identity = ProjectionFileSystemSafety.InspectOpenedRegularFile(
                            observed.SafeFileHandle, "diagnostic file", true).Identity;
                    }
                    catch (FileNotFoundException) when (mode != FileMode.Open) { }
                    catch (DirectoryNotFoundException) when (mode != FileMode.Open) { }
                    var actualMode = mode == FileMode.OpenOrCreate
                        ? identity.HasValue ? FileMode.Open : FileMode.CreateNew : mode;
                    Interlocked.Exchange(ref BeforeWritableOpen, null)?.Invoke(path);
                    var file = OpenWritableNoFollow(path, actualMode, identity);
                    try
                    {
                        ValidateDirectory(directory, token);
                        return file;
                    }
                    catch { file.Dispose(); throw; }
                }
                catch (IOException) when (attempt < 20) { Thread.Sleep(5); }
            }
        }

        string ReadBounded(FileStream file, int limit = MaximumBytes, string channel = "stage")
        {
            long length = file.Length;
            if (length > limit) throw new InvalidDataException("truncated: " + channel + " byte cap exceeded.");
            Interlocked.Exchange(ref BeforeBoundedRead, null)?.Invoke(file);
            var bytes = new byte[(int)length];
            try { file.ReadExactly(bytes); }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("truncated: diagnostic file length changed during bounded read.");
            }
            if (file.Length != length)
                throw new InvalidDataException("truncated: diagnostic file length changed during bounded read.");
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        string[] Parse(string text)
        {
            string[] lines = text.Split('\n');
            if (lines.Length > MaximumRecords + 3) throw new InvalidDataException("truncated: stage count cap exceeded.");
            if (lines.Skip(1).Count(line => line.Length != 0 && line != "truncated") > MaximumRecords)
                throw new InvalidDataException("truncated: stage count cap exceeded.");
            if (lines[0] != Header)
                throw new InvalidDataException(lines[0].Contains('|') ? "mismatch: token/owner" : "corrupt: stage header");
            foreach (string line in lines.Skip(1).Where(line => line.Length != 0))
            {
                if (Encoding.UTF8.GetByteCount(line) > MaximumRecordBytes) throw new InvalidDataException("corrupt: record cap");
                if (line == "truncated") continue;
                string[] fields = line.Split('|');
                if (fields.Length != 8 || fields[0] != token || fields[1] != owner.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("mismatch/corrupt: stage record");
                if (!int.TryParse(fields[2], out int pid) || pid <= 0 || !Stages.Contains(fields[3]) ||
                    !DateTimeOffset.TryParseExact(fields[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                    fields.Skip(5).Any(value => value.Length > 96 || !Regex.IsMatch(value, @"^[A-Za-z0-9 .()_-]+$")))
                    throw new InvalidDataException("corrupt: stage metadata");
            }
            return lines;
        }

        internal void ValidateHeader()
        {
            lock (Sync)
            {
                using var file = Open(StagePath, FileMode.Open);
                Parse(ReadBounded(file));
            }
        }

        internal void Record(string stage)
        {
            if (!Stages.Contains(stage)) throw new InvalidDataException("Unknown diagnostic stage.");
            lock (Sync)
            {
                using var file = Open(StagePath, FileMode.Open);
                var lines = Parse(ReadBounded(file));
                if (lines.Contains("truncated")) return;
                string record;
                if (lines.Count(line => line.Length != 0) - 1 >= MaximumRecords)
                    record = "truncated\n";
                else
                    record = $"{token}|{owner}|{Environment.ProcessId}|{stage}|{DateTimeOffset.UtcNow:O}|" +
                        $"{RuntimeInformation.FrameworkDescription}|{Environment.Version}|{RuntimeInformation.ProcessArchitecture}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(record);
                if (bytes.Length > MaximumRecordBytes || file.Length + bytes.Length > MaximumBytes)
                    throw new InvalidDataException("truncated: stage write cap exceeded.");
                file.Position = file.Length;
                file.Write(bytes);
                file.Flush();
            }
        }

        internal void RetainError(string error)
        {
            lock (Sync)
            {
                using var file = Open(ErrorPath, FileMode.OpenOrCreate);
                if (ReadBounded(file, 1_024, "error").EndsWith("\ntruncated\n", StringComparison.Ordinal)) return;
                byte[] bytes = Encoding.UTF8.GetBytes(Prefix(error, 256) + "\n");
                byte[] marker = Encoding.UTF8.GetBytes("\ntruncated\n");
                file.Position = file.Length;
                if (file.Length + bytes.Length > 1_024 - marker.Length)
                {
                    if (file.Length + marker.Length > 1_024)
                        throw new InvalidDataException("truncated: error byte cap exceeded; retention marker cannot fit.");
                    file.Write(marker);
                }
                else file.Write(bytes);
            }
        }

        internal string Snapshot()
        {
            try
            {
                lock (Sync)
                {
                    using var file = Open(StagePath, FileMode.Open);
                    var lines = Parse(ReadBounded(file));
                    var report = new StringBuilder("child-stages: ");
                    if (lines.Count(line => line.Length != 0) == 1) report.Append("absent; runtime=unknown");
                    var seen = new HashSet<string>();
                    foreach (string line in lines.Skip(1).Where(line => line.Length != 0))
                    {
                        if (line == "truncated") { report.Append("\ntruncated"); continue; }
                        string[] fields = line.Split('|');
                        seen.Add(fields[3]);
                        report.Append($"\nowner-pid={fields[1]}; managed-pid={fields[2]}; stage={fields[3]}; time={fields[4]}; " +
                            $"runtime={fields[5]}; version={fields[6]}; arch={fields[7]}; launch-token={token}");
                    }
                    report.Append("\nabsent-stages (unknown): " + string.Join(", ", Stages.Where(stage => !seen.Contains(stage))));
                    try
                    {
                        using var errors = Open(ErrorPath, FileMode.Open);
                        if (errors.Length > 1_024) report.Append("\nsecondary-errors: truncated");
                        else report.Append("\nsecondary-errors: " + ReadBounded(errors, 1_024, "error"));
                    }
                    catch (FileNotFoundException) { report.Append("\nsecondary-errors: absent"); }
                    catch (DirectoryNotFoundException) { report.Append("\nsecondary-errors: absent"); }
                    catch (Exception ex) { report.Append("\nsecondary-errors: read-error; " + Error(ex)); }
                    return Redact(report.ToString(), directory, MaximumBytes);
                }
            }
            catch (FileNotFoundException) { return "child-stages: absent; runtime=unknown"; }
            catch (Exception ex)
            {
                string state = ex is InvalidDataException && (ex.Message.StartsWith("corrupt", StringComparison.Ordinal) ||
                    ex.Message.StartsWith("mismatch", StringComparison.Ordinal) || ex.Message.StartsWith("truncated", StringComparison.Ordinal))
                    ? Prefix(ex.Message, 96) : "read-error";
                return "child-stages: " + state + "; runtime=unknown; " + Error(ex);
            }
        }
    }
}
