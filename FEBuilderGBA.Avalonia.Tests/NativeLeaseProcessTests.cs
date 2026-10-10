using System.Diagnostics;
using static FEBuilderGBA.Avalonia.Tests.PatchManagerOperationGuardTests;

namespace FEBuilderGBA.Avalonia.Tests;

public class NativeLeaseProcessTests
{
    const string Root = @"C:\owned-private-root";

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritableRaceSecondaryFailurePreservesPrimaryObjectAndStack(bool errors)
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "Native writable no-follow race requires Windows, Linux or macOS.");
        using var race = new NativeProbeDiagnosticsTests.WritableRace(errors);
        var primary = new IOException("owned primary result-wait failure");
        var dependencies = new FakeDependencies
        {
            WaitError = primary,
            DiagnosticFactory = (_, _) => { race.Attempt(); return race.Session; },
        };
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Same(primary, failure.Primary);
        Assert.Contains(nameof(FakeDependencies.WaitUntil), primary.StackTrace);
        Assert.Contains("secondary diagnostic error:", failure.Message);
        Assert.Equal(1, race.HookCalls);
        race.AssertOutsideUnchanged();
        Assert.True(dependencies.Child.Disposed);
        Assert.DoesNotContain(race.Session.DirectoryPath, failure.Message);
    }

    [Fact]
    public async Task SaturatedHostTraceCannotHideIndependentStageAndRuntimeEvidence()
    {
        using var fixture = new NativeProbeDiagnosticsTests.DiagnosticFixture();
        var dependencies = new FakeDependencies
        {
            DiagnosticFactory = (root, token) =>
            {
                fixture.Token = token;
                var session = NativeProbeDiagnostics.Prepare(root, token, Environment.ProcessId);
                session.Record("managed-entry");
                session.Record("fixture-start");
                return session;
            },
        };
        dependencies.Child.Error = new StringReader(new string('h', 32_768));
        using var process = new NativeLeaseProcess(fixture.Root, false, dependencies);
        await process.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        dependencies.Child.Code = 137;
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.Contains("[truncated]", failure.Message);
        Assert.Contains("stage=managed-entry", failure.Message);
        Assert.Contains("stage=fixture-start", failure.Message);
        Assert.Contains("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, failure.Message);
        Assert.Equal(0, dependencies.Child.Kills);
        Assert.DoesNotContain(fixture.Root, failure.Message);
        Assert.True(failure.Message.IndexOf("stage=managed-entry", StringComparison.Ordinal)
            < failure.Message.IndexOf("stderr:", StringComparison.Ordinal));
    }

    [Fact]
    public void AlreadyExited137HasNoKillAndIncludesBoundedLifecycleAndIndependentStages()
    {
        var dependencies = new FakeDependencies { MarkerExists = false };
        dependencies.Child.Exited = true;
        dependencies.Child.Code = 137;
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Equal(0, dependencies.Child.Kills);
        Assert.Equal(new[] { 60_000 }, dependencies.Child.Waits);
        Assert.Contains("before-start", failure.Message);
        Assert.Contains("after-start", failure.Message);
        Assert.Contains("observed-exit", failure.Message);
        Assert.Contains("exit-code=137", failure.Message);
        Assert.Contains("child-stages: absent; runtime=unknown", failure.Message);
    }

    [Fact]
    public void TimeoutLedgerRecordsExactlyOneOwnedKillAttempt()
    {
        var dependencies = new FakeDependencies { MarkerExists = false, WaitResult = false };
        dependencies.Child.ExitOnWait = false;
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Equal(1, dependencies.Child.Kills);
        Assert.Equal(1, failure.Message.Split("kill-attempt", StringSplitOptions.None).Length - 1);
        Assert.Contains("pid=123", failure.Message);
    }

    [Fact]
    public void DiagnosticInitializationFailureDoesNotReplaceOriginalFailure()
    {
        var primary = new IOException("owned primary wait");
        var dependencies = new FakeDependencies
        {
            WaitError = primary,
            DiagnosticError = new IOException("owned diagnostic failure"),
        };
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Same(primary, failure.Primary);
        Assert.Contains(nameof(FakeDependencies.WaitUntil), primary.StackTrace);
        Assert.Contains("secondary diagnostic error", failure.Message);
        Assert.Contains("owned diagnostic failure", failure.Message);
        Assert.True(dependencies.Child.Disposed);
    }

    [Fact]
    public void StartupFailurePreservesPrimaryAndRedactsPaths()
    {
        var primary = new IOException("launch " + Root + " " + typeof(NativeLeaseProcessTests).Assembly.Location);
        var dependencies = new FakeDependencies { StartError = primary };
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, true, dependencies));
        Assert.Same(primary, failure.Primary);
        Assert.Contains(nameof(FakeDependencies.Start), primary.StackTrace);
        Assert.Contains(nameof(FakeDependencies.Start), failure.Message);
        Assert.Contains("launch", failure.Message);
        Assert.Contains("not-started", failure.Message);
        Assert.Contains("--Tests:", failure.Message);
        Assert.DoesNotContain(Root, failure.Message);
        Assert.DoesNotContain(typeof(NativeLeaseProcessTests).Assembly.Location, failure.Message);
        Assert.DoesNotContain(Root, failure.ToString());
        Assert.DoesNotContain(typeof(NativeLeaseProcessTests).Assembly.Location, failure.ToString());
        Assert.DoesNotMatch(@"[A-Za-z]:[\\/]|/[A-Za-z]|\\\\", failure.ToString());
        Assert.False(dependencies.Child.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingResultIsFailureWithExitAndMarkerDiagnostics(bool exited)
    {
        var dependencies = new FakeDependencies { MarkerExists = false, WaitResult = exited };
        dependencies.Child.Exited = exited;
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Contains(exited ? "No native result" : "result wait timed out", failure.Message);
        Assert.Contains("pid=123", failure.Message);
        Assert.Contains("native-action-result", failure.Message);
        Assert.Contains("native-action-result.pending", failure.Message);
        Assert.Contains("native-action-release", failure.Message);
        Assert.Contains("launch=", failure.Message);
        Assert.Contains("exit=", failure.Message);
        Assert.True(dependencies.Child.Disposed);
        Assert.Contains(60_000, dependencies.Child.Waits);
    }

    [Fact]
    public void NonzeroExitCannotLookSuccessful()
    {
        var dependencies = new FakeDependencies();
        dependencies.Child.Code = 17;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.Contains("exit-code=17", failure.Message);
        Assert.Contains("nonzero", failure.Message);
        Assert.True(dependencies.Child.Disposed);
        process.Dispose();
    }

    [Fact]
    public void ResultReadFailurePreservesPrimaryAndCleansUp()
    {
        var primary = new IOException("owned result read failed");
        var dependencies = new FakeDependencies { ReadError = primary };
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => _ = process.Result);
        Assert.Same(primary, failure.Primary);
        Assert.Contains("owned result read failed", failure.Message);
        Assert.True(dependencies.Child.Disposed);
        process.Dispose();
    }

    [Fact]
    public void SuccessfulCleanupIsIdempotentAndUsesOriginalWaitBudget()
    {
        var dependencies = new FakeDependencies();
        var process = new NativeLeaseProcess(Root, true, dependencies);
        Assert.Equal("acquired", process.Result);
        process.Dispose();
        process.Dispose();
        Assert.Equal(new[] { 60_000 }, dependencies.Child.Waits);
        Assert.Equal(1, dependencies.ReleaseCalls);
        Assert.Equal(1, dependencies.Child.DisposeCalls);
    }

    [Fact]
    public void PrimaryAndMultipleCleanupFailuresAreRenderedWithoutReplacingPrimary()
    {
        var primary = new IOException("owned primary");
        var dependencies = new FakeDependencies
        {
            WaitError = primary,
            ReleaseError = new IOException("release " + Root),
        };
        dependencies.Child.DisposeError = new IOException("dispose failed");
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Same(primary, failure.Primary);
        Assert.Contains(nameof(FakeDependencies.WaitUntil), primary.StackTrace);
        Assert.Contains("owned primary", failure.Message);
        Assert.Contains("release", failure.Message);
        Assert.Contains("dispose failed", failure.Message);
        Assert.Contains("owned primary", failure.ToString());
        Assert.Contains("dispose failed", failure.ToString());
        Assert.DoesNotContain(Root, failure.Message);
        Assert.True(dependencies.Child.Disposed);
    }

    [Fact]
    public void LongErrorsCannotCrowdOutProcessMarkersAndBothOutputStreams()
    {
        var dependencies = new FakeDependencies
        {
            WaitError = new IOException(new string('p', 100_000)),
            ReleaseError = new IOException(new string('r', 100_000)),
            MarkerError = new IOException(new string('m', 100_000)),
        };
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(() => new NativeLeaseProcess(Root, false, dependencies));
        Assert.Contains("diagnostics truncated", failure.Message);
        Assert.Contains("pid=123", failure.Message);
        Assert.Contains("native-action-release", failure.Message);
        Assert.Contains("cleanup error", failure.Message);
        Assert.Contains("stdout:\nowned stdout", failure.Message);
        Assert.Contains("stderr:\nowned stderr", failure.Message);
        Assert.True(failure.Message.Length < 40_000);
    }

    [Fact]
    public void ReleaseFailureStillTerminatesReapsAndDisposesOnlyOnce()
    {
        var dependencies = new FakeDependencies { ReleaseError = new IOException("release failed") };
        dependencies.Child.ExitOnWait = false;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.Contains("release failed", failure.Message);
        Assert.Contains("timed out", failure.Message);
        Assert.Equal(1, dependencies.Child.Kills);
        Assert.Equal(new[] { 60_000, 5_000 }, dependencies.Child.Waits);
        Assert.True(dependencies.Child.Disposed);
        process.Dispose();
        Assert.Equal(1, dependencies.Child.DisposeCalls);
        Assert.Equal(1, dependencies.ReleaseCalls);
    }

    [Fact]
    public void FailedKillAndReapDoNotPreventOutputAndProcessDisposal()
    {
        var dependencies = new FakeDependencies();
        dependencies.Child.ExitOnWait = false;
        dependencies.Child.KillError = new IOException("kill failed");
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.Contains("kill failed", failure.Message);
        Assert.Contains("reap", failure.Message);
        Assert.True(dependencies.Child.Disposed);
    }

    [Fact]
    public Task ConcurrentDrainContinuesBeyondRetentionCap() => AssertPressureDrain(false);

    [Fact]
    public Task ConcurrentDrainWaitsForDeferredSecondStream() => AssertPressureDrain(true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessStatusSamplesExitStateOnlyOnce(bool initiallyExited)
    {
        var dependencies = new FakeDependencies();
        using var process = new NativeLeaseProcess(Root, false, dependencies);
        int reads = 0;
        dependencies.Child.Code = 17;
        dependencies.Child.HasExitedValue = () => ++reads == 1 ? initiallyExited : !initiallyExited;
        try
        {
            string status = process.ProcessStatus();
            Assert.Equal(1, reads);
            Assert.Contains(initiallyExited ? "status=exited; exit-code=17" : "status=running; exit-code=unavailable", status);
            Assert.Equal(initiallyExited ? 1 : 0, dependencies.Child.ExitCodeReads);
            Assert.Equal(initiallyExited ? 1 : 0, dependencies.Child.ExitTimeReads);
        }
        finally
        {
            dependencies.Child.Code = 0;
            dependencies.Child.HasExitedValue = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PressureTeardownPreservesPrimaryAndAlwaysDisposes(bool failTeardownAwait)
    {
        var dependencies = new FakeDependencies();
        dependencies.Child.Code = 2;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var primary = new IOException("owned pressure assertion or await failure");
        var failure = await Assert.ThrowsAsync<IOException>(() => ExecutePressureTest(process, dependencies.Child,
            () => failTeardownAwait ? Task.CompletedTask : Task.FromException(primary),
            () => failTeardownAwait ? Task.FromException(primary) : Task.CompletedTask));
        Assert.Same(primary, failure);
        Assert.True(dependencies.Child.Disposed);
        Assert.Equal(1, dependencies.Child.DisposeCalls);
    }

    [Fact]
    public async Task PressureTeardownSurfacesUnexpectedCleanupAlongsidePrimary()
    {
        var dependencies = new FakeDependencies();
        dependencies.Child.Code = 2;
        dependencies.Child.DisposeError = new IOException("unexpected dispose failure");
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var primary = new IOException("original assertion failure");
        var awaitFailure = new IOException("secondary await failure");
        var failure = await Assert.ThrowsAsync<AggregateException>(() => ExecutePressureTest(process, dependencies.Child,
            () => Task.FromException(primary), () => Task.FromException(awaitFailure)));
        Assert.Same(primary, failure.InnerExceptions[0]);
        Assert.Same(awaitFailure, failure.InnerExceptions[1]);
        var cleanup = Assert.IsType<NativeLeaseProcess.Failure>(failure.InnerExceptions[2]);
        Assert.Contains("unexpected dispose failure", cleanup.Message);
        Assert.DoesNotContain("nonzero", cleanup.Message);
        Assert.True(dependencies.Child.Disposed);
        Assert.Equal(1, dependencies.Child.DisposeCalls);
    }

    static async Task ExecutePressureTest(NativeLeaseProcess process, FakeChild child,
        Func<Task> body, Func<Task> finish)
    {
        var failures = new List<Exception>();
        try
        {
            try { await body(); }
            catch (Exception ex) { failures.Add(ex); }
        }
        finally
        {
            try
            {
                try { await finish(); }
                catch (Exception ex) { failures.Add(ex); }
            }
            finally
            {
                // The body tests nonzero-exit diagnostics; teardown must not manufacture that failure again.
                child.Code = 0;
                try { process.Dispose(); }
                catch (Exception ex) { failures.Add(ex); }
            }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Pressure test failed; secondary teardown failures follow.", failures);
    }

    static async Task AssertPressureDrain(bool deferSecondStream)
    {
        var permission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new PressureReader();
        var error = new PressureReader();
        output.PeerStarted = error.Started.Task;
        error.PeerStarted = output.Started.Task;
        if (deferSecondStream) error.Permission = permission.Task;
        var dependencies = new FakeDependencies();
        dependencies.Child.Output = output;
        dependencies.Child.Error = error;
        dependencies.Child.Code = 2;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        await ExecutePressureTest(process, dependencies.Child, async () =>
        {
            await Task.WhenAll(output.Started.Task, error.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
            if (deferSecondStream)
            {
                Assert.Equal(0, error.ReadCount);
                Assert.False(error.Completed.Task.IsCompleted);
            }
            permission.TrySetResult();
            // Drain/cap assertions concern completed finite streams, not the separate open-pipe timeout.
            await Task.WhenAll(output.Completed.Task, error.Completed.Task).WaitAsync(TimeSpan.FromSeconds(10));
            await process.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
            Assert.Contains("exit-code=2", failure.Message);
            Assert.DoesNotContain("output completion timed out", failure.Message);
            Assert.DoesNotContain("read error", failure.Message);
            Assert.Equal(32_768, output.ReadCount);
            Assert.Equal(32_768, error.ReadCount);
            Assert.InRange(output.MaxBuffer, 1, 1_024);
            Assert.InRange(error.MaxBuffer, 1, 1_024);
            Assert.Contains("stdout", failure.Message);
            Assert.Contains("stderr", failure.Message);
            Assert.Contains("truncated", failure.Message);
            Assert.Contains("stdout:\n" + new string('x', 8_192) + "\n[truncated]", failure.Message);
            Assert.Contains("stderr:\n" + new string('x', 8_192) + "\n[truncated]", failure.Message);
            Assert.True(failure.Message.Length < 20_000);
        }, async () =>
        {
            permission.TrySetResult();
            await Task.WhenAll(output.Completed.Task, error.Completed.Task).WaitAsync(TimeSpan.FromSeconds(10));
            await process.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        });
    }

    [Fact]
    public async Task OpenPipesHaveBoundedCompletionAndCancellation()
    {
        var output = new OpenReader();
        var error = new OpenReader();
        var dependencies = new FakeDependencies();
        dependencies.Child.Output = output;
        dependencies.Child.Error = error;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        await Task.WhenAll(output.Started.Task, error.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = Stopwatch.StartNew();
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Contains("output completion timed out", failure.Message);
        Assert.True(output.Disposed);
        Assert.True(error.Disposed);
        Assert.True(dependencies.Child.Disposed);
    }

    [Fact]
    public void DiagnosticAndReadErrorsAreVisible()
    {
        var dependencies = new FakeDependencies { MarkerError = new IOException("marker metadata failed") };
        dependencies.Child.Output = new BrokenReader();
        dependencies.Child.Code = 3;
        var process = new NativeLeaseProcess(Root, false, dependencies);
        var failure = Assert.Throws<NativeLeaseProcess.Failure>(process.Dispose);
        Assert.Contains("marker metadata failed", failure.Message);
        Assert.Contains("output read failed", failure.Message);
    }

    sealed class FakeDependencies : NativeLeaseProcess.Dependencies
    {
        internal FakeChild Child { get; } = new();
        internal Exception? StartError, WaitError, ReleaseError, MarkerError, ReadError, DiagnosticError;
        internal Func<string, string, NativeProbeDiagnostics.Session?>? DiagnosticFactory;
        internal bool MarkerExists = true, WaitResult = true;
        internal int ReleaseCalls;
        internal override int OutputWaitMilliseconds => 500;
        internal override NativeProbeDiagnostics.Session? PrepareDiagnostics(string root, string token)
        {
            if (DiagnosticError != null) throw DiagnosticError;
            return DiagnosticFactory?.Invoke(root, token);
        }
        internal override NativeLeaseProcess.IChild Start(ProcessStartInfo start)
        {
            Assert.True(start.RedirectStandardOutput && start.RedirectStandardError);
            Assert.Equal("1", start.Environment["DOTNET_HOST_TRACE"]);
            Assert.Equal("1", start.Environment["COREHOST_TRACE"]);
            Assert.False(start.Environment.ContainsKey("DOTNET_HOST_TRACEFILE"));
            Assert.False(start.Environment.ContainsKey("COREHOST_TRACEFILE"));
            if (StartError != null) throw StartError;
            return Child;
        }
        internal override void Delete(string path) { }
        internal override bool Exists(string path) => MarkerExists;
        internal override string Read(string path)
        {
            if (ReadError != null) throw ReadError;
            return "acquired";
        }
        internal override void Release(string path)
        {
            ReleaseCalls++;
            if (ReleaseError != null) throw ReleaseError;
        }
        internal override bool WaitUntil(Func<bool> predicate, int milliseconds)
        {
            Assert.Equal(60_000, milliseconds);
            if (WaitError != null) throw WaitError;
            return WaitResult;
        }
        internal override string Marker(string path)
        {
            if (MarkerError != null) throw MarkerError;
            return MarkerExists ? "exists; modified=2026-10-04T00:00:00Z" : "absent";
        }
    }

    sealed class FakeChild : NativeLeaseProcess.IChild
    {
        internal bool Exited, ExitOnWait = true, Disposed;
        internal int Code, Kills, DisposeCalls, ExitCodeReads, ExitTimeReads;
        internal Exception? DisposeError, KillError;
        internal List<int> Waits { get; } = new();
        internal TextReader Output = new StringReader("owned stdout");
        internal TextReader Error = new StringReader("owned stderr");
        internal Func<bool>? HasExitedValue;
        public int Id => 123;
        public bool HasExited => HasExitedValue?.Invoke() ?? Exited;
        public int ExitCode { get { ExitCodeReads++; return Code; } }
        public DateTimeOffset ExitTime { get { ExitTimeReads++; return new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero); } }
        public TextReader StandardOutput => Output;
        public TextReader StandardError => Error;
        public bool WaitForExit(int milliseconds)
        {
            Waits.Add(milliseconds);
            Exited = ExitOnWait;
            return Exited;
        }
        public void Kill()
        {
            Kills++;
            if (KillError != null) throw KillError;
        }
        public void Dispose()
        {
            DisposeCalls++;
            Disposed = true;
            if (DisposeError != null) throw DisposeError;
        }
    }

    sealed class PressureReader : TextReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task PeerStarted = Task.CompletedTask, Permission = Task.CompletedTask;
        internal int ReadCount, MaxBuffer;
        bool started;
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!started)
            {
                started = true;
                Started.TrySetResult();
                await Permission;
                await PeerStarted;
            }
            MaxBuffer = Math.Max(MaxBuffer, buffer.Length);
            int count = Math.Min(buffer.Length, 32_768 - ReadCount);
            buffer.Span[..count].Fill('x');
            ReadCount += count;
            if (count == 0) Completed.TrySetResult();
            return count;
        }
    }

    sealed class OpenReader : TextReader
    {
        readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new(completion.Task);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            completion.TrySetResult(0);
            base.Dispose(disposing);
        }
    }

    sealed class BrokenReader : TextReader
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("output read failed"));
    }
}
