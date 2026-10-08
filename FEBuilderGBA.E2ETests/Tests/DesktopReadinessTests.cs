using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using FEBuilderGBA.E2ETests.Helpers;

namespace FEBuilderGBA.E2ETests.Tests;

public class DesktopReadinessTests
{
    private static DesktopReadinessResult Ready() =>
        DesktopReadiness.Evaluate(new DesktopObservation(1, 0, true));

    [Theory]
    [InlineData(1, 0, true, DesktopReadinessState.Ready)]
    [InlineData(0, 0, true, DesktopReadinessState.Blocked)]
    [InlineData(1, 1, true, DesktopReadinessState.Blocked)]
    [InlineData(1, 4, true, DesktopReadinessState.Blocked)]
    [InlineData(1, 0, false, DesktopReadinessState.Blocked)]
    [InlineData(null, 0, true, DesktopReadinessState.Unknown)]
    [InlineData(1, null, true, DesktopReadinessState.Unknown)]
    [InlineData(1, -1, true, DesktopReadinessState.Unknown)]
    [InlineData(1, 10, true, DesktopReadinessState.Unknown)]
    [InlineData(1, 0, null, DesktopReadinessState.Unknown)]
    [InlineData(null, null, null, DesktopReadinessState.Unknown)]
    public void Evaluation_RequiresAffirmativeObservations(
        int? session, int? state, bool? input, DesktopReadinessState expected)
    {
        Assert.Equal(expected, DesktopReadiness.Evaluate(
            new DesktopObservation((uint?)session, state, input)).State);
    }

    [Fact]
    public void Probe_QueriesOnlyOwnSessionAndBorrowedThreadDesktop()
    {
        var native = new DesktopNative();
        Assert.Equal(DesktopReadinessState.Ready, DesktopReadiness.Probe(native).State);
        Assert.Equal(17u, native.QueriedSession);
        Assert.Equal(new IntPtr(20), native.QueriedDesktop);
        Assert.Equal(1, native.Frees);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("wts-query")]
    [InlineData("wts-throws")]
    [InlineData("null-buffer")]
    [InlineData("short-buffer")]
    [InlineData("long-buffer")]
    [InlineData("invalid-state")]
    [InlineData("read-throws")]
    [InlineData("free-throws")]
    [InlineData("desktop")]
    [InlineData("input-query")]
    [InlineData("input-length")]
    [InlineData("input-value")]
    [InlineData("throws")]
    public void Probe_FailuresAreUnknownAndOwnedMemoryIsFreed(string failure)
    {
        var native = new DesktopNative { Failure = failure };
        Assert.Equal(DesktopReadinessState.Unknown, DesktopReadiness.Probe(native).State);
        Assert.Equal(failure is "session" or "null-buffer" or "throws" ? 0 : 1, native.Frees);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(17, 4, 1)]
    [InlineData(17, 0, 0)]
    public void Probe_KnownUnsafeObservationsBlock(uint session, int state, int input)
    {
        var native = new DesktopNative { Session = session, State = state, Input = input };
        Assert.Equal(DesktopReadinessState.Blocked, DesktopReadiness.Probe(native).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuiEntryPoints_RejectBeforeStarting(bool unknown)
    {
        Func<DesktopReadinessResult> probe = () => Rejected(unknown);
        int starts = 0;
        (int, string, string) Execute(ProcessStartInfo info, int timeout)
        {
            starts++;
            return (0, "", "");
        }

        using var process = new Process();
        Assert.Throws<DesktopUnavailableException>(() =>
            AppRunner.Launch("missing.exe", "", probe, _ => { starts++; return process; }));
        Assert.Throws<DesktopUnavailableException>(() =>
            AppRunner.RunGui("missing.exe", "", 10, null, probe, Execute));
        Assert.Throws<DesktopUnavailableException>(() =>
            AvaloniaAppRunner.Run("missing.exe", "--data-verify", 10, probe, Execute));
        Assert.Equal(0, starts);
    }

    [Fact]
    public void GuiEntryPoints_ReadyDispatchesExactlyOnce()
    {
        int starts = 0;
        (int, string, string) Execute(ProcessStartInfo info, int timeout)
        {
            starts++;
            return (0, "ok", "");
        }
        using var process = new Process();
        Assert.Same(process, AppRunner.Launch("app.exe", "--rom test", Ready,
            info => { starts++; Assert.Equal("--rom test", info.Arguments); return process; }));
        Assert.Equal("ok", AppRunner.RunGui("app.exe", "", 10, null, Ready, Execute).Stdout);
        Assert.Equal("ok", AvaloniaAppRunner.Run("app.exe", "", 10, Ready, Execute).Stdout);
        Assert.Equal(3, starts);
    }

    [Fact]
    public void GuiEntryPoint_ProbeExceptionIsExplicitUnknown()
    {
        var failure = Assert.Throws<DesktopUnavailableException>(() =>
            AppRunner.RunGui("missing.exe", "", 10, null,
                () => throw new InvalidOperationException("Query failed"),
                (_, _) => throw new Exception("Must not execute")));
        Assert.Equal(DesktopReadinessState.Unknown, failure.Readiness.State);
        Assert.Equal(DesktopReadinessReason.NativeQueryFailed, failure.Readiness.Reason);
    }

    [Theory]
    [InlineData("FEBuilderGBA.CLI.exe", "")]
    [InlineData("FEBuilderGBA.CLI.exe", "--lint")]
    [InlineData("FEBuilderGBA.exe", "--version")]
    public void GenuineCli_DoesNotRequireDesktop(string exe, string args)
    {
        int starts = 0;
        var result = AppRunner.Run(exe, args, 123,
            new Dictionary<string, string?> { ["DESKTOP_TEST"] = "kept" },
            (info, timeout) =>
            {
                starts++;
                Assert.Equal(args, info.Arguments);
                Assert.Equal(123, timeout);
                Assert.Equal("kept", info.Environment["DESKTOP_TEST"]);
                Assert.False(info.UseShellExecute);
                return (0, "cli", "");
            });
        Assert.Equal("cli", result.Stdout);
        Assert.Equal(1, starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoArgsSmokeTest_DoesNotSwallowReadinessRejection(bool unknown)
    {
        Assert.Throws<DesktopUnavailableException>(() =>
            CliHelpTests.AssertNoArgsDoesNotCrash(() =>
                AppRunner.RunGui("missing.exe", "", 10, null, () => Rejected(unknown),
                    (_, _) => throw new Exception("Must not execute"))));
    }

    [Fact]
    public void NoArgsSmokeTest_PreservesBestEffortTimeoutHandling()
    {
        CliHelpTests.AssertNoArgsDoesNotCrash(() => throw new TimeoutException());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_RejectedDesktopDoesNotInspectTargetOrAllocate(bool unknown)
    {
        using var process = new Process();
        var native = new CaptureNative();
        Assert.Throws<DesktopUnavailableException>(() =>
            Capture(process, native, () => Rejected(unknown)));
        Assert.Equal(0, native.IdentityQueries);
        Assert.Equal(0, native.ProcessIdQueries);
        Assert.Empty(native.Surfaces);
    }

    [Theory]
    [InlineData("exited")]
    [InlineData("reused-pid")]
    [InlineData("missing-handle")]
    [InlineData("invalid-pid")]
    [InlineData("identity-query")]
    [InlineData("foreign")]
    [InlineData("stale-window")]
    [InlineData("process-id-query")]
    [InlineData("zero-window")]
    public void Capture_RejectsInvalidOwnershipBeforeCapture(string failure)
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = failure };
        Assert.Throws<WindowCaptureException>(() =>
            ScreenshotHelper.CaptureWindow(process, failure == "zero-window" ? IntPtr.Zero : new(12),
                "test", ".", false, Ready, native));
        Assert.Equal(0, native.BoundsQueries);
        Assert.Empty(native.Surfaces);
        Assert.Equal(0, native.Prints);
    }

    [Fact]
    public void Capture_PublicEntryPointsRequireRetainedProcess()
    {
        var methods = typeof(ScreenshotHelper).GetMethods()
            .Where(m => m.Name is "CaptureWindow" or "CaptureWindowDeterministic").ToArray();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, m => Assert.Equal(typeof(Process), m.GetParameters()[0].ParameterType));
    }

    [Theory]
    [InlineData("bounds")]
    [InlineData("empty-bounds")]
    public void Capture_RejectsInvalidDimensionsWithoutAllocating(string failure)
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = failure };
        Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Empty(native.Surfaces);
    }

    [Theory]
    [InlineData(1400, false)]
    [InlineData(5, true)]
    public void GetWindowRectFailure_PreservesStaleRefusalAndNativeFaultClassification(
        int errorCode, bool hasNativeCause)
    {
        var failure = ScreenshotHelper.CreateBoundsFailure(new IntPtr(12), errorCode);

        if (!hasNativeCause)
        {
            Assert.Null(failure.InnerException);
            return;
        }

        var cause = Assert.IsType<Win32Exception>(failure.InnerException);
        Assert.Equal(errorCode, cause.NativeErrorCode);
    }

    [Fact]
    public void Capture_SuccessReleasesHdcAndDisposesSurface()
    {
        using var process = new Process();
        var native = new CaptureNative();
        Assert.EndsWith("test.png", Capture(process, native, Ready));
        Assert.Same(process, native.Process);
        Assert.Equal(new uint[] { 2 }, native.Flags);
        var surface = Assert.Single(native.Surfaces);
        Assert.Equal(1, surface.Releases);
        Assert.Equal(1, surface.Disposals);
        Assert.Equal(1, surface.Saves);
    }

    [Fact]
    public void Capture_PrintWindowFalseIsNeverSavedEvenIfPixelsHaveContent()
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = "print-false" };
        Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Equal(new uint[] { 2, 0, 2, 0, 2, 0 }, native.Flags);
        Assert.Equal(6, native.Surfaces.Count);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(1, surface.Releases);
            Assert.Equal(1, surface.Disposals);
            Assert.Equal(0, surface.Saves);
            Assert.Equal(0, surface.ContentChecks);
        });
    }

    [Fact]
    public void Capture_FallbackUsesOnlyAnotherTargetPrintWindow()
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = "first-print-false" };
        Assert.EndsWith("test.png", Capture(process, native, Ready));
        Assert.Equal(new uint[] { 2, 0 }, native.Flags);
        Assert.Equal(0, native.Surfaces[0].Saves);
        Assert.Equal(1, native.Surfaces[1].Saves);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(1, surface.Releases);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Fact]
    public void Capture_EmptyImageFailsWithoutSaving()
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = "empty-content" };
        Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.All(native.Surfaces, surface => Assert.Equal(0, surface.Saves));
    }

    [Theory]
    [InlineData("get-hdc", 0)]
    [InlineData("zero-hdc", 0)]
    [InlineData("print-throws", 1)]
    [InlineData("content-throws", 1)]
    [InlineData("save-throws", 1)]
    [InlineData("release-throws", 1)]
    public void Capture_ExceptionsRemainExplicitAndCleanUp(string failure, int releases)
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = failure };
        var delays = new List<int>();
        Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready, delays.Add));
        var surface = Assert.Single(native.Surfaces);
        Assert.Equal(releases, surface.Releases);
        Assert.Equal(1, surface.Disposals);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData("print-false", "100x100", 6)]
    [InlineData("empty-content", "100x100", 6)]
    [InlineData("empty-bounds", "0x100", 0)]
    public void Capture_TransientFailureBudgetAndDiagnosticsAreExact(
        string failure, string dimensions, int prints)
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = failure };
        var delays = new List<int>();

        var rejected = Assert.Throws<WindowCaptureException>(() =>
            Capture(process, native, Ready, delays.Add));

        Assert.Contains("test", rejected.Message);
        Assert.Contains("HWND=0xC", rejected.Message);
        Assert.Contains("PID=42", rejected.Message);
        Assert.Contains(dimensions, rejected.Message);
        Assert.Contains("attempt 3/3", rejected.Message);
        Assert.Null(rejected.InnerException);
        Assert.Equal(new[] { 100, 100 }, delays);
        Assert.Equal(3, native.BoundsQueries);
        Assert.Equal(prints, native.Prints);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.Releases);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Theory]
    [InlineData("empty-bounds")]
    [InlineData("empty-content")]
    [InlineData("print-false")]
    public void Capture_DirectRefusalIsReportedAndNextDiagnosticStillCaptures(string failure)
    {
        using var process = new Process();
        var native = new CaptureNative();
        var windows = new List<IntPtr>();
        var reports = new List<string>();
        var waits = new List<int>();
        int kills = 0;
        var cleanup = new OwnedProcessCleanup(() => false, () => kills++,
            timeout => { waits.Add(timeout); return true; });

        Assert.True(StartupCloseDiagnostics.CaptureAndCleanup(
            () => [new IntPtr(12), new IntPtr(13)],
            window =>
            {
                windows.Add(window);
                native.Failure = window == new IntPtr(12) ? failure : "";
                ScreenshotHelper.CaptureWindow(process, window, "diagnostic", ".", false,
                    Ready, native, _ => { });
            },
            reports.Add, cleanup));

        Assert.Equal(new[] { new IntPtr(12), new IntPtr(13) }, windows);
        string report = Assert.Single(reports);
        Assert.Contains("Optional diagnostic capture refused; no screenshot evidence", report);
        Assert.Contains("HWND=0xC", report);
        Assert.Contains("attempt 3/3", report);
        Assert.Equal(1, native.Surfaces.Sum(surface => surface.Saves));
        Assert.All(native.Surfaces, surface => Assert.Equal(1, surface.Disposals));
        Assert.Equal(1, kills);
        Assert.Equal(new[] { 5_000 }, waits);
    }

    [Theory]
    [InlineData("identity-query")]
    [InlineData("bounds")]
    [InlineData("get-hdc")]
    [InlineData("print-throws")]
    [InlineData("content-throws")]
    [InlineData("save-throws")]
    [InlineData("release-throws")]
    public void Capture_NativeFaultStillPropagatesThroughDiagnosticAfterCleanup(string failure)
    {
        using var process = new Process();
        var native = new CaptureNative { Failure = failure };
        int captures = 0, reports = 0, kills = 0;
        var waits = new List<int>();
        var cleanup = new OwnedProcessCleanup(() => false, () => kills++,
            timeout => { waits.Add(timeout); return true; });

        var rejected = Assert.Throws<WindowCaptureException>(() =>
            StartupCloseDiagnostics.CaptureAndCleanup(
                () => [new IntPtr(12), new IntPtr(13)],
                window =>
                {
                    captures++;
                    ScreenshotHelper.CaptureWindow(process, window, "diagnostic", ".", false,
                        Ready, native, _ => { });
                },
                _ => reports++, cleanup));

        Assert.IsType(failure == "save-throws" ? typeof(IOException) : typeof(InvalidOperationException),
            rejected.InnerException);
        Assert.Contains("HWND=0xC", rejected.Message);
        Assert.Equal(1, captures);
        Assert.Equal(0, reports);
        Assert.Equal(1, kills);
        Assert.Equal(new[] { 5_000 }, waits);
        Assert.All(native.Surfaces, surface => Assert.Equal(1, surface.Disposals));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_ReadinessRejectionStillPropagatesThroughDiagnosticAfterCleanup(bool unknown)
    {
        using var process = new Process();
        var native = new CaptureNative();
        int reports = 0, kills = 0;
        var cleanup = new OwnedProcessCleanup(() => false, () => kills++, _ => true);

        var rejected = Assert.Throws<DesktopUnavailableException>(() =>
            StartupCloseDiagnostics.CaptureAndCleanup(
                () => [new IntPtr(12)],
                window => ScreenshotHelper.CaptureWindow(process, window, "diagnostic", ".", false,
                    () => Rejected(unknown), native, _ => { }),
                _ => reports++, cleanup));

        Assert.Equal(Rejected(unknown), rejected.Readiness);
        Assert.Equal(0, native.IdentityQueries);
        Assert.Empty(native.Surfaces);
        Assert.Equal(0, reports);
        Assert.Equal(1, kills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_GeometryRetryRechecksAdmissionBeforeBounds(bool revokeDesktop)
    {
        using var process = new Process();
        var native = new CaptureNative { BoundsResults = new(new[] { (0, 100), (100, 100) }) };
        bool revoked = false;
        Action<int> wait = _ =>
        {
            revoked = true;
            if (!revokeDesktop) native.Failure = "exited";
        };

        if (revokeDesktop)
            Assert.Throws<DesktopUnavailableException>(() =>
                Capture(process, native, () => revoked ? Rejected(false) : Ready(), wait));
        else
            Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready, wait));
        Assert.Equal(1, native.BoundsQueries);
        Assert.Equal(0, native.Prints);
        Assert.Empty(native.Surfaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_RetriesTransientRenderingUntilContentAppears(bool emptyContent)
    {
        using var process = new Process();
        var native = new CaptureNative();
        if (emptyContent)
            native.ContentResults = new(new[] { false, false, true });
        else
            native.PrintResults = new(new[] { false, false, true });

        Assert.EndsWith("test.png", Capture(process, native, Ready));
        Assert.Equal(new uint[] { 2, 0, 2 }, native.Flags);
        Assert.Equal(2, native.BoundsQueries);
        Assert.Equal(3, native.Surfaces.Count);
        Assert.Equal(new[] { 0, 0, 1 }, native.Surfaces.Select(surface => surface.Saves));
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(1, surface.Releases);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Capture_RetriesOnlyNonpositiveGeometryUntilSized(int width, int height)
    {
        using var process = new Process();
        var native = new CaptureNative
        {
            BoundsResults = new(new[] { (width, height), (100, 100) })
        };

        Assert.EndsWith("test.png", Capture(process, native, Ready));
        Assert.Equal(2, native.BoundsQueries);
        var surface = Assert.Single(native.Surfaces);
        Assert.Equal(1, surface.Saves);
        Assert.Equal(1, surface.Releases);
        Assert.Equal(1, surface.Disposals);
    }

    [Theory]
    [InlineData("bounds")]
    [InlineData("surface")]
    [InlineData("hdc")]
    public void Capture_RechecksOwnershipImmediatelyBeforePrint(string revokeAt)
    {
        using var process = new Process();
        var native = new CaptureNative();
        Action revoke = () => native.Failure = "foreign";
        if (revokeAt == "bounds") native.OnBounds = revoke;
        if (revokeAt == "surface") native.OnCreateSurface = revoke;
        if (revokeAt == "hdc") native.OnGetHdc = revoke;

        var failure = Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Contains("different process", failure.Message);
        Assert.Equal(0, native.Prints);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Theory]
    [InlineData("surface")]
    [InlineData("hdc")]
    [InlineData("fallback")]
    public void Capture_RechecksReadinessImmediatelyBeforeEachPrint(string revokeAt)
    {
        using var process = new Process();
        var native = new CaptureNative();
        bool blocked = false;
        Action revoke = () => blocked = true;
        if (revokeAt == "surface") native.OnCreateSurface = revoke;
        if (revokeAt == "hdc") native.OnGetHdc = revoke;
        if (revokeAt == "fallback")
        {
            native.PrintResults = new(new[] { false });
            native.OnPrint = revoke;
        }

        Assert.Throws<DesktopUnavailableException>(() =>
            Capture(process, native, () => blocked ? Rejected(false) : Ready()));
        Assert.Equal(revokeAt == "fallback" ? 1 : 0, native.Prints);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Fact]
    public void Capture_RevokedOwnershipPreventsTargetFallback()
    {
        using var process = new Process();
        var native = new CaptureNative { PrintResults = new(new[] { false }) };
        native.OnPrint = () => native.Failure = "foreign";

        var failure = Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Contains("different process", failure.Message);
        Assert.Equal(new uint[] { 2 }, native.Flags);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("exited")]
    [InlineData("reused-pid")]
    public void Capture_RevokedOwnershipStopsRetry(string failure)
    {
        using var process = new Process();
        var native = new CaptureNative { PrintResults = new(new[] { false, false }) };
        native.OnPrint = () =>
        {
            if (native.Prints == 2) native.Failure = failure;
        };

        var rejected = Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Contains(failure == "foreign" ? "different process" : "identity", rejected.Message);
        Assert.Equal(2, native.Prints);
        Assert.Equal(1, native.BoundsQueries);
        Assert.All(native.Surfaces, surface => Assert.Equal(0, surface.Saves));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_RevokedDesktopStopsRetry(bool unknown)
    {
        using var process = new Process();
        var native = new CaptureNative { PrintResults = new(new[] { false, false }) };
        bool revoked = false;
        native.OnPrint = () =>
        {
            if (native.Prints == 2) revoked = true;
        };

        var failure = Assert.Throws<DesktopUnavailableException>(() =>
            Capture(process, native, () => revoked ? Rejected(unknown) : Ready()));
        Assert.Equal(Rejected(unknown), failure.Readiness);
        Assert.Equal(2, native.Prints);
        Assert.Equal(1, native.BoundsQueries);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.Disposals);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_RechecksAdmissionBeforeSaving(bool revokeDesktop)
    {
        using var process = new Process();
        var native = new CaptureNative();
        bool blocked = false;
        native.OnContent = () =>
        {
            if (revokeDesktop) blocked = true;
            else native.Failure = "foreign";
        };

        if (revokeDesktop)
            Assert.Throws<DesktopUnavailableException>(() =>
                Capture(process, native, () => blocked ? Rejected(false) : Ready()));
        else
            Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        var surface = Assert.Single(native.Surfaces);
        Assert.Equal(0, surface.Saves);
        Assert.Equal(1, surface.Releases);
        Assert.Equal(1, surface.Disposals);
    }

    [Theory]
    [InlineData(11, 9, 2, 3)]
    [InlineData(11, 9, 9, 7)]
    [InlineData(1, 5, 0, 1)]
    [InlineData(5, 1, 1, 0)]
    public void BitmapContent_DetectsSparseInteriorRgbVariation(int width, int height, int x, int y)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.Black);
        bitmap.SetPixel(x, y, Color.White);

        Assert.True(BitmapHasContent(bitmap));
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void BitmapContent_HandlesNegativeStride()
    {
        const int width = 11;
        const int height = 9;
        const int stride = width * 4;
        IntPtr memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(
            stride * height);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(
                new byte[stride * height], 0, memory, stride * height);
            using var bitmap = new Bitmap(
                width,
                height,
                -stride,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                IntPtr.Add(memory, stride * (height - 1)));
            bitmap.SetPixel(7, 5, Color.White);

            Assert.True(BitmapHasContent(bitmap));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(12, 34, 56)]
    public void BitmapContent_RejectsUniformRgb(int red, int green, int blue)
    {
        using var bitmap = new Bitmap(11, 9);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(red, green, blue));

        Assert.False(BitmapHasContent(bitmap));
    }

    [Fact]
    public void BitmapContent_RejectsAlphaOnlyVariation()
    {
        using var bitmap = new Bitmap(11, 9);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(12, 34, 56));
        bitmap.SetPixel(10, 8, Color.FromArgb(1, 12, 34, 56));

        Assert.False(BitmapHasContent(bitmap));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_UniformOrAlphaOnlyBitmapNeverSaves(bool alphaVariation)
    {
        using var bitmap = new Bitmap(11, 9);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(12, 34, 56));
        if (alphaVariation)
            bitmap.SetPixel(10, 8, Color.FromArgb(1, 12, 34, 56));
        using var process = new Process();
        var native = new CaptureNative { ContentProbe = () => BitmapHasContent(bitmap) };

        Assert.Throws<WindowCaptureException>(() => Capture(process, native, Ready));
        Assert.Equal(6, native.Surfaces.Count);
        Assert.All(native.Surfaces, surface =>
        {
            Assert.Equal(0, surface.Saves);
            Assert.Equal(1, surface.ContentChecks);
            Assert.Equal(1, surface.Releases);
            Assert.Equal(1, surface.Disposals);
        });
    }

    private static bool BitmapHasContent(Bitmap bitmap) => ScreenshotHelper.HasContent(bitmap);

    [Fact]
    public void Workflow_DirectDiagnosticsAreCliOnly()
    {
        string workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "e2e-run.yml"));
        Assert.DoesNotContain("GUI startup probe", workflow);
        Assert.DoesNotContain("- name: Capture WinForms screenshots", workflow);
        var starts = System.Text.RegularExpressions.Regex.Matches(workflow, @"Start-Process[^\r\n]*");
        Assert.Single(starts);
        Assert.Contains("-ArgumentList \"--version\"", starts[0].Value);
        Assert.Contains("- name: Run E2E tests", workflow);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FEBuilderGBA.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("E2E source checkout not found.");
    }

    private static string Capture(Process process, CaptureNative native,
        Func<DesktopReadinessResult> probe, Action<int>? wait = null) =>
        ScreenshotHelper.CaptureWindow(process, new IntPtr(12), "test", ".", false, probe, native,
            wait ?? (_ => { }));

    private static DesktopReadinessResult Rejected(bool unknown) =>
        DesktopReadiness.Evaluate(unknown
            ? new DesktopObservation(null, null, null)
            : new DesktopObservation(0, 0, true));

    private sealed class DesktopNative : IDesktopReadinessNative
    {
        public string Failure = "";
        public uint Session = 17;
        public int State;
        public int Input = 1;
        public uint QueriedSession;
        public IntPtr QueriedDesktop;
        public int Frees;

        public bool TryGetOwnSession(out uint session)
        {
            if (Failure == "throws") throw new InvalidOperationException();
            session = Session;
            return Failure != "session";
        }
        public bool QuerySessionState(uint session, out IntPtr buffer, out uint bytes)
        {
            QueriedSession = session;
            buffer = Failure == "null-buffer" ? IntPtr.Zero : new IntPtr(10);
            bytes = Failure == "short-buffer" ? 3u : Failure == "long-buffer" ? 8u : 4u;
            if (Failure == "wts-throws") throw new InvalidOperationException();
            return Failure != "wts-query";
        }
        public int ReadState(IntPtr buffer)
        {
            if (Failure == "read-throws") throw new InvalidOperationException();
            return Failure == "invalid-state" ? 10 : State;
        }
        public void FreeSessionBuffer(IntPtr buffer)
        {
            Assert.Equal(new IntPtr(10), buffer);
            Frees++;
            if (Failure == "free-throws") throw new InvalidOperationException();
        }
        public IntPtr GetOwnThreadDesktop() => Failure == "desktop" ? IntPtr.Zero : new(20);
        public bool QueryInputDesktop(IntPtr desktop, out int input, out uint bytes)
        {
            QueriedDesktop = desktop;
            input = Failure == "input-value" ? 2 : Input;
            bytes = Failure == "input-length" ? 0u : 4u;
            return Failure != "input-query";
        }
    }

    private sealed class CaptureNative : IWindowCaptureNative
    {
        public string Failure = "";
        public int IdentityQueries, ProcessIdQueries, BoundsQueries, Prints;
        public Process? Process;
        public List<uint> Flags = new();
        public List<CaptureSurface> Surfaces = new();
        public Queue<(int Width, int Height)> BoundsResults = new();
        public Queue<bool> PrintResults = new();
        public Queue<bool> ContentResults = new();
        public Action? OnBounds, OnCreateSurface, OnGetHdc, OnPrint, OnContent;
        public Func<bool>? ContentProbe;

        public CaptureProcessIdentity GetProcessIdentity(Process process)
        {
            Process = process;
            IdentityQueries++;
            if (Failure == "identity-query") throw new InvalidOperationException();
            return new(Failure == "invalid-pid" ? -1 : 42, Failure != "exited",
                Failure == "reused-pid" ? 43u : Failure == "missing-handle" ? 0u : 42u);
        }
        public uint GetWindowProcessId(IntPtr window)
        {
            ProcessIdQueries++;
            if (Failure == "process-id-query") throw new InvalidOperationException();
            return Failure == "foreign" ? 43u : Failure == "stale-window" ? 0u : 42u;
        }
        public (int Width, int Height) GetWindowSize(IntPtr window)
        {
            BoundsQueries++;
            OnBounds?.Invoke();
            if (Failure == "bounds") throw new InvalidOperationException();
            if (BoundsResults.Count > 0) return BoundsResults.Dequeue();
            return (Failure == "empty-bounds" ? 0 : 100, 100);
        }
        public IWindowCaptureSurface CreateSurface(int width, int height)
        {
            var surface = new CaptureSurface
            {
                Failure = Failure,
                ContentResults = ContentResults,
                OnGetHdc = OnGetHdc,
                OnContent = OnContent,
                ContentProbe = ContentProbe
            };
            Surfaces.Add(surface);
            OnCreateSurface?.Invoke();
            return surface;
        }
        public bool PrintWindow(IntPtr window, IntPtr hdc, uint flags)
        {
            Prints++;
            Flags.Add(flags);
            OnPrint?.Invoke();
            if (Failure == "print-throws") throw new InvalidOperationException();
            if (PrintResults.Count > 0) return PrintResults.Dequeue();
            return Failure != "print-false" && !(Failure == "first-print-false" && Prints == 1);
        }
    }

    private sealed class CaptureSurface : IWindowCaptureSurface
    {
        public string Failure = "";
        public int Releases, Disposals, Saves, ContentChecks;
        public Queue<bool> ContentResults = new();
        public Action? OnGetHdc, OnContent;
        public Func<bool>? ContentProbe;
        public IntPtr GetHdc()
        {
            OnGetHdc?.Invoke();
            if (Failure == "get-hdc") throw new InvalidOperationException();
            return Failure == "zero-hdc" ? IntPtr.Zero : new(30);
        }
        public void ReleaseHdc(IntPtr hdc)
        {
            Assert.Equal(new IntPtr(30), hdc);
            Releases++;
            if (Failure == "release-throws") throw new InvalidOperationException();
        }
        public bool HasContent()
        {
            ContentChecks++;
            OnContent?.Invoke();
            if (Failure == "content-throws") throw new InvalidOperationException();
            if (ContentProbe != null) return ContentProbe();
            if (ContentResults.Count > 0) return ContentResults.Dequeue();
            return Failure != "empty-content";
        }
        public void Save(string path)
        {
            Saves++;
            if (Failure == "save-throws") throw new IOException();
        }
        public void Dispose() => Disposals++;
    }
}
