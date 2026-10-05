using System.Diagnostics;

namespace FEBuilderGBA.Avalonia.Tests;

public class NativeProbeDiagnosticsTests
{
    [Theory]
    [InlineData("valid-discovery")]
    [InlineData("launcher-entry")]
    [InlineData("missing-host-entry")]
    [InlineData("late-host-entry")]
    [InlineData("inconsistent-host-pid")]
    [InlineData("parent-host")]
    [InlineData("missing-host-runtime")]
    [InlineData("inconsistent-host-runtime")]
    public void RoundTripDistinguishesDiscoveryEntryFromActualTesthost(string scenario)
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, Environment.ProcessId);
        foreach (string stage in new[] { "managed-entry", "fixture-start", "fixture-end", "probe-entry",
            "acquire-start", "acquire-end", "publish-start", "publish-end" })
            session.Record(stage);
        int[] childPids = Enumerable.Range(1, 4).Where(pid => pid != Environment.ProcessId).Take(3).ToArray();
        int host = childPids[0], discovery = childPids[1], launcher = childPids[2];
        var lines = session.Snapshot().Split('\n').Where(line => line.StartsWith("owner-pid=", StringComparison.Ordinal))
            .Select(line => line.Replace($"managed-pid={Environment.ProcessId};", $"managed-pid={host};")).ToList();
        string discoveryEntry = lines[0].Replace($"managed-pid={host};",
            $"managed-pid={(scenario == "launcher-entry" ? launcher : discovery)};");
        if (scenario == "missing-host-entry") lines.RemoveAt(0);
        if (scenario == "late-host-entry") { string entry = lines[0]; lines.RemoveAt(0); lines.Add(entry); }
        if (scenario == "inconsistent-host-pid")
            lines[^1] = lines[^1].Replace($"managed-pid={host};", $"managed-pid={discovery};");
        if (scenario == "parent-host")
            lines = lines.Select(line => line.Replace($"managed-pid={host};", $"managed-pid={Environment.ProcessId};")).ToList();
        if (scenario == "missing-host-runtime")
            lines[1] = lines[1].Replace("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ";", "runtime=unknown;");
        if (scenario == "inconsistent-host-runtime")
            lines[1] = lines[1].Replace("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ";", "runtime=.NET different;");
        lines.Insert(0, discoveryEntry);
        string report = string.Join("\n", lines);
        if (scenario is "valid-discovery" or "launcher-entry")
            Assert.Equal(host, PatchManagerOperationGuardTests.AssertNativeDiagnosticRoundTrip(report, Environment.ProcessId, launcher));
        else
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                PatchManagerOperationGuardTests.AssertNativeDiagnosticRoundTrip(report, Environment.ProcessId, launcher));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing-stage")]
    [InlineData("parent-pid")]
    [InlineData("missing-runtime")]
    [InlineData("launcher-pid")]
    [InlineData("mismatched-pid")]
    public void RoundTripContractRejectsMissingOrParentInjectedEvidence(string scenario)
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, Environment.ProcessId);
        foreach (string stage in new[] { "managed-entry", "fixture-start", "fixture-end", "probe-entry",
            "acquire-start", "acquire-end", "publish-start", "publish-end" })
            session.Record(stage);
        string report = session.Snapshot();
        if (scenario != "parent-pid")
            report = report.Replace("managed-pid=" + Environment.ProcessId + ";",
                "managed-pid=" + (Environment.ProcessId == 1 ? 2 : 1) + ";");
        if (scenario == "missing-stage") report = report.Replace("stage=fixture-end;", "stage=fixture-unknown;");
        if (scenario == "missing-runtime")
            report = report.Replace("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ";", "runtime=unknown;");
        int selectedPid = Environment.ProcessId == 1 ? 2 : 1;
        if (scenario == "mismatched-pid")
            report = report.Replace($"managed-pid={selectedPid}; stage=publish-end;", $"managed-pid={selectedPid + 1}; stage=publish-end;");
        if (scenario == "valid")
            PatchManagerOperationGuardTests.AssertNativeDiagnosticRoundTrip(report, Environment.ProcessId);
        else
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                PatchManagerOperationGuardTests.AssertNativeDiagnosticRoundTrip(report, Environment.ProcessId,
                    scenario == "launcher-pid" ? selectedPid : null));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("share")]
    [InlineData("redirect")]
    public void SecondaryReadFailurePreservesParsedStageJournal(string failure)
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, Environment.ProcessId);
        session.Record("managed-entry");
        session.Record("fixture-start");
        string path = Path.Combine(session.DirectoryPath, "native-probe-errors");
        File.WriteAllBytes(path, failure == "utf8" ? [0xff] : [0x65]);
        FileStream? held = null;
        try
        {
            if (failure == "share") held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (failure == "redirect")
            {
                File.Delete(path);
                File.CreateSymbolicLink(path, session.StagePath);
            }
            string report = session.Snapshot();
            Assert.Contains("stage=managed-entry", report);
            Assert.Contains("stage=fixture-start", report);
            Assert.Contains("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, report);
            Assert.Contains("secondary-errors: read-error;", report);
            Assert.DoesNotContain(session.DirectoryPath, report);
        }
        finally { held?.Dispose(); }
    }

    [Fact]
    public void EachCappedChannelExplicitlyMarksTruncationAfterPathRedaction()
    {
        string path = @"C:\private-profile\partial";
        string report = NativeProbeDiagnostics.Redact(path + new string('x', 2_000), "", 64);
        Assert.Contains("<owned-path>", report);
        Assert.Contains("[diagnostics truncated]", report);
        Assert.DoesNotContain("private-profile", report);
        string exception = NativeProbeDiagnostics.ExceptionText(new IOException(new string('e', 2_000)), "", 64);
        Assert.Contains("[diagnostics truncated]", exception);
    }

    [Theory]
    [InlineData(@"trace '\\private-server\users\private-profile\file")]
    [InlineData("truncated path C:")]
    public void RedactionIncludesUncAndTruncatedDrivePrefixes(string text)
    {
        string report = NativeProbeDiagnostics.Redact(text, "", 1_024);
        Assert.Contains("<owned-path>", report);
        Assert.DoesNotContain("private", report);
        Assert.DoesNotContain("C:", report);
    }

    [Fact]
    public void DiagnosticsDoNotMutateLeaseFixtureSnapshot()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, Environment.ProcessId);
        session.Record("managed-entry");
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "native-probe-" + fixture.Token)));
        Assert.StartsWith(Path.Combine(AppContext.BaseDirectory, "TestResults") + Path.DirectorySeparatorChar,
            session.DirectoryPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [Fact]
    public void ParserRejectsThirteenthRecordWithoutTruncationSentinel()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, Environment.ProcessId);
        for (int i = 0; i < NativeProbeDiagnostics.MaximumRecords; i++)
            session.Record("managed-entry");
        string text = File.ReadAllText(session.StagePath);
        File.AppendAllText(session.StagePath, text.Split('\n')[1] + "\n");
        Assert.Contains("stage count cap exceeded", session.Snapshot());
    }

    [Fact]
    public void StagesCarryTokenOwnerPidRuntimeAndOrderedEntriesIndependentlyOfStreams()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        session.Record("managed-entry");
        session.Record("fixture-start");
        session.Record("fixture-end");
        session.Record("probe-entry");
        string report = session.Snapshot();
        Assert.Contains("owner-pid=123", report);
        Assert.Contains("managed-pid=" + Environment.ProcessId, report);
        Assert.Contains("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, report);
        Assert.True(report.IndexOf("managed-entry", StringComparison.Ordinal) < report.IndexOf("fixture-start", StringComparison.Ordinal));
        Assert.Contains("fixture-end", report);
        Assert.Contains("probe-entry", report);
        Assert.DoesNotContain(fixture.Root, report);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("../escape")]
    public void InvalidLaunchTokensAreRejected(string token)
    {
        using var fixture = new DiagnosticFixture();
        Assert.Throws<InvalidDataException>(() => NativeProbeDiagnostics.Prepare(fixture.Root, token, 123));
    }

    [Fact]
    public void RootContainmentIsSeparatorAwareAndRejectsRedirectedFiles()
    {
        using var fixture = new DiagnosticFixture();
        Assert.Throws<InvalidDataException>(() => NativeProbeDiagnostics.Prepare(
            Path.Combine(AppContext.BaseDirectory, "TestResults-other", fixture.Token), fixture.Token, 123));
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        File.Delete(session.StagePath);
        Directory.CreateDirectory(session.StagePath);
        Assert.Contains("read-error", session.Snapshot());
        Directory.Delete(session.StagePath);
    }

    [Fact]
    public void MissingCorruptMismatchedAndOversizedRecordsAreExplicit()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        Assert.Contains("absent", session.Snapshot());
        File.WriteAllText(session.StagePath, "broken");
        Assert.Contains("corrupt", session.Snapshot());
        File.WriteAllText(session.StagePath, Guid.NewGuid().ToString("N") + "|123|1|managed-entry|time|runtime\n");
        Assert.Contains("mismatch", session.Snapshot());
        File.WriteAllBytes(session.StagePath, new byte[NativeProbeDiagnostics.MaximumBytes + 1]);
        Assert.Contains("truncated", session.Snapshot());
    }

    [Fact]
    public void WriterAndParserRespectCountAndRecordCaps()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        for (int i = 0; i < NativeProbeDiagnostics.MaximumRecords + 2; i++) session.Record("probe-entry");
        Assert.True(new FileInfo(session.StagePath).Length <= NativeProbeDiagnostics.MaximumBytes);
        Assert.Contains("truncated", session.Snapshot());
        Assert.Throws<InvalidDataException>(() => session.Record("arbitrary-stage"));
        File.WriteAllText(session.StagePath, new string('x', NativeProbeDiagnostics.MaximumRecordBytes + 1));
        Assert.Contains("corrupt", session.Snapshot());
    }

    [Fact]
    public async Task ConcurrentWritersRemainParseableAndPidCorrelated()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => session.Record("probe-entry"))));
        var report = session.Snapshot();
        Assert.DoesNotContain("corrupt", report);
        Assert.DoesNotContain("read-error", report);
        Assert.Equal(8, report.Split("managed-pid=", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TraceConfigurationIsChildLocalAndRemovesBothTraceFileVariables()
    {
        var start = new ProcessStartInfo("dotnet");
        start.Environment["DOTNET_HOST_TRACEFILE"] = "private-path";
        start.Environment["COREHOST_TRACEFILE"] = "private-path";
        NativeProbeDiagnostics.ConfigureTrace(start);
        Assert.Equal("1", start.Environment["DOTNET_HOST_TRACE"]);
        Assert.Equal("1", start.Environment["COREHOST_TRACE"]);
        Assert.False(start.Environment.ContainsKey("DOTNET_HOST_TRACEFILE"));
        Assert.False(start.Environment.ContainsKey("COREHOST_TRACEFILE"));
    }

    [Fact]
    public void RedactionCoversSlashVariantsAndPrefixesAtRetentionBoundary()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "TestResults", "private-owned-root");
        string prefix = root[..^4];
        string report = NativeProbeDiagnostics.Redact("before " + root.Replace('\\', '/') + " after " + prefix,
            root, 40_000);
        Assert.DoesNotContain("private-owned", report);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), report);
        Assert.Contains("<owned-path>", report);
    }

    [Fact]
    public void InvalidActivationIsInertButRetainsSecondaryEvidence()
    {
        var activation = NativeProbeDiagnostics.Activate("invalid", "invalid", "invalid");
        Assert.Null(activation.Session);
        Assert.Contains("diagnostic-init-error", activation.Error);
        var ordinary = NativeProbeDiagnostics.Activate(null, null, null);
        Assert.Null(ordinary.Session);
        Assert.Null(ordinary.Error);
    }

    [Fact]
    public void ValidActivationRequiresMatchingHeaderAndOwner()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        Assert.NotNull(NativeProbeDiagnostics.Activate(fixture.Token, session.DirectoryPath, "123").Session);
        var mismatch = NativeProbeDiagnostics.Activate(fixture.Token, session.DirectoryPath, "124");
        Assert.Null(mismatch.Session);
        Assert.Contains("diagnostic-init-error", mismatch.Error);
        Assert.Null(NativeProbeDiagnostics.Activate(Guid.NewGuid().ToString("N"), session.DirectoryPath, "123").Session);
    }

    [Fact]
    public void SymlinkRootAndFixedStageFileRedirectAreRejected()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        string link = Path.Combine(fixture.Root, "redirect");
        try
        {
            Directory.CreateSymbolicLink(link, session.DirectoryPath);
            Assert.Throws<InvalidDataException>(() => NativeProbeDiagnostics.Prepare(link, Guid.NewGuid().ToString("N"), 123));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
        File.Delete(session.StagePath);
        string target = Path.Combine(session.DirectoryPath, "native-probe-errors");
        File.WriteAllText(target, "must not read redirected file");
        File.CreateSymbolicLink(session.StagePath, target);
        Assert.Contains("read-error", session.Snapshot());
    }

    [Fact]
    public void SecondaryErrorFileIsBoundedAndExplicitlyReportsTruncation()
    {
        using var fixture = new DiagnosticFixture();
        var session = NativeProbeDiagnostics.Prepare(fixture.Root, fixture.Token, 123);
        for (int i = 0; i < 8; i++) session.RetainError(new string('e', 256));
        Assert.True(new FileInfo(Path.Combine(session.DirectoryPath, "native-probe-errors")).Length <= 1_024);
        Assert.Contains("secondary-errors", session.Snapshot());
        Assert.Contains("truncated", session.Snapshot());
    }

    internal sealed class DiagnosticFixture : IDisposable
    {
        internal string Token { get; set; } = Guid.NewGuid().ToString("N");
        internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "diagnostic-test-" + Guid.NewGuid().ToString("N"));
        internal DiagnosticFixture() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            string owned = Path.Combine(AppContext.BaseDirectory, "TestResults", "native-probe-" + Token);
            foreach (string name in new[] { "native-probe-stages", "native-probe-errors" })
            {
                string path = Path.Combine(owned, name);
                if (File.Exists(path)) File.Delete(path);
            }
            if (Directory.Exists(owned)) Directory.Delete(owned);
            Directory.Delete(Root);
        }
    }
}
