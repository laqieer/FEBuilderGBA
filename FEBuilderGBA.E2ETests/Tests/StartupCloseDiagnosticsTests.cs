using FEBuilderGBA.E2ETests.Helpers;

namespace FEBuilderGBA.E2ETests.Tests;

public class StartupCloseDiagnosticsTests
{
    [Fact]
    public void RefusalIsReportedAndRemainingWindowsAreCaptured()
    {
        var process = new ProcessOperations();
        var captures = new List<IntPtr>();
        var reports = new List<string>();

        bool exited = StartupCloseDiagnostics.CaptureAndCleanup(
            () => [new IntPtr(1), new IntPtr(2)],
            window =>
            {
                captures.Add(window);
                if (window == new IntPtr(1))
                    throw new WindowCaptureException("Invalid capture dimensions.");
            },
            reports.Add, process.CreateCleanup());

        Assert.True(exited);
        Assert.Equal(new[] { new IntPtr(1), new IntPtr(2) }, captures);
        Assert.Contains("Invalid capture dimensions.", Assert.Single(reports));
        Assert.Equal(1, process.Kills);
        Assert.Equal(new[] { 5_000 }, process.WaitTimeouts);
    }

    [Fact]
    public void MultipleRefusalsAreReportedWithoutCaptureSuccess()
    {
        var process = new ProcessOperations();
        var reports = new List<string>();
        int attempts = 0;

        Assert.True(StartupCloseDiagnostics.CaptureAndCleanup(
            () => [new IntPtr(1), new IntPtr(2)],
            _ =>
            {
                attempts++;
                throw new WindowCaptureException("No valid screenshot.");
            },
            reports.Add, process.CreateCleanup()));

        Assert.Equal(2, attempts);
        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Contains("No valid screenshot.", report));
        Assert.Equal(1, process.Kills);
    }

    [Fact]
    public void EmptyEnumerationStillCleansUp()
    {
        var process = new ProcessOperations();
        Assert.True(StartupCloseDiagnostics.CaptureAndCleanup(
            () => [], _ => throw new Exception("Unexpected capture."),
            _ => throw new Exception("Unexpected report."), process.CreateCleanup()));
        Assert.Equal(1, process.Kills);
    }

    [Theory]
    [InlineData("readiness")]
    [InlineData("wrapped-capture")]
    [InlineData("unexpected")]
    public void NonRefusalCaptureErrorsPropagateAfterCleanup(string failure)
    {
        Exception expected = failure switch
        {
            "readiness" => new DesktopUnavailableException(
                new(DesktopReadinessState.Blocked, DesktopReadinessReason.SessionInactive)),
            "wrapped-capture" => new WindowCaptureException(
                "Unexpected capture failure.", new InvalidOperationException("Native failure.")),
            _ => new InvalidOperationException("Unexpected callback failure.")
        };
        var process = new ProcessOperations();
        int reports = 0;

        Exception? actual = Record.Exception(() => StartupCloseDiagnostics.CaptureAndCleanup(
            () => [new IntPtr(1)], _ => throw expected,
            _ => reports++, process.CreateCleanup()));

        Assert.Same(expected, actual);
        Assert.Equal(0, reports);
        Assert.Equal(1, process.Kills);
        Assert.Equal(new[] { 5_000 }, process.WaitTimeouts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumerationErrorsPropagateAfterCleanup(bool afterFirstWindow)
    {
        var expected = new InvalidOperationException("Enumeration failed.");
        var process = new ProcessOperations();
        int captures = 0;
        IEnumerable<IntPtr> Windows()
        {
            if (afterFirstWindow)
                yield return new IntPtr(1);
            throw expected;
        }

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            StartupCloseDiagnostics.CaptureAndCleanup(
                Windows, _ => captures++, _ => { }, process.CreateCleanup())));
        Assert.Equal(afterFirstWindow ? 1 : 0, captures);
        Assert.Equal(1, process.Kills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReporterErrorsAreNotTreatedAsCaptureRefusals(bool captureException)
    {
        Exception expected = captureException
            ? new WindowCaptureException("Reporter failed.")
            : new InvalidOperationException("Reporter failed.");
        var process = new ProcessOperations();
        int captures = 0;

        Exception? actual = Record.Exception(() => StartupCloseDiagnostics.CaptureAndCleanup(
            () => [new IntPtr(1), new IntPtr(2)],
            _ =>
            {
                captures++;
                throw new WindowCaptureException("Capture refused.");
            },
            _ => throw expected, process.CreateCleanup()));

        Assert.Same(expected, actual);
        Assert.Equal(1, captures);
        Assert.Equal(1, process.Kills);
    }

    [Fact]
    public void UnconfirmedCleanupIsNotSuccess()
    {
        var process = new ProcessOperations { ExitConfirmed = false };
        Assert.False(StartupCloseDiagnostics.CaptureAndCleanup(
            () => [], _ => { }, _ => { }, process.CreateCleanup()));
        Assert.Equal(1, process.Kills);
        Assert.Equal(new[] { 5_000 }, process.WaitTimeouts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateCleanupDoesNotRepeatOrExtendItsBudget(bool exitConfirmed)
    {
        var process = new ProcessOperations { ExitConfirmed = exitConfirmed };
        OwnedProcessCleanup cleanup = process.CreateCleanup();

        Assert.Equal(exitConfirmed, cleanup.TryCleanup(5_000));
        Assert.Equal(exitConfirmed, cleanup.TryCleanup(3_000));
        Assert.Equal(1, process.LivenessReads);
        Assert.Equal(1, process.Kills);
        Assert.Equal(new[] { 5_000 }, process.WaitTimeouts);
    }

    [Theory]
    [InlineData("liveness")]
    [InlineData("kill")]
    [InlineData("wait")]
    public void FailedCleanupCallbacksCannotBeRetried(string failure)
    {
        var process = new ProcessOperations { Failure = failure };
        OwnedProcessCleanup cleanup = process.CreateCleanup();

        Assert.Same(process.Error, Assert.Throws<InvalidOperationException>(() =>
            cleanup.TryCleanup(5_000)));
        var counts = (process.LivenessReads, process.Kills, process.WaitTimeouts.Count);
        Assert.False(cleanup.TryCleanup(3_000));
        Assert.Equal(counts, (process.LivenessReads, process.Kills, process.WaitTimeouts.Count));
    }

    [Fact]
    public void AlreadyExitedProcessIsNeverKilled()
    {
        var process = new ProcessOperations { AlreadyExited = true };
        Assert.True(process.CreateCleanup().TryCleanup(3_000));
        Assert.Equal(0, process.Kills);
        Assert.Equal(new[] { 3_000 }, process.WaitTimeouts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidBudgetIsRejectedBeforeProcessObservation(int timeoutMs)
    {
        var process = new ProcessOperations();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            process.CreateCleanup().TryCleanup(timeoutMs));
        Assert.Equal(0, process.LivenessReads);
        Assert.Equal(0, process.Kills);
        Assert.Empty(process.WaitTimeouts);
    }

    [Fact]
    public void CleanupFailureStillSurfacesAfterAnOptionalRefusal()
    {
        var process = new ProcessOperations { Failure = "kill" };
        var reports = new List<string>();

        Assert.Same(process.Error, Assert.Throws<InvalidOperationException>(() =>
            StartupCloseDiagnostics.CaptureAndCleanup(
                () => [new IntPtr(1)],
                _ => throw new WindowCaptureException("Capture refused."),
                reports.Add, process.CreateCleanup())));
        Assert.Single(reports);
        Assert.Equal(1, process.Kills);
    }

    private sealed class ProcessOperations
    {
        internal bool AlreadyExited { get; init; }
        internal bool ExitConfirmed { get; init; } = true;
        internal string? Failure { get; init; }
        internal InvalidOperationException Error { get; } = new("Cleanup callback failed.");
        internal int LivenessReads { get; private set; }
        internal int Kills { get; private set; }
        internal List<int> WaitTimeouts { get; } = [];

        internal OwnedProcessCleanup CreateCleanup() => new(
            () =>
            {
                LivenessReads++;
                if (Failure == "liveness") throw Error;
                return AlreadyExited;
            },
            () =>
            {
                Kills++;
                if (Failure == "kill") throw Error;
            },
            timeout =>
            {
                WaitTimeouts.Add(timeout);
                if (Failure == "wait") throw Error;
                return ExitConfirmed;
            });
    }
}
