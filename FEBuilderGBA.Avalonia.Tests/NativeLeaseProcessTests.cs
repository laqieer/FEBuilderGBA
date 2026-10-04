using System.Diagnostics;
using static FEBuilderGBA.Avalonia.Tests.PatchManagerOperationGuardTests;

namespace FEBuilderGBA.Avalonia.Tests;

public class NativeLeaseProcessTests
{
    const string Root = @"C:\owned-private-root";

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
        try
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
        }
        finally
        {
            permission.TrySetResult();
            await Task.WhenAll(output.Completed.Task, error.Completed.Task).WaitAsync(TimeSpan.FromSeconds(10));
            await process.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            process.Dispose();
        }
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
        internal Exception? StartError, WaitError, ReleaseError, MarkerError, ReadError;
        internal bool MarkerExists = true, WaitResult = true;
        internal int ReleaseCalls;
        internal override int OutputWaitMilliseconds => 500;
        internal override NativeLeaseProcess.IChild Start(ProcessStartInfo start)
        {
            Assert.True(start.RedirectStandardOutput && start.RedirectStandardError);
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
        internal int Code, Kills, DisposeCalls;
        internal Exception? DisposeError, KillError;
        internal List<int> Waits { get; } = new();
        internal TextReader Output = new StringReader("owned stdout");
        internal TextReader Error = new StringReader("owned stderr");
        public int Id => 123;
        public bool HasExited => Exited;
        public int ExitCode => Code;
        public DateTimeOffset ExitTime => new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
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
