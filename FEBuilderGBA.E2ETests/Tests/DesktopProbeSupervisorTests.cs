using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FEBuilderGBA.DesktopReadinessProbe;

namespace FEBuilderGBA.E2ETests.Tests;

public class DesktopProbeSupervisorTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly long Now = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static Dictionary<string, object?> PacketData()
    {
        object Pin(string root, string path) => new { root, path, bytes = 10, sha256 = Hash };
        return new()
        {
            ["schema"] = "desktop-probe-packet-v1",
            ["sourceHead"] = new string('c', 40),
            ["sourceTree"] = new string('d', 40),
            ["sourceGateReference"] = "https://github.com/laqieer/FEBuilderGBA/issues/2161#issuecomment-1234",
            ["sourceGateSha256"] = Hash,
            ["attemptId"] = new string('b', 32),
            ["host"] = new { path = @"C:\dotnet\dotnet.exe", bytes = 10, sha256 = Hash },
            ["toolDirectory"] = @"C:\probe",
            ["runtimeVersion"] = "10.0.12",
            ["hostFxrVersion"] = "10.0.12",
            ["evidenceDirectory"] = @"C:\evidence",
            ["systemRoot"] = @"C:\Windows",
            ["versionDirectories"] = new
            {
                hostfxr = new[] { "10.0.12" },
                core = new[] { "10.0.12", "8.0.31" },
                desktop = new[] { "10.0.12", "8.0.31" }
            },
            ["files"] = new[]
            {
                Pin("tool", "DesktopReadinessProbe.dll"),
                Pin("tool", "DesktopReadinessProbe.deps.json"),
                Pin("tool", "DesktopReadinessProbe.runtimeconfig.json"),
                Pin("tool", "FEBuilderGBA.E2ETests.dll"),
                Pin("hostfxr", "hostfxr.dll"),
                Pin("core", "System.Private.CoreLib.dll"),
                Pin("desktop", "System.Windows.Forms.dll")
            }
        };
    }

    private static Dictionary<string, object?> GrantData() => new()
    {
        ["schema"] = "desktop-probe-grant-v1",
        ["attemptId"] = new string('b', 32),
        ["packetSha256"] = Hash,
        ["sourceHead"] = new string('c', 40),
        ["sourceTree"] = new string('d', 40),
        ["operationalSourceReference"] = "https://github.com/laqieer/FEBuilderGBA/issues/2161#issuecomment-2345",
        ["operationalSourceSha256"] = Hash,
        ["issuerReference"] = "https://github.com/laqieer/FEBuilderGBA/issues/2161#issuecomment-3456",
        ["issuedUtcTicks"] = Now,
        ["expiresUtcTicks"] = Now + TimeSpan.FromMinutes(10).Ticks,
        ["authorized"] = true,
        ["operationalSourceSecurityAccepted"] = true,
        ["retainedUnconfirmedCustodyAccepted"] = true
    };

    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static ProbePacket Packet() => ProbeSupervisor.ParsePacket(Json(PacketData()));
    private static ProbeGrant Grant(ProbePacket packet) =>
        ProbeSupervisor.ParseGrant(Json(GrantData()), packet, Hash, Now);

    [Fact]
    public void ExactDataSchemas_AreAccepted()
    {
        var packet = Packet();
        var grant = Grant(packet);
        Assert.Equal(7, packet.Files.Length);
        Assert.Equal(packet.AttemptId, grant.AttemptId);
        Assert.Equal("10.0.12", packet.RuntimeVersion);
    }

    [Theory]
    [InlineData("10.0.11", "10.0.11", "10.0.12")]
    [InlineData("10.0.9", "10.0.9", "10.0.10")]
    [InlineData("10.0.12", "10.0.12", "11.0.0")]
    public void OlderHostfxrSelectionIsRejectedBeforeConsumptionOrFactory(
        string selected, string first, string second)
    {
        var fixture = new Fixture();
        var data = PacketData();
        data["hostFxrVersion"] = selected;
        data["versionDirectories"] = new
        {
            hostfxr = new[] { first, second },
            core = new[] { "10.0.12" }, desktop = new[] { "10.0.12" }
        };
        Assert.Throws<InvalidDataException>(() =>
        {
            var packet = ProbeSupervisor.ParsePacket(Json(data));
            ProbeSupervisor.Execute(packet, Grant(packet), Hash, Hash, fixture.Admission,
                () => { fixture.FactoryCalled = true; return fixture.Child; }, fixture.Clock);
        });
        Assert.False(fixture.FactoryCalled);
        Assert.False(fixture.Admission.Consumed);
    }

    [Theory]
    [InlineData("10.0.12", "10.0.11", "10.0.12")]
    [InlineData("10.0.12", "10.0.12", "10.0.11")]
    [InlineData("10.0.10", "10.0.9", "10.0.10")]
    [InlineData("10.0.10", "9.0.99", "10.0.10")]
    public void GreatestCanonicalHostfxrUsesNumericNotLexicalOrdering(
        string selected, string first, string second)
    {
        var data = PacketData();
        data["hostFxrVersion"] = selected;
        data["versionDirectories"] = new
        {
            hostfxr = new[] { first, second },
            core = new[] { "10.0.12" }, desktop = new[] { "10.0.12" }
        };
        Assert.Equal(selected, ProbeSupervisor.ParsePacket(Json(data)).HostFxrVersion);
    }

    [Theory]
    [InlineData("10.0.012")]
    [InlineData("10.00.12")]
    [InlineData("010.0.12")]
    [InlineData("10.0.2147483648")]
    [InlineData("10.0.12-preview.1")]
    [InlineData("10.0.12+build")]
    [InlineData("10.0.12.0")]
    public void UnsupportedOrNoncanonicalHostfxrNamesFailClosed(string name)
    {
        var data = PacketData();
        data["versionDirectories"] = new
        {
            hostfxr = new[] { "10.0.12", name },
            core = new[] { "10.0.12" }, desktop = new[] { "10.0.12" }
        };
        Assert.Throws<InvalidDataException>(() => ProbeSupervisor.ParsePacket(Json(data)));
    }

    [Theory]
    [InlineData("schema", "other")]
    [InlineData("sourceHead", "not-a-commit")]
    [InlineData("sourceTree", "bad")]
    [InlineData("sourceGateSha256", "bad")]
    [InlineData("sourceGateReference", "https://example.com/approval")]
    [InlineData("attemptId", "../again")]
    [InlineData("toolDirectory", @"\\server\share")]
    [InlineData("toolDirectory", @"C:\probe\..\other")]
    [InlineData("evidenceDirectory", @"C:\probe\receipt")]
    [InlineData("runtimeVersion", "10.0.latest")]
    [InlineData("hostFxrVersion", "../10.0.12")]
    [InlineData("command", "private-command")]
    public void MalformedPacketField_IsRejected(string key, string value)
    {
        var data = PacketData();
        data[key] = value;
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket(Json(data)));
    }

    [Fact]
    public void PacketRejectsMissingAndCaseCollidingKeys()
    {
        var data = PacketData();
        data.Remove("sourceHead");
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket(Json(data)));
        var text = Encoding.UTF8.GetString(Json(PacketData()));
        text = text.Replace("\"schema\":", "\"Schema\":\"other\",\"schema\":", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket(Encoding.UTF8.GetBytes(text)));
    }

    [Theory]
    [InlineData(@"..\outside.dll")]
    [InlineData(@"C:\outside.dll")]
    [InlineData("outside/different.dll")]
    [InlineData("con.dll")]
    [InlineData("trailing.")]
    [InlineData("stream.dll:payload")]
    public void ClosureRejectsUnsafeRelativePaths(string path)
    {
        var data = JsonNode.Parse(Json(PacketData()))!;
        data["files"]!.AsArray().Add(new JsonObject
        {
            ["root"] = "tool", ["path"] = path, ["bytes"] = 10, ["sha256"] = Hash
        });
        Assert.Throws<InvalidDataException>(() => ProbeSupervisor.ParsePacket(Json(data)));
    }

    [Fact]
    public void ClosureRejectsDuplicatePathsMissingRootsAndOversize()
    {
        var data = JsonNode.Parse(Json(PacketData()))!;
        var duplicate = data["files"]![0]!.DeepClone();
        duplicate["path"] = "desktopreadinessprobe.DLL";
        data["files"]!.AsArray().Add(duplicate);
        Assert.Throws<InvalidDataException>(() => ProbeSupervisor.ParsePacket(Json(data)));
        data["files"] = new JsonArray();
        Assert.Throws<InvalidDataException>(() => ProbeSupervisor.ParsePacket(Json(data)));
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket(new byte[2097153]));
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket([0xef, 0xbb, 0xbf, 123, 125]));
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParsePacket([0xff, 0xfe]));
    }

    [Theory]
    [InlineData("authorized")]
    [InlineData("operationalSourceSecurityAccepted")]
    [InlineData("retainedUnconfirmedCustodyAccepted")]
    public void EveryRequiredGrantBooleanMustBeTrue(string key)
    {
        var packet = Packet();
        var data = GrantData();
        data[key] = false;
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParseGrant(Json(data), packet, Hash, Now));
        data[key] = "true";
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParseGrant(Json(data), packet, Hash, Now));
    }

    [Theory]
    [InlineData("attemptId")]
    [InlineData("packetSha256")]
    [InlineData("sourceHead")]
    [InlineData("sourceTree")]
    [InlineData("operationalSourceReference")]
    [InlineData("operationalSourceSha256")]
    [InlineData("issuerReference")]
    [InlineData("extra")]
    public void IncorrectGrantBindingsAreRejected(string key)
    {
        var data = GrantData();
        data[key] = "mismatch";
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParseGrant(Json(data), Packet(), Hash, Now));
    }

    [Theory]
    [InlineData(-1, 600)]
    [InlineData(0, 601)]
    [InlineData(1, 600)]
    [InlineData(0, 15)]
    [InlineData(0, 0)]
    public void InvalidGrantTimeOrReserveIsRejected(int issuedSeconds, int expiresSeconds)
    {
        var data = GrantData();
        data["issuedUtcTicks"] = Now + TimeSpan.FromSeconds(issuedSeconds).Ticks;
        data["expiresUtcTicks"] = Now + TimeSpan.FromSeconds(expiresSeconds).Ticks;
        Assert.ThrowsAny<Exception>(() => ProbeSupervisor.ParseGrant(Json(data), Packet(), Hash, Now));
    }

    [Fact]
    public void ExactSixteenSecondReserveIsAccepted()
    {
        var data = GrantData();
        data["expiresUtcTicks"] = Now + TimeSpan.FromSeconds(16).Ticks;
        Assert.NotNull(ProbeSupervisor.ParseGrant(Json(data), Packet(), Hash, Now));
    }

    [Fact]
    public void ChildEnvironmentIsFixedAndContainsNoInheritedHooks()
    {
        var env = ProbeSupervisor.ChildEnvironment(Packet());
        Assert.Equal(new[] { "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_EnableDiagnostics",
            "DOTNET_MULTILEVEL_LOOKUP", "DOTNET_ROLL_FORWARD", "DOTNET_ROOT_X86",
            "SystemRoot", "TEMP", "TMP", "windir" }, env.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Disable", env["DOTNET_ROLL_FORWARD"]);
        Assert.Equal("0", env["DOTNET_EnableDiagnostics"]);
        Assert.Equal(@"C:\evidence", env["TEMP"]);
        Assert.DoesNotContain("PATH", env.Keys);
        Assert.DoesNotContain("DOTNET_STARTUP_HOOKS", env.Keys);
    }

    [Fact]
    public void ActualStartDescriptorHasOnlyTheFixedChildAndClearedEnvironment()
    {
        var packet = Packet();
        var info = ProbeSupervisor.ChildStartInfo(packet);
        Assert.Equal(packet.Host.Path, info.FileName);
        Assert.Equal(new[] { @"C:\probe\DesktopReadinessProbe.dll", "--probe-own-desktop" }, info.ArgumentList);
        Assert.Equal("", info.Arguments);
        Assert.Equal(packet.ToolDirectory, info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.Equal(9, info.Environment.Count);
        Assert.Equal("Disable", info.Environment["DOTNET_ROLL_FORWARD"]);
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(128, 127)]
    [InlineData(128, 128)]
    [InlineData(128, 129)]
    [InlineData(128, 65536)]
    [InlineData(1024, 1024)]
    [InlineData(1024, 1025)]
    [InlineData(1024, 65536)]
    public void RealRawDrainKeepsOnlyTheCapAndOneSentinel(int cap, int bytes)
    {
        using var stream = new MemoryStream(new byte[bytes]);
        using var drain = new ProbeSupervisor.Drain(stream, cap);
        drain.Begin();
        for (int i = 0; i < 4; i++) drain.Pump();
        Assert.Equal(Math.Min(cap + 1, bytes), drain.Bytes().Length);
        Assert.Equal(bytes > cap, drain.Failed);
        Assert.Equal(bytes <= cap, drain.Eof);
    }

    [Theory]
    [InlineData(9970)]
    [InlineData(10000)]
    public void ExitObservedAtOrAfterRunDeadlineIsNotPromoted(int observationDelay)
    {
        var fixture = new Fixture();
        fixture.Child.ExitObservationDelay = observationDelay;
        Assert.Equal("TimedOut", fixture.Run().Outcome);
    }

    [Fact]
    public void EofObservedAfterDrainBudgetIsNotPromoted()
    {
        var fixture = new Fixture();
        fixture.Child.DrainDelay = 1001;
        Assert.NotEqual("Observed", fixture.Run().Outcome);
    }

    [Fact]
    public void TimeConsumedByAdmissionCannotSpendTheNativeReserve()
    {
        var fixture = new Fixture();
        fixture.Admission.PinDelayTicks = TimeSpan.FromSeconds(585).Ticks;
        var report = fixture.Run();
        Assert.Equal("AdmissionFailed", report.Outcome);
        Assert.False(fixture.FactoryCalled);
        Assert.False(fixture.Admission.Consumed);
        Assert.True(fixture.Admission.Pins.Disposed);
    }

    [Fact]
    public void MalformedSupervisorDispatchDoesNotReadFilesOrDiscloseArguments()
    {
        using var output = new StringWriter();
        int code = ProbeSupervisor.Run(["--supervise-own-desktop", "private-argument"], output);
        Assert.Equal(4, code);
        Assert.Equal("Supervisor=AdmissionFailed;State=None;Reason=None\n", output.ToString());
    }

    [Theory]
    [InlineData("Ready", "ActiveInputDesktop", 0)]
    [InlineData("Blocked", "SessionZero", 2)]
    [InlineData("Blocked", "SessionInactive", 2)]
    [InlineData("Blocked", "ThreadDesktopNotInput", 2)]
    [InlineData("Unknown", "SessionQueryFailed", 3)]
    [InlineData("Unknown", "SessionStateUnknown", 3)]
    [InlineData("Unknown", "ThreadDesktopUnknown", 3)]
    [InlineData("Unknown", "NativeQueryFailed", 3)]
    public void ValidObservationPreservesStateAndExit(string state, string reason, int code)
    {
        var fixture = new Fixture();
        fixture.Child.Stdout = Encoding.ASCII.GetBytes($"State={state};Reason={reason}\n");
        fixture.Child.Code = code;
        var report = fixture.Run();
        Assert.Equal("Observed", report.Outcome);
        Assert.Equal(state, report.State);
        Assert.Equal(reason, report.Reason);
        Assert.Equal(code, report.ExitCode);
        Assert.True(report.ExitConfirmed);
        Assert.Equal(0, report.KillAttempts);
        Assert.Equal(new[] { "start", "stdin", "drains", "observe" }, fixture.Child.Events.Take(4));
        Assert.True(fixture.Admission.Consumed);
        Assert.True(fixture.Child.Disposed);
        Assert.True(fixture.Admission.Pins.Disposed);
    }

    [Theory]
    [InlineData("State=Ready;Reason=ActiveInputDesktop\r\n", 0)]
    [InlineData("State=Ready;Reason=ActiveInputDesktop\n\n", 0)]
    [InlineData("State=Ready;Reason=ActiveInputDesktop", 0)]
    [InlineData("State=Ready;Reason=SessionInactive\n", 0)]
    [InlineData("State=Ready;Reason=ActiveInputDesktop\n", 2)]
    [InlineData("State=1;Reason=ActiveInputDesktop\n", 0)]
    [InlineData("State=Unknown;Reason=SessionZero\n", 3)]
    [InlineData("State=ExecutionError;Reason=ProbeFailed\n", 4)]
    public void InvalidOrErrorProtocolIsNeverObservation(string line, int exitCode)
    {
        var fixture = new Fixture();
        fixture.Child.Stdout = Encoding.UTF8.GetBytes(line);
        fixture.Child.Code = exitCode;
        Assert.NotEqual("Observed", fixture.Run().Outcome);
    }

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    [InlineData("stderr-nonempty")]
    [InlineData("missing-eof")]
    [InlineData("read-error")]
    public void OutputFailuresNeverBecomeSuccess(string fault)
    {
        var fixture = new Fixture();
        if (fault == "stdout") fixture.Child.Stdout = new byte[129];
        if (fault == "stderr") fixture.Child.Stderr = new byte[1025];
        if (fault == "stderr-nonempty") fixture.Child.Stderr = [65];
        if (fault == "missing-eof") fixture.Child.Eof = false;
        if (fault == "read-error") fixture.Child.ReadError = true;
        var report = fixture.Run();
        Assert.NotEqual("Observed", report.Outcome);
        Assert.True(report.ExitConfirmed);
        Assert.InRange(report.ElapsedMilliseconds, 0, 16000);
    }

    [Theory]
    [InlineData("pins")]
    [InlineData("marker")]
    [InlineData("expiry")]
    public void AdmissionRefusalNeverCreatesChild(string refusal)
    {
        var fixture = new Fixture();
        fixture.Admission.PinFailure = refusal == "pins";
        fixture.Admission.MarkerFailure = refusal == "marker";
        if (refusal == "expiry")
            fixture.Admission.UtcNowTicks = Now + TimeSpan.FromMinutes(10).Ticks;
        var report = fixture.Run();
        Assert.Equal("AdmissionFailed", report.Outcome);
        Assert.False(fixture.FactoryCalled);
        Assert.False(fixture.Child.Started);
    }

    [Fact]
    public void SpawnFailureConsumesIdentityWithoutRetry()
    {
        var fixture = new Fixture();
        fixture.Child.StartFailure = true;
        var report = fixture.Run();
        Assert.True(report.Consumed);
        Assert.Equal("StartFailed", report.Outcome);
        Assert.Equal(1, fixture.Child.StartCalls);
        Assert.Equal(0, report.KillAttempts);
    }

    [Theory]
    [InlineData("early-exit")]
    [InlineData("wrong-image")]
    [InlineData("not-live-after")]
    [InlineData("query-error")]
    public void UnconfirmedLiveIdentityIsNeverAccepted(string fault)
    {
        var fixture = new Fixture();
        fixture.Child.IdentityFault = fault;
        Assert.NotEqual("Observed", fixture.Run().Outcome);
    }

    [Fact]
    public void TimeoutKillsOnlyOnceAndStillFailsAfterConfirmedCleanup()
    {
        var fixture = new Fixture();
        fixture.Child.ExitAt = long.MaxValue;
        var report = fixture.Run();
        Assert.Equal("TimedOut", report.Outcome);
        Assert.Equal(1, report.KillAttempts);
        Assert.True(report.ExitConfirmed);
        Assert.Equal(1, fixture.Child.KillCalls);
        Assert.InRange(report.ElapsedMilliseconds, 10000, 16000);
    }

    [Fact]
    public void OrdinaryExitDuringTheSingleCleanupAttemptRemainsConfirmed()
    {
        var fixture = new Fixture();
        fixture.Child.ExitAt = long.MaxValue;
        fixture.Child.ExitDuringKill = true;
        fixture.Child.KillFailure = true;
        var report = fixture.Run();
        Assert.Equal("TimedOut", report.Outcome);
        Assert.True(report.ExitConfirmed);
        Assert.Equal(1, fixture.Child.KillCalls);
        Assert.False(fixture.Admission.Retained);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfirmedCleanupRetainsOwnerAndPinsEvenIfPublishingFails(bool publishingFails)
    {
        var fixture = new Fixture();
        fixture.Child.ExitAt = long.MaxValue;
        fixture.Child.KillFailure = true;
        fixture.Admission.PublishFailure = publishingFails;
        var report = fixture.Run();
        Assert.Equal("CleanupUnconfirmed", report.Outcome);
        Assert.False(report.ExitConfirmed);
        Assert.Equal(1, fixture.Child.KillCalls);
        Assert.True(fixture.Admission.Retained);
        Assert.False(fixture.Child.Disposed);
        Assert.False(fixture.Admission.Pins.Disposed);
        Assert.Equal(publishingFails, report.ReportingFailed);
    }

    [Fact]
    public void ReportingFailureCannotPromoteAnObservation()
    {
        var fixture = new Fixture();
        fixture.Admission.PublishFailure = true;
        var report = fixture.Run();
        Assert.True(report.ReportingFailed);
        Assert.True(report.ExitConfirmed);
        Assert.True(fixture.Child.Disposed);
    }

    private sealed class Clock : IProbeClock
    {
        public long Milliseconds { get; private set; }
        public void Delay(int milliseconds) => Milliseconds += milliseconds;
    }

    private sealed class HeldPins : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Admission : IProbeAdmission
    {
        public long UtcNowTicks { get; set; } = Now;
        public HeldPins Pins { get; } = new();
        public bool PinFailure, MarkerFailure, PublishFailure, Consumed, Retained;
        public long PinDelayTicks;
        public IDisposable VerifyAndHold(ProbePacket packet)
        {
            if (PinFailure) throw new IOException("private pin failure");
            UtcNowTicks += PinDelayTicks;
            return Pins;
        }
        public void Consume(ProbePacket packet, string packetHash, string grantHash)
        {
            if (MarkerFailure || Consumed) throw new IOException("already consumed");
            Consumed = true;
        }
        public void Publish(ProbePacket packet, string packetHash, string grantHash, ProbeReport report)
        {
            if (PublishFailure) throw new IOException("private report failure");
        }
        public void Retain(IProbeChild child, IDisposable pins, ProbeReport report) => Retained = true;
    }

    private sealed class Child(Clock clock, Admission admission) : IProbeChild
    {
        public bool Started { get; private set; }
        public bool StartFailure, KillFailure, ReadError, Disposed, ExitDuringKill;
        public bool Eof = true;
        public long ExitAt = 30;
        public string IdentityFault = "";
        public int StartCalls, KillCalls, Code = 2;
        public int ExitObservationDelay, DrainDelay;
        private bool exitObserved, drainDelayed;
        public List<string> Events { get; } = [];
        public byte[] Stdout = Encoding.ASCII.GetBytes("State=Blocked;Reason=SessionInactive\n");
        public byte[] Stderr = [];
        public void Start()
        {
            Assert.True(admission.Consumed);
            StartCalls++;
            Events.Add("start");
            if (StartFailure) throw new IOException("private startup failure");
            Started = true;
        }
        public void CloseInput() => Events.Add("stdin");
        public void BeginDrains() => Events.Add("drains");
        public ProbeIdentity Observe()
        {
            Assert.Contains("drains", Events);
            Events.Add("observe");
            if (IdentityFault == "query-error") throw new IOException("private image failure");
            return new(123, 123456, IdentityFault == "wrong-image" ? @"C:\other.exe" : @"C:\dotnet\dotnet.exe",
                IdentityFault != "early-exit", IdentityFault != "not-live-after");
        }
        public bool HasExited
        {
            get
            {
                bool exited = clock.Milliseconds >= ExitAt;
                if (exited && !exitObserved)
                {
                    exitObserved = true;
                    clock.Delay(ExitObservationDelay);
                }
                return exited;
            }
        }
        public int ExitCode => Code;
        public ProbeStreams Pump()
        {
            if (exitObserved && !drainDelayed)
            {
                drainDelayed = true;
                clock.Delay(DrainDelay);
            }
            bool eof = Eof && clock.Milliseconds >= ExitAt;
            return clock.Milliseconds >= 20
                ? new(Stdout, Stderr, eof, eof, ReadError)
                : new([], [], false, false);
        }
        public void Kill()
        {
            KillCalls++;
            if (ExitDuringKill) ExitAt = clock.Milliseconds;
            if (KillFailure) throw new IOException("private kill failure");
            ExitAt = clock.Milliseconds + 10;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public Admission Admission { get; } = new();
        public Child Child { get; }
        public bool FactoryCalled;
        public Fixture() => Child = new(Clock, Admission);
        public ProbeReport Run()
        {
            var packet = Packet();
            return ProbeSupervisor.Execute(packet, Grant(packet), Hash, Hash, Admission,
                () => { FactoryCalled = true; return Child; }, Clock);
        }
    }
}
