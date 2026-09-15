using System.Diagnostics;
using System.Reflection;

namespace FEBuilderGBA.Core.Tests;

[Collection("ContentRepoGitGuard")]
public class PatchDatabaseOperationLeaseCoreTests
{
    [Theory]
    [InlineData("descriptor")]
    [InlineData("shared")]
    [InlineData("other-version")]
    [InlineData("backup")]
    [InlineData("marker")]
    [InlineData("git")]
    [InlineData("rename")]
    [InlineData("directory")]
    [InlineData("delete")]
    public void ExistingContentSnapshotDetectsWholeTreeChangesWithoutTrustingTimeOrLength(string change)
    {
        using var fixture = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        string tree = Path.Combine(fixture.Root, "config", "patch2");
        string selected = Path.Combine(tree, "FE8U");
        Directory.CreateDirectory(selected);
        string path = change switch
        {
            "shared" => Path.Combine(tree, "shared.bin"),
            "other-version" => Path.Combine(tree, "FE7U", "data.bin"),
            "backup" => Path.Combine(selected, ".backup_PATCH_test.txt"),
            "marker" => Path.Combine(selected, PatchDatabaseZipReaderCore.OwnershipFileName),
            "git" => Path.Combine(tree, ".git"),
            _ => Path.Combine(selected, "PATCH_test.txt"),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
        var before = PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default);
        Assert.True(before.Matches(probe, default));
        DateTime time = File.GetLastWriteTimeUtc(path);
        if (change == "rename") File.Move(path, path + ".renamed");
        else if (change == "directory") Directory.CreateDirectory(Path.Combine(selected, "empty"));
        else if (change == "delete") File.Delete(path);
        else
        {
            File.WriteAllBytes(path, new byte[] { 4, 3, 2, 1 });
            File.SetLastWriteTimeUtc(path, time);
            Assert.Equal(4, new FileInfo(path).Length);
            Assert.Equal(time, File.GetLastWriteTimeUtc(path));
        }
        Assert.False(before.Matches(probe, default));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Fact]
    public void ExistingContentSnapshotDistinguishesAbsenceEmptyRootsAndAdmittedScopes()
    {
        using var fixture = new Fixture();
        using var other = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
        var absent = PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "config")));
        Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        Assert.False(absent.Matches(probe, default));
        var empty = PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default);
        Directory.CreateDirectory(selected);
        Assert.False(empty.Matches(probe, default));
        var foreign = PatchDatabaseOperationLeaseCore.ProbeExisting(other.Root, "FE8U",
            Path.Combine(other.Root, "config", "patch2", "FE8U"));
        Assert.False(empty.Matches(foreign, default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(other.Root));
    }

    [Fact]
    public void ExistingContentSnapshotHonorsBoundsCancellationAndReadOnlyDataWithoutRepair()
    {
        using var fixture = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(selected);
        string path = Path.Combine(selected, "data.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var original = File.GetAttributes(path);
        File.SetAttributes(path, original | FileAttributes.ReadOnly);
        var readOnly = File.GetAttributes(path);
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
        try
        {
            var snapshot = PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default);
            Assert.True(snapshot.Matches(probe, default));
            Assert.Equal(readOnly, File.GetAttributes(path));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Throws<IOException>(() => PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default,
                new PatchDatabaseImportCore.InventoryLimits { MaxOperationNodes = 1 }));
            Assert.Throws<OperationCanceledException>(() =>
                PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, new CancellationToken(true)));
            string deep = selected;
            for (int i = 0; i < 33; i++) deep = Directory.CreateDirectory(Path.Combine(deep, "d")).FullName;
            Assert.Throws<IOException>(() => PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default));
        }
        finally { File.SetAttributes(path, original); }
    }

    [SkippableFact]
    public void ExistingContentSnapshotRejectsSymlinksWithoutFollowingThem()
    {
        using var fixture = new Fixture();
        using var outside = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(selected);
        string link = Path.Combine(selected, "link");
        try { Directory.CreateSymbolicLink(link, outside.Root); }
        catch (UnauthorizedAccessException) { Skip.If(true, "Symbolic-link creation is unavailable to this account."); }
        try
        {
            var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
            Assert.Throws<IOException>(() => PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside.Root));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
    }

    [SkippableFact]
    public void ExistingContentSnapshotRejectsUnixFifoInBoundedNativeChild()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix FIFO validation requires Linux/macOS.");
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(selected);
        string fifo = Path.Combine(selected, "fifo");
        Assert.Equal(0, CreateSnapshotFifo(fifo, 0x180));
        Assert.Equal("refused", RunExistingProbe(fixture.Root, true, snapshot: true));
        Assert.True(File.Exists(fifo));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    static extern int CreateSnapshotFifo(string path, uint mode);

    [Theory]
    [InlineData("workspace")]
    [InlineData("marker")]
    public void ExistingIncompleteManagedStateIsNeverDowngradedOrRepaired(string state)
    {
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(selected);
        if (state == "workspace") Directory.CreateDirectory(Path.Combine(fixture.Root, ".patch2-import"));
        else File.WriteAllText(Path.Combine(selected, PatchDatabaseZipReaderCore.OwnershipFileName), "presence, not trusted contents");
        var before = Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories);
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
        Assert.True(probe.Managed);
        Assert.ThrowsAny<IOException>(() => PatchDatabaseOperationLeaseCore.AcquireExisting(probe));
        Assert.Equal(before, Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("workspace-file")]
    [InlineData("lock-directory")]
    [InlineData("marker-directory")]
    [InlineData("wrong-base")]
    [InlineData("wrong-version")]
    public void ExistingProbeRejectsUnsafeTypesAndScopes(string kind)
    {
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        Directory.CreateDirectory(selected);
        if (kind == "workspace-file") File.WriteAllText(Path.Combine(fixture.Root, ".patch2-import"), "retain");
        if (kind == "lock-directory") Directory.CreateDirectory(Path.Combine(fixture.Root, ".patch2-import", "lease.lock"));
        if (kind == "marker-directory") Directory.CreateDirectory(Path.Combine(selected, PatchDatabaseZipReaderCore.OwnershipFileName));
        Assert.ThrowsAny<IOException>(() => PatchDatabaseOperationLeaseCore.ProbeExisting(
            kind == "wrong-base" ? Path.Combine(fixture.Root, "other") : fixture.Root,
            kind == "wrong-version" ? "FE7U" : "FE8U", selected));
    }

    [Fact]
    public void ExistingReadOnlyManagedLockFailsWithoutPermissionRepair()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        string path = Path.Combine(fixture.Root, ".patch2-import", "lease.lock");
        File.WriteAllText(path, "fixed identity bytes");
        var original = File.GetAttributes(path);
        var creation = File.GetCreationTimeUtc(path);
        UnixFileMode mode = default;
        if (OperatingSystem.IsWindows()) File.SetAttributes(path, original | FileAttributes.ReadOnly);
        else { mode = File.GetUnixFileMode(path); File.SetUnixFileMode(path, UnixFileMode.UserRead); }
        var before = File.GetAttributes(path);
        try
        {
            var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U",
                Path.Combine(fixture.Root, "config", "patch2", "FE8U"));
            Assert.Throws<UnauthorizedAccessException>(() => PatchDatabaseOperationLeaseCore.AcquireExisting(probe));
            Assert.Equal(before, File.GetAttributes(path));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path));
            Assert.Equal("fixed identity bytes", File.ReadAllText(path));
            Assert.Equal(creation, File.GetCreationTimeUtc(path));
        }
        finally
        {
            if (OperatingSystem.IsWindows()) File.SetAttributes(path, original);
            else File.SetUnixFileMode(path, mode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingReaderAndWriterExcludeEachOtherInNativeChildProcess(bool readerHolds)
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        string path = Path.Combine(fixture.Root, ".patch2-import", "lease.lock");
        File.WriteAllText(path, "unchanged fixed lock");
        var created = File.GetCreationTimeUtc(path);
        var attributes = File.GetAttributes(path);
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U",
            Path.Combine(fixture.Root, "config", "patch2", "FE8U"));
        using (readerHolds ? PatchDatabaseOperationLeaseCore.AcquireExisting(probe) : PatchDatabaseOperationLeaseCore.Acquire(fixture.Root))
            Assert.Equal("busy", RunExistingProbe(fixture.Root, reader: !readerHolds));
        Assert.Equal("acquired", RunExistingProbe(fixture.Root, reader: !readerHolds));
        Assert.Equal("unchanged fixed lock", File.ReadAllText(path));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.Equal(attributes, File.GetAttributes(path));
    }

    [Fact]
    public void ChildExistingLeaseProbe()
    {
        string? root = Environment.GetEnvironmentVariable("FEBUILDER_TEST_EXISTING_LEASE_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        Assert.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestResults")) + Path.DirectorySeparatorChar,
            Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        string result;
        try
        {
            var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(root, "FE8U", Path.Combine(root, "config", "patch2", "FE8U"));
            using var lease = Environment.GetEnvironmentVariable("FEBUILDER_TEST_EXISTING_LEASE_READER") == "1"
                ? PatchDatabaseOperationLeaseCore.AcquireExisting(probe) : PatchDatabaseOperationLeaseCore.Acquire(root);
            result = "acquired";
            if (Environment.GetEnvironmentVariable("FEBUILDER_TEST_EXISTING_LEASE_SNAPSHOT") == "1")
            {
                try { PatchDatabaseOperationLeaseCore.ExistingReadSnapshot.Capture(probe, default); }
                catch (IOException) { result = "refused"; }
            }
        }
        catch (PatchDatabaseOperationLeaseCore.BusyException ex)
        {
            Assert.True(PatchDatabaseOperationLeaseCore.IsLeaseContention(Assert.IsType<IOException>(ex.InnerException).HResult));
            result = "busy";
        }
        File.WriteAllText(Path.Combine(root, "existing-reader-result.txt"), result);
    }

    static string RunExistingProbe(string root, bool reader, bool snapshot = false)
    {
        string result = Path.Combine(root, "existing-reader-result.txt");
        if (File.Exists(result)) File.Delete(result);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--Tests:FEBuilderGBA.Core.Tests.PatchDatabaseOperationLeaseCoreTests.ChildExistingLeaseProbe");
        start.Environment["FEBUILDER_TEST_EXISTING_LEASE_ROOT"] = root;
        start.Environment["FEBUILDER_TEST_EXISTING_LEASE_READER"] = reader ? "1" : "0";
        start.Environment["FEBUILDER_TEST_EXISTING_LEASE_SNAPSHOT"] = snapshot ? "1" : "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); throw new TimeoutException("Existing reader native probe timed out."); }
        Assert.True(process.ExitCode == 0, stdout.Result + stderr.Result);
        return File.ReadAllText(result);
    }

    [Fact]
    public void ExistingReaderNeverRecreatesMissingManagedLock()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U",
            Path.Combine(fixture.Root, "config", "patch2", "FE8U"));
        string lockPath = Path.Combine(fixture.Root, ".patch2-import", "lease.lock");
        File.Delete(lockPath);
        Assert.ThrowsAny<IOException>(() => { using var lease = PatchDatabaseOperationLeaseCore.AcquireExisting(probe); });
        Assert.False(File.Exists(lockPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingProbeDoesNotCreateLegacyOrMissingStorage(bool library)
    {
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "config", "patch2", "FE8U");
        if (library) Directory.CreateDirectory(selected);
        string[] before = Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories);
        var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(fixture.Root, "FE8U", selected);
        Assert.False(probe.Managed);
        Assert.ThrowsAny<IOException>(() => PatchDatabaseOperationLeaseCore.AcquireExisting(probe));
        Assert.Equal(before, Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(35)]
    [InlineData(unchecked((int)0x80070020))]
    [InlineData(unchecked((int)0x80070021))]
    public void IsLeaseContention_RecognizesOnlyCurrentPlatformCodes(int hr)
    {
        bool expected = hr switch
        {
            11 => OperatingSystem.IsLinux() || OperatingSystem.IsAndroid(),
            35 => OperatingSystem.IsMacOS() || OperatingSystem.IsIOS(),
            _ => OperatingSystem.IsWindows(),
        };
        Assert.Equal(expected, PatchDatabaseOperationLeaseCore.IsLeaseContention(hr));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(0x10006)]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x8007000B))]
    [InlineData(unchecked((int)0x80070023))]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80040020))]
    [InlineData(unchecked((int)0x80040021))]
    [InlineData(unchecked((int)0x80131620))]
    public void IsLeaseContention_RejectsUnrelatedErrorsAndHResultAliases(int hr)
    {
        Assert.False(PatchDatabaseOperationLeaseCore.IsLeaseContention(hr));
    }

    [Fact]
    public void GenericPatch2GitEntryPointHonorsTheBaseLease()
    {
        using var fixture = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        bool invoked = false;
        var result = ContentRepoGitService.InitializeOrUpdateWithLease(
            Path.Combine(fixture.Root, "config", "patch2"), () =>
            {
                invoked = true;
                return new Patch2GitResult { Kind = Patch2GitResultKind.Success };
            });
        Assert.Equal(Patch2GitResultKind.AlreadyRunning, result.Kind);
        Assert.False(invoked);
    }

    [Fact]
    public void RelativeCanonicalPatch2GitPathsUseTheSameLease()
    {
        using var fixture = new Fixture();
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(fixture.Root, "config", "patch2"));
        bool invoked = false;
        var result = ContentRepoGitService.InitializeOrUpdateWithLease(relative, () =>
        {
            invoked = true;
            return new Patch2GitResult { Kind = Patch2GitResultKind.Success };
        });
        Assert.Equal(Patch2GitResultKind.AlreadyRunning, result.Kind);
        Assert.False(invoked);
    }

    [Fact]
    public void OneBaseHasOneLeaseAcrossOperationIds()
    {
        using var fixture = new Fixture();
        using var first = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        var busy = Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() =>
            PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
        var native = Assert.IsType<IOException>(busy.InnerException);
        int expectedHResult = OperatingSystem.IsWindows() ? unchecked((int)0x80070020)
            : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ? 35 : 11;
        Assert.Equal(expectedHResult, native.HResult);
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".patch2-import", "lease.lock")));
    }

    [Fact]
    public void DisposingLeaseDoesNotDeleteItsIdentityFile()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".patch2-import", "lease.lock")));
        using var next = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    [Fact]
    public void Patch2RepoUsesSameBaseLease()
    {
        using var fixture = new Fixture();
        string repo = Path.Combine(fixture.Root, "config", "patch2");
        Assert.Equal(Path.GetFullPath(fixture.Root),
            PatchDatabaseOperationLeaseCore.BaseForPatch2Repository(repo));
        using var first = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
        Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() =>
            PatchDatabaseOperationLeaseCore.AcquireForPatch2Repository(repo));
        Assert.Null(PatchDatabaseOperationLeaseCore.AcquireForPatch2Repository(
            Path.Combine(fixture.Root, "resources", "FE-Repo")));
    }

    [Fact]
    public void ForeignProcessCannotAcquireUntilHolderReleases()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root))
            Assert.Equal("busy", RunProbe(fixture.Root));
        Assert.Equal("acquired", RunProbe(fixture.Root));
    }

    [Fact]
    public void ChildLeaseProbe()
    {
        string? root = Environment.GetEnvironmentVariable("FEBUILDER_TEST_ZIP_LEASE_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        string allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestResults")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(allowed, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        string result;
        try
        {
            using var lease = PatchDatabaseOperationLeaseCore.Acquire(root);
            result = "acquired";
        }
        catch (PatchDatabaseOperationLeaseCore.BusyException) { result = "busy"; }
        File.WriteAllText(Path.Combine(root, "child-result.txt"), result);
    }

    static string RunProbe(string root)
    {
        string result = Path.Combine(root, "child-result.txt");
        if (File.Exists(result)) File.Delete(result);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--Tests:FEBuilderGBA.Core.Tests.PatchDatabaseOperationLeaseCoreTests.ChildLeaseProbe");
        start.Environment["FEBUILDER_TEST_ZIP_LEASE_ROOT"] = root;
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(true);
            throw new TimeoutException("The cooperating lease probe timed out.");
        }
        Assert.True(process.ExitCode == 0, stdout.Result + stderr.Result);
        Assert.True(File.Exists(result));
        return File.ReadAllText(result);
    }

    sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults",
            "lease-" + Guid.NewGuid().ToString("N"));

        public Fixture() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
