using FEBuilderGBA.DesktopReadinessProbe;
using FEBuilderGBA.E2ETests.Helpers;

namespace FEBuilderGBA.E2ETests.Tests;

public class DesktopProbeCommandTests
{
    public static TheoryData<string[], string> InvalidArguments => new()
    {
        { [], "OptInRequired" },
        { [""], "InvalidArguments" },
        { ["--help"], "InvalidArguments" },
        { ["--version"], "InvalidArguments" },
        { ["--PROBE-OWN-DESKTOP"], "InvalidArguments" },
        { [" --probe-own-desktop"], "InvalidArguments" },
        { ["--probe-own-desktop "], "InvalidArguments" },
        { ["--probe-own-desktop=true"], "InvalidArguments" },
        { ["--probe-own-desktop\n"], "InvalidArguments" },
        { ["--probe-own-desktop", "--probe-own-desktop"], "InvalidArguments" },
        { ["--probe-own-desktop", "--help"], "InvalidArguments" },
        { ["--help", "--probe-own-desktop"], "InvalidArguments" },
        { ["private-argument-\u0080\nState=Ready"], "InvalidArguments" },
        { [null!], "InvalidArguments" }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void InvalidOptIn_NeverCallsProbe(string[] args, string reason)
    {
        int calls = 0;
        using var output = new StringWriter();

        int exitCode = ProbeCommand.Run(args, () =>
        {
            calls++;
            throw new InvalidOperationException("The probe must not be invoked.");
        }, output);

        Assert.Equal(4, exitCode);
        Assert.Equal(0, calls);
        AssertOutput(output, "ExecutionError", reason);
    }

    [Theory]
    [InlineData(DesktopReadinessState.Ready, DesktopReadinessReason.ActiveInputDesktop, 0)]
    [InlineData(DesktopReadinessState.Blocked, DesktopReadinessReason.SessionZero, 2)]
    [InlineData(DesktopReadinessState.Blocked, DesktopReadinessReason.SessionInactive, 2)]
    [InlineData(DesktopReadinessState.Blocked, DesktopReadinessReason.ThreadDesktopNotInput, 2)]
    [InlineData(DesktopReadinessState.Unknown, DesktopReadinessReason.SessionQueryFailed, 3)]
    [InlineData(DesktopReadinessState.Unknown, DesktopReadinessReason.SessionStateUnknown, 3)]
    [InlineData(DesktopReadinessState.Unknown, DesktopReadinessReason.ThreadDesktopUnknown, 3)]
    [InlineData(DesktopReadinessState.Unknown, DesktopReadinessReason.NativeQueryFailed, 3)]
    public void ExplicitOptIn_InvokesOnceAndEmitsBoundedResult(
        DesktopReadinessState state, DesktopReadinessReason reason, int expectedExitCode)
    {
        int calls = 0;
        using var output = new StringWriter { NewLine = "untrusted-newline-\u0080\n" };

        int exitCode = ProbeCommand.Run(["--probe-own-desktop"], () =>
        {
            calls++;
            return new(state, reason);
        }, output);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(1, calls);
        AssertOutput(output, state.ToString(), reason.ToString());
    }

    [Theory]
    [InlineData((DesktopReadinessState)(-1), DesktopReadinessReason.ActiveInputDesktop)]
    [InlineData((DesktopReadinessState)int.MaxValue, DesktopReadinessReason.SessionZero)]
    [InlineData(DesktopReadinessState.Ready, (DesktopReadinessReason)(-1))]
    [InlineData(DesktopReadinessState.Unknown, (DesktopReadinessReason)int.MaxValue)]
    [InlineData(DesktopReadinessState.Ready, DesktopReadinessReason.SessionInactive)]
    [InlineData(DesktopReadinessState.Blocked, DesktopReadinessReason.ActiveInputDesktop)]
    [InlineData(DesktopReadinessState.Unknown, DesktopReadinessReason.SessionZero)]
    public void InvalidResult_IsExecutionError(
        DesktopReadinessState state, DesktopReadinessReason reason)
    {
        int calls = 0;
        using var output = new StringWriter();

        int exitCode = ProbeCommand.Run(["--probe-own-desktop"], () =>
        {
            calls++;
            return new(state, reason);
        }, output);

        Assert.Equal(4, exitCode);
        Assert.Equal(1, calls);
        AssertOutput(output, "ExecutionError", "InvalidProbeResult");
    }

    [Fact]
    public void ProbeException_IsNotRetriedOrDisclosed()
    {
        int calls = 0;
        using var output = new StringWriter();

        int exitCode = ProbeCommand.Run(["--probe-own-desktop"], () =>
        {
            calls++;
            throw new InvalidOperationException("private-detail-\u0080\nState=Ready");
        }, output);

        Assert.Equal(4, exitCode);
        Assert.Equal(1, calls);
        AssertOutput(output, "ExecutionError", "ProbeFailed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputFailure_IsExecutionErrorWithoutRetry(bool optIn)
    {
        int calls = 0;
        using var output = new FailingWriter();

        int exitCode = ProbeCommand.Run(optIn ? ["--probe-own-desktop"] : [], () =>
        {
            calls++;
            return new(DesktopReadinessState.Ready, DesktopReadinessReason.ActiveInputDesktop);
        }, output);

        Assert.Equal(4, exitCode);
        Assert.Equal(optIn ? 1 : 0, calls);
        Assert.Equal(1, output.Writes);
    }

    private static void AssertOutput(StringWriter output, string state, string reason)
    {
        string text = output.ToString();
        Assert.Equal($"State={state};Reason={reason}\n", text);
        Assert.InRange(text.Length, 1, 128);
        Assert.All(text, character => Assert.InRange((int)character, 0, 127));
    }

    private sealed class FailingWriter : StringWriter
    {
        public int Writes { get; private set; }

        public override void Write(string? value)
        {
            Writes++;
            throw new IOException("private-output-failure");
        }
    }
}
