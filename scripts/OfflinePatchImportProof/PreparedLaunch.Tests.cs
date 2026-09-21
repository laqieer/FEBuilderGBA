using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

#if PREPARED_SHARED_START_TEST
// This compilation is confined to the fresh shared-start test host, never the runtime helper.
public sealed class DesktopResult
{
    public bool Passed { get; set; }
    public bool WorkerJoined { get; set; }
    public bool DispatchAdmissionClosed { get; set; }
    public bool SimulationOnly { get; set; } = true;
    public string CancellationRequestedUtc { get; set; }
    public string Failure { get; set; }
}

public static class BoundedProcessImage
{
    public static int Reads { get; private set; }
    public static object Read(Microsoft.Win32.SafeHandles.SafeProcessHandle handle)
    {
        if (handle == null || handle.IsClosed || handle.IsInvalid) throw new Exception("Missing retained test handle.");
        Reads++;
        return new object();
    }
}

public static class BoundedDesktopSmoke
{
    public static int Calls { get; private set; }
    public static DesktopResult Run(Process process, string executable, string root,
        string descriptor, string payload, Action<DesktopResult> requestStop)
    {
        Calls++;
        if (process == null || process.HasExited || process.StartInfo.FileName != executable ||
            descriptor != new string('d', 64) || payload != new string('e', 64) ||
            !File.Exists(Path.Combine(root, "app-identity.json")) ||
            File.Exists(Path.Combine(root, "app-identity.pending")))
            throw new Exception("Shared workflow operands/identity publication.");
        requestStop(new DesktopResult { Failure = "timeout:shared-start-test", WorkerJoined = false,
            DispatchAdmissionClosed = true, CancellationRequestedUtc = DateTime.UtcNow.ToString("o") });
        if (!File.Exists(Path.Combine(root, "worker-stop.json")) ||
            File.Exists(Path.Combine(root, "worker-stop.pending")))
            throw new Exception("Shared worker-stop callback publication.");
        if (!process.WaitForExit(5000) || process.ExitCode != 0)
            throw new Exception("Owned inert process did not exit.");
        return new DesktopResult { Passed = true, WorkerJoined = true, DispatchAdmissionClosed = true };
    }
}
#endif

public static class PreparedLaunchTests
{
    public static int RunManifest()
    {
        int count = 0;
        string hash = new string('1', 64);
        var original = new Hashtable { ["path"] = @"app\data.bin", ["bytes"] = 1, ["sha256"] = hash };
        var bundle = new Hashtable { ["files"] = new[] { original } };
        foreach (string kind in new[] { "valid", "missing-empty", "hash", "foreign-path", "boolean-size",
            "duplicate", "extra-key", "missing-payload", "private-path" })
        {
            var row = (Hashtable)original.Clone();
            var empty = new Hashtable { ["path"] = "empty-git.config", ["bytes"] = 0,
                ["sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" };
            object[] files = { row, empty };
            switch (kind)
            {
                case "missing-empty": files = new object[] { row }; break;
                case "hash": row["sha256"] = new string('2', 64); break;
                case "foreign-path": row["path"] = "other.bin"; break;
                case "boolean-size": row["bytes"] = true; break;
                case "duplicate": files = new object[] { row, row, empty }; break;
                case "extra-key": row["other"] = "value"; break;
                case "missing-payload": files = new object[] { empty }; break;
                case "private-path": row["path"] = @"app\config\log\log.txt"; break;
            }
            bool passed = false;
            try { PreparedContentLease.ValidateManifest(new Hashtable { ["files"] = files, ["directories"] = Array.Empty<string>() }, bundle); passed = true; }
            catch (InvalidDataException) { }
            if (passed != (kind == "valid")) throw new Exception("prepared-manifest-" + kind);
            count++;
        }
        return count;
    }
    public static int RunJson()
    {
        int cases = 0;
        void Check(bool condition) { if (!condition) throw new Exception("prepared-json-case-" + cases); cases++; }
        var data = PreparedJson.Parse(System.Text.Encoding.UTF8.GetBytes("{\"files\":[],\"n\":17,\"flag\":false,\"empty\":null}"));
        Check(data["n"] is long && (long)data["n"] == 17);
        Check(data["flag"] is bool && !(bool)data["flag"] && data["empty"] == null);
        foreach (byte[] invalid in new[] {
            Array.Empty<byte>(), new byte[] { 255 }, System.Text.Encoding.UTF8.GetBytes("\ufeff{}"),
            System.Text.Encoding.UTF8.GetBytes("{\"a\":1,\"A\":2}"), System.Text.Encoding.UTF8.GetBytes("[]"),
            System.Text.Encoding.UTF8.GetBytes("{\"n\":1.2}"), System.Text.Encoding.UTF8.GetBytes("{\"n\":1e2}"),
            System.Text.Encoding.UTF8.GetBytes("{\"n\":9223372036854775808}"),
            System.Text.Encoding.UTF8.GetBytes(new string('[', 34) + "0" + new string(']', 34)),
            new byte[4194305] })
        {
            bool refused = false;
            try { PreparedJson.Parse(invalid); } catch { refused = true; }
            Check(refused);
        }
        return cases;
    }
    public static int Run(string root)
    {
        int cases = 0;
        void Check(bool condition) { if (!condition) throw new Exception("prepared-case-" + cases); cases++; }
        void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected);
        }
        string workspace = Path.Combine(root, "prepared-model");
        Directory.CreateDirectory(workspace);
        string payload = Path.Combine(workspace, "owned.bin");
        File.WriteAllBytes(payload, new byte[] { 1, 2, 3 });
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payload))).ToLowerInvariant();
        var inventory = new Hashtable {
            ["files"] = new[] { new Hashtable { ["path"] = "owned.bin", ["bytes"] = 3, ["sha256"] = digest } },
            ["directories"] = Array.Empty<string>()
        };
        using (var lease = PreparedContentLease.Open(workspace, inventory, Stopwatch.GetTimestamp()))
        {
            Check(lease.Files == 1 && lease.Bytes == 3);
            if (OperatingSystem.IsWindows()) Reject(() => File.WriteAllText(payload, "changed"));
        }
        string commit = Path.Combine(root, "prepared-commit.json");
        Check(!PreparedAttempt.Transition(() => false, commit, "first", Stopwatch.GetTimestamp()));
        Check(!File.Exists(commit));
        using (var lease = PreparedContentLease.Open(workspace, inventory, Stopwatch.GetTimestamp()))
            Check(lease.Files == 1);
        Check(PreparedAttempt.Transition(() => true, commit, "second", Stopwatch.GetTimestamp()));
        Check(File.ReadAllText(commit) == "second");
        Reject(() => PreparedAttempt.Transition(() => true, commit, "duplicate", Stopwatch.GetTimestamp()));
        Reject(() => PreparedAttempt.Transition(() => true, commit + ".late", "late",
            Stopwatch.GetTimestamp() - Stopwatch.Frequency * 6));
        Check(!File.Exists(commit + ".late"));
        PreparedContentLease.ContentDeadline(Stopwatch.GetTimestamp() - Stopwatch.Frequency * 14);
        Check(true);
        Reject(() => PreparedContentLease.ContentDeadline(
            Stopwatch.GetTimestamp() - Stopwatch.Frequency * 15));
        Reject(() => PreparedContentLease.ContentDeadline(0));
        Reject(() => PreparedContentLease.ContentDeadline(
            Stopwatch.GetTimestamp() + Stopwatch.Frequency));
        foreach (string privatePath in new[] { @"config\log\log.txt", @"config\logs\other.txt",
            "generated-core-suite-log-preserved.txt", "private.gba", @"a\game.ROM", @"..\escape" })
            Check(!PreparedContentLease.PublicResource(privatePath));
        Check(PreparedContentLease.PublicResource(@"fixtures\zipdb-proof.gba", true));
        Check(!PreparedContentLease.PublicResource(@"fixtures\other.gba", true));
        File.WriteAllText(Path.Combine(workspace, "extra.txt"), "extra");
        Reject(() => PreparedContentLease.Open(workspace, inventory, Stopwatch.GetTimestamp()).Dispose());
        File.Delete(Path.Combine(workspace, "extra.txt"));
        File.WriteAllBytes(payload, new byte[] { 1, 2, 4 });
        Reject(() => PreparedContentLease.Open(workspace, inventory, Stopwatch.GetTimestamp()).Dispose());
        File.Delete(payload);
        Reject(() => PreparedContentLease.Open(workspace, inventory, Stopwatch.GetTimestamp()).Dispose());
        Directory.Delete(workspace);
        File.Delete(commit);
        return cases;
    }
}
