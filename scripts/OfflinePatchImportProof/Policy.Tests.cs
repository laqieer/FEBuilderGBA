using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class DesktopPolicyTests
{
    public static int RunSnapshotTests(string parent)
    {
        int count = 0, guards = 0;
        void Verify(bool condition)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Installed snapshot case failed: " + count);
        }
        void Refused(Action action, string expected)
        {
            bool refused = false;
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message == expected) { refused = true; }
            Verify(refused);
        }
        string root = Path.Combine(parent, "snapshot-" + Guid.NewGuid().ToString("N"));
        string proof = Path.Combine(root, "proof");
        string descriptor = Path.Combine(proof, "PATCH_offline.txt");
        string payload = Path.Combine(proof, "payload.bin");
        string marker = Path.Combine(root, ".febuilder-patch-import.json");
        const string owner = "FEBuilderGBA.PatchDatabaseImport";
        const string id = "abcdef0123456789abcdef0123456789";
        string valid = owner + "\n" + id + "\nFE8U\n";
        string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        string Capture() => InstalledDatabaseSnapshot.Capture(root, () => guards++, Hash);
        void Marker(string text) => File.WriteAllBytes(marker, Encoding.ASCII.GetBytes(text));
        Directory.CreateDirectory(proof);
        try
        {
            File.WriteAllText(descriptor, "NAME=Owned proof", new UTF8Encoding(false));
            File.WriteAllBytes(payload, new byte[] { 1 });
            Marker(valid);
            string before = Capture();
            Verify(DesktopPolicy.Sha256(before));
            Verify(Capture() == before);
            File.Delete(marker);
            Refused(() => Capture(), "installed-tree-shape");
            foreach (string invalidMarker in new[] { "", "X" + valid.Substring(1),
                valid.Replace("FE8U", "FE7U"), valid.Replace(id, id.ToUpperInvariant()),
                valid.Replace(id, new string('g', 32)), valid.TrimEnd('\n'),
                valid + "x", valid.Replace("\n", "\r\n") })
            {
                Marker(invalidMarker);
                Refused(() => Capture(), "installed-marker");
            }
            byte[] nonAscii = Encoding.ASCII.GetBytes(valid);
            nonAscii[0] = 0x80;
            File.WriteAllBytes(marker, nonAscii);
            Refused(() => Capture(), "installed-marker");
            Marker(valid.Replace(id, new string('1', 32)));
            string changed = Capture();
            Verify(changed != before);
            string[] original = { before, before, before, before };
            string[] altered = { before, before, before, changed };
            Verify(!DesktopPolicy.Preserved(original, altered, "row", "row", true));
            Marker(valid);
            Verify(Capture() == before);
            Verify(DesktopPolicy.Preserved(original, original, "row", "row", true));
            string extra = Path.Combine(root, "extra.txt");
            File.WriteAllText(extra, "unexpected");
            Refused(() => Capture(), "unexpected-installed-file");
            File.Delete(extra);
            string nestedMarker = Path.Combine(proof, ".febuilder-patch-import.json");
            File.Copy(marker, nestedMarker);
            Refused(() => Capture(), "unexpected-installed-file");
            File.Delete(nestedMarker);
            File.Delete(marker);
            Directory.CreateDirectory(marker);
            Refused(() => Capture(), "unexpected-installed-directory");
            Directory.Delete(marker);
            Marker(valid);
            File.Delete(payload);
            Refused(() => Capture(), "installed-tree-shape");
            File.WriteAllBytes(payload, new byte[] { 1 });
            string unexpectedDirectory = Path.Combine(root, "other");
            Directory.CreateDirectory(unexpectedDirectory);
            Refused(() => Capture(), "unexpected-installed-directory");
            Directory.Delete(unexpectedDirectory);
            using (var stream = new FileStream(payload, FileMode.Open, FileAccess.Write))
                stream.SetLength(16777216);
            Verify(DesktopPolicy.Sha256(Capture()));
            using (var stream = new FileStream(payload, FileMode.Open, FileAccess.Write))
                stream.SetLength(16777217);
            Refused(() => Capture(), "hash-size-bound");
            File.WriteAllBytes(payload, new byte[] { 1 });
            for (int i = 0; i < 17; i++) File.WriteAllText(Path.Combine(root, "extra-" + i), "x");
            Refused(() => Capture(), "unexpected-installed-file");
            for (int i = 0; i < 17; i++) File.Delete(Path.Combine(root, "extra-" + i));
            File.Delete(descriptor);
            Refused(() => Capture(), "installed-tree-shape");
            Verify(guards > 0);
            return count;
        }
        finally { Directory.Delete(root, true); }
    }

    static int cases;
    static void Check(bool condition)
    {
        cases++;
        if (!condition) throw new InvalidOperationException("Pure decision case failed: " + cases);
    }

    public static int Run()
    {
        cases = 0;
        foreach (int session in new[] { -1, 0, 2 })
        foreach (int state in new[] { -1, 0, 1, 4, 5, 9, 10 })
        foreach (bool interactive in new[] { false, true })
        foreach (bool input in new[] { false, true })
        foreach (bool foreground in new[] { false, true })
            Check(BoundedWindowsReadiness.Decide(session, state, interactive, input, foreground) ==
                (session > 0 && state == 0 && interactive && input && foreground));

        const string exe = @"C:\owned\app\FEBuilderGBA.Avalonia.exe";
        Check(DesktopPolicy.Process(42, 123, exe, 42, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 41, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 124, exe, false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 123, exe + ".other", false));
        Check(!DesktopPolicy.Process(42, 123, exe, 42, 123, exe, true));
        Check(!DesktopPolicy.Process(0, 123, exe, 0, 123, exe, false));
        Check(!DesktopPolicy.Process(42, 0, exe, 42, 0, exe, false));
        Check(!DesktopPolicy.Process(42, 123, "", 42, 123, "", false));
        Check(DesktopPolicy.AvaloniaClass("Avalonia-1cbfe925-1340-4151-890b-03b818cd779f"));
        foreach (string name in new[] { "", "Avalonia-", "Other-1cbfe925-1340-4151-890b-03b818cd779f", "#32770" })
            Check(!DesktopPolicy.AvaloniaClass(name));

        Check(DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 41, 42, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 41, 100, 100, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 0, 0, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 101, "expected", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "other", "expected", true, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", false, true));
        Check(!DesktopPolicy.Window(42, 42, 42, 100, 100, "expected", "expected", true, false));
        Check(DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 201, 200, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 201, true, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, false, true, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, false, true, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, false, "Edit", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Other", "Button", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Other", 1));
        Check(!DesktopPolicy.Picker(200, 200, 200, true, true, true, "Edit", "Button", 2));
        Check(DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(false, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 10, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 100, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, false, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, false));

        string hash = new string('a', 64);
        string other = new string('b', 64);
        string[] before = { hash, hash, hash, hash };
        Check(DesktopPolicy.Preserved(before, (string[])before.Clone(), "row", "row", true));
        for (int i = 0; i < before.Length; i++)
        {
            var after = (string[])before.Clone();
            after[i] = other;
            Check(!DesktopPolicy.Preserved(before, after, "row", "row", true));
        }
        Check(!DesktopPolicy.Preserved(before, before, "row", "changed", true));
        Check(!DesktopPolicy.Preserved(before, before, "row", "row", false));
        Check(!DesktopPolicy.Preserved(new[] { "" }, new[] { "" }, "row", "row", true));
        Check(!DesktopPolicy.Preserved(before, null, "row", "row", true));
        Check(!DesktopPolicy.Expired(99, 100));
        Check(DesktopPolicy.Expired(100, 100));
        Check(DesktopPolicy.Expired(101, 100));
        Check(DesktopPolicy.Expired(-1, 100));
        Check(DesktopPolicy.Expired(0, 0));
        // Exercise the actual dispatch gate, including a deschedule after the last check.
        var beforeAdmission = new DesktopDispatchGate();
        Check(!beforeAdmission.IsClosed);
        beforeAdmission.Close();
        Check(!beforeAdmission.TryBegin());
        Check(!beforeAdmission.CanReturn(false));
        Check(beforeAdmission.CanReturn(true));

        var admitted = new DesktopDispatchGate();
        Check(admitted.TryBegin());
        Check(!admitted.TryBegin());
        Check(!admitted.IsClosed); // The worker can deschedule here, before its native call.
        admitted.Close();
        Check(!admitted.TryBegin());
        Check(!admitted.CanReturn(false));
        Check(!admitted.CanReturn(true)); // A closed gate does not abort an admitted call.
        admitted.End();
        Check(!admitted.TryBegin());
        Check(!admitted.CanReturn(false)); // Lease release is not a thread-join receipt.
        Check(admitted.CanReturn(true));
        admitted.Close();
        Check(!admitted.TryBegin());

        var completedBeforeCancel = new DesktopDispatchGate();
        Check(completedBeforeCancel.TryBegin());
        completedBeforeCancel.End();
        Check(completedBeforeCancel.TryBegin());
        completedBeforeCancel.End();
        Check(!completedBeforeCancel.CanReturn(true));
        completedBeforeCancel.Close();
        Check(completedBeforeCancel.CanReturn(true));
        Check(!completedBeforeCancel.TryBegin());
        bool unmatchedEndRefused = false;
        try { completedBeforeCancel.End(); }
        catch (InvalidOperationException) { unmatchedEndRefused = true; }
        Check(unmatchedEndRefused);
        for (int mask = 0; mask < 8; mask++)
        {
            bool boundaryExited = (mask & 1) != 0, retainedIdentity = (mask & 2) != 0;
            bool killAttempted = (mask & 4) != 0;
            Check(DesktopPolicy.MayCleanupApp(boundaryExited, retainedIdentity, killAttempted) ==
                (mask == 3));
        }
        for (int mask = 0; mask < 256; mask++)
        {
            bool requested = (mask & 1) != 0, editorOpen = (mask & 2) != 0;
            bool mainGone = (mask & 4) != 0, editorGone = (mask & 8) != 0;
            bool exited = (mask & 16) != 0, zero = (mask & 32) != 0;
            bool killed = (mask & 64) != 0, timedOut = (mask & 128) != 0;
            Check(DesktopPolicy.NormalClose(requested, editorOpen, mainGone, editorGone, exited,
                zero ? 0 : 1, killed, timedOut) == (mask == 63));
        }
        Check(DesktopPolicy.RelativeFile(@"config\patch2\FE8U\proof\PATCH_offline.txt"));
        foreach (string bad in new[] { "", ".", "..", @"..\escape", @"C:\outside", @"\absolute",
            "a/b", @"a\..\b", @"a\.\b", @"a\\b", "a:stream", "a.", "a ", @"a\NUL", "CON.txt",
            "CONIN$", "CONOUT$", "NUL .txt" })
            Check(!DesktopPolicy.RelativeFile(bad));
        return cases;
    }
}
