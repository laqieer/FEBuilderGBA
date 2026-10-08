using System.Diagnostics;
using System.Reflection;
using System.Text;

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
        Assert.Equal("refused", RunExistingProbe(fixture.Root, true, expected: "refused", snapshot: true));
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

    [Fact]
    public async Task ExistingProbeOutputCaptureIsBoundedAndContinuesDraining()
    {
        const int outputByteBudget = 8 * 1024;
        string astral = char.ConvertFromUtf32(0x1F600);
        string content = string.Concat(Enumerable.Repeat("é\"\\", outputByteBudget)) + astral;
        using var reader = new StringReader(content);

        var captured = await ExistingProbeDiagnostics.CaptureOutputAsync(reader);

        string encoded = System.Text.Json.JsonSerializer.Serialize(captured.Text);
        Assert.True(Encoding.UTF8.GetByteCount(encoded) - 2 <= outputByteBudget);
        Assert.True(captured.Truncated);
        Assert.Equal(-1, reader.Peek());
    }

    [Fact]
    public async Task ExistingProbeOutputCapturePreservesAstralScalarsAcrossReadBoundaries()
    {
        string astral = char.ConvertFromUtf32(0x1F600);
        foreach (string content in new[] { "before" + astral + "after", new string('x', 1_023) + astral + "after" })
        {
            using var reader = new StringReader(content);

            var captured = await ExistingProbeDiagnostics.CaptureOutputAsync(reader);

            Assert.Equal(content, captured.Text);
            Assert.Null(captured.Failure);
        }
    }

    [Fact]
    public async Task ExistingProbeOutputCaptureReplacesMalformedUtf16AndRetainsPrefixOnReadFailure()
    {
        using var malformed = new StringReader("prefix" + '\uD83D' + "suffix");
        var replaced = await ExistingProbeDiagnostics.CaptureOutputAsync(malformed);
        Assert.Equal("prefix\uFFFDsuffix", replaced.Text);
        Assert.Null(replaced.Failure);

        var failed = await ExistingProbeDiagnostics.CaptureOutputAsync(new ThrowAfterPrefixReader("captured-prefix"));
        Assert.Equal("captured-prefix", failed.Text);
        Assert.IsType<IOException>(failed.Failure);
    }

    [Fact]
    public void ExistingProbeDoesNotReportSuccessWhenOutputDrainFailsOrDoesNotFinish()
    {
        Assert.Equal("drain-failure", ExistingProbeDiagnostics.Outcome(
            timedOut: false, waitFailure: null, exitCode: 0, marker: "expected:busy", expected: "busy",
            drainFailure: new IOException("read failed"), drainsFinished: false));
        Assert.Equal("drain-failure", ExistingProbeDiagnostics.Outcome(
            timedOut: false, waitFailure: null, exitCode: 0, marker: "expected:busy", expected: "busy",
            drainFailure: null, drainsFinished: false));
        Assert.Equal("wait-failure", ExistingProbeDiagnostics.Outcome(
            timedOut: false, waitFailure: new InvalidOperationException("primary"), exitCode: 0,
            marker: "expected:busy", expected: "busy", drainFailure: new IOException("secondary"),
            drainsFinished: false));
        Assert.Equal("success", ExistingProbeDiagnostics.Outcome(
            timedOut: false, waitFailure: null, exitCode: 0, marker: "expected:busy", expected: "busy",
            drainFailure: null, drainsFinished: true));
    }

    [Fact]
    public async Task ExistingProbeDrainFailureRetainsPrefixAndStopsTheChild()
    {
        bool stopped = false;
        var failed = Task.FromResult(new ExistingProbeDiagnostics.CapturedOutput(
            "captured-prefix", false, new IOException("synthetic read failure")));
        var complete = Task.FromResult(new ExistingProbeDiagnostics.CapturedOutput("", false));

        Exception? failure = await ExistingProbeDiagnostics.MonitorDrainFailureAsync(
            failed, complete, () => stopped = true);

        Assert.IsType<IOException>(failure);
        Assert.True(stopped);
    }

    [Theory]
    [InlineData("busy", "busy", "expected:busy")]
    [InlineData("acquired", "acquired", "expected:acquired")]
    [InlineData("other", "busy", "malformed-or-oversize")]
    [InlineData("", "busy", "malformed-or-oversize")]
    public void ExistingProbeMarkerReportsOnlyBoundedSafeState(string marker, string expected, string expectedState)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(marker));

        Assert.Equal(expectedState, ExistingProbeDiagnostics.ReadMarker(() => stream, expected));
    }

    [Fact]
    public void ExistingProbeMarkerDistinguishesMissingOversizeAndReadFailure()
    {
        Assert.Equal("missing", ExistingProbeDiagnostics.ReadMarker(
            () => throw new FileNotFoundException(), "busy"));
        Assert.Equal("malformed-or-oversize", ExistingProbeDiagnostics.ReadMarker(
            () => new MemoryStream(new byte[ExistingProbeDiagnostics.MaximumMarkerBytes + 1]), "busy"));
        Assert.Equal("read-error:IOException", ExistingProbeDiagnostics.ReadMarker(
            () => throw new IOException("untrusted detail"), "busy"));
    }

    [Fact]
    public void ExistingProbeFailureReportRetainsExitCodeAndBoundsStreamsAndPrimaryException()
    {
        var primary = new InvalidOperationException("primary failure");
        string report = ExistingProbeDiagnostics.Report("nonzero-exit", 123, TimeSpan.FromMilliseconds(321),
            new(new string('o', ExistingProbeDiagnostics.MaximumOutputBytes), true),
            new(new string('e', ExistingProbeDiagnostics.MaximumOutputBytes), true),
            "expected:busy", primary, null, exitCode: 37, hasExited: true, drainsFinished: true);
        using var json = System.Text.Json.JsonDocument.Parse(report);
        var root = json.RootElement;

        Assert.Equal(37, root.GetProperty("exitCode").GetInt32());
        Assert.True(Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(
            root.GetProperty("stdout").GetProperty("text").GetString())) - 2 <= 8 * 1024);
        Assert.True(root.GetProperty("stdout").GetProperty("truncated").GetBoolean());
        Assert.True(Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(
            root.GetProperty("stderr").GetProperty("text").GetString())) - 2 <= 8 * 1024);
        Assert.True(root.GetProperty("stderr").GetProperty("truncated").GetBoolean());
        Assert.Equal(nameof(InvalidOperationException),
            root.GetProperty("primaryException").GetProperty("type").GetString());
        Assert.Equal("expected:busy", root.GetProperty("resultMarker").GetString());
    }

    [Fact]
    public void ExistingProbeFailurePreservesActualNonzeroExitAndMissingMarker()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "missing-native-probe-child.dll"));
        string marker = Path.Combine(AppContext.BaseDirectory, "TestResults",
            "missing-native-probe-" + Guid.NewGuid().ToString("N") + ".txt");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            ExistingProbeDiagnostics.Run(start, marker, "busy"));
        using var json = System.Text.Json.JsonDocument.Parse(failure.Message);
        var root = json.RootElement;

        Assert.Equal("nonzero-exit", root.GetProperty("outcome").GetString());
        Assert.NotEqual(0, root.GetProperty("exitCode").GetInt32());
        Assert.True(root.GetProperty("processId").GetInt32() > 0);
        Assert.Equal("missing", root.GetProperty("resultMarker").GetString());
        Assert.Equal(typeof(InvalidOperationException), failure.InnerException?.GetType());
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
            Assert.Equal("busy", RunExistingProbe(fixture.Root, reader: !readerHolds, expected: "busy"));
        Assert.Equal("acquired", RunExistingProbe(fixture.Root, reader: !readerHolds, expected: "acquired"));
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

    static string RunExistingProbe(string root, bool reader, string expected, bool snapshot = false)
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
        return ExistingProbeDiagnostics.Run(start, result, expected);
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

    static class ExistingProbeDiagnostics
    {
        internal const int MaximumOutputBytes = 8 * 1024;
        internal const int MaximumMarkerBytes = 64;
        const int MaximumPostKillWaitMs = 5_000;
        const int MaximumExceptionMessageChars = 1_024;
        const int MaximumExceptionStackChars = 2_048;

        internal readonly record struct CapturedOutput(string Text, bool Truncated, Exception? Failure = null);

        internal static async Task<CapturedOutput> CaptureOutputAsync(TextReader reader)
        {
            var output = new StringBuilder(MaximumOutputBytes);
            var buffer = new char[1_024];
            int encodedBytes = 0;
            bool retaining = true;
            bool truncated = false;
            char? pendingHighSurrogate = null;
            Exception? failure = null;

            void AppendScalar(string scalar)
            {
                if (!retaining) return;
                int scalarBytes = System.Text.Json.JsonEncodedText.Encode(scalar).EncodedUtf8Bytes.Length;
                if (encodedBytes + scalarBytes <= MaximumOutputBytes)
                {
                    output.Append(scalar);
                    encodedBytes += scalarBytes;
                }
                else
                {
                    truncated = true;
                    retaining = false;
                }
            }

            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        char current = buffer[i];
                        if (pendingHighSurrogate is { } high)
                        {
                            if (char.IsLowSurrogate(current))
                            {
                                AppendScalar(new string(new[] { high, current }));
                                pendingHighSurrogate = null;
                                continue;
                            }

                            AppendScalar("\uFFFD");
                            pendingHighSurrogate = null;
                        }

                        if (char.IsHighSurrogate(current)) pendingHighSurrogate = current;
                        else if (char.IsLowSurrogate(current)) AppendScalar("\uFFFD");
                        else AppendScalar(current.ToString());
                    }
                }
                if (pendingHighSurrogate.HasValue) AppendScalar("\uFFFD");
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            return new CapturedOutput(output.ToString(), truncated, failure);
        }

        internal static string Outcome(bool timedOut, Exception? waitFailure, int? exitCode,
            string marker, string expected, Exception? drainFailure, bool drainsFinished)
        {
            return timedOut ? "timeout" :
                waitFailure != null ? "wait-failure" :
                exitCode is { } code && code != 0 ? "nonzero-exit" :
                drainFailure != null || !drainsFinished ? "drain-failure" :
                exitCode == 0 && marker == "expected:" + expected ? "success" : "probe-failure";
        }

        internal static async Task<Exception?> MonitorDrainFailureAsync(
            Task<CapturedOutput> stdout, Task<CapturedOutput> stderr, Action stopProcess)
        {
            var pending = new List<Task<CapturedOutput>> { stdout, stderr };
            while (pending.Count > 0)
            {
                Task<CapturedOutput> completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);

                Exception? failure = completed.IsFaulted
                    ? completed.Exception?.Flatten().InnerExceptions.FirstOrDefault()
                    : completed.IsCanceled ? new TaskCanceledException() : completed.Result.Failure;
                if (failure == null) continue;

                try { stopProcess(); }
                catch { }
                return failure;
            }
            return null;
        }

        static Exception? CaptureFailure(Task<CapturedOutput> task)
        {
            if (task.IsFaulted) return task.Exception?.Flatten().InnerExceptions.FirstOrDefault();
            if (task.IsCanceled) return new TaskCanceledException();
            return task.IsCompletedSuccessfully ? task.Result.Failure : null;
        }

        internal static string ReadMarker(Func<Stream> open, string expected)
        {
            try
            {
                using var stream = open();
                var bytes = new byte[MaximumMarkerBytes + 1];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = stream.Read(bytes, count, bytes.Length - count);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaximumMarkerBytes) return "malformed-or-oversize";
                string value;
                try { value = new UTF8Encoding(false, true).GetString(bytes, 0, count); }
                catch (DecoderFallbackException) { return "malformed-or-oversize"; }
                return value == expected ? "expected:" + expected : "malformed-or-oversize";
            }
            catch (FileNotFoundException) { return "missing"; }
            catch (DirectoryNotFoundException) { return "missing"; }
            catch (Exception ex) { return "read-error:" + ex.GetType().Name; }
        }

        internal static string Run(ProcessStartInfo start, string resultPath, string expected)
        {
            var timer = Stopwatch.StartNew();
            Process process;
            try
            {
                process = Process.Start(start)
                    ?? throw new InvalidOperationException("Process.Start returned no process.");
            }
            catch (Exception ex)
            {
                timer.Stop();
                throw new InvalidOperationException(Report("start-failure", null, timer.Elapsed,
                    default, default, ReadMarker(() => File.OpenRead(resultPath), expected), ex, null), ex);
            }

            using (process)
            {
                int pid;
                try { pid = process.Id; }
                catch { pid = -1; }
                Task<CapturedOutput> stdout = CaptureOutputAsync(process.StandardOutput);
                Task<CapturedOutput> stderr = CaptureOutputAsync(process.StandardError);
                Exception? killFailure = null;
                Task<Exception?> drainMonitor = MonitorDrainFailureAsync(stdout, stderr, () =>
                {
                    try
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) { killFailure ??= ex; }
                });
                bool exited;
                Exception? waitFailure = null;
                try { exited = process.WaitForExit(60_000); }
                catch (Exception ex) { exited = false; waitFailure = ex; }

                bool timedOut = !exited && waitFailure == null;
                if (!exited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception ex) { killFailure = ex; }
                    try { process.WaitForExit(MaximumPostKillWaitMs); }
                    catch (Exception ex) { killFailure ??= ex; }
                }

                bool hasExited;
                int? exitCode = null;
                try
                {
                    hasExited = process.HasExited;
                    if (hasExited) exitCode = process.ExitCode;
                }
                catch (Exception ex)
                {
                    hasExited = false;
                    waitFailure ??= ex;
                }

                bool drainsFinished = false;
                Exception? drainFailure = null;
                try
                {
                    drainsFinished = Task.WaitAll(new Task[] { stdout, stderr }, MaximumPostKillWaitMs);
                    if (!drainsFinished)
                    {
                        drainFailure = new TimeoutException("Native probe output drains did not finish within the bounded post-exit wait.");
                    }
                    else
                    {
                        if (!drainMonitor.Wait(MaximumPostKillWaitMs))
                            drainFailure = new TimeoutException("Native probe drain status was not observed within the bounded wait.");
                        else
                            drainFailure = drainMonitor.GetAwaiter().GetResult();
                        drainsFinished = drainFailure == null;
                    }
                }
                catch (Exception ex)
                {
                    drainFailure = CaptureFailure(stdout) ?? CaptureFailure(stderr) ??
                        ex.GetBaseException();
                    drainsFinished = false;
                }
                timer.Stop();
                var capturedOut = stdout.IsCompletedSuccessfully ? stdout.Result : default;
                var capturedError = stderr.IsCompletedSuccessfully ? stderr.Result : default;
                string marker = ReadMarker(() => File.OpenRead(resultPath), expected);
                string outcome = Outcome(timedOut, waitFailure, exitCode, marker, expected,
                    drainFailure, drainsFinished);
                if (outcome == "success") return expected;

                Exception primary = timedOut
                    ? new TimeoutException("Existing reader native probe exceeded its unchanged 60,000 ms wait.")
                    : waitFailure ?? drainFailure ??
                        new InvalidOperationException("Existing reader native probe did not complete successfully.");
                throw new InvalidOperationException(Report(outcome, pid, timer.Elapsed,
                    capturedOut, capturedError, marker, primary, killFailure,
                    exitCode, hasExited, drainsFinished, drainFailure), primary);
            }
        }

        internal static string Report(string outcome, int? pid, TimeSpan elapsed, CapturedOutput stdout,
            CapturedOutput stderr, string marker, Exception? primary, Exception? cleanup,
            int? exitCode = null, bool? hasExited = null, bool? drainsFinished = null, Exception? drainFailure = null)
        {
            static object Text(CapturedOutput capture) => new
            {
                text = capture.Text ?? "",
                truncated = capture.Truncated,
            };

            static object? ExceptionInfo(Exception? exception) => exception == null ? null : new
            {
                type = exception.GetType().Name,
                hresult = exception.HResult,
                message = Prefix(exception.Message, MaximumExceptionMessageChars),
                stack = Prefix(exception.StackTrace ?? "<stack unavailable>", MaximumExceptionStackChars),
            };

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                outcome,
                processId = pid,
                elapsedMilliseconds = elapsed.TotalMilliseconds,
                exitCode,
                hasExited,
                outputDrainsFinished = drainsFinished,
                stdout = Text(stdout),
                stderr = Text(stderr),
                resultMarker = marker,
                primaryException = ExceptionInfo(primary),
                cleanupException = ExceptionInfo(cleanup),
                drainException = ExceptionInfo(drainFailure),
            });
        }

        static string Prefix(string value, int maximum) =>
            value.Length <= maximum ? value : value[..maximum] + "[truncated]";
    }

    sealed class ThrowAfterPrefixReader(string prefix) : TextReader
    {
        bool returnedPrefix;

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (returnedPrefix) throw new IOException("synthetic read failure");
            returnedPrefix = true;
            prefix.CopyTo(0, buffer, index, prefix.Length);
            return Task.FromResult(prefix.Length);
        }
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
