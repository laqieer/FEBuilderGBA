using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class DesktopPolicyTests
{
    public static int RunStartupTests()
    {
        const string firstClass = "Avalonia-11111111-1111-1111-1111-111111111111";
        const string otherClass = "Avalonia-22222222-2222-2222-2222-222222222222";
        int count = 0;
        DesktopStartupRoot Root(long handle = 200, bool main = true, bool loading = false,
            bool wizard = false, long owner = 0, string windowClass = firstClass, bool visible = true)
            => new DesktopStartupRoot(handle, owner, windowClass, visible, main, loading, wizard);
        DesktopStartupRoot Loading() => Root(100, main: false, loading: true);
        DesktopStartupRoot Wizard() => Root(300, main: false, wizard: true, owner: 200);
        DesktopStartupRoot Unknown() => Root(100, main: false);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected startup decision.");
        }
        void Refused(Action action, string reason)
        {
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message == reason) { return; }
            throw new InvalidOperationException("Expected startup refusal: " + reason);
        }
        void Case(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { throw new InvalidOperationException("Startup case failed: " + name, ex); }
            count++;
        }
        DesktopStartupObservation Observed()
        {
            var state = new DesktopStartupObservation();
            Verify(!state.Observe(5, new[] { Loading() }, true).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 5 && state.LoadingHandle == 100 &&
                state.WindowClass == firstClass);
            return state;
        }
        Case("main-first-honest-fast-route", () =>
        {
            var state = new DesktopStartupObservation();
            var decision = state.Observe(10, new[] { Root() }, false);
            Verify(decision.Ready && decision.Route == DesktopStartupObservation.UnobservedRoute &&
                decision.MainHandle == 200 && decision.WizardHandle == 0 && decision.WindowClass == firstClass &&
                !state.LoadingObserved && state.LoadingHandle == 0 && state.LoadingAt == -1);
            Verify(state.Revalidate(10, new[] { Root() }, false).Ready);
        });
        Case("original-handoff-still-requires-observation", () =>
            Verify(!DesktopPolicy.Handoff(false, -1, 10, 0, 200, true, true)));
        Case("observed-destroyed-loading-route", () =>
        {
            var state = Observed();
            var decision = state.Observe(10, new[] { Root() }, false);
            Verify(decision.Ready && decision.Route == DesktopStartupObservation.ObservedRoute);
            Verify(state.Revalidate(11, new[] { Root() }, false).Route == decision.Route);
        });
        Case("empty-sample-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(0, Array.Empty<DesktopStartupRoot>(), false).Ready));
        Case("loading-without-main-waits", () =>
            Verify(!Observed().Observe(10, new[] { Loading() }, true).Ready));
        Case("unrecognized-root-without-main-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Unknown() }, false).Ready));
        Case("wizard-without-main-waits", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Wizard() }, false).Ready));
        Case("offscreen-main-control-is-not-main", () =>
            Verify(!new DesktopStartupObservation().Observe(1, new[] { Root(main: false) }, false).Ready));
        Case("invisible-main-projection-refused", () =>
            Refused(() => new DesktopStartupObservation().Observe(1, new[] { Root(visible: false) }, false),
                "startup-root-projection"));
        foreach (bool mainFirst in new[] { false, true })
        {
            Case("loading-and-main-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                var sample = mainFirst ? new[] { Root(), Loading() } : new[] { Loading(), Root() };
                Verify(!state.Observe(5, sample, false).Ready && state.LoadingObserved);
                Verify(state.Observe(10, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
            });
            Case("unlabeled-leftover-order-" + mainFirst, () =>
            {
                var sample = mainFirst ? new[] { Root(), Unknown() } : new[] { Unknown(), Root() };
                var state = new DesktopStartupObservation();
                Verify(!state.Observe(5, sample, false).Ready && !state.LoadingObserved);
            });
            Case("exact-main-owned-wizard-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                var sample = mainFirst ? new[] { Root(), Wizard() } : new[] { Wizard(), Root() };
                var decision = state.Observe(5, sample, false);
                Verify(decision.Ready && decision.WizardHandle == 300 &&
                    decision.Route == DesktopStartupObservation.UnobservedRoute);
                Verify(state.Revalidate(5, sample, false).Ready);
            });
        }
        Case("loading-label-loss-is-sticky", () =>
        {
            var state = Observed();
            Verify(!state.Observe(10, new[] { Root(), Unknown() }, true).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 5);
            Verify(!state.Observe(15, new[] { Root() }, true).Ready);
            Verify(state.Observe(20, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
        });
        Case("unlabeled-retained-root-blocks-even-gone-flag", () =>
            Verify(!Observed().Observe(10, new[] { Root(), Unknown() }, false).Ready));
        Case("loading-still-exists-outside-visible-sample", () =>
            Verify(!Observed().Observe(10, new[] { Root() }, true).Ready));
        Case("named-loading-blocks-even-gone-flag", () =>
            Verify(!Observed().Observe(10, new[] { Root(), Loading() }, false).Ready));
        Case("unobserved-loading-exists-flag-is-invalid", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root() }, true),
                "startup-loading-projection"));
        Case("unknown-root-disappearance-is-not-loading-proof", () =>
        {
            var state = new DesktopStartupObservation();
            Verify(!state.Observe(5, new[] { Unknown(), Root() }, false).Ready);
            Verify(state.Observe(10, new[] { Root() }, false).Route == DesktopStartupObservation.UnobservedRoute &&
                !state.LoadingObserved);
        });
        Case("observed-main-with-owned-wizard", () =>
            Verify(Observed().Observe(10, new[] { Root(), Wizard() }, false).Route ==
                DesktopStartupObservation.ObservedRoute));
        Case("wizard-plus-unknown-still-blocks", () =>
            Verify(!new DesktopStartupObservation().Observe(10, new[] { Root(), Wizard(), Unknown() }, false).Ready));
        foreach (long owner in new long[] { 0, 999, 300 })
            Case("wizard-wrong-immediate-owner-" + owner, () =>
                Refused(() => new DesktopStartupObservation().Observe(10,
                    new[] { Root(), Root(300, main: false, wizard: true, owner: owner) }, false),
                    "startup-setup-wizard"));
        Case("wizard-wrong-class", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false),
                "startup-setup-wizard"));
        Case("wizard-missing-visible-control-is-unknown", () =>
            Verify(!new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, owner: 200) }, false).Ready));
        Case("duplicate-wizards", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Wizard(), Root(301, main: false, wizard: true, owner: 200) }, false),
                "startup-duplicate-wizard"));
        Case("wizard-reuses-main-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(main: false, wizard: true, owner: 200) }, false), "startup-duplicate-root"));
        Case("native-picker-not-startup-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, owner: 200, windowClass: "#32770") }, false),
                "startup-root-class"));
        Case("duplicate-root-projections", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(), Root() }, false),
                "startup-duplicate-root"));
        Case("duplicate-main-candidates", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(), Root(201) }, false),
                "startup-duplicate-main"));
        Case("duplicate-loading-candidates", () =>
            Refused(() => new DesktopStartupObservation().Observe(10,
                new[] { Loading(), Root(101, main: false, loading: true) }, true), "startup-duplicate-loading"));
        Case("ambiguous-main-loading-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(loading: true) }, false),
                "startup-root-role"));
        Case("ambiguous-main-wizard-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(wizard: true) }, false),
                "startup-root-role"));
        Case("zero-native-root", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new[] { Root(0) }, false),
                "startup-root-projection"));
        Case("null-root-projection", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, new DesktopStartupRoot[] { null }, false),
                "startup-root-projection"));
        Case("null-sample", () =>
            Refused(() => new DesktopStartupObservation().Observe(10, null, false), "startup-root-bound"));
        Case("eight-roots-with-unknowns-waits", () =>
        {
            var roots = new DesktopStartupRoot[8];
            for (int i = 0; i < roots.Length; i++) roots[i] = Root(200 + i, main: i == 0);
            Verify(!new DesktopStartupObservation().Observe(10, roots, false).Ready);
        });
        Case("nine-roots-refused", () =>
        {
            var roots = new DesktopStartupRoot[9];
            for (int i = 0; i < roots.Length; i++) roots[i] = Root(200 + i, main: i == 0);
            Refused(() => new DesktopStartupObservation().Observe(10, roots, false), "startup-root-bound");
        });
        foreach (string windowClass in new[] { null, "", "Avalonia-invalid", "other" })
            Case("invalid-native-class-" + (windowClass ?? "null"), () =>
                Refused(() => new DesktopStartupObservation().Observe(10,
                    new[] { Root(windowClass: windowClass) }, false), "startup-root-class"));
        Case("observed-class-remains-pinned", () =>
            Refused(() => Observed().Observe(10, new[] { Root(windowClass: otherClass) }, false),
                "startup-class-changed"));
        Case("observed-wizard-class-remains-pinned", () =>
            Refused(() => Observed().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false),
                "startup-class-changed"));
        Case("observed-loading-handle-cannot-change", () =>
            Refused(() => Observed().Observe(10, new[] { Root(101, main: false, loading: true), Root() }, false),
                "startup-loading-changed"));
        Case("main-cannot-reuse-observed-loading-handle", () =>
            Refused(() => Observed().Observe(10, new[] { Root(100) }, false), "startup-handoff-refused"));
        Case("negative-initial-observation", () =>
            Refused(() => new DesktopStartupObservation().Observe(-1, new[] { Root() }, false),
                "startup-observation-order"));
        foreach (long at in new long[] { 4, 5 })
        {
            Case("observed-unchanged-or-backward-" + at, () =>
                Refused(() => Observed().Observe(at, new[] { Root() }, false), "startup-observation-order"));
            Case("empty-unchanged-or-backward-" + at, () =>
            {
                var state = new DesktopStartupObservation();
                state.Observe(5, Array.Empty<DesktopStartupRoot>(), false);
                Refused(() => state.Observe(at, new[] { Root() }, false), "startup-observation-order");
            });
        }
        Case("revalidation-requires-candidate", () =>
            Refused(() => new DesktopStartupObservation().Revalidate(10, new[] { Root() }, false),
                "startup-acceptance-candidate"));
        Case("observed-waiting-cannot-bypass-candidate", () =>
            Refused(() => Observed().Revalidate(10, new[] { Root() }, false), "startup-acceptance-candidate"));
        Case("revalidation-clock-cannot-go-backward", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(9, new[] { Root() }, false), "startup-observation-order");
        });
        Case("acceptance-main-root-must-remain-identical", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(201) }, false), "startup-acceptance-changed");
        });
        Case("acceptance-class-must-remain-identical", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(windowClass: otherClass) }, false),
                "startup-class-changed");
        });
        Case("acceptance-missing-main-blocks", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, Array.Empty<DesktopStartupRoot>(), false).Ready);
        });
        Case("acceptance-offscreen-main-control-blocks", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, new[] { Root(main: false) }, false).Ready);
        });
        Case("acceptance-hidden-native-main-refused", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(visible: false) }, false),
                "startup-root-projection");
        });
        foreach (bool mainFirst in new[] { false, true })
            Case("acceptance-unlabeled-leftover-order-" + mainFirst, () =>
            {
                var state = new DesktopStartupObservation();
                state.Observe(10, new[] { Root() }, false);
                var roots = mainFirst ? new[] { Root(), Unknown() } : new[] { Unknown(), Root() };
                Verify(!state.Revalidate(11, roots, false).Ready && !state.LoadingObserved);
            });
        Case("late-loading-makes-observation-sticky", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(10, new[] { Root(), Loading() }, false).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 10);
            Verify(!state.Observe(15, new[] { Root(), Unknown() }, true).Ready);
            Verify(state.Observe(20, new[] { Root() }, false).Route == DesktopStartupObservation.ObservedRoute);
        });
        Case("acceptance-loading-still-exists-blocks", () =>
        {
            var state = Observed();
            state.Observe(10, new[] { Root() }, false);
            Verify(!state.Revalidate(11, new[] { Root() }, true).Ready);
        });
        Case("acceptance-duplicate-main-refused", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Refused(() => state.Revalidate(11, new[] { Root(), Root(201) }, false), "startup-duplicate-main");
        });
        Case("acceptance-wizard-owner-rechecked", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            Refused(() => state.Revalidate(11,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 999) }, false),
                "startup-setup-wizard");
        });
        Case("acceptance-wizard-control-rechecked", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            Verify(!state.Revalidate(11, new[] { Root(), Root(300, main: false, owner: 200) }, false).Ready);
        });
        Case("acceptance-wizard-may-have-closed", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root(), Wizard() }, false);
            var accepted = state.Revalidate(11, new[] { Root() }, false);
            Verify(accepted.Ready && accepted.WizardHandle == 0);
        });
        Case("acceptance-exact-wizard-may-have-appeared", () =>
        {
            var state = new DesktopStartupObservation();
            state.Observe(10, new[] { Root() }, false);
            Verify(state.Revalidate(11, new[] { Root(), Wizard() }, false).WizardHandle == 300);
        });
        foreach (string rejection in new[] { "chronology", "loading-handle", "class", "reused-main" })
            Case("invalid-observed-handoff-never-falls-back-" + rejection, () =>
            {
                var state = Observed();
                string reason;
                Action reject;
                switch (rejection)
                {
                    case "chronology":
                        reason = "startup-observation-order";
                        reject = () => state.Observe(5, new[] { Root() }, false);
                        break;
                    case "loading-handle":
                        reason = "startup-loading-changed";
                        reject = () => state.Observe(10, new[] { Root(101, main: false, loading: true) }, false);
                        break;
                    case "class":
                        reason = "startup-class-changed";
                        reject = () => state.Observe(10, new[] { Root(windowClass: otherClass) }, false);
                        break;
                    default:
                        reason = "startup-handoff-refused";
                        reject = () => state.Observe(10, new[] { Root(100) }, false);
                        break;
                }
                Refused(reject, reason);
                Refused(() => state.Observe(20, new[] { Root() }, false), reason);
                Verify(state.LoadingObserved);
            });
        return count;
    }

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
