using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using FEBuilderGBA.E2ETests.Helpers;

namespace FEBuilderGBA.DesktopReadinessProbe;

internal sealed record ProbeFilePin(string Path, long Bytes, string Sha256);
internal sealed record ProbeClosureFile(string Root, string Path, long Bytes, string Sha256);
internal sealed record ProbePacket(string SourceHead, string SourceTree, string SourceGateReference,
    string SourceGateSha256, string AttemptId, ProbeFilePin Host, string ToolDirectory,
    string RuntimeVersion, string HostFxrVersion, string EvidenceDirectory, string SystemRoot,
    Dictionary<string, string[]> VersionDirectories, ProbeClosureFile[] Files);
internal sealed record ProbeGrant(string AttemptId, string PacketSha256, string SourceHead,
    string SourceTree, string OperationalSourceReference, string OperationalSourceSha256,
    string IssuerReference, long IssuedUtcTicks, long ExpiresUtcTicks);
internal sealed record ProbeIdentity(int Pid, long CreationFileTime, string Image,
    bool LiveBefore, bool LiveAfter);
internal sealed record ProbeStreams(byte[] Stdout, byte[] Stderr, bool StdoutEof, bool StderrEof,
    bool Failed = false);

internal sealed class ProbeReport
{
    public string Outcome { get; set; } = "AdmissionFailed";
    public bool Started { get; set; }
    public bool Consumed { get; set; }
    public bool ConsumptionAttempted { get; set; }
    public bool ExitConfirmed { get; set; }
    public bool ReportingFailed { get; set; }
    public int KillAttempts { get; set; }
    public int? ExitCode { get; set; }
    public ProbeIdentity? Identity { get; set; }
    public ProbeStreams Streams { get; set; } = new([], [], false, false);
    public string? State { get; set; }
    public string? Reason { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

internal interface IProbeChild : IDisposable
{
    bool Started { get; }
    void Start();
    void CloseInput();
    void BeginDrains();
    ProbeIdentity Observe();
    bool HasExited { get; }
    int ExitCode { get; }
    ProbeStreams Pump();
    void Kill();
}

internal interface IProbeClock
{
    long Milliseconds { get; }
    void Delay(int milliseconds);
}

internal interface IProbeAdmission
{
    long UtcNowTicks { get; }
    IDisposable VerifyAndHold(ProbePacket packet);
    void Consume(ProbePacket packet, string packetHash, string grantHash);
    void Publish(ProbePacket packet, string packetHash, string grantHash, ProbeReport report);
    void Retain(IProbeChild child, IDisposable pins, ProbeReport report);
}

internal static class ProbeSupervisor
{
    private const int PacketLimit = 2097152, GrantLimit = 16384, FileLimit = 268435456;
    private static readonly string[] RootNames = ["tool", "hostfxr", "core", "desktop"];
    private static readonly string[] ToolFiles = ["DesktopReadinessProbe.dll",
        "DesktopReadinessProbe.deps.json", "DesktopReadinessProbe.runtimeconfig.json",
        "FEBuilderGBA.E2ETests.dll"];

    private static void Require(bool value)
    {
        if (!value) throw new InvalidDataException("Probe supervision admission rejected.");
    }

    private static bool Matches(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string Hex(string value, int length)
    {
        Require(value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        return value;
    }

    private static string Reference(string value)
    {
        Require(Matches(value, @"\Ahttps://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+\z"));
        return value;
    }

    private static void Segment(string value)
    {
        Require(value.Length > 0 && value is not "." and not ".." &&
            !value.EndsWith('.') && !value.EndsWith(' ') &&
            !value.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) &&
            !Matches(value.Split('.')[0], @"\A(?i:CON|PRN|AUX|NUL|(?:COM|LPT)[1-9\u00b9\u00b2\u00b3])\z"));
    }

    private static string Absolute(string value)
    {
        Require(value.Length is >= 3 and <= 1024 &&
            Matches(value, @"\A[A-Za-z]:\\") && Path.IsPathFullyQualified(value) &&
            Path.GetFullPath(value) == value && !value.Contains('/'));
        if (value.Length > 3)
            foreach (string segment in value[3..].Split('\\')) Segment(segment);
        return value;
    }

    private static string Relative(string value)
    {
        Require(value.Length is >= 1 and <= 512 && !Path.IsPathRooted(value));
        foreach (string segment in value.Split('\\')) Segment(segment);
        return value;
    }

    private static string Version(string value)
    {
        Require(value.Length <= 32 &&
            Matches(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"));
        foreach (string component in value.Split('.'))
            Require(int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        return value;
    }

    private static JsonDocument StrictJson(byte[] bytes, int maximum)
    {
        Require(bytes.Length is > 0 && bytes.Length <= maximum);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        Require(text.Length > 0 && text[0] != '\ufeff');
        var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        try
        {
            var pending = new Stack<JsonElement>();
            pending.Push(document.RootElement);
            int nodes = 0;
            while (pending.TryPop(out var item))
            {
                Require(++nodes <= 65536);
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in item.EnumerateObject())
                    {
                        Require(property.Name.Length <= 64 && names.Add(property.Name));
                        pending.Push(property.Value);
                    }
                }
                else if (item.ValueKind == JsonValueKind.Array)
                    foreach (var value in item.EnumerateArray()) pending.Push(value);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static void Keys(JsonElement value, params string[] expected)
    {
        Require(value.ValueKind == JsonValueKind.Object &&
            value.EnumerateObject().Count() == expected.Length);
        foreach (var property in value.EnumerateObject())
            Require(expected.Contains(property.Name, StringComparer.Ordinal));
    }

    private static string Text(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        Require(property.ValueKind == JsonValueKind.String);
        string result = property.GetString()!;
        Require(result.Length is > 0 and <= 1024);
        return result;
    }

    private static ProbeFilePin FilePin(JsonElement value, bool absolute)
    {
        Keys(value, "path", "bytes", "sha256");
        long bytes = value.GetProperty("bytes").GetInt64();
        Require(bytes is >= 0 and <= FileLimit);
        string path = Text(value, "path");
        return new(absolute ? Absolute(path) : Relative(path), bytes, Hex(Text(value, "sha256"), 64));
    }

    internal static ProbePacket ParsePacket(byte[] bytes)
    {
        try
        {
            using var document = StrictJson(bytes, PacketLimit);
            var data = document.RootElement;
            Keys(data, "schema", "sourceHead", "sourceTree", "sourceGateReference", "sourceGateSha256",
                "attemptId", "host", "toolDirectory", "runtimeVersion", "hostFxrVersion",
                "evidenceDirectory", "systemRoot", "versionDirectories", "files");
            Require(Text(data, "schema") == "desktop-probe-packet-v1");
            var versions = data.GetProperty("versionDirectories");
            Keys(versions, "hostfxr", "core", "desktop");
            var directories = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string name in RootNames.Skip(1))
            {
                var rows = versions.GetProperty(name);
                Require(rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() is >= 1 and <= 32);
                string[] names = rows.EnumerateArray().Select(v => Version(v.GetString()!)).ToArray();
                Require(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length);
                directories.Add(name, names);
            }
            var files = data.GetProperty("files");
            Require(files.ValueKind == JsonValueKind.Array && files.GetArrayLength() is >= 1 and <= 4096);
            var result = new List<ProbeClosureFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in files.EnumerateArray())
            {
                Keys(row, "root", "path", "bytes", "sha256");
                string root = Text(row, "root"), path = Relative(Text(row, "path"));
                long length = row.GetProperty("bytes").GetInt64();
                Require(RootNames.Contains(root, StringComparer.Ordinal) &&
                    seen.Add(root + "\\" + path) && length is >= 0 and <= FileLimit);
                result.Add(new(root, path, length, Hex(Text(row, "sha256"), 64)));
            }
            foreach (string root in RootNames) Require(result.Any(row => row.Root == root));
            foreach (string name in ToolFiles)
                Require(result.Any(row => row.Root == "tool" && row.Path == name && row.Bytes > 0));
            var packet = new ProbePacket(Hex(Text(data, "sourceHead"), 40),
                Hex(Text(data, "sourceTree"), 40), Reference(Text(data, "sourceGateReference")),
                Hex(Text(data, "sourceGateSha256"), 64), Hex(Text(data, "attemptId"), 32),
                FilePin(data.GetProperty("host"), true), Absolute(Text(data, "toolDirectory")),
                Version(Text(data, "runtimeVersion")), Version(Text(data, "hostFxrVersion")),
                Absolute(Text(data, "evidenceDirectory")), Absolute(Text(data, "systemRoot")),
                directories, result.ToArray());
            Require(packet.RuntimeVersion.StartsWith("10.0.", StringComparison.Ordinal) &&
                packet.HostFxrVersion.StartsWith("10.0.", StringComparison.Ordinal) &&
                Path.GetFileName(packet.Host.Path) == "dotnet.exe" && packet.Host.Bytes > 0);
            Require(directories["hostfxr"].Contains(packet.HostFxrVersion, StringComparer.Ordinal) &&
                directories["core"].Contains(packet.RuntimeVersion, StringComparer.Ordinal) &&
                directories["desktop"].Contains(packet.RuntimeVersion, StringComparer.Ordinal));
            // Muxer resolver selection is independent of framework roll-forward.
            Require(packet.HostFxrVersion == directories["hostfxr"]
                .MaxBy(static name => System.Version.Parse(name)));
            foreach (string root in Roots(packet).Values.Append(Path.GetDirectoryName(packet.Host.Path)!))
                Require(!Under(packet.EvidenceDirectory, root) && !Under(root, packet.EvidenceDirectory));
            return packet;
        }
        catch (Exception) { throw new InvalidDataException("Invalid probe packet."); }
    }

    internal static ProbeGrant ParseGrant(byte[] bytes, ProbePacket packet, string packetHash,
        long nowTicks)
    {
        try
        {
            using var document = StrictJson(bytes, GrantLimit);
            var data = document.RootElement;
            Keys(data, "schema", "attemptId", "packetSha256", "sourceHead", "sourceTree",
                "operationalSourceReference", "operationalSourceSha256", "issuerReference",
                "issuedUtcTicks", "expiresUtcTicks", "authorized",
                "operationalSourceSecurityAccepted", "retainedUnconfirmedCustodyAccepted");
            Require(Text(data, "schema") == "desktop-probe-grant-v1");
            foreach (string name in new[] { "authorized", "operationalSourceSecurityAccepted",
                "retainedUnconfirmedCustodyAccepted" })
                Require(data.GetProperty(name).ValueKind == JsonValueKind.True);
            var grant = new ProbeGrant(Hex(Text(data, "attemptId"), 32),
                Hex(Text(data, "packetSha256"), 64), Hex(Text(data, "sourceHead"), 40),
                Hex(Text(data, "sourceTree"), 40), Reference(Text(data, "operationalSourceReference")),
                Hex(Text(data, "operationalSourceSha256"), 64), Reference(Text(data, "issuerReference")),
                data.GetProperty("issuedUtcTicks").GetInt64(), data.GetProperty("expiresUtcTicks").GetInt64());
            ValidateGrant(grant, packet, packetHash, nowTicks);
            return grant;
        }
        catch (Exception) { throw new InvalidDataException("Invalid probe grant."); }
    }

    private static void ValidateGrant(ProbeGrant grant, ProbePacket packet, string packetHash, long now)
    {
        Require(grant.AttemptId == packet.AttemptId && grant.PacketSha256 == Hex(packetHash, 64) &&
            grant.SourceHead == packet.SourceHead && grant.SourceTree == packet.SourceTree &&
            grant.IssuedUtcTicks >= 0 && grant.ExpiresUtcTicks <= DateTime.MaxValue.Ticks &&
            grant.ExpiresUtcTicks > grant.IssuedUtcTicks && now >= grant.IssuedUtcTicks &&
            grant.ExpiresUtcTicks - grant.IssuedUtcTicks <= TimeSpan.FromSeconds(600).Ticks &&
            grant.ExpiresUtcTicks - now >= TimeSpan.FromSeconds(16).Ticks);
    }

    private static bool Under(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Roots(ProbePacket packet)
    {
        string host = Path.GetDirectoryName(packet.Host.Path)!;
        return new(StringComparer.Ordinal)
        {
            ["tool"] = packet.ToolDirectory,
            ["hostfxr"] = Path.Combine(host, "host", "fxr", packet.HostFxrVersion),
            ["core"] = Path.Combine(host, "shared", "Microsoft.NETCore.App", packet.RuntimeVersion),
            ["desktop"] = Path.Combine(host, "shared", "Microsoft.WindowsDesktop.App", packet.RuntimeVersion)
        };
    }

    internal static Dictionary<string, string> ChildEnvironment(ProbePacket packet) => new(StringComparer.Ordinal)
    {
        ["SystemRoot"] = packet.SystemRoot,
        ["windir"] = packet.SystemRoot,
        ["DOTNET_ROOT_X86"] = Path.GetDirectoryName(packet.Host.Path)!,
        ["DOTNET_ROLL_FORWARD"] = "Disable",
        ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
        ["DOTNET_EnableDiagnostics"] = "0",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["TEMP"] = packet.EvidenceDirectory,
        ["TMP"] = packet.EvidenceDirectory
    };

    internal static ProcessStartInfo ChildStartInfo(ProbePacket packet)
    {
        var info = new ProcessStartInfo(packet.Host.Path)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = packet.ToolDirectory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(Path.Combine(packet.ToolDirectory, "DesktopReadinessProbe.dll"));
        info.ArgumentList.Add("--probe-own-desktop");
        info.Environment.Clear();
        foreach (var pair in ChildEnvironment(packet)) info.Environment.Add(pair.Key, pair.Value);
        return info;
    }

    internal static ProbeReport Execute(ProbePacket packet, ProbeGrant grant, string packetHash,
        string grantHash, IProbeAdmission admission, Func<IProbeChild> factory, IProbeClock clock)
    {
        var report = new ProbeReport();
        IDisposable? pins = null;
        IProbeChild? child = null;
        bool verified = false;
        try
        {
            Hex(grantHash, 64);
            ValidateGrant(grant, packet, packetHash, admission.UtcNowTicks);
            pins = admission.VerifyAndHold(packet);
            ValidateGrant(grant, packet, packetHash, admission.UtcNowTicks);
            verified = true;
            report.ConsumptionAttempted = true;
            admission.Consume(packet, packetHash, grantHash);
            report.Consumed = true;
            ValidateGrant(grant, packet, packetHash, admission.UtcNowTicks);
            report.Outcome = "StartFailed";
            child = factory();
            Supervise(packet, child, clock, report);
        }
        catch (Exception) { }
        finally
        {
            if (child != null) report.Started = child.Started;
            try
            {
                if (verified) admission.Publish(packet, packetHash, grantHash, report);
            }
            catch (Exception) { report.ReportingFailed = true; }
            finally
            {
                if (child?.Started == true && !report.ExitConfirmed)
                {
                    report.Outcome = "CleanupUnconfirmed";
                    // Transfer strong ownership, including on a reporting failure.
                    admission.Retain(child, pins!, report);
                }
                else
                {
                    try { child?.Dispose(); }
                    finally { pins?.Dispose(); }
                }
            }
        }
        return report;
    }

    private static void Supervise(ProbePacket packet, IProbeChild child, IProbeClock clock, ProbeReport report)
    {
        long start = clock.Milliseconds;
        try
        {
            child.Start();
            report.Started = child.Started;
            Require(report.Started);
            report.Outcome = "ObservationFailed";
            child.CloseInput();
            child.BeginDrains();
            if (clock.Milliseconds - start >= 10000) report.Outcome = "TimedOut";
            else
            {
                report.Identity = child.Observe();
                Require(report.Identity.Pid > 0 && report.Identity.CreationFileTime > 0 &&
                    report.Identity.LiveBefore && report.Identity.LiveAfter &&
                    report.Identity.Image.Equals(packet.Host.Path, StringComparison.OrdinalIgnoreCase));
                report.Outcome = "OutputFailed";
                while (clock.Milliseconds - start < 10000)
                {
                    report.Streams = child.Pump();
                    Require(!report.Streams.Failed && report.Streams.Stdout.Length <= 128 &&
                        report.Streams.Stderr.Length <= 1024);
                    bool exited = child.HasExited;
                    if (clock.Milliseconds - start >= 10000)
                    {
                        report.ExitConfirmed = exited;
                        report.Outcome = "TimedOut";
                        break;
                    }
                    if (exited)
                    {
                        report.ExitConfirmed = true;
                        report.Outcome = "ProtocolFailed";
                        break;
                    }
                    clock.Delay(10);
                }
                if (!report.ExitConfirmed) report.Outcome = "TimedOut";
            }
        }
        catch (Exception) { }
        finally
        {
            report.Started = child.Started;
            if (child.Started && !report.ExitConfirmed)
            {
                try { report.ExitConfirmed = child.HasExited; } catch (Exception) { }
                if (!report.ExitConfirmed)
                {
                    report.KillAttempts = 1;
                    long cleanup = clock.Milliseconds;
                    try { child.Kill(); } catch (Exception) { }
                    while (clock.Milliseconds - cleanup < 5000)
                    {
                        try { report.ExitConfirmed = child.HasExited; } catch (Exception) { }
                        if (report.ExitConfirmed) break;
                        clock.Delay(10);
                    }
                }
                if (!report.ExitConfirmed) report.Outcome = "CleanupUnconfirmed";
            }
            if (report.ExitConfirmed)
            {
                long drain = clock.Milliseconds;
                try
                {
                    report.ExitCode = child.ExitCode;
                    do
                    {
                        report.Streams = child.Pump();
                        if (clock.Milliseconds - drain > 1000)
                        {
                            report.Outcome = "OutputFailed";
                            break;
                        }
                        if (report.Streams.Failed ||
                            report.Streams.Stdout.Length > 128 || report.Streams.Stderr.Length > 1024 ||
                            (report.Streams.StdoutEof && report.Streams.StderrEof)) break;
                        clock.Delay(10);
                    } while (clock.Milliseconds - drain < 1000);
                    if (report.Outcome == "ProtocolFailed") AcceptProtocol(report);
                }
                catch (Exception) { report.Outcome = "OutputFailed"; }
            }
            report.ElapsedMilliseconds = clock.Milliseconds - start;
        }
    }

    private static void AcceptProtocol(ProbeReport report)
    {
        ProbeStreams streams = report.Streams;
        if (streams.Failed || !streams.StdoutEof || !streams.StderrEof ||
            streams.Stderr.Length != 0 || streams.Stdout.Length is < 1 or > 128 ||
            streams.Stdout.Any(b => b > 127)) return;
        string line = Encoding.ASCII.GetString(streams.Stdout);
        foreach (var (state, reason, code) in new[]
        {
            ("Ready", "ActiveInputDesktop", 0), ("Blocked", "SessionZero", 2),
            ("Blocked", "SessionInactive", 2), ("Blocked", "ThreadDesktopNotInput", 2),
            ("Unknown", "SessionQueryFailed", 3), ("Unknown", "SessionStateUnknown", 3),
            ("Unknown", "ThreadDesktopUnknown", 3), ("Unknown", "NativeQueryFailed", 3)
        })
            if (line == $"State={state};Reason={reason}\n" && report.ExitCode == code)
            {
                report.State = state;
                report.Reason = reason;
                report.Outcome = "Observed";
                return;
            }
    }

    internal static int Run(string[] args, TextWriter output)
    {
        try
        {
            Require(args.Length == 5 && args[0] == "--supervise-own-desktop");
            string packetPath = Absolute(args[1]), packetHash = Hex(args[2], 64);
            string grantPath = Absolute(args[3]), grantHash = Hex(args[4], 64);
            ProbePacket packet = ParsePacket(ReadPinnedData(packetPath, packetHash, PacketLimit));
            ProbeGrant grant = ParseGrant(ReadPinnedData(grantPath, grantHash, GrantLimit),
                packet, packetHash, DateTime.UtcNow.Ticks);
            var admission = new RuntimeAdmission(packet, grant, packetHash, output);
            ProbeReport report = Execute(packet, grant, packetHash, grantHash, admission,
                () => new WindowsChild(packet), new Clock());
            if (!admission.Published) WriteSummary(output, report);
            return !report.ReportingFailed && report.Outcome == "Observed" ? report.ExitCode!.Value : 4;
        }
        catch (Exception)
        {
            try { output.Write("Supervisor=AdmissionFailed;State=None;Reason=None\n"); } catch (Exception) { }
            return 4;
        }
    }

    private static void WriteSummary(TextWriter output, ProbeReport report)
    {
        string outcome = report.ReportingFailed ? "ReportingFailed" : report.Outcome;
        bool observed = outcome == "Observed";
        output.Write($"Supervisor={outcome};State={(observed ? report.State : "None")};Reason={(observed ? report.Reason : "None")}\n");
        output.Flush();
    }

    private static void Ancestry(string path)
    {
        Absolute(path);
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0);
    }

    private static string Digest(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    private static byte[] ReadPinnedData(string path, string hash, int maximum)
    {
        Ancestry(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 && stream.Length <= maximum);
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1 &&
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == hash);
        return bytes;
    }

    private sealed class Clock : IProbeClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public long Milliseconds => watch.ElapsedMilliseconds;
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
    }

    private sealed class HeldFiles : IDisposable
    {
        public List<FileStream> Streams { get; } = [];
        public void Dispose()
        {
            foreach (var stream in Streams) stream.Dispose();
        }
    }

    internal sealed class RuntimeAdmission(ProbePacket packet, ProbeGrant grant,
        string packetHash, TextWriter output, Func<long>? utcNowTicks = null,
        Action<string, string>? writeMarker = null) : IProbeAdmission
    {
        public long UtcNowTicks => utcNowTicks?.Invoke() ?? DateTime.UtcNow.Ticks;
        public bool Published { get; private set; }
        private string Marker => Path.Combine(packet.EvidenceDirectory, packet.AttemptId + ".consumed.json");
        private string Receipt => Path.Combine(packet.EvidenceDirectory, packet.AttemptId + ".receipt.json");
        private void Reserve() => ValidateGrant(grant, packet, packetHash, UtcNowTicks);

        public IDisposable VerifyAndHold(ProbePacket value)
        {
            var held = new HeldFiles();
            try
            {
                Require(OperatingSystem.IsWindows() && !Environment.Is64BitProcess &&
                    Environment.Version.ToString() == packet.RuntimeVersion &&
                    Environment.ProcessPath?.Equals(packet.Host.Path, StringComparison.OrdinalIgnoreCase) == true &&
                    Assembly.GetExecutingAssembly().Location.Equals(
                        Path.Combine(packet.ToolDirectory, "DesktopReadinessProbe.dll"), StringComparison.OrdinalIgnoreCase) &&
                    typeof(DesktopReadiness).Assembly.Location.Equals(
                        Path.Combine(packet.ToolDirectory, "FEBuilderGBA.E2ETests.dll"), StringComparison.OrdinalIgnoreCase));
                var roots = Roots(packet);
                Require(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\').Equals(roots["core"],
                    StringComparison.OrdinalIgnoreCase) &&
                    Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.System))!
                        .Equals(packet.SystemRoot, StringComparison.OrdinalIgnoreCase));
                Ancestry(packet.EvidenceDirectory);
                Require(Directory.Exists(packet.EvidenceDirectory) && !Path.Exists(Marker) && !Path.Exists(Receipt));
                Hold(packet.Host.Path, packet.Host.Bytes, packet.Host.Sha256, held);
                foreach (var root in roots)
                {
                    Ancestry(root.Value);
                    if (root.Key != "tool")
                    {
                        string parent = Path.GetDirectoryName(root.Value)!;
                        var versions = new List<string>();
                        foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
                        {
                            Reserve();
                            Ancestry(entry);
                            Require(Directory.Exists(entry) && versions.Count < 32);
                            versions.Add(Path.GetFileName(entry));
                        }
                        Require(versions.Order(StringComparer.Ordinal).SequenceEqual(
                            packet.VersionDirectories[root.Key].Order(StringComparer.Ordinal), StringComparer.Ordinal));
                    }
                    var expected = packet.Files.Where(row => row.Root == root.Key)
                        .ToDictionary(row => row.Path, StringComparer.Ordinal);
                    var directories = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string name in expected.Keys)
                        for (string? parent = Path.GetDirectoryName(name); !string.IsNullOrEmpty(parent);
                            parent = Path.GetDirectoryName(parent)) directories.Add(parent);
                    var pending = new Stack<string>();
                    pending.Push(root.Value);
                    int count = 0, entries = 0;
                    while (pending.TryPop(out string? directory))
                        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            Reserve();
                            Require(++entries <= 8192);
                            Ancestry(path);
                            string relative = Path.GetRelativePath(root.Value, path);
                            if (Directory.Exists(path))
                            {
                                Require(directories.Contains(relative));
                                pending.Push(path);
                            }
                            else
                            {
                                Require(expected.TryGetValue(relative, out var pin));
                                Hold(path, pin!.Bytes, pin.Sha256, held);
                                count++;
                            }
                        }
                    Require(count == expected.Count);
                }
                VerifyRuntimeConfig();
                Reserve();
                return held;
            }
            catch { held.Dispose(); throw; }
        }

        private void Hold(string path, long bytes, string hash, HeldFiles held)
        {
            Reserve();
            Ancestry(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            held.Streams.Add(stream);
            Require(stream.Length == bytes && Digest(stream) == hash);
            Reserve();
        }

        private void VerifyRuntimeConfig()
        {
            var pin = packet.Files.Single(row => row.Root == "tool" &&
                row.Path == "DesktopReadinessProbe.runtimeconfig.json");
            using var document = StrictJson(ReadPinnedData(Path.Combine(packet.ToolDirectory, pin.Path),
                pin.Sha256, GrantLimit), GrantLimit);
            Keys(document.RootElement, "runtimeOptions");
            var options = document.RootElement.GetProperty("runtimeOptions");
            Keys(options, "tfm", "rollForward", "frameworks", "configProperties");
            Require(Text(options, "tfm") == "net10.0" && Text(options, "rollForward") == "Disable");
            var frameworks = options.GetProperty("frameworks");
            Require(frameworks.ValueKind == JsonValueKind.Array && frameworks.GetArrayLength() == 2);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var framework in frameworks.EnumerateArray())
            {
                Keys(framework, "name", "version");
                string name = Text(framework, "name");
                Require(name is "Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App" &&
                    names.Add(name) && Text(framework, "version") == packet.RuntimeVersion);
            }
            var properties = options.GetProperty("configProperties");
            Keys(properties, "System.Reflection.Metadata.MetadataUpdater.IsSupported",
                "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization",
                "CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS");
            foreach (var property in properties.EnumerateObject())
                Require(property.Value.ValueKind == JsonValueKind.False);
        }

        public void Consume(ProbePacket value, string hash, string grantHash)
        {
            Reserve();
            if (writeMarker == null) PersistMarker(hash, grantHash);
            else writeMarker(hash, grantHash);
        }

        private void PersistMarker(string hash, string grantHash)
        {
            Ancestry(packet.EvidenceDirectory);
            Require(!Path.Exists(Receipt));
            WriteNew(Marker, new { schema = "desktop-probe-consumed-v1", packet.AttemptId,
                packetSha256 = hash, grantSha256 = grantHash });
        }

        private static void WriteNew(string path, object value)
        {
            Ancestry(Path.GetDirectoryName(path)!);
            Require(!Path.Exists(path));
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            Require(bytes.Length <= 32768);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(true);
        }

        public void Publish(ProbePacket value, string hash, string grantHash, ProbeReport report)
        {
            try
            {
                WriteNew(Receipt, new { schema = "desktop-probe-receipt-v1", packet.AttemptId,
                    packet.SourceHead, packet.SourceTree, packetSha256 = hash, grantSha256 = grantHash,
                    grant.OperationalSourceReference, grant.OperationalSourceSha256, grant.IssuerReference,
                    observedUtcTicks = UtcNowTicks, report });
            }
            catch { report.ReportingFailed = true; throw; }
            finally
            {
                Published = true;
                try { WriteSummary(output, report); } catch (Exception) { report.ReportingFailed = true; }
            }
        }

        public void Retain(IProbeChild child, IDisposable pins, ProbeReport report)
        {
            // Admission expires, but custody of this original child does not.
            while (true)
            {
                try
                {
                    if (child.HasExited) break;
                }
                catch (Exception) { }
                try { Thread.Sleep(1000); } catch (Exception) { }
            }
            try
            {
                WriteNew(Path.Combine(packet.EvidenceDirectory, packet.AttemptId + ".reaped.json"),
                    new { schema = "desktop-probe-late-reap-v1", packet.AttemptId,
                        packetSha256 = packetHash, exitConfirmed = true, nativeObservationAccepted = false,
                        observedUtcTicks = UtcNowTicks });
            }
            catch (Exception) { }
            finally
            {
                try { child.Dispose(); }
                finally { pins.Dispose(); }
            }
        }
    }

    private sealed class WindowsChild : IProbeChild
    {
        private readonly Process process = new();
        private SafeProcessHandle? handle;
        private Drain? stdout, stderr;
        public bool Started { get; private set; }

        public WindowsChild(ProbePacket packet)
        {
            process.StartInfo = ChildStartInfo(packet);
        }

        public void Start()
        {
            try
            {
                Started = process.Start();
                Require(Started);
                handle = process.SafeHandle;
            }
            catch
            {
                try
                {
                    handle = process.SafeHandle;
                    Started = !handle.IsInvalid && !handle.IsClosed;
                }
                catch (Exception) { }
                throw;
            }
        }

        public void CloseInput() => process.StandardInput.Close();
        public void BeginDrains()
        {
            stdout = new Drain(process.StandardOutput.BaseStream, 128);
            stderr = new Drain(process.StandardError.BaseStream, 1024);
            stdout.Begin();
            stderr.Begin();
        }

        public ProbeIdentity Observe()
        {
            Require(handle != null && !handle.IsInvalid && !handle.IsClosed);
            bool before = !process.HasExited;
            Require(before && Native.GetProcessId(handle!) == process.Id);
            Require(Native.GetProcessTimes(handle!, out var creation, out _, out _, out _));
            uint capacity = 1024;
            var image = new StringBuilder((int)capacity);
            Require(Native.QueryFullProcessImageName(handle!, 0, image, ref capacity));
            bool after = !process.HasExited;
            return new(process.Id, creation.Value, image.ToString(), before, after);
        }

        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public ProbeStreams Pump()
        {
            stdout?.Pump();
            stderr?.Pump();
            return new(stdout?.Bytes() ?? [], stderr?.Bytes() ?? [],
                stdout?.Eof == true, stderr?.Eof == true, stdout?.Failed == true || stderr?.Failed == true);
        }
        public void Kill() => process.Kill();
        public void Dispose()
        {
            try { stdout?.Dispose(); }
            finally
            {
                try { stderr?.Dispose(); }
                finally
                {
                    try
                    {
                        if (Started)
                        {
                            process.StandardInput.Dispose();
                            process.StandardOutput.Dispose();
                            process.StandardError.Dispose();
                        }
                    }
                    finally { process.Dispose(); }
                }
            }
        }
    }

    internal sealed class Drain(Stream pipe, int maximum) : IDisposable
    {
        private readonly byte[] buffer = new byte[maximum + 1];
        private Task<int>? pending;
        private int count;
        public bool Eof { get; private set; }
        public bool Failed { get; private set; }
        public byte[] Bytes() => buffer.AsSpan(0, count).ToArray();
        public void Begin() => pending = pipe.ReadAsync(buffer, 0, buffer.Length);
        public void Pump()
        {
            if (pending?.IsCompleted != true || Eof || Failed) return;
            try
            {
                int read = pending.GetAwaiter().GetResult();
                pending = null;
                if (read == 0) Eof = true;
                else
                {
                    count += read;
                    if (count > maximum) Failed = true;
                    else pending = pipe.ReadAsync(buffer, count, buffer.Length - count);
                }
            }
            catch (Exception) { Failed = true; }
        }
        public void Dispose()
        {
            try { pipe.Dispose(); }
            finally
            {
                if (pending != null)
                    _ = pending.ContinueWith(task => { _ = task.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            public uint Low, High;
            public readonly long Value => ((long)High << 32) | Low;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint GetProcessId(SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime creation,
            out FileTime exit, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
            StringBuilder image, ref uint size);
    }
}
