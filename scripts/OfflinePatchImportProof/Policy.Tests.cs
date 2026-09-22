using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class RetainedProcessImageTests
{
    const string Expected = @"C:\expected\tool.exe";
    sealed class NativeModel
    {
        internal string Path = Expected;
        internal bool Fail, Throw, BadTerminator;
        internal uint? Length;
        internal int Error, Calls, Capacity;
        internal uint Flags;
        internal IntPtr Handle;
        internal bool Query(IntPtr handle, uint flags, char[] buffer, ref uint characters, out int error)
        {
            Calls++; Handle = handle; Flags = flags; Capacity = buffer.Length;
            if (Throw) throw new InvalidOperationException("Private exception must not be disclosed.");
            Path.CopyTo(0, buffer, 0, Math.Min(Path.Length, buffer.Length));
            characters = Length ?? (uint)Path.Length;
            if (BadTerminator && characters < buffer.Length) buffer[characters] = 'x';
            error = Error;
            return !Fail;
        }
    }
    static void Check(bool value) { if (!value) throw new Exception("Retained-image model assertion failed."); }
    static SafeProcessHandle Fake() => new SafeProcessHandle(
        IntPtr.Size == 8 ? new IntPtr(0x12345678000000abL) : new IntPtr(0x123400ab), false);
    static ProcessImageRead ReadInjected(SafeProcessHandle handle, NativeModel model)
    {
        var method = typeof(BoundedProcessImage).GetMethod(nameof(BoundedProcessImage.Read),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Internal image-reader seam missing.");
        var query = Delegate.CreateDelegate(method.GetParameters()[1].ParameterType, model, nameof(NativeModel.Query));
        return (ProcessImageRead)method.Invoke(null, new object[] { handle, query });
    }
    static ProcessImageRead Read(NativeModel model)
    {
        using (var handle = Fake()) return ReadInjected(handle, model);
    }
    static Dictionary<string, object> Describe(ProcessImageRead read, string expected = Expected,
        StringComparison comparison = StringComparison.Ordinal) =>
        BoundedProcessImage.Describe(read, expected, comparison, "prepare-initial");

    public static int Run()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Action<string, Action> test = (name, body) =>
        {
            Check(names.Add(name));
            try { body(); } catch (Exception ex) { throw new Exception("Retained image case: " + name, ex); }
        };
        test("matching-executable", () => Check((string)Describe(Read(new NativeModel()))["code"] == "image-observed"));
        test("different-executable", () => Check((string)Describe(Read(new NativeModel { Path = @"C:\other\tool.exe" }))["code"] == "image-mismatch"));
        test("ordinal-case-refusal", () => Check((string)Describe(Read(new NativeModel { Path = Expected.ToUpperInvariant() }))["code"] == "image-mismatch"));
        test("fixed-windows-case", () => Check((string)Describe(Read(new NativeModel { Path = Expected.ToUpperInvariant() }), Expected, StringComparison.OrdinalIgnoreCase)["code"] == "image-observed"));
        test("flags-zero", () => { var m = new NativeModel(); Read(m); Check(m.Flags == 0 && m.Calls == 1); });
        test("fixed-buffer", () => { var m = new NativeModel(); Read(m); Check(m.Capacity == 32768 && m.Calls == 1); });
        test("full-width-borrowed-handle", () =>
        {
            var m = new NativeModel(); using (var h = Fake())
            { ReadInjected(h, m); Check(m.Handle == h.DangerousGetHandle() && m.Calls == 1); }
        });
        test("success-does-not-dispose-owner", () =>
        {
            using (var h = Fake()) { ReadInjected(h, new NativeModel()); Check(!h.IsClosed); }
        });
        test("failure-does-not-dispose-owner", () =>
        {
            using (var h = Fake()) { ReadInjected(h, new NativeModel { Fail = true }); Check(!h.IsClosed); }
        });
        test("zero-handle-no-query", () =>
        {
            var m = new NativeModel(); using (var h = new SafeProcessHandle(IntPtr.Zero, false))
            { Check(ReadInjected(h, m).Code == "invalid-handle" && m.Calls == 0); }
        });
        test("minus-one-no-query", () =>
        {
            var m = new NativeModel(); using (var h = new SafeProcessHandle(new IntPtr(-1), false))
            { Check(ReadInjected(h, m).Code == "invalid-handle" && m.Calls == 0); }
        });
        test("closed-handle-no-query", () =>
        {
            var m = new NativeModel(); var h = Fake(); h.Dispose();
            Check(ReadInjected(h, m).Code == "invalid-handle" && m.Calls == 0);
        });
        test("null-handle-no-query", () =>
        {
            var m = new NativeModel(); Check(ReadInjected(null, m).Code == "invalid-handle" && m.Calls == 0);
        });
        foreach (int error in new[] { 5, 6, 31, 122, 0 })
        {
            int value = error;
            test("native-error-" + value, () =>
            {
                var m = new NativeModel { Fail = true, Error = value }; var r = Read(m);
                Check(r.Code == "native-error" && r.NativeError == value && r.Path == null && m.Calls == 1);
            });
        }
        test("zero-length", () => Check(Read(new NativeModel { Length = 0 }).Code == "invalid-image"));
        test("capacity-length", () => Check(Read(new NativeModel { Length = 32768 }).Code == "invalid-image"));
        test("out-of-bounds-length", () => Check(Read(new NativeModel { Length = uint.MaxValue }).Code == "invalid-image"));
        test("missing-terminator", () => Check(Read(new NativeModel { BadTerminator = true }).Code == "invalid-image"));
        test("embedded-nul", () => Check(Read(new NativeModel { Path = "a\0b" }).Code == "invalid-image"));
        test("unpaired-high-surrogate", () => Check(Read(new NativeModel { Path = "a\ud800" }).Code == "invalid-image"));
        test("unpaired-low-surrogate", () => Check(Read(new NativeModel { Path = "a\udc00" }).Code == "invalid-image"));
        test("maximum-valid-image", () =>
        {
            var path = @"C:\" + new string('a', 32764); var r = Read(new NativeModel { Path = path });
            Check(r.Path == path && r.Characters == 32767 && (string)Describe(r, path)["code"] == "image-observed");
        });
        test("failed-buffer-not-disclosed", () =>
        {
            var d = Describe(Read(new NativeModel { Fail = true, Path = "private-buffer", Error = 5 }));
            Check(d["observedPathPreview"] == null && d["observedPathSha256"] == null && d["returnedChars"] == null);
        });
        test("bounded-preview-and-digest", () =>
        {
            var path = @"C:\" + new string('a', 252) + "\ud83d\ude00" + new string('b', 20);
            var d = Describe(Read(new NativeModel { Path = path }), path);
            Check(((string)d["observedPathPreview"]).Length == 255 && (bool)d["previewTruncated"] &&
                (string)d["observedPathSha256"] == (string)d["expectedPathSha256"]);
        });
        test("full-comparison-beyond-preview", () =>
        {
            var prefix = @"C:\" + new string('a', 300);
            var d = Describe(Read(new NativeModel { Path = prefix + "x" }), prefix + "y");
            Check((string)d["code"] == "image-mismatch" && (string)d["observedPathSha256"] != (string)d["expectedPathSha256"]);
        });
        test("exception-releases-borrow", () =>
        {
            var m = new NativeModel { Throw = true }; var h = Fake(); var r = ReadInjected(h, m);
            Check(r.Code == "query-exception" && m.Calls == 1 && r.Path == null && !h.IsClosed);
            h.Dispose(); Check(h.IsClosed);
        });
        test("no-prefix-normalization", () =>
            Check((string)Describe(Read(new NativeModel { Path = @"\\?\" + Expected }))["code"] == "image-mismatch"));
        test("independent-initial-cleanup-snapshots", () =>
        {
            var first = Describe(Read(new NativeModel { Fail = true, Error = 5 }));
            var later = Describe(Read(new NativeModel())); later["code"] = "changed";
            Check((string)first["code"] == "native-error" && (int)first["nativeError"] == 5);
        });
        Check(names.Count == 32);
        return names.Count;
    }
}

public static class DesktopPolicyTests
{
    sealed class ElementNotAvailableException : Exception { }
    sealed class OwnedNode
    {
        internal int Id, Pid = 7;
        internal long Handle;
        internal int[] RuntimeId = null;
        internal OwnedNode Parent;
        internal bool Offscreen = false, NullIdentity = false;
        internal string AutomationId = "", Name = "";
        internal DesktopQueryControl Control;
        internal readonly List<OwnedNode> Children = new List<OwnedNode>();
    }

    sealed class OwnedNative
    {
        internal int Pid = 7;
        internal uint Root, Owner;
        internal bool Alive = true, Visible = true;
        internal string Class = "Avalonia-11111111-1111-1111-1111-111111111111";
    }

    sealed class OwnedModel : IDesktopOwnedTreeAdapter<OwnedNode>
    {
        public DesktopTreeBudget Budget { private get; set; }
        internal readonly Dictionary<uint, OwnedNative> Native = new Dictionary<uint, OwnedNative>();
        internal readonly Dictionary<uint, OwnedNode> Roots = new Dictionary<uint, OwnedNode>();
        internal readonly List<OwnedNode> SeedNodes = new List<OwnedNode>();
        internal readonly List<string> Reads = new List<string>();
        internal readonly List<string> NativeReads = new List<string>();
        internal readonly DesktopRootBindings Bindings = new DesktopRootBindings();
        internal Action<string, OwnedNode> Before = null;
        internal Action GuardAction = () => { };
        internal Func<IReadOnlyList<OwnedNode>, IReadOnlyList<OwnedNode>> SeedResults = null;
        internal Func<OwnedNode, DesktopSelector, IReadOnlyList<OwnedNode>, IReadOnlyList<OwnedNode>> CandidateResults = null;
        internal int SeedQueries, CandidateQueries, ProviderNodes, ProviderProperties;
        internal OwnedNode Main, MainButton, Wizard, Close;
        internal OwnedModel()
        {
            Main = Add(null, 1, 100);
            MainButton = Add(Main, 2, 0, DesktopSelector.Main);
            Wizard = Add(Main, 3, 200);
            Close = Add(Wizard, 4, 0, DesktopSelector.Wizard);
            Native.Add(100, new OwnedNative { Root = 100 });
            Native.Add(200, new OwnedNative { Root = 200, Owner = 100,
                Class = "Avalonia-22222222-2222-2222-2222-222222222222" });
            Roots.Add(100, Main); Roots.Add(200, Wizard);
            SeedNodes.Add(Main);
        }
        internal OwnedNode Add(OwnedNode parent, int id, long handle = 0,
            DesktopSelector selector = DesktopSelector.Discovery)
        {
            var node = new OwnedNode { Id = id, Handle = handle, Parent = parent,
                Control = handle == 0 ? DesktopQueryControl.Unknown : DesktopQueryControl.Window,
                Name = handle == 0 ? "" : "FEBuilderGBA" };
            switch (selector)
            {
                case DesktopSelector.Main: node.AutomationId = DesktopCandidateQuery.MainButton; node.Control = DesktopQueryControl.Button; break;
                case DesktopSelector.Wizard: node.AutomationId = DesktopCandidateQuery.WizardButton; node.Control = DesktopQueryControl.Button; break;
                case DesktopSelector.Import: node.AutomationId = DesktopCandidateQuery.ImportButton; node.Control = DesktopQueryControl.Button; break;
                case DesktopSelector.Status: node.AutomationId = DesktopCandidateQuery.StatusLabel; node.Control = DesktopQueryControl.Text; break;
                case DesktopSelector.List: node.AutomationId = DesktopCandidateQuery.PatchList; node.Control = DesktopQueryControl.List; break;
                case DesktopSelector.Loading: node.Control = DesktopQueryControl.Text; node.Name = DesktopCandidateQuery.LoadingName; break;
                case DesktopSelector.FilenameHost: node.AutomationId = "1148"; node.Control = DesktopQueryControl.Edit; break;
                case DesktopSelector.FilenameEdit: node.Control = DesktopQueryControl.Edit; break;
                case DesktopSelector.PickerOpen: node.AutomationId = "1"; node.Control = DesktopQueryControl.Button; break;
                case DesktopSelector.Row: node.Control = DesktopQueryControl.ListItem; break;
                case DesktopSelector.RowName: node.Control = DesktopQueryControl.Text; node.Name = DesktopCandidateQuery.ExpectedRow; break;
                case DesktopSelector.ConfirmationYes: node.AutomationId = "MessageBoxContent_Yes_Button"; node.Control = DesktopQueryControl.Button; break;
                case DesktopSelector.ConfirmationMessage: node.AutomationId = "MessageBoxContent_Message_Label"; node.Control = DesktopQueryControl.Text; break;
            }
            parent?.Children.Add(node);
            return node;
        }
        TValue Observe<TValue>(string name, OwnedNode node, Func<TValue> value)
        {
            Reads.Add(name + ":" + (node?.Id ?? 0));
            Before?.Invoke(name, node);
            return value();
        }
        TValue Read<TValue>(string name, OwnedNode node, Func<TValue> value) => Budget.Call(() => Observe(name, node, value));
        internal DesktopOwnedTree<OwnedNode> Tree() =>
            new DesktopOwnedTree<OwnedNode>(this, 7, Bindings, "loading-handoff", () => GuardAction());
        internal void CloseBudget() => Budget.Close();
        public IReadOnlyList<OwnedNode> Seeds()
        {
            var seeds = Read("seeds", null, () =>
            {
                SeedQueries++;
                IReadOnlyList<OwnedNode> found = SeedNodes.FindAll(node => node.Pid == 7);
                return SeedResults == null ? found : SeedResults(found);
            });
            return DesktopCandidateQuery.Collect(Budget,
                () => Observe("seed-count", null, () => seeds.Count),
                index => Observe("seed-item", seeds[index], () => seeds[index]), 8);
        }
        public int ProcessId(OwnedNode node) => Read("pid", node, () => node.Pid);
        public int[] Identity(OwnedNode node) => Read("identity", node, () =>
            node.NullIdentity ? null : node.RuntimeId ?? new[] { node.Id });
        public uint Handle(OwnedNode node) => Read("handle", node, () => DesktopHwnd.Key(node.Handle));
        public OwnedNode Parent(OwnedNode node) => Read("parent", node, () => node.Parent);
        static object Property(OwnedNode node, DesktopQueryProperty property)
        {
            switch (property)
            {
                case DesktopQueryProperty.ProcessId: return node.Pid;
                case DesktopQueryProperty.NativeWindowHandle: return unchecked((int)DesktopHwnd.Key(node.Handle));
                case DesktopQueryProperty.ControlType: return node.Control;
                case DesktopQueryProperty.AutomationId: return node.AutomationId;
                case DesktopQueryProperty.Name: return node.Name;
                default: throw new InvalidOperationException("Unknown modeled property.");
            }
        }
        public IReadOnlyList<OwnedNode> Candidates(OwnedNode subtree, DesktopSelector selector)
        {
            var predicate = DesktopCandidateQuery.Compile(new DesktopPredicateCompiler<OwnedNode>((node, property) =>
            {
                ProviderProperties++;
                return Property(node, property);
            }), 7, selector);
            var candidates = Read("candidates-" + selector, subtree, () =>
            {
                CandidateQueries++;
                var matches = new List<OwnedNode>();
                var pending = new Stack<OwnedNode>();
                var visited = new HashSet<OwnedNode> { subtree };
                for (int i = subtree.Children.Count - 1; i >= 0; i--) pending.Push(subtree.Children[i]);
                while (pending.Count != 0)
                {
                    var node = pending.Pop();
                    if (!visited.Add(node)) continue;
                    ProviderNodes++;
                    if (predicate(node)) matches.Add(node);
                    for (int i = node.Children.Count - 1; i >= 0; i--) pending.Push(node.Children[i]);
                }
                return CandidateResults == null ? matches : CandidateResults(subtree, selector, matches);
            });
            if (candidates == null) return null;
            return DesktopCandidateQuery.Collect(Budget,
                () => Observe("candidate-count", subtree, () => candidates.Count),
                index => Observe("candidate-item", candidates[index], () => candidates[index]));
        }
        public OwnedNode FromHandle(uint handle) => Read("from-handle", null, () => Roots.TryGetValue(handle, out var node) ? node : null);
        public bool Alive(uint handle) => Read("alive", null, () =>
        {
            NativeReads.Add("alive:" + handle);
            return Native.TryGetValue(handle, out var native) && native.Alive;
        });
        public int NativePid(uint handle) => Read("native-pid", null, () =>
        {
            NativeReads.Add("native-pid:" + handle);
            return Native.TryGetValue(handle, out var native) ? native.Pid : 0;
        });
        public uint NativeRoot(uint handle) => Read("native-root", null, () =>
        {
            NativeReads.Add("native-root:" + handle);
            return Native.TryGetValue(handle, out var native) ? native.Root : 0;
        });
        public uint Owner(uint handle) => Read("owner", null, () => Native[handle].Owner);
        public string Class(uint handle) => Read("class", null, () => Native[handle].Class);
        public bool Visible(uint handle) => Read("visible", null, () => Native[handle].Visible);
        public bool Offscreen(OwnedNode node) => Read("offscreen", node, () => node.Offscreen);
        public bool Matches(OwnedNode node, DesktopSelector selector) => Observe("match-" + selector, node, () =>
            DesktopCandidateQuery.Compile(new DesktopPredicateCompiler<OwnedNode>((candidate, property) =>
                Read("property-" + property, candidate, () => Property(candidate, property))), 7, selector)(node));
    }

    static readonly List<string> ownedTreeCaseNames = new List<string>();
    static readonly List<string> ownedTreeCaseFailures = new List<string>();
    static readonly List<Dictionary<string, long>> ownedTreeScaleProfiles = new List<Dictionary<string, long>>();
    public static string[] OwnedTreeCaseNames => ownedTreeCaseNames.ToArray();
    public static string[] OwnedTreeCaseFailures => ownedTreeCaseFailures.ToArray();
    public static Dictionary<string, long>[] OwnedTreeScaleProfiles => ownedTreeScaleProfiles.ToArray();
    static readonly List<string> startupCaseNames = new List<string>();
    static readonly List<string> startupCaseFailures = new List<string>();
    public static string[] StartupCaseNames => startupCaseNames.ToArray();
    public static string[] StartupCaseFailures => startupCaseFailures.ToArray();
    static readonly List<string> windowIdentityCaseNames = new List<string>();
    static readonly List<string> windowIdentityCaseFailures = new List<string>();
    static readonly List<DesktopStartupFailure> startupDiagnosticSamples = new List<DesktopStartupFailure>();
    public static string[] WindowIdentityCaseNames => windowIdentityCaseNames.ToArray();
    public static string[] WindowIdentityCaseFailures => windowIdentityCaseFailures.ToArray();
    public static DesktopStartupFailure[] StartupDiagnosticSamples => startupDiagnosticSamples.ToArray();
    const uint QueryCandidateKey = 0xe13579bd, QueryOtherKey = 0xe2468ace;
    const long QueryWideHandle = 0x12345678e13579bd;
    const int QueryForeignPid = 1987654321, QueryRuntimeId = 1976543210;
    const string QueryPrivateText = "QUERY-PRIVATE-OBSERVATION-f915c642";
    static readonly List<string> queryDiagnosticCaseNames = new List<string>();
    static readonly List<string> queryDiagnosticCaseFailures = new List<string>();
    static readonly List<string> queryDiagnosticSampleNames = new List<string>();
    static readonly List<DesktopQueryFailure> queryDiagnosticSamples = new List<DesktopQueryFailure>();
    static readonly string[] queryDiagnosticSampleInventory =
    {
        "collection-nine-seeds", "seed-dead-zero", "seed-dead-owned", "seed-dead-foreign",
        "seed-alive-zero", "seed-alive-foreign", "seed-different-key", "seed-highbits-sign-extended",
        "seed-highbits-zero-extended", "seed-second-zero", "raw-parent-dead-foreign",
        "direct-seed-dead", "resolve-success-before-registration-failure",
        "seed-private-provider-failure", "cancel-after-handle", "cancel-after-native-pid",
        "owner-diagnostics-nondefault"
    };
    public static string[] QueryDiagnosticCaseNames => queryDiagnosticCaseNames.ToArray();
    public static string[] QueryDiagnosticCaseFailures => queryDiagnosticCaseFailures.ToArray();
    public static string[] QueryDiagnosticSampleNames => queryDiagnosticSampleNames.ToArray();
    public static DesktopQueryFailure[] QueryDiagnosticSamples => queryDiagnosticSamples.ToArray();
    public static string[] QueryDiagnosticPrivateSentinels => new[]
    {
        QueryCandidateKey.ToString(), QueryOtherKey.ToString(), QueryWideHandle.ToString(),
        unchecked((int)QueryCandidateKey).ToString(), QueryForeignPid.ToString(), QueryRuntimeId.ToString(),
        "e13579bd", "e2468ace", QueryPrivateText
    };

    public static void AssertQueryDiagnosticSampleInventory()
    {
        if (queryDiagnosticSamples.Count != 17 ||
            string.Join("\n", queryDiagnosticSampleNames) != string.Join("\n", queryDiagnosticSampleInventory))
            throw new InvalidOperationException("Query diagnostic sample inventory changed.");
    }

    public static void AssertQueryDiagnosticCaseInventory()
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", queryDiagnosticCaseNames)))).ToLowerInvariant();
        if (queryDiagnosticCaseNames.Count != 40 || digest != "46962af70ac337dd2ea717112ff7bf25cc117691afdd75dc4613e16bea721de5")
            throw new InvalidOperationException("Query diagnostic case names/order changed.");
        if (queryDiagnosticCaseFailures.Count != 0)
            throw new InvalidOperationException("Query diagnostic cases failed: " + string.Join("; ", queryDiagnosticCaseFailures));
    }

    public static int RunQueryDiagnosticTests()
    {
        queryDiagnosticCaseNames.Clear(); queryDiagnosticCaseFailures.Clear();
        queryDiagnosticSampleNames.Clear(); queryDiagnosticSamples.Clear();
        string currentCase = null;
        const string collection = "seeds:0|seed-count:0|seed-item:1";
        const string outer = collection + "|pid:1|handle:1";
        const string resolve = outer + "|pid:1|identity:1|handle:1";
        const string probe = resolve + "|alive:0|native-pid:0";
        const string resolved = probe + "|native-root:0|native-pid:0|alive:0";
        const string refresh = "alive:0|native-pid:0|native-root:0|native-pid:0";
        string Registration(int id) => "alive:0|native-pid:0|native-root:0|class:0|owner:0|from-handle:0|" +
            "pid:" + id + "|handle:" + id + "|identity:" + id + "|visible:0|offscreen:" + id +
            "|alive:0|native-pid:0|native-root:0|class:0|owner:0";
        string NativeProbe(uint handle) => "alive:" + handle + "|native-pid:" + handle;
        string NativeResolved(uint handle) => NativeProbe(handle) + "|native-root:" + handle +
            "|native-pid:" + handle + "|alive:" + handle;
        string NativeRegistration(uint handle) => NativeProbe(handle) + "|native-root:" + handle +
            "|" + NativeProbe(handle) + "|native-root:" + handle;
        string NativeRefresh(uint handle) => NativeProbe(handle) + "|native-root:" + handle + "|native-pid:" + handle;
        void Verify(bool condition, string contract)
        {
            if (!condition) throw new InvalidOperationException(contract);
        }
        void Case(string name, Action action)
        {
            if (queryDiagnosticCaseNames.Contains(name)) throw new InvalidOperationException("Duplicate query diagnostic case.");
            currentCase = name; queryDiagnosticCaseNames.Add(name);
            try { action(); }
            catch (Exception ex) { queryDiagnosticCaseFailures.Add(name + ": " + ex.Message); }
        }
        OwnedModel Model(long handle = QueryCandidateKey)
        {
            var model = new OwnedModel();
            model.Main.Children.Clear(); model.Main.Handle = handle;
            model.Main.Name = QueryPrivateText;
            model.Main.AutomationId = QueryPrivateText + @"\private-path";
            model.Main.RuntimeId = new[] { QueryRuntimeId };
            model.Native.Clear(); model.Roots.Clear();
            uint key = DesktopHwnd.Key(handle);
            model.Native.Add(key, new OwnedNative { Root = key });
            model.Roots.Add(key, model.Main);
            return model;
        }
        DesktopQueryFailure Capture(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == predicate) { return ex.Failure; }
            throw new InvalidOperationException("Expected original refusal: " + predicate);
        }
        DesktopQueryFailure Refused(OwnedModel model, DesktopOwnedTree<OwnedNode> tree, Action action,
            string predicate, string trace, string nativeTrace, int nodes = 1, int windows = 0, int seedQueries = 1,
            int candidateQueries = 0)
        {
            var failure = Capture(action, predicate);
            if (Array.IndexOf(queryDiagnosticSampleInventory, currentCase) >= 0)
            {
                Verify(queryDiagnosticSamples.Count < 17, "Query diagnostic sample bound.");
                queryDiagnosticSampleNames.Add(currentCase); queryDiagnosticSamples.Add(failure);
            }
            Verify(failure.Stage == "loading-handoff" && failure.Selector == "Discovery", "Legacy query context.");
            Verify(string.Join("|", model.Reads) == trace, "Exact adapter call order.");
            Verify(string.Join("|", model.NativeReads) == nativeTrace, "Exact native targets; no unverified refresh.");
            int calls = trace.Length == 0 ? 0 : trace.Split('|').Length;
            Verify(failure.Calls == calls && tree.Budget.Calls == calls && failure.Nodes == nodes &&
                tree.Budget.Nodes == nodes && failure.Windows == windows && tree.Windows.Count == windows,
                "Exact legacy counters (model seed collection is one call shorter than production).");
            Verify(model.SeedQueries == seedQueries && model.CandidateQueries == candidateQueries &&
                model.ProviderNodes == 0 && model.ProviderProperties == 0, "No provider expansion.");
            Verify(tree.Budget.Closed, "Failure must close the budget.");
            Verify(ReferenceEquals(Capture(tree.Discover, predicate), failure) &&
                ReferenceEquals(Capture(() => tree.Seed(model.Main), predicate), failure), "Original failure must latch.");
            bool closed = false;
            try { tree.Budget.Call<int>(() => throw new InvalidOperationException("Closed adapter was called.")); }
            catch (DesktopTreeGuardException ex) when (ex.Message == "query-budget-closed") { closed = true; }
            Verify(closed && tree.Budget.Calls == calls && model.Reads.Count == calls &&
                string.Join("|", model.NativeReads) == nativeTrace, "No calls after closure.");
            var rejectedClass = typeof(DesktopQueryFailure).GetProperty("RejectedRootClass");
            Verify(rejectedClass != null && (string)rejectedClass.GetValue(failure) ==
                (currentCase == "resolve-success-before-registration-failure" ? "OwnedAuxiliaryClass" : null),
                "Exact rejected class or explicit null in original query cases.");
            return failure;
        }
        void Observed(DesktopQueryFailure failure, int? ordinal, bool? equal, bool? alive, string relation)
        {
            Verify(failure.SeedOrdinal == ordinal, "SeedOrdinal missing or stale.");
            Verify(failure.SeedResolveKeyEqual == equal, "SeedResolveKeyEqual missing or stale.");
            Verify(failure.ResolveAlive == alive, "ResolveAlive missing or stale.");
            Verify(failure.ResolvePidRelation == relation, "ResolvePidRelation missing or stale.");
        }
        void Unowned(DesktopQueryFailure failure)
        {
            Verify(failure.ExpectedOwnedRoot == 0 && failure.PreviouslyOwnedHandle == 0 &&
                failure.OwnedRootBefore == 0 && failure.OwnedRootAfter == 0 &&
                failure.AliveBefore == null && failure.AliveAfter == null &&
                failure.OwnPidBefore == null && failure.OwnPidAfter == null &&
                failure.RootMatchesBefore == null && failure.RootMatchesAfter == null, "Unverified candidate became legacy owned context.");
        }
        Case("nullable-dto-shape", () =>
        {
            var properties = typeof(DesktopQueryFailure).GetProperties();
            Verify(properties.Length == 33 &&
                typeof(DesktopQueryFailure).GetProperty("SeedOrdinal").PropertyType == typeof(int?) &&
                typeof(DesktopQueryFailure).GetProperty("SeedResolveKeyEqual").PropertyType == typeof(bool?) &&
                typeof(DesktopQueryFailure).GetProperty("ResolveAlive").PropertyType == typeof(bool?) &&
                typeof(DesktopQueryFailure).GetProperty("ResolvePidRelation").PropertyType == typeof(string) &&
                typeof(DesktopQueryFailure).GetProperty("OwnerDepth").PropertyType == typeof(int?) &&
                typeof(DesktopQueryFailure).GetProperty("OwnerIsSelfRoot").PropertyType == typeof(bool?) &&
                typeof(DesktopQueryFailure).GetProperty("OwnerChainCount").PropertyType == typeof(int?) &&
                typeof(DesktopQueryFailure).GetProperty("OwnerChainTransient").PropertyType == typeof(bool?), "Nullable DTO seam.");
            Observed(new DesktopQueryFailure(), null, null, null, null);
        });
        Case("collection-nine-seeds", () =>
        {
            var model = Model(); model.SeedResults = found => new OwnedNode[9]; var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-seed-bound", "seeds:0|seed-count:0", "", 0);
            Unowned(failure); Observed(failure, null, null, null, null);
        });
        Case("collection-post-read-cancellation", () =>
        {
            var model = Model(); bool cancel = false;
            model.Before = (read, node) => { if (read == "seed-item") cancel = true; };
            model.GuardAction = () => { if (cancel) throw new DesktopTreeGuardException("query-test-cancelled"); };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-test-cancelled", collection, "", 0);
            Unowned(failure); Observed(failure, null, null, null, null);
        });
        Case("seed-null-before-pid", () =>
        {
            var model = Model(); model.SeedResults = found => new OwnedNode[] { null }; var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-seed-pid", "seeds:0|seed-count:0|seed-item:0", "", 0);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("seed-pid-before-handle", () =>
        {
            var model = Model(); model.Before = (read, node) => { if (read == "pid") node.Pid = QueryForeignPid; };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-seed-pid", collection + "|pid:1", "", 0);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("seed-zero-outer-key", () =>
        {
            var model = Model(); model.Main.Handle = 0; var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-seed-root", outer, "", 0);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("seed-outer-handle-post-read-cancellation", () =>
        {
            var model = Model(); bool cancel = false;
            model.Before = (read, node) => { if (read == "handle") cancel = true; };
            model.GuardAction = () => { if (cancel) throw new DesktopTreeGuardException("query-test-cancelled"); };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-test-cancelled", outer, "", 0);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        foreach (int nativePid in new[] { 0, 7, QueryForeignPid })
        {
            string relation = nativePid == 0 ? "zero" : nativePid == 7 ? "owned" : "foreign";
            Case("seed-dead-" + relation, () =>
            {
                var model = Model();
                model.Before = (read, node) =>
                {
                    if (read == "alive") { model.Native[QueryCandidateKey].Alive = false; model.Native[QueryCandidateKey].Pid = nativePid; }
                };
                var tree = model.Tree();
                var failure = Refused(model, tree, tree.Discover, "query-native-gone", probe, NativeProbe(QueryCandidateKey));
                Unowned(failure); Observed(failure, 1, true, false, relation);
            });
        }
        foreach (int nativePid in new[] { 0, QueryForeignPid })
        {
            string relation = nativePid == 0 ? "zero" : "foreign";
            Case("seed-alive-" + relation, () =>
            {
                var model = Model(); model.Before = (read, node) =>
                { if (read == "native-pid") model.Native[QueryCandidateKey].Pid = nativePid; };
                var tree = model.Tree();
                var failure = Refused(model, tree, tree.Discover, "query-native-pid", probe, NativeProbe(QueryCandidateKey));
                Unowned(failure); Observed(failure, 1, true, true, relation);
            });
        }
        Case("seed-different-key", () =>
        {
            var model = Model(); int handles = 0;
            model.Native.Add(QueryOtherKey, new OwnedNative { Root = QueryOtherKey, Alive = false, Pid = QueryForeignPid });
            model.Before = (read, node) => { if (read == "handle" && ++handles == 2) node.Handle = QueryOtherKey; };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-native-gone", probe, NativeProbe(QueryOtherKey));
            Unowned(failure); Observed(failure, 1, false, false, "foreign");
        });
        foreach (bool signed in new[] { true, false })
            Case("seed-highbits-" + (signed ? "sign-extended" : "zero-extended"), () =>
            {
                var model = Model(QueryWideHandle); int handles = 0;
                model.Native[QueryCandidateKey].Alive = false;
                model.Before = (read, node) =>
                { if (read == "handle" && ++handles == 2) node.Handle = signed ? unchecked((int)QueryCandidateKey) : (long)QueryCandidateKey; };
                var tree = model.Tree();
                var failure = Refused(model, tree, tree.Discover, "query-native-gone", probe, NativeProbe(QueryCandidateKey));
                Unowned(failure); Observed(failure, 1, true, false, "owned");
            });
        Case("seed-second-zero", () =>
        {
            var model = Model(); int handles = 0;
            model.Before = (read, node) => { if (read == "handle" && ++handles == 2) node.Handle = 0; };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-raw-parent-missing", resolve + "|parent:1", "");
            Unowned(failure); Observed(failure, 1, false, null, null);
        });
        foreach (string fault in new[] { "dead-foreign", "foreign-uia", "identity", "zero-wrapper" })
            Case("raw-parent-" + fault, () =>
            {
                var model = Model(); int handles = 0;
                var parent = model.Add(null, 9, QueryOtherKey); model.Main.Parent = parent;
                model.Native.Add(QueryOtherKey, new OwnedNative { Root = QueryOtherKey, Alive = false, Pid = QueryForeignPid });
                if (fault == "foreign-uia") parent.Pid = QueryForeignPid;
                if (fault == "identity") parent.NullIdentity = true;
                if (fault == "zero-wrapper") { parent.Handle = 0; parent.Parent = model.Add(null, 10, QueryOtherKey); }
                model.Before = (read, node) => { if (read == "handle" && node == model.Main && ++handles == 2) node.Handle = 0; };
                string suffix = "|parent:1|pid:9";
                if (fault != "foreign-uia") suffix += "|identity:9";
                if (fault == "dead-foreign" || fault == "zero-wrapper") suffix += "|handle:9";
                if (fault == "zero-wrapper") suffix += "|parent:9|pid:10|identity:10|handle:10";
                bool probed = fault == "dead-foreign" || fault == "zero-wrapper";
                if (probed) suffix += "|alive:0|native-pid:0";
                string predicate = probed ? "query-native-gone" : fault == "foreign-uia" ? "query-uia-pid" : "query-runtime-id";
                var tree = model.Tree();
                var failure = Refused(model, tree, tree.Discover, predicate, resolve + suffix,
                    probed ? NativeProbe(QueryOtherKey) : "", fault == "zero-wrapper" ? 3 : 2);
                Unowned(failure); Observed(failure, 1, null, probed ? (bool?)false : null, probed ? "foreign" : null);
            });
        Case("raw-parent-cycle", () =>
        {
            var model = Model(); int handles = 0; model.Main.Parent = model.Main;
            model.Before = (read, node) => { if (read == "handle" && ++handles == 2) node.Handle = 0; };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-parent-cycle",
                resolve + "|parent:1|pid:1|identity:1", "", 2);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("raw-parent-post-handle-cancellation", () =>
        {
            var model = Model(); int handles = 0; bool cancel = false;
            var parent = model.Add(null, 9, QueryOtherKey); model.Main.Parent = parent;
            model.Before = (read, node) =>
            {
                if (read != "handle") return;
                if (node == model.Main && ++handles == 2) node.Handle = 0;
                if (node == parent) cancel = true;
            };
            model.GuardAction = () => { if (cancel) throw new DesktopTreeGuardException("query-test-cancelled"); };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-test-cancelled",
                resolve + "|parent:1|pid:9|identity:9|handle:9", "", 2);
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("direct-seed-dead", () =>
        {
            var model = Model(); model.Native[QueryCandidateKey].Alive = false; var tree = model.Tree();
            var failure = Refused(model, tree, () => tree.Seed(model.Main), "query-native-gone",
                "pid:1|handle:1|pid:1|identity:1|handle:1|alive:0|native-pid:0", NativeProbe(QueryCandidateKey), seedQueries: 0);
            Unowned(failure); Observed(failure, null, true, false, "owned");
        });
        Case("second-seed-preserves-first-owned-context", () =>
        {
            var model = Model(100); var next = model.Add(null, 9, QueryCandidateKey); model.SeedNodes.Add(next);
            model.Native.Add(QueryCandidateKey, new OwnedNative { Root = QueryCandidateKey, Alive = false, Pid = QueryForeignPid });
            var tree = model.Tree();
            string trace = resolved.Replace("seed-item:1", "seed-item:1|seed-item:9") + "|" + Registration(1) +
                "|pid:9|handle:9|pid:9|identity:9|handle:9|alive:0|native-pid:0|" + refresh;
            string native = NativeResolved(100) + "|" + NativeRegistration(100) + "|" +
                NativeProbe(QueryCandidateKey) + "|" + NativeRefresh(100);
            var failure = Refused(model, tree, tree.Discover, "query-native-gone", trace, native, 2, 1);
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 100 && failure.OwnedRootAfter == 100 &&
                failure.ExpectedOwnedRoot == 0 && failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                failure.RootMatchesBefore == false && failure.RootMatchesAfter == false, "Legacy owned refresh changed.");
            Observed(failure, 2, true, false, "foreign");
        });
        Case("second-seed-zero-before-resolve", () =>
        {
            var model = Model(100); var next = model.Add(null, 9); model.SeedNodes.Add(next);
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-seed-root",
                resolved.Replace("seed-item:1", "seed-item:1|seed-item:9") + "|" + Registration(1) +
                "|pid:9|handle:9|" + refresh,
                NativeResolved(100) + "|" + NativeRegistration(100) + "|" + NativeRefresh(100), 1, 1);
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 100 &&
                failure.OwnedRootAfter == 100 && failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                failure.RootMatchesBefore == false && failure.RootMatchesAfter == false, "Outer seed refusal legacy refresh.");
            Observed(failure, 2, null, null, null);
        });
        Case("eighth-seed-bounded-ordinal", () =>
        {
            var model = Model(100); model.SeedNodes.Clear();
            var trace = new List<string> { "seeds:0", "seed-count:0" };
            var native = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                uint key = i == 7 ? QueryCandidateKey : (uint)(100 + i);
                var node = model.Add(null, 10 + i, key); model.SeedNodes.Add(node);
                model.Roots[key] = node;
                model.Native[key] = new OwnedNative { Root = key, Alive = i != 7 };
                trace.Add("seed-item:" + node.Id);
            }
            for (int i = 0; i < 8; i++)
            {
                int id = 10 + i; uint key = i == 7 ? QueryCandidateKey : (uint)(100 + i);
                trace.Add("pid:" + id + "|handle:" + id + "|pid:" + id + "|identity:" + id + "|handle:" + id + "|alive:0|native-pid:0");
                native.Add(NativeProbe(key));
                if (i == 7) break;
                trace.Add("native-root:0|native-pid:0|alive:0|" + Registration(id));
                native.Add("native-root:" + key + "|native-pid:" + key + "|alive:" + key + "|" + NativeRegistration(key));
            }
            trace.Add(refresh); native.Add(NativeRefresh(106));
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-native-gone",
                string.Join("|", trace), string.Join("|", native), 8, 7);
            Verify(failure.PreviouslyOwnedHandle == 106 && failure.OwnedRootAfter == 106, "Eighth seed cannot replace legacy owned context.");
            Observed(failure, 8, true, false, "owned");
        });
        Case("resolve-success-before-registration-failure", () =>
        {
            var model = Model(100); model.Native[100].Class = "OwnedAuxiliaryClass"; var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-root-class",
                resolved + "|alive:0|native-pid:0|native-root:0|class:0|" + refresh,
                NativeResolved(100) + "|" + NativeProbe(100) + "|native-root:100|" + NativeRefresh(100));
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 100 &&
                failure.OwnedRootAfter == 100 && failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                failure.RootMatchesBefore == false && failure.RootMatchesAfter == false, "Registration legacy refresh changed.");
            Observed(failure, null, null, null, null);
        });
        Case("owned-probe-root-pid-refusal", () =>
        {
            var model = Model(100); model.Native[100].Root = 0; var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-native-root-pid",
                probe + "|native-root:0|alive:0|native-pid:0|native-root:0",
                NativeProbe(100) + "|native-root:100|" + NativeProbe(100) + "|native-root:100");
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 0 && failure.OwnedRootAfter == 0 &&
                failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                failure.RootMatchesBefore == null && failure.RootMatchesAfter == false, "Owned probe refusal legacy context.");
            Observed(failure, 1, true, true, "owned");
        });
        Case("owned-probe-root-gone-refusal", () =>
        {
            var model = Model(100); int aliveCalls = 0;
            model.Before = (read, node) => { if (read == "alive" && ++aliveCalls == 2) model.Native[100].Alive = false; };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-native-root-gone",
                resolved + "|alive:0|native-pid:0", NativeResolved(100) + "|" + NativeProbe(100));
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 100 && failure.OwnedRootAfter == 0 &&
                failure.AliveBefore == true && failure.AliveAfter == false &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                failure.RootMatchesBefore == false && failure.RootMatchesAfter == null, "Failure refresh must not overwrite completed probe facts.");
            Observed(failure, 1, true, true, "owned");
        });
        Case("direct-seed-refresh-clears-ordinal", () =>
        {
            var model = Model(100); model.SeedResults = found => new OwnedNode[9]; var tree = model.Tree();
            string trace = resolved.Substring(collection.Length + 1) + "|" + Registration(1) + "|seeds:0|seed-count:0";
            var failure = Refused(model, tree, () => tree.Seed(model.Main), "query-seed-bound", trace,
                NativeResolved(100) + "|" + NativeRegistration(100), 1, 1);
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.AliveAfter == null, "Closed collection must not refresh.");
            Observed(failure, null, null, null, null);
        });
        Case("resolve-success-before-scan-failure", () =>
        {
            var model = Model(100); model.CandidateResults = (node, selector, found) => new OwnedNode[257];
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-candidate-bound",
                resolved + "|" + Registration(1) + "|" + Registration(1) +
                "|pid:1|identity:1|candidates-Discovery:1|candidate-count:1",
                NativeResolved(100) + "|" + NativeRegistration(100) + "|" + NativeRegistration(100), 1, 1, candidateQueries: 1);
            Verify(failure.PreviouslyOwnedHandle == 100 && failure.AliveAfter == null, "Closed scan must not refresh.");
            Observed(failure, null, null, null, null);
        });
        Case("seed-private-provider-failure", () =>
        {
            var model = Model(); model.Before = (read, node) =>
            { if (read == "identity") throw new InvalidOperationException(QueryPrivateText); };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-provider-failure", outer + "|pid:1|identity:1", "");
            Unowned(failure); Observed(failure, 1, null, null, null);
        });
        Case("cancel-before-collection", () =>
        {
            var model = Model(); model.GuardAction = () => { throw new DesktopTreeGuardException("query-test-cancelled"); };
            var tree = model.Tree();
            var failure = Refused(model, tree, tree.Discover, "query-test-cancelled", "", "", 0, seedQueries: 0);
            Unowned(failure); Observed(failure, null, null, null, null);
        });
        foreach (string read in new[] { "handle", "alive", "native-pid" })
            foreach (bool after in new[] { false, true })
                Case("cancel-" + (after ? "after-" : "before-") + read, () =>
                {
                    var model = Model(); bool cancel = false; int checks = 0;
                    int call = read == "handle" ? 8 : read == "alive" ? 9 : 10;
                    model.Before = (name, node) => { if (after && model.Reads.Count == call) cancel = true; };
                    model.GuardAction = () =>
                    {
                        if (cancel || (!after && model.Reads.Count == call - 1 && ++checks == 2))
                            throw new DesktopTreeGuardException("query-test-cancelled");
                    };
                    string[] all = probe.Split('|');
                    string trace = string.Join("|", all, 0, after ? call : call - 1);
                    string native = call == 10 ? "alive:" + QueryCandidateKey : "";
                    if (after && call >= 9) native += (native.Length == 0 ? "" : "|") + read + ":" + QueryCandidateKey;
                    var tree = model.Tree();
                    var failure = Refused(model, tree, tree.Discover, "query-test-cancelled", trace, native);
                    Unowned(failure); Observed(failure, 1, call > 8 ? (bool?)true : null,
                        call > 9 ? (bool?)true : null, null);
                });
        Case("owner-diagnostics-nondefault", () =>
        {
            var model = Model(500);
            model.Native[500].Class = "ComboLBox";
            model.Native[500].Owner = 400;
            model.Native.Add(400, new OwnedNative { Root = 400, Class = "#32770" });
            var failure = Capture(model.Tree().Discover, "query-owner-root");
            queryDiagnosticSampleNames.Add(currentCase);
            queryDiagnosticSamples.Add(failure);
            Verify(failure.OwnerHandle == 400 && failure.OwnerClass == "#32770" &&
                failure.OwnerParent == 0 && failure.OwnerNativeRoot == 400 &&
                failure.OwnerDepth == 0 && failure.OwnerIsSelfRoot == true &&
                failure.OwnerChainCount == null && failure.OwnerChainTransient == null &&
                failure.OwnerChainFirstHandle == 0 && failure.OwnerChainFirstClass == null &&
                failure.OwnerChainLastHandle == 0 && failure.OwnerChainLastClass == null,
                "Non-default owner diagnostic sample.");
        });
        return queryDiagnosticCaseNames.Count;
    }

    static readonly List<string> rejectedRootClassCaseNames = new List<string>();
    static readonly List<string> rejectedRootClassCaseFailures = new List<string>();
    static readonly List<DesktopQueryFailure> rejectedRootClassSamples = new List<DesktopQueryFailure>();
    static readonly List<string> rejectedRootClassExpectedValues = new List<string>();
    public static string[] RejectedRootClassCaseNames => rejectedRootClassCaseNames.ToArray();
    public static string[] RejectedRootClassCaseFailures => rejectedRootClassCaseFailures.ToArray();
    public static DesktopQueryFailure[] RejectedRootClassSamples => rejectedRootClassSamples.ToArray();
    public static string[] RejectedRootClassExpectedValues => rejectedRootClassExpectedValues.ToArray();

    public static void AssertRejectedRootClassCaseInventory()
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", rejectedRootClassCaseNames)))).ToLowerInvariant();
        if (rejectedRootClassCaseNames.Count != 40 ||
            new HashSet<string>(rejectedRootClassCaseNames, StringComparer.Ordinal).Count != 40 ||
            digest != "d64699f354eab3ab60a5ee4b5571684d058f259cb9f92eb3bc38a9cc267069d2")
            throw new InvalidOperationException("Rejected root class case inventory changed.");
    }

    public static int RunRejectedRootClassTests()
    {
        rejectedRootClassCaseNames.Clear(); rejectedRootClassCaseFailures.Clear();
        rejectedRootClassSamples.Clear(); rejectedRootClassExpectedValues.Clear();
        const string prefix = "seeds:0|seed-count:0|seed-item:1|pid:1|handle:1|pid:1|identity:1|handle:1|" +
            "alive:0|native-pid:0|native-root:0|native-pid:0|alive:0|alive:0|native-pid:0|native-root:0|class:0";
        const string nativePrefix = "alive:100|native-pid:100|native-root:100|native-pid:100|alive:100|" +
            "alive:100|native-pid:100|native-root:100";
        const string refresh = "|alive:0|native-pid:0|native-root:0|native-pid:0";
        const string nativeRefresh = "|alive:100|native-pid:100|native-root:100|native-pid:100";
        var property = typeof(DesktopQueryFailure).GetProperty("RejectedRootClass");
        void Verify(bool condition, string contract)
        {
            if (!condition) throw new InvalidOperationException(contract);
        }
        DesktopQueryFailure Capture(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == predicate) { return ex.Failure; }
            throw new InvalidOperationException("Expected original refusal: " + predicate);
        }
        void Case(string name, string value, string expected, Action<OwnedModel> setup = null,
            string trace = prefix + refresh, string native = nativePrefix + nativeRefresh,
            string predicate = "query-root-class")
        {
            rejectedRootClassCaseNames.Add(name);
            try
            {
                var model = new OwnedModel();
                model.Main.Children.Clear(); model.Native.Remove(200); model.Roots.Remove(200);
                model.Main.Name = QueryPrivateText; model.Main.AutomationId = QueryPrivateText;
                model.Main.RuntimeId = new[] { QueryRuntimeId }; model.Native[100].Class = value;
                setup?.Invoke(model);
                var tree = model.Tree();
                var failure = Capture(tree.Discover, predicate);
                rejectedRootClassSamples.Add(failure); rejectedRootClassExpectedValues.Add(expected);
                Verify(string.Join("|", model.Reads) == trace, "Exact unchanged adapter trace: " + string.Join("|", model.Reads));
                Verify(string.Join("|", model.NativeReads) == native, "Exact unchanged native trace.");
                int calls = trace.Split('|').Length;
                Verify(failure.Calls == calls && tree.Budget.Calls == calls &&
                    model.Reads.Count == calls && failure.Nodes == 1 && tree.Budget.Nodes == 1 &&
                    failure.Windows == 0 && tree.Windows.Count == 0 && model.Bindings.Count == 0 &&
                    model.SeedQueries == 1 && model.CandidateQueries == 0 &&
                    model.ProviderNodes == 0 && model.ProviderProperties == 0 && tree.Budget.Closed,
                    "Refusal must not admit class, owner, window, provider expansion or calls.");
                string before = failure.Predicate;
                model.Native[100].Class = "DifferentLaterClass"; model.Native[100].Pid = QueryForeignPid;
                Verify(ReferenceEquals(Capture(tree.Discover, predicate), failure) &&
                    ReferenceEquals(Capture(() => tree.Seed(model.Main), predicate), failure) &&
                    failure.Predicate == before && model.Reads.Count == calls, "First failure frozen without retry.");
                Verify(property != null && property.PropertyType == typeof(string), "Nullable RejectedRootClass missing.");
                Verify((string)property.GetValue(failure) == expected, "Rejected class retention/suppression.");
                if (expected != null)
                    Verify(failure.PreviouslyOwnedHandle == 100 && failure.OwnedRootBefore == 100 &&
                        failure.OwnedRootAfter == 100 && failure.AliveBefore == true && failure.AliveAfter == true &&
                        failure.OwnPidBefore == true && failure.OwnPidAfter == true &&
                        failure.ExpectedOwnedRoot == 0 && failure.RootMatchesBefore == false &&
                        failure.RootMatchesAfter == false, "RootMatches false must not suppress owned class.");
            }
            catch (Exception ex) { rejectedRootClassCaseFailures.Add(name + ": " + ex.Message); }
        }
        Case("owned-class-rootmatches-false", "OwnedAuxiliaryClass", "OwnedAuxiliaryClass");
        Case("printable-one-space", " ", " ");
        Case("printable-upper-bound", new string('~', 256), new string('~', 256));
        Case("printable-json-escapes", "Owned\"\\Class", "Owned\"\\Class");
        var printableAscii = new StringBuilder();
        for (char c = (char)32; c <= 126; c++) printableAscii.Append(c);
        Case("all-printable-ascii", printableAscii.ToString(), printableAscii.ToString());
        Case("null-class", null, null);
        Case("empty-class", "", null);
        Case("oversize-no-truncation", new string('A', 257), null);
        foreach (char c in new[] { '\0', '\t', '\n', '\r', '\x1f', '\x7f', '\x80', '\u2028', '\ud800' })
            Case("nonprintable-" + ((int)c).ToString("x4"), "Owned" + c + "Class", null);
        Case("original-class-not-reread", "OwnedAuxiliaryClass", "OwnedAuxiliaryClass", model =>
            model.Before = (read, node) => { if (model.Reads.Count == 18) model.Native[100].Class = QueryPrivateText; });
        Case("refresh-dead", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (model.Reads.Count == 18) model.Native[100].Alive = false; },
            prefix + "|alive:0|native-pid:0", nativePrefix + "|alive:100|native-pid:100");
        foreach (int changedPid in new[] { 0, QueryForeignPid })
            Case("refresh-pid-" + (changedPid == 0 ? "zero" : "foreign"), "OwnedAuxiliaryClass", null, model =>
                model.Before = (read, node) => { if (model.Reads.Count == 19) model.Native[100].Pid = changedPid; },
                prefix + "|alive:0|native-pid:0", nativePrefix + "|alive:100|native-pid:100");
        Case("refresh-root-zero", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (model.Reads.Count == 20) model.Native[100].Root = 0; },
            prefix + "|alive:0|native-pid:0|native-root:0", nativePrefix + "|alive:100|native-pid:100|native-root:100");
        Case("refresh-root-changed-owned", "OwnedAuxiliaryClass", null, model =>
        {
            model.Native[300] = new OwnedNative { Root = 300 };
            model.Before = (read, node) => { if (model.Reads.Count == 20) model.Native[100].Root = 300; };
        }, prefix + refresh, nativePrefix + "|alive:100|native-pid:100|native-root:100|native-pid:300");
        Case("refresh-root-pid-changed", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (model.Reads.Count == 21) model.Native[100].Pid = QueryForeignPid; });
        foreach (int call in new[] { 18, 19, 20, 21 })
        {
            string partial = string.Join("|", (prefix + refresh).Split('|'), 0, call);
            string nativePartial = string.Join("|", (nativePrefix + nativeRefresh).Split('|'), 0, 8 + call - 18);
            Case("refresh-provider-exception-" + call, "OwnedAuxiliaryClass", null, model =>
                model.Before = (read, node) => { if (model.Reads.Count == call) throw new InvalidOperationException(QueryPrivateText); },
                partial, nativePartial);
            Case("refresh-post-read-cancellation-" + call, "OwnedAuxiliaryClass", null, model =>
                model.GuardAction = () => { if (model.Reads.Count == call) throw new DesktopTreeGuardException("query-test-cancelled"); },
                partial, nativePartial + "|" + (call == 18 ? "alive" : call == 20 ? "native-root" : "native-pid") + ":100");
        }
        Case("class-provider-exception", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (read == "class") throw new InvalidOperationException(QueryPrivateText); },
            predicate: "query-provider-failure");
        Case("class-post-read-cancellation", "OwnedAuxiliaryClass", null, model =>
            model.GuardAction = () => { if (model.Reads.Count == 17) throw new DesktopTreeGuardException("query-test-cancelled"); },
            prefix, nativePrefix, "query-test-cancelled");
        Case("class-closes-budget", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (read == "class") model.CloseBudget(); },
            prefix, nativePrefix, "query-budget-closed");
        Case("refresh-pre-read-cancellation", "OwnedAuxiliaryClass", null, model =>
        {
            int checks = 0;
            model.GuardAction = () => { if (model.Reads.Count == 17 && ++checks == 2) throw new DesktopTreeGuardException("query-test-cancelled"); };
        }, prefix, nativePrefix);
        Case("refresh-last-read-closes-budget", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (model.Reads.Count == 21) model.CloseBudget(); });
        Case("other-predicate-root-self", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (model.Reads.Count == 16) model.Native[100].Root = 300; },
            prefix.Substring(0, prefix.LastIndexOf('|')) + "|alive:0|native-pid:0|native-root:0|native-pid:0",
            nativePrefix + "|alive:100|native-pid:100|native-root:100|native-pid:300", "query-root-self");
        Case("other-predicate-class-changed", "OwnedAuxiliaryClass", null, model =>
            model.Before = (read, node) => { if (read == "class") throw new DesktopTreeGuardException("query-root-class-changed"); },
            predicate: "query-root-class-changed");
        rejectedRootClassCaseNames.Add("cross-root-nonzero-expected-root");
        try
        {
            var model = new OwnedModel();
            model.Main.Children.Clear();
            var auxiliary = model.Add(model.Main, 9, 200);
            foreach (var node in new[] { model.Main, auxiliary })
            {
                node.Name = QueryPrivateText; node.AutomationId = QueryPrivateText;
                node.RuntimeId = new[] { QueryRuntimeId, node.Id };
            }
            model.Roots[200] = auxiliary; model.Native[200].Class = "OwnedAuxiliaryClass";
            string registration = "alive:0|native-pid:0|native-root:0|class:0|owner:0|from-handle:0|" +
                "pid:1|handle:1|identity:1|visible:0|offscreen:1|alive:0|native-pid:0|native-root:0|class:0|owner:0";
            string nativeRegistration = "alive:100|native-pid:100|native-root:100|alive:100|native-pid:100|native-root:100";
            string trace = string.Join("|", prefix.Split('|'), 0, 13) + "|" + registration + "|" + registration +
                "|pid:1|identity:1|candidates-Discovery:1|candidate-count:1|candidate-item:9|" +
                "pid:9|identity:9|handle:9|alive:0|native-pid:0|native-root:0|native-pid:0|alive:0|identity:9|" +
                "alive:0|native-pid:0|native-root:0|class:0" + refresh;
            string native = string.Join("|", nativePrefix.Split('|'), 0, 5) + "|" +
                nativeRegistration + "|" + nativeRegistration +
                "|alive:200|native-pid:200|native-root:200|native-pid:200|alive:200|" +
                "alive:200|native-pid:200|native-root:200|alive:200|native-pid:200|native-root:200|native-pid:200";
            var tree = model.Tree();
            var failure = Capture(tree.Discover, "query-root-class");
            rejectedRootClassSamples.Add(failure); rejectedRootClassExpectedValues.Add("OwnedAuxiliaryClass");
            Verify(string.Join("|", model.Reads) == trace && string.Join("|", model.NativeReads) == native,
                "Cross-root exact adapter/native trace.");
            Verify(failure.Calls == 67 && tree.Budget.Calls == 67 && model.Reads.Count == 67 &&
                failure.Nodes == 2 && tree.Budget.Nodes == 2 && failure.Windows == 1 &&
                tree.Windows.Count == 1 && model.Bindings.Count == 1 &&
                model.SeedQueries == 1 && model.CandidateQueries == 1 && model.ProviderNodes == 1 &&
                model.ProviderProperties == 2 && tree.Budget.Closed, "Cross-root exact counters and containment.");
            Verify(failure.ExpectedOwnedRoot == 100 && failure.PreviouslyOwnedHandle == 200 &&
                failure.OwnedRootBefore == 200 && failure.OwnedRootAfter == 200 &&
                failure.RootMatchesBefore == false && failure.RootMatchesAfter == false &&
                failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true, "Different expected root still owned.");
            model.Native[200].Class = QueryPrivateText; model.Native[200].Pid = QueryForeignPid;
            Verify(ReferenceEquals(Capture(tree.Discover, "query-root-class"), failure) &&
                ReferenceEquals(Capture(() => tree.Seed(auxiliary), "query-root-class"), failure) &&
                model.Reads.Count == 67, "Cross-root first failure frozen.");
            Verify(property != null && (string)property.GetValue(failure) == "OwnedAuxiliaryClass",
                "Different expected owned root must not suppress rejected class.");
        }
        catch (Exception ex) { rejectedRootClassCaseFailures.Add("cross-root-nonzero-expected-root: " + ex.Message); }
        return rejectedRootClassCaseNames.Count;
    }

    static readonly List<string> startupAcquisitionCaseNames = new List<string>();
    static readonly List<string> startupAcquisitionCaseFailures = new List<string>();
    public static string[] StartupAcquisitionCaseNames => startupAcquisitionCaseNames.ToArray();
    public static string[] StartupAcquisitionCaseFailures => startupAcquisitionCaseFailures.ToArray();

    static void AcquisitionAssert(bool condition, string contract)
    {
        if (!condition) throw new InvalidOperationException("StartupAcquisition." + contract);
    }

    static DesktopTreeException AcquisitionRefusal(Action operation)
    {
        try { operation(); }
        catch (DesktopTreeException ex) { return ex; }
        throw new InvalidOperationException("StartupAcquisition.ExpectedTypedRefusal");
    }

    static void AcquisitionError(Action operation, string code)
    {
        try { operation(); }
        catch (InvalidOperationException ex) when (ex.Message == code) { return; }
        throw new InvalidOperationException("StartupAcquisition.ExpectedRefusal:" + code);
    }

    static DesktopTreeException InitialUnavailable()
    {
        var model = new OwnedModel();
        model.Native[100].Alive = false; model.Native[100].Pid = 0;
        return AcquisitionRefusal(model.Tree().Discover);
    }

    static DesktopTreeException ChangedUnavailable(string field, object value)
    {
        var original = InitialUnavailable().Failure;
        var changed = new DesktopQueryFailure();
        foreach (var property in typeof(DesktopQueryFailure).GetProperties())
            property.SetValue(changed, property.GetValue(original));
        typeof(DesktopQueryFailure).GetProperty(field).SetValue(changed, value);
        return new DesktopTreeException(changed);
    }

    // This fixture drives the production seam and real owned-tree projections.
    // Its guarded tail/terminal latch model the caller, not a historical GUI run.
    sealed class StartupAcquisitionFixture
    {
        internal readonly OwnedModel Model = new OwnedModel();
        internal readonly DesktopStartupObservation Observation;
        internal readonly DesktopStartupAcquisition Acquisition;
        internal readonly List<string> Events = new List<string>();
        internal DesktopTreeException Terminal;
        internal string TerminalCode;
        internal DesktopStartupSample<OwnedNode> LastSample;
        internal DesktopStartupDecision LastDecision;
        internal int EventCount, Dispatches, Tails, Guards;
        internal long At = 10;
        internal bool Cancelled, ProcessAlive = true, Ready = true;

        internal StartupAcquisitionFixture()
        {
            Observation = new DesktopStartupObservation(Model.Bindings);
            Acquisition = new DesktopStartupAcquisition(Model.Bindings, Observation);
            Model.GuardAction = Guard;
        }

        internal void Guard()
        {
            Guards++;
            string code = Cancelled ? "cancelled" : EventCount >= 240 ? "event-bound" :
                DesktopPolicy.Expired(At, 45000) ? "deadline" :
                !ProcessAlive ? "process-identity" : !Ready ? "readiness-lost" : null;
            if (code != null) throw new DesktopTreeGuardException(code);
        }

        void Record(string code)
        {
            Guard();
            AcquisitionAssert(code == "startup-snapshot-unavailable", "FixedSafeObservation");
            Events.Add(code); EventCount++;
        }

        internal string Step(char outcome, bool acceptance = false, Action afterDiscover = null)
        {
            if (TerminalCode != null) return "terminal";
            Model.Native[100].Alive = outcome != 'U';
            Model.Native[100].Pid = outcome == 'U' ? 0 : 7;
            Model.MainButton.Offscreen = outcome == 'N';
            LastSample = null;
            try
            {
                if (!Acquisition.TryDiscover(Model, 7, "loading-handoff", Guard, Record, out var tree))
                {
                    AcquisitionAssert(tree == null &&
                        (!Acquisition.Recovering || Acquisition.RetainedUnavailable != null),
                        "WholeFailedTreeDiscarded");
                    Guard(); Tails++; At += 25;
                    return Acquisition.Recovering ? "unavailable" : "not-ready";
                }
                afterDiscover?.Invoke();
                LastSample = ProjectStartup(Model, tree);
                Guard();
                bool loadingExists = Observation.LoadingObserved &&
                    Model.Native.TryGetValue((uint)Observation.LoadingHandle, out var loading) && loading.Alive;
                LastDecision = LastSample.Observe(Observation, At, loadingExists, acceptance);
                bool accepted = Acquisition.Complete(LastDecision, acceptance);
                if (accepted && acceptance)
                {
                    LastSample.RefreshAndCheckRevision();
                    Guard();
                    Dispatches++;
                    return "accepted";
                }
                Guard(); Tails++; At += 25;
                return accepted ? "candidate" : LastDecision.Ready ? "discarded" : "not-ready";
            }
            catch (DesktopTreeException ex)
            {
                Terminal = ex; TerminalCode = ex.Failure.Predicate;
                return "terminal";
            }
            catch (DesktopTreeGuardException ex)
            {
                TerminalCode = ex.Message;
                return "terminal";
            }
        }

        internal void Expect(char outcome, string expected, int used, bool recovering,
            bool acceptance = false, Action afterDiscover = null)
        {
            string actual = Step(outcome, acceptance, afterDiscover);
            AcquisitionAssert(actual == expected, outcome + ":Expected-" + expected + "-Actual-" + actual);
            AcquisitionAssert(Acquisition.Used == used && Acquisition.Recovering == recovering, "ChargeState");
            AcquisitionAssert(ReferenceEquals(Acquisition.Bindings, Model.Bindings) &&
                ReferenceEquals(Acquisition.Observation, Observation), "AttemptHistoryIdentity");
            AcquisitionAssert(Dispatches == (expected == "accepted" ? 1 : 0), "ZeroEarlyDispatch");
        }
    }

    public static int RunStartupAcquisitionTests()
    {
        startupAcquisitionCaseNames.Clear(); startupAcquisitionCaseFailures.Clear();
        void Case(string name, Action operation)
        {
            if (startupAcquisitionCaseNames.Contains(name))
                throw new InvalidOperationException("Duplicate startup acquisition case.");
            startupAcquisitionCaseNames.Add(name);
            try { operation(); }
            catch (InvalidOperationException ex) { startupAcquisitionCaseFailures.Add(name + ": " + ex.Message); }
        }
        void Reject(string field, object value) =>
            AcquisitionAssert(!DesktopStartupAcquisition.IsUnavailable(ChangedUnavailable(field, value), 0),
                "ClosedConjunction:" + field);

        Case("default-state-is-not-recovering", () =>
        {
            var f = new StartupAcquisitionFixture();
            AcquisitionAssert(f.Acquisition.Used == 0 && !f.Acquisition.Recovering &&
                f.Acquisition.RetainedUnavailable == null && f.Dispatches == 0, "DefaultRefusalState");
        });
        Case("null-exception-refuses", () =>
            AcquisitionAssert(!DesktopStartupAcquisition.IsUnavailable(null, 0), "NullRefuses"));
        Case("actual-first-equal-nonzero-unowned-seed-qualifies", () =>
        {
            var ex = InitialUnavailable();
            AcquisitionAssert(ex.Failure.SeedOrdinal == 1 && ex.Failure.SeedResolveKeyEqual == true &&
                ex.Failure.ResolveAlive == false && ex.Failure.ResolvePidRelation == "zero", "ActualTypedOperands");
            AcquisitionAssert(DesktopStartupAcquisition.IsUnavailable(ex, 0), "ExpectedEligibleInitialDiscover");
        });
        foreach (string field in new[] { "Stage", "Selector", "Predicate", "ResolvePidRelation" })
        {
            Case("missing-" + field, () => Reject(field, null));
            Case("wrong-" + field, () => Reject(field, "wrong"));
            Case("case-sensitive-" + field, () =>
                Reject(field, ((string)typeof(DesktopQueryFailure).GetProperty(field)
                    .GetValue(InitialUnavailable().Failure)).ToUpperInvariant()));
        }
        foreach (int? ordinal in new int?[] { null, 0, 2, 8, 9 })
            Case("ordinal-" + (ordinal?.ToString() ?? "null"), () => Reject("SeedOrdinal", ordinal));
        foreach (bool? equal in new bool?[] { null, false })
            Case("seed-key-equal-" + (equal?.ToString() ?? "null"), () => Reject("SeedResolveKeyEqual", equal));
        foreach (bool? alive in new bool?[] { null, true })
            Case("resolve-alive-" + (alive?.ToString() ?? "null"), () => Reject("ResolveAlive", alive));
        foreach (string relation in new[] { "owned", "foreign", "" })
            Case("resolve-pid-" + relation, () => Reject("ResolvePidRelation", relation));
        foreach (string field in new[] { "ExpectedOwnedRoot", "PreviouslyOwnedHandle", "OwnedRootBefore", "OwnedRootAfter" })
            Case("owned-handle-" + field, () => Reject(field, 100L));
        foreach (string field in new[] { "AliveBefore", "AliveAfter", "OwnPidBefore", "OwnPidAfter",
            "RootMatchesBefore", "RootMatchesAfter" })
        {
            Case("legacy-true-" + field, () => Reject(field, true));
            Case("legacy-false-" + field, () => Reject(field, false));
        }
        foreach (int windows in new[] { -1, 1, 8, 9 })
            Case("current-windows-" + windows, () =>
                AcquisitionAssert(!DesktopStartupAcquisition.IsUnavailable(InitialUnavailable(), windows),
                    "CurrentTreeNotLedger"));
        Case("inconsistent-diagnostic-windows-refuses", () => Reject("Windows", 1));
        Case("empty-default-diagnostic-refuses", () =>
            AcquisitionAssert(!DesktopStartupAcquisition.IsUnavailable(
                new DesktopTreeException(new DesktopQueryFailure()), 0), "DefaultDiagnosticRefuses"));
        Case("zero-seed-is-not-unavailability", () =>
        {
            var model = new OwnedModel(); model.Main.Handle = 0;
            var ex = AcquisitionRefusal(model.Tree().Discover);
            AcquisitionAssert(ex.Failure.Predicate == "query-seed-root" &&
                !DesktopStartupAcquisition.IsUnavailable(ex, 0), "ZeroSeedRefuses");
        });
        Case("changed-resolve-key-refuses", () =>
        {
            var model = new OwnedModel(); int reads = 0;
            model.Native[101] = new OwnedNative { Root = 101, Alive = false, Pid = 0 };
            model.Before = (name, node) => { if (name == "handle" && ++reads == 2) node.Handle = 101; };
            var ex = AcquisitionRefusal(model.Tree().Discover);
            AcquisitionAssert(ex.Failure.SeedResolveKeyEqual == false &&
                !DesktopStartupAcquisition.IsUnavailable(ex, 0), "ChangedKeyRefuses");
        });
        Case("later-seed-and-partial-roots-refuse", () =>
        {
            var model = new OwnedModel(); model.SeedNodes.Add(model.Wizard);
            model.Native[200].Alive = false; model.Native[200].Pid = 0;
            var tree = model.Tree(); var ex = AcquisitionRefusal(tree.Discover);
            AcquisitionAssert(tree.Windows.Count == 1 && ex.Failure.SeedOrdinal == 2 &&
                !DesktopStartupAcquisition.IsUnavailable(ex, tree.Windows.Count), "PartialTreeRefuses");
        });
        Case("low-level-discover-and-fail-remain-sticky", () =>
        {
            var model = new OwnedModel(); model.Native[100].Alive = false; model.Native[100].Pid = 0;
            var tree = model.Tree(); var first = AcquisitionRefusal(tree.Discover);
            int reads = model.Reads.Count;
            model.Native[100].Alive = true; model.Native[100].Pid = 7;
            var later = AcquisitionRefusal(tree.Discover);
            AcquisitionAssert(ReferenceEquals(first.Failure, later.Failure) && tree.Budget.Closed &&
                tree.Windows.Count == 0 && model.Reads.Count == reads, "StickyLowLevelRefusal");
        });
        Case("direct-seed-never-enters-recovery", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
            var ex = AcquisitionRefusal(() => f.Model.Tree().Seed(f.Model.Main));
            AcquisitionAssert(ex.Failure.SeedOrdinal == null && !f.Acquisition.Recovering &&
                f.Events.Count == 0, "SeedProvenance");
        });
        Case("empty-refresh-identical-dto-is-not-callsite-authority", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
            var ex = AcquisitionRefusal(f.Model.Tree().RefreshCatalog);
            AcquisitionAssert(ex.Failure.SeedOrdinal == 1 && ex.Failure.Windows == 0 &&
                f.Acquisition.RetainedUnavailable == null && f.Events.Count == 0, "RefreshNotDiscover");
        });
        Case("ordinary-candidate-and-acceptance-are-uncharged", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('C', "candidate", 0, false);
            var candidate = f.LastSample;
            f.Expect('A', "accepted", 0, false, true);
            AcquisitionAssert(!ReferenceEquals(candidate.Tree, f.LastSample.Tree) && f.Events.Count == 0,
                "TwoFreshOrdinaryTrees");
        });
        Case("U-C1-A2", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            var unavailable = f.Acquisition.RetainedUnavailable;
            f.Expect('C', "candidate", 1, true);
            f.Expect('A', "accepted", 2, false, true);
            AcquisitionAssert(f.Events.Count == 1 && unavailable.Failure.Predicate == "query-native-gone",
                "SeparateActualUnavailable");
        });
        Case("U-N1-normal-polls-U-C2-A3", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('N', "not-ready", 1, false);
            for (int i = 0; i < 6; i++) f.Expect('N', "not-ready", 1, false);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('C', "candidate", 2, true);
            f.Expect('A', "accepted", 3, false, true);
            AcquisitionAssert(f.Events.Count == 2, "OneEventPerScheduledRecovery");
        });
        Case("U-C1-U2-C3-discard-normal-C-normal-A", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('C', "candidate", 1, true);
            f.Expect('U', "unavailable", 2, true, true);
            f.Expect('C', "discarded", 3, false);
            var discarded = f.LastSample;
            AcquisitionAssert(f.Terminal == null && f.Dispatches == 0, "NoStaleGoneAfterReady3");
            f.Expect('C', "candidate", 3, false);
            var candidate = f.LastSample;
            f.Expect('A', "accepted", 3, false, true);
            AcquisitionAssert(!ReferenceEquals(discarded.Tree, candidate.Tree) &&
                !ReferenceEquals(discarded.Tree, f.LastSample.Tree) &&
                !ReferenceEquals(candidate.Tree, f.LastSample.Tree), "NeverReuseReady3");
        });
        Case("U-U1-U2-U3-terminal-no-fifth", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            var prior = f.Acquisition.RetainedUnavailable;
            AcquisitionAssert(f.Step('U') == "terminal" && f.Acquisition.Used == 3 &&
                f.TerminalCode == "query-native-gone" && !ReferenceEquals(prior.Failure, f.Terminal.Failure),
                "FourthActualUnavailableIsTerminal");
            int reads = f.Model.Reads.Count;
            AcquisitionAssert(f.Step('C') == "terminal" && f.Model.Reads.Count == reads &&
                f.Model.SeedQueries == 4 && f.Events.Count == 3 && f.Dispatches == 0, "NoFifthProviderAcquisition");
        });
        Case("complete-N3-ends-recovery-without-refund", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            f.Expect('N', "not-ready", 3, false);
            f.Expect('N', "not-ready", 3, false);
            f.Expect('C', "candidate", 3, false);
            f.Expect('A', "accepted", 3, false, true);
        });
        Case("new-unavailable-after-N3-is-current-terminal-cause", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            f.Expect('N', "not-ready", 3, false);
            var prior = f.Acquisition.RetainedUnavailable;
            AcquisitionAssert(f.Step('U') == "terminal" && f.Acquisition.Used == 3 &&
                !ReferenceEquals(prior.Failure, f.Terminal.Failure), "NoAllowanceRefund");
        });
        Case("acceptance3-unavailable-is-terminal-actual-cause", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('N', "not-ready", 1, false);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('C', "candidate", 2, true);
            var prior = f.Acquisition.RetainedUnavailable;
            AcquisitionAssert(f.Step('U', true) == "terminal" && f.Acquisition.Used == 3 &&
                f.TerminalCode == "query-native-gone" && !ReferenceEquals(prior.Failure, f.Terminal.Failure) &&
                f.Dispatches == 0, "Acceptance3Failure");
        });
        Case("not-ready-acceptance-ends-recovery-without-refund", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('C', "candidate", 1, true);
            f.Expect('N', "not-ready", 2, false, true);
            f.Expect('N', "not-ready", 2, false);
            f.Expect('C', "candidate", 2, false);
            f.Expect('A', "accepted", 2, false, true);
        });
        foreach (string phase in new[] { "initial", "recovery", "acceptance" })
            Case("cancel-before-" + phase, () =>
            {
                var f = new StartupAcquisitionFixture();
                if (phase != "initial") f.Expect('U', "unavailable", 0, true);
                if (phase == "acceptance") f.Expect('C', "candidate", 1, true);
                int reads = f.Model.Reads.Count, used = f.Acquisition.Used;
                f.Cancelled = true;
                AcquisitionAssert(f.Step('C', phase == "acceptance") == "terminal" &&
                    f.TerminalCode == "cancelled" && f.Model.Reads.Count == reads &&
                    f.Acquisition.Used == used && f.Dispatches == 0, "CancellationBeforeChargeOrRead");
            });
        foreach (string guard in new[] { "deadline", "process-identity", "readiness-lost", "event-bound" })
            Case("later-guard-priority-" + guard, () =>
            {
                var f = new StartupAcquisitionFixture();
                f.Expect('U', "unavailable", 0, true);
                int reads = f.Model.Reads.Count;
                if (guard == "deadline") f.At = 45000;
                if (guard == "process-identity") f.ProcessAlive = false;
                if (guard == "readiness-lost") f.Ready = false;
                if (guard == "event-bound") f.EventCount = 240;
                AcquisitionAssert(f.Step('C') == "terminal" && f.TerminalCode == guard &&
                    f.Model.Reads.Count == reads && f.Acquisition.Used == 0 && f.Dispatches == 0, "GuardWins");
            });
        Case("event239-final-slot-prevents-next-provider-read", () =>
        {
            var f = new StartupAcquisitionFixture { EventCount = 239 };
            AcquisitionAssert(f.Step('U') == "terminal" && f.TerminalCode == "event-bound" &&
                f.EventCount == 240 && f.Events.Count == 1 && f.Model.SeedQueries == 1 &&
                f.Dispatches == 0, "EventCapAtCommonTail");
        });
        foreach (string phase in new[] { "capture", "observation", "revalidation", "action" })
            Case("wrong-callsite-" + phase, () =>
            {
                var f = new StartupAcquisitionFixture();
                var sample = ProjectStartup(f.Model);
                if (phase == "revalidation") sample.Observe(f.Observation, 1, false, false);
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
                Action operation = phase == "capture" ? (Action)(() => ProjectStartup(f.Model, sample.Tree)) :
                    phase == "action" ? () => sample.Tree.ValidateWindow(f.Model.Main, DesktopRootKind.Avalonia) :
                    () => sample.Observe(f.Observation, 2, false, phase == "revalidation");
                var ex = AcquisitionRefusal(operation);
                AcquisitionAssert(ex.Failure.Predicate != null && f.Acquisition.RetainedUnavailable == null &&
                    f.Events.Count == 0 && f.Dispatches == 0, "NoRecoveryOutsideInitialDiscover");
            });
        Case("successful-discover-but-incomplete-capture-cannot-reset", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            AcquisitionAssert(f.Step('C', afterDiscover: () =>
            {
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
            }) == "terminal" && f.TerminalCode == "query-native-gone" &&
                f.Acquisition.Used == 1 && f.Acquisition.Recovering && f.LastSample == null &&
                f.Events.Count == 1, "CaptureFailureNotCompleteN");
        });
        foreach (string cause in new[] { "query-native-pid", "query-provider-failure", "query-node-bound",
            "query-call-bound", "query-depth", "query-seed-bound" })
            Case("later-terminal-cause-" + cause, () =>
            {
                var f = new StartupAcquisitionFixture();
                f.Expect('U', "unavailable", 0, true);
                f.Model.Before = (name, node) =>
                {
                    if (name != "seeds") return;
                    if (cause == "query-provider-failure") throw new InvalidOperationException("PRIVATE provider detail");
                    throw new DesktopTreeGuardException(cause);
                };
                AcquisitionAssert(f.Step('C') == "terminal" && f.TerminalCode == cause &&
                    !ReferenceEquals(f.Terminal.Failure, f.Acquisition.RetainedUnavailable.Failure) &&
                    f.Events.Count == 1 && f.Dispatches == 0, "LaterTerminalNotRetainedGone");
            });
        foreach (string mutation in new[] { "class", "identity", "owner" })
            Case("retained-history-" + mutation, () =>
            {
                var f = new StartupAcquisitionFixture(); f.Model.Tree().Discover();
                f.Expect('U', "unavailable", 0, true);
                string expected;
                if (mutation == "class")
                {
                    f.Model.Native[100].Class = "Avalonia-33333333-3333-3333-3333-333333333333";
                    expected = "query-root-class-changed";
                }
                else if (mutation == "identity")
                {
                    f.Model.Main.RuntimeId = new[] { 999 };
                    expected = "query-root-alias";
                }
                else { f.Model.Native[200].Owner = 999; expected = "query-owner-pid"; }
                AcquisitionAssert(f.Step('C') == "terminal" && f.TerminalCode == expected &&
                    f.Model.Bindings.Count == 2 && f.Dispatches == 0, "HistoryNeverRebound");
            });
        Case("retained-loading-chronology-survives-unavailability", () =>
        {
            var f = new StartupAcquisitionFixture();
            var loading = f.Model.Add(null, 10, 300);
            f.Model.Add(loading, 11, 0, DesktopSelector.Loading);
            f.Model.Native[300] = new OwnedNative { Root = 300,
                Class = "Avalonia-33333333-3333-3333-3333-333333333333" };
            f.Model.Roots[300] = loading;
            f.Model.SeedNodes.Clear(); f.Model.SeedNodes.Add(loading);
            AcquisitionAssert(!ProjectStartup(f.Model).Observe(f.Observation, 1, true, false).Ready, "LoadingOnly");
            f.Model.Native[300].Alive = false;
            f.Model.SeedNodes.Clear(); f.Model.SeedNodes.Add(f.Model.Main);
            f.Expect('U', "unavailable", 0, true);
            AcquisitionAssert(f.Observation.LoadingObserved && f.Observation.LoadingAt == 1 &&
                f.Observation.LoadingHandle == 300 && f.Model.Bindings.Count == 1, "StickyLoadingHistory");
            f.Expect('C', "candidate", 1, true);
            f.Expect('A', "accepted", 2, false, true);
            AcquisitionAssert(f.LastDecision.Route == DesktopStartupObservation.ObservedRoute &&
                f.Model.Bindings.Count == 3, "OriginalObservedRoute");
        });
        Case("invalidation-only-removes-provisional-candidate", () =>
        {
            var f = new StartupAcquisitionFixture(); var sample = ProjectStartup(f.Model);
            AcquisitionAssert(sample.Observe(f.Observation, 1, false, false).Ready, "CandidateReady");
            f.Observation.InvalidateCandidate();
            AcquisitionError(() => sample.Observe(f.Observation, 2, false, true), "startup-acceptance-candidate");
            AcquisitionAssert(f.Model.Bindings.Count == 2, "BindingsRetained");
        });
        Case("invalidation-retains-observation-order", () =>
        {
            var f = new StartupAcquisitionFixture(); var sample = ProjectStartup(f.Model);
            sample.Observe(f.Observation, 10, false, false); f.Observation.InvalidateCandidate();
            AcquisitionError(() => sample.Observe(f.Observation, 10, false, false), "startup-observation-order");
        });
        Case("invalidation-retains-sticky-observation-refusal", () =>
        {
            var observation = new DesktopStartupObservation();
            AcquisitionError(() => observation.Observe(1, null, false), "startup-root-bound");
            observation.InvalidateCandidate();
            AcquisitionError(() => observation.Observe(2, new DesktopStartupRoot[0], false), "startup-root-bound");
        });
        Case("invalidation-retains-startup-class-history-bound", () =>
        {
            var observation = new DesktopStartupObservation();
            const string windowClass = "Avalonia-11111111-1111-1111-1111-111111111111";
            for (int i = 1; i <= 32; i++)
                observation.Observe(i, new[] { new DesktopStartupRoot(i, 0, windowClass, true, false, false, false) }, false);
            observation.InvalidateCandidate();
            AcquisitionError(() => observation.Observe(33,
                new[] { new DesktopStartupRoot(33, 0, windowClass, true, false, false, false) }, false),
                "startup-class-history-bound");
        });
        Case("replacement-retains-native-history-bound", () =>
        {
            var f = new StartupAcquisitionFixture();
            for (uint handle = 1000; handle < 1032; handle++)
                f.Model.Bindings.BindCanonical(handle, f.Model.Native[100].Class, handle.ToString(), 0);
            f.Expect('U', "unavailable", 0, true);
            AcquisitionAssert(f.Step('C') == "terminal" && f.TerminalCode == "query-root-history-bound" &&
                f.Model.Bindings.Count == 32, "NativeHistoryBoundRetained");
        });
        Case("null-decision-cannot-complete", () =>
        {
            var f = new StartupAcquisitionFixture();
            bool refused = false;
            try { f.Acquisition.Complete(null, false); }
            catch (ArgumentNullException) { refused = true; }
            AcquisitionAssert(refused && f.Dispatches == 0, "MissingCompleteObservation");
        });
        foreach (string stage in new[] { "open-editor", "import", "normal-close" })
            Case("actual-wrong-stage-" + stage, () =>
            {
                var f = new StartupAcquisitionFixture();
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
                var ex = AcquisitionRefusal(() => f.Acquisition.TryDiscover(f.Model, 7, stage,
                    f.Guard, code => throw new InvalidOperationException("Wrong-stage recovery event."),
                    out var tree));
                AcquisitionAssert(ex.Failure.Stage == stage && f.Acquisition.Used == 0 &&
                    !f.Acquisition.Recovering, "OnlyHandoffStage");
            });
        foreach (int nativePid in new[] { 7, QueryForeignPid })
            Case("actual-dead-nonzero-native-pid-" + nativePid, () =>
            {
                var f = new StartupAcquisitionFixture();
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = nativePid;
                var ex = AcquisitionRefusal(() => f.Acquisition.TryDiscover(f.Model, 7, "loading-handoff",
                    f.Guard, code => throw new InvalidOperationException("Nonzero-PID recovery event."),
                    out var tree));
                AcquisitionAssert(ex.Failure.Predicate == "query-native-gone" &&
                    !f.Acquisition.Recovering && f.Acquisition.Used == 0, "NativePidMustBeZero");
            });
        Case("event240-refuses-before-initial-provider-read", () =>
        {
            var f = new StartupAcquisitionFixture { EventCount = 240 };
            AcquisitionAssert(f.Step('U') == "terminal" && f.TerminalCode == "event-bound" &&
                f.Model.Reads.Count == 0 && f.Acquisition.Used == 0 && f.Events.Count == 0, "EventCapBeforeRead");
        });
        Case("charge3-incomplete-capture-does-not-discard-as-success", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            AcquisitionAssert(f.Step('C', afterDiscover: () =>
            {
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
            }) == "terminal" && f.Acquisition.Used == 3 && f.Acquisition.Recovering &&
                f.TerminalCode == "query-native-gone" && f.LastSample == null, "DiscoverAloneIsNotReady3");
        });
        Case("discarded-Ready3-cannot-be-revalidated", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            f.Expect('C', "discarded", 3, false);
            AcquisitionError(() => f.LastSample.Observe(f.Observation, f.At, false, true),
                "startup-acceptance-candidate");
        });
        Case("acceptance3-provider-failure-keeps-current-cause", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('N', "not-ready", 1, false);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('C', "candidate", 2, true);
            f.Model.Before = (name, node) =>
            { if (name == "seeds") throw new InvalidOperationException("PRIVATE provider detail"); };
            AcquisitionAssert(f.Step('A', true) == "terminal" && f.Acquisition.Used == 3 &&
                f.TerminalCode == "query-provider-failure" && f.Dispatches == 0, "Acceptance3ProviderFailure");
        });
        Case("retained-unavailable-does-not-latch-ordinary-polling", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            var actual = f.Acquisition.RetainedUnavailable;
            f.Expect('N', "not-ready", 1, false);
            f.Expect('C', "candidate", 1, false);
            AcquisitionAssert(f.Terminal == null && actual.Failure.Predicate == "query-native-gone" &&
                f.Acquisition.Used == 1, "RecoveryEvidenceIsNotTerminalLatch");
        });
        Case("empty-discover-discards-before-capture", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.SeedResults = seeds =>
            {
                if (f.Model.SeedQueries == 1) return Array.Empty<OwnedNode>();
                f.Model.Native[100].Alive = false;
                f.Model.Native[100].Pid = 0;
                return seeds;
            };
            f.Expect('C', "not-ready", 0, false);
            AcquisitionAssert(f.Model.SeedQueries == 1 && f.LastSample == null &&
                f.Terminal == null && f.Events.Count == 0 && f.Tails == 1 &&
                f.Acquisition.RetainedUnavailable == null, "EmptyTreeNeverEntersCapture");
        });
        Case("empty-discover-followed-by-fresh-unavailable", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.SeedNodes.Clear();
            f.Expect('C', "not-ready", 0, false);
            AcquisitionAssert(f.Model.SeedQueries == 1 && f.LastSample == null, "OneEmptyQuery");
            f.Model.SeedNodes.Add(f.Model.Main);
            f.Expect('U', "unavailable", 0, true);
            AcquisitionAssert(f.Events.Count == 1 && f.Terminal == null, "FreshDiscoverOwnsRecovery");
            f.Expect('C', "candidate", 1, true);
            f.Expect('A', "accepted", 2, false, true);
        });
        Case("repeated-empty-discover-stays-uncharged", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.SeedNodes.Clear();
            for (int poll = 1; poll <= 5; poll++)
            {
                f.Expect('C', "not-ready", 0, false);
                AcquisitionAssert(f.Model.SeedQueries == poll && f.LastSample == null &&
                    f.Tails == poll && f.Events.Count == 0 && f.Acquisition.RetainedUnavailable == null,
                    "OneQueryPerGuardedEmptyPoll");
            }
        });
        Case("empty-acceptance-invalidates-candidate", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('C', "candidate", 0, false);
            f.Model.SeedNodes.Clear();
            int queries = f.Model.SeedQueries;
            f.Expect('A', "not-ready", 0, false, true);
            AcquisitionAssert(f.Model.SeedQueries == queries + 1 && f.LastSample == null &&
                f.Dispatches == 0, "EmptyAcceptanceDoesNotProject");
            AcquisitionError(() => f.Observation.Revalidate(f.At, Array.Empty<DesktopStartupRoot>(), false),
                "startup-acceptance-candidate");
        });
        Case("empty-acceptance-requires-new-ordinary-pair", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('C', "candidate", 0, false);
            f.Model.SeedNodes.Clear();
            f.Expect('A', "not-ready", 0, false, true);
            AcquisitionAssert(f.LastSample == null && f.Dispatches == 0, "EmptyIsNeverAcceptance");
            f.Model.SeedNodes.Add(f.Model.Main);
            f.Expect('C', "candidate", 0, false);
            f.Expect('A', "accepted", 0, false, true);
        });
        for (int charge = 1; charge <= 3; charge++)
            Case("empty-recovery-charge-" + charge + "-retained", () =>
            {
                var f = new StartupAcquisitionFixture();
                f.Expect('U', "unavailable", 0, true);
                for (int used = 1; used < charge; used++) f.Expect('U', "unavailable", used, true);
                var retained = f.Acquisition.RetainedUnavailable;
                f.Model.SeedNodes.Clear();
                int queries = f.Model.SeedQueries;
                f.Expect('C', "not-ready", charge, false);
                AcquisitionAssert(f.LastSample == null && f.Model.SeedQueries == queries + 1 &&
                    ReferenceEquals(retained, f.Acquisition.RetainedUnavailable) && f.Events.Count == charge,
                    "EmptyEndsRecoveryWithoutRefundOrEvidenceReset");
                f.Model.SeedNodes.Add(f.Model.Main);
                f.Expect('C', "candidate", charge, false);
                f.Expect('A', "accepted", charge, false, true);
            });
        Case("empty-at-charge3-new-unavailable-is-terminal", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Expect('U', "unavailable", 0, true);
            f.Expect('U', "unavailable", 1, true);
            f.Expect('U', "unavailable", 2, true);
            var prior = f.Acquisition.RetainedUnavailable;
            f.Model.SeedNodes.Clear();
            f.Expect('C', "not-ready", 3, false);
            AcquisitionAssert(f.LastSample == null, "LastChargeEmptyIsDiscarded");
            f.Model.SeedNodes.Add(f.Model.Main);
            f.Expect('U', "terminal", 3, false);
            AcquisitionAssert(f.TerminalCode == "query-native-gone" &&
                !ReferenceEquals(prior, f.Terminal) && f.Events.Count == 3,
                "EmptyDoesNotRestoreRecoveryBudget");
        });
        Case("empty-discover-preserves-loading-and-bindings", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.MainButton.Offscreen = true;
            f.Model.Add(f.Model.Main, 5, 0, DesktopSelector.Loading);
            var sample = ProjectStartup(f.Model);
            AcquisitionAssert(!sample.Observe(f.Observation, 1, true, false).Ready &&
                f.Observation.LoadingObserved, "LoadingHistoryEstablished");
            string windowClass = f.Model.Bindings.Expected(100, DesktopRootKind.Avalonia);
            f.Model.SeedNodes.Clear();
            f.Expect('N', "not-ready", 0, false);
            AcquisitionAssert(f.LastSample == null && f.Observation.LoadingObserved &&
                f.Observation.LoadingHandle == 100 && f.Observation.LoadingAt == 1 &&
                f.Model.Bindings.HasCanonical(100) &&
                f.Model.Bindings.Expected(100, DesktopRootKind.Avalonia) == windowClass,
                "EmptyPreservesStickyLoadingAndCanonicalBindings");
        });
        Case("empty-discover-guard-precedes-provider-read", () =>
        {
            var f = new StartupAcquisitionFixture { At = 45000 };
            f.Model.SeedNodes.Clear();
            f.Expect('C', "terminal", 0, false);
            AcquisitionAssert(f.TerminalCode == "deadline" && f.Model.SeedQueries == 0 &&
                f.Model.Reads.Count == 0 && f.Events.Count == 0, "EmptyCannotBypassGuard");
        });
        Case("nonempty-capture-native-gone-stays-terminal-after-empty", () =>
        {
            var f = new StartupAcquisitionFixture();
            f.Model.SeedNodes.Clear();
            f.Expect('C', "not-ready", 0, false);
            f.Model.SeedNodes.Add(f.Model.Main);
            AcquisitionAssert(f.Step('C', afterDiscover: () =>
            {
                f.Model.Native[100].Alive = false; f.Model.Native[100].Pid = 0;
            }) == "terminal" && f.TerminalCode == "query-native-gone" &&
                f.Acquisition.Used == 0 && !f.Acquisition.Recovering &&
                f.Acquisition.RetainedUnavailable == null && f.Events.Count == 0 && f.Dispatches == 0,
                "EmptyDoesNotBroadenNonemptyCaptureRecovery");
        });
        return startupAcquisitionCaseNames.Count;
    }

    public static void AssertStartupAcquisitionCaseInventory()
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", startupAcquisitionCaseNames)))).ToLowerInvariant();
        if (startupAcquisitionCaseNames.Count != 115 ||
            digest != "bf03596c4f1f37a75f440d50587f8372a5bd7ba1fdaa7af828dbd39879a95a2f")
            throw new InvalidOperationException("Startup acquisition case inventory changed.");
        string originalDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", startupAcquisitionCaseNames.GetRange(0, 103))))).ToLowerInvariant();
        if (originalDigest != "231875f268ba40c2423ba30541c789056c03c844cd890b553cb3ec6c00b89909")
            throw new InvalidOperationException("Original startup acquisition case prefix changed.");
        if (startupAcquisitionCaseFailures.Count != 0)
            throw new InvalidOperationException("Startup acquisition assertions failed: " +
                string.Join("; ", startupAcquisitionCaseFailures));
    }

    sealed class ThrowingStartupRoots : IReadOnlyList<DesktopStartupRoot>
    {
        public int Count => throw new InvalidOperationException("PRIVATE diagnostic source");
        public DesktopStartupRoot this[int index] => throw new InvalidOperationException("PRIVATE diagnostic row");
        public IEnumerator<DesktopStartupRoot> GetEnumerator() => throw new InvalidOperationException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static Dictionary<string, long> RunDistinctClassLifecycle()
    {
        const string mainClass = "Avalonia-11111111-1111-1111-1111-111111111111";
        const string wizardClass = "Avalonia-22222222-2222-2222-2222-222222222222";
        const string loadingClass = "Avalonia-33333333-3333-3333-3333-333333333333";
        const string editorClass = "Avalonia-44444444-4444-4444-4444-444444444444";
        const string confirmationClass = "Avalonia-55555555-5555-5555-5555-555555555555";
        var model = new OwnedModel();
        model.Native[100].Class = mainClass; model.Native[200].Class = wizardClass;
        OwnedNode Root(uint handle, int id, OwnedNode parent, uint owner, string windowClass)
        {
            var node = model.Add(parent, id, handle);
            model.Native.Add(handle, new OwnedNative { Root = handle, Owner = owner, Class = windowClass });
            model.Roots.Add(handle, node);
            return node;
        }
        void Close(uint handle, OwnedNode parent)
        {
            parent?.Children.Remove(model.Roots[handle]);
            model.SeedNodes.Remove(model.Roots[handle]);
            model.Native[handle].Alive = false;
        }
        var loading = Root(300, 10, null, 0, loadingClass);
        model.Add(loading, 11, 0, DesktopSelector.Loading);
        model.SeedNodes.Clear(); model.SeedNodes.Add(loading);
        var observation = new DesktopStartupObservation(model.Bindings);
        if (ProjectStartup(model).Observe(observation, 10, true, false).Ready)
            throw new InvalidOperationException("Loading-only lifecycle became ready.");
        Close(300, null); model.SeedNodes.Add(model.Main);
        var sample = ProjectStartup(model);
        var decision = sample.Observe(observation, 20, false, false);
        if (!decision.Ready || decision.MainHandle != 100 || decision.WizardHandle != 200)
            throw new InvalidOperationException("Distinct-class main/wizard lifecycle failed.");
        Close(200, model.Main);
        var editor = Root(400, 20, null, 0, editorClass); model.SeedNodes.Add(editor);
        model.Add(editor, 21, 0, DesktopSelector.Import);
        var tree = model.Tree(); tree.Discover(); tree.ValidateWindow(editor, DesktopRootKind.Avalonia, 0);
        var picker = Root(500, 30, editor, 400, "#32770");
        tree = model.Tree(); tree.Discover(); tree.ValidateWindow(picker, DesktopRootKind.NativeDialog, 400);
        Close(500, editor);
        var confirmation = Root(600, 40, editor, 400, confirmationClass);
        model.Add(confirmation, 41, 0, DesktopSelector.ConfirmationYes);
        tree = model.Tree(); tree.Discover(); tree.ValidateWindow(confirmation, DesktopRootKind.Avalonia, 400);
        Close(600, editor);
        picker = Root(700, 50, editor, 400, "#32770");
        tree = model.Tree(); tree.Discover(); tree.ValidateWindow(picker, DesktopRootKind.NativeDialog, 400);
        Close(700, editor);
        tree = model.Tree(); tree.Discover();
        tree.ValidateWindow(model.Main, DesktopRootKind.Avalonia); tree.ValidateWindow(editor, DesktopRootKind.Avalonia, 0);
        if (observation.WindowClass != mainClass || observation.LoadingWindowClass != loadingClass ||
            model.Bindings.Count != 7) throw new InvalidOperationException("Per-window lifecycle bindings lost.");
        return new Dictionary<string, long> { ["distinctRootLifetimes"] = 7, ["avaloniaClasses"] = 5,
            ["nativePickers"] = 2, ["finalWindows"] = tree.Windows.Count };
    }

    public static int RunWindowIdentityTests()
    {
        const string mainClass = "Avalonia-11111111-1111-1111-1111-111111111111";
        const string otherClass = "Avalonia-33333333-3333-3333-3333-333333333333";
        windowIdentityCaseNames.Clear(); windowIdentityCaseFailures.Clear(); startupDiagnosticSamples.Clear();
        void Verify(bool value)
        {
            if (!value) throw new InvalidOperationException("Unexpected window-identity result.");
        }
        void Case(string name, Action action)
        {
            if (windowIdentityCaseNames.Contains(name)) throw new InvalidOperationException("Duplicate window-identity case.");
            windowIdentityCaseNames.Add(name);
            try { action(); }
            catch (Exception ex) { windowIdentityCaseFailures.Add(name + ": " + ex.Message); }
        }
        void Refused(Action action, string reason)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == reason) { return; }
            catch (DesktopTreeGuardException ex) when (ex.Message == reason) { return; }
            catch (InvalidOperationException ex) when (ex.Message == reason) { return; }
            throw new InvalidOperationException("Expected window-identity refusal: " + reason);
        }
        OwnedNode AddRoot(OwnedModel model, uint handle, int id, uint owner = 100, string windowClass = otherClass)
        {
            var root = model.Add(model.Main, id, handle);
            model.Native.Add(handle, new OwnedNative { Root = handle, Owner = owner, Class = windowClass });
            model.Roots.Add(handle, root);
            return root;
        }
        (OwnedModel model, IReadOnlyList<DesktopStartupRoot> roots) Snapshot(int count = 2)
        {
            var model = new OwnedModel();
            for (int i = 2; i < count; i++) AddRoot(model, (uint)(300 + i), 10 + i);
            var tree = model.Tree(); tree.Discover();
            var sample = DesktopStartupSample<OwnedNode>.Capture(tree, root =>
                new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root), false, false, false));
            return (model, sample.Roots);
        }
        void RejectSnapshot(string name, Func<IReadOnlyList<DesktopStartupRoot>, IReadOnlyList<DesktopStartupRoot>> alter)
        {
            Case(name, () =>
            {
                var fixture = Snapshot();
                var capture = new DesktopStartupFailureCapture();
                capture.Record("startup-root-role", alter(fixture.roots), fixture.model.Bindings);
                Verify(capture.Value == null);
            });
        }
        Case("distinct-class-real-query-lifecycle", () => Verify(RunDistinctClassLifecycle()["distinctRootLifetimes"] == 7));
        Case("different-main-wizard-fast-route", () =>
        {
            var model = new OwnedModel(); var sample = ProjectStartup(model);
            var observation = new DesktopStartupObservation(model.Bindings);
            var decision = sample.Observe(observation, 10, false, false);
            Verify(decision.Ready && decision.MainHandle == 100 && decision.WizardHandle == 200 &&
                observation.WindowClass == model.Native[100].Class && observation.LoadingWindowClass == null);
        });
        Case("same-valid-class-different-handles-allowed", () =>
        {
            var model = new OwnedModel(); model.Native[200].Class = mainClass;
            Verify(ProjectStartup(model).Observe(new DesktopStartupObservation(model.Bindings), 10, false, false).Ready);
        });
        Case("fresh-trees-share-canonical-class-bindings", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 3; i++) model.Tree().Discover();
            Verify(model.Bindings.Count == 2 && model.Bindings.Matches(100, mainClass) &&
                model.Bindings.Matches(200, model.Native[200].Class));
        });
        Case("same-main-hwnd-class-mutation-at-operation-refused", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Native[100].Class = otherClass;
            Refused(() => model.Tree().ValidateWindow(model.Main, DesktopRootKind.Avalonia), "query-root-class-changed");
        });
        Case("same-wizard-hwnd-class-mutation-across-tree-refused", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Native[200].Class = otherClass;
            Refused(() => model.Tree().Discover(), "query-root-class-changed");
        });
        Case("owner-class-continuity-without-owner-seed", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Native[100].Class = otherClass;
            model.SeedNodes.Clear(); model.SeedNodes.Add(model.Wizard);
            Refused(() => model.Tree().Discover(), "query-root-class-changed");
        });
        Case("canonical-runtime-identity-mutation-across-tree-refused", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Main.RuntimeId = new[] { 999 };
            Refused(() => model.Tree().Discover(), "query-root-alias");
        });
        Case("window-argument-canonical-alias-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            var alias = model.Add(null, 999, 100);
            Refused(() => tree.ValidateWindow(alias, DesktopRootKind.Avalonia), "query-root-alias");
        });
        Case("native-child-handle-is-not-root-binding", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100, Class = "Button" });
            var tree = model.Tree(); tree.Discover();
            Verify(!model.Bindings.HasCanonical(150) && model.Bindings.Count == 2);
            Refused(() => tree.ValidateWindow(model.MainButton, DesktopRootKind.Avalonia), "query-window-native-root");
        });
        Case("native-picker-kind-is-not-avalonia", () =>
        {
            var model = new OwnedModel(); var picker = AddRoot(model, 300, 10, 100, "#32770");
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.ValidateWindow(picker, DesktopRootKind.Avalonia), "query-root-kind");
        });
        Case("avalonia-kind-is-not-native-picker", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.ValidateWindow(model.Main, DesktopRootKind.NativeDialog), "query-root-kind");
        });
        foreach (string badClass in new[] { "", "other", "Avalonia-invalid", mainClass + " ", mainClass + new string(' ', 5000) })
            Case("invalid-class-shape-" + (badClass.Length > 45 ? badClass.Length.ToString() : badClass), () =>
            {
                var model = new OwnedModel(); model.Native[200].Class = badClass;
                Refused(() => model.Tree().Discover(), "query-root-class");
            });
        Case("class-kind-mutation-does-not-rebind", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Native[200].Class = "#32770";
            Refused(() => model.Tree().Discover(), "query-root-class-changed");
        });
        Case("closed-handle-class-binding-not-evicted", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover();
            model.Main.Children.Remove(model.Wizard); model.Native[200].Alive = false; model.Tree().Discover();
            model.Main.Children.Add(model.Wizard); model.Native[200].Alive = true; model.Native[200].Class = otherClass;
            Refused(() => model.Tree().Discover(), "query-root-class-changed");
        });
        Case("closed-handle-canonical-identity-not-rebound", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover();
            model.Main.Children.Remove(model.Wizard); model.Native[200].Alive = false; model.Tree().Discover();
            model.Main.Children.Add(model.Wizard); model.Native[200].Alive = true; model.Wizard.RuntimeId = new[] { 999 };
            Refused(() => model.Tree().Discover(), "query-root-alias");
        });
        Case("foreign-reassigned-root-is-not-class-authorized", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover(); model.Native[100].Pid = 8;
            Refused(() => model.Tree().ValidateWindow(model.Main, DesktopRootKind.Avalonia), "query-native-pid");
        });
        foreach (string role in new[] { "wizard", "editor", "confirmation", "picker" })
            Case("operation-owner-rechecked-" + role, () =>
            {
                var model = new OwnedModel();
                uint expectedOwner = role == "wizard" ? 100u : role == "editor" ? 0u : 400u;
                if (role == "confirmation" || role == "picker") AddRoot(model, 400, 20, 0);
                var root = AddRoot(model, 300, 10, role == "wizard" ? 0u : 100u,
                    role == "picker" ? "#32770" : otherClass);
                var tree = model.Tree(); tree.Discover();
                Refused(() => tree.ValidateWindow(root, role == "picker" ? DesktopRootKind.NativeDialog :
                    DesktopRootKind.Avalonia, expectedOwner), "query-window-owner");
            });
        Case("history-32-then-33-without-eviction", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover();
            model.Main.Children.Remove(model.Wizard); model.Native[200].Alive = false;
            for (int i = 0; i < 30; i++)
            {
                uint h = (uint)(300 + i); var root = AddRoot(model, h, 100 + i);
                var tree = model.Tree(); tree.Discover(); tree.ValidateWindow(root, DesktopRootKind.Avalonia, 100);
                model.Main.Children.Remove(root); model.Native[h].Alive = false;
            }
            Verify(model.Bindings.Count == 32);
            AddRoot(model, 400, 200);
            Refused(() => model.Tree().Discover(), "query-root-history-bound");
            Verify(model.Bindings.Count == 32);
        });
        Case("startup-history-32-then-33", () =>
        {
            var observation = new DesktopStartupObservation();
            for (int i = 1; i <= 32; i++)
                Verify(observation.Observe(i, new[] { new DesktopStartupRoot(i, 0, mainClass, true, true, false, false) }, false).Ready);
            Refused(() => observation.Observe(33,
                new[] { new DesktopStartupRoot(33, 0, mainClass, true, true, false, false) }, false), "startup-class-history-bound");
        });
        Case("same-loading-handle-class-mutation-remains-negative", () =>
        {
            var observation = new DesktopStartupObservation();
            observation.Observe(1, new[] { new DesktopStartupRoot(100, 0, mainClass, true, false, true, false) }, true);
            Refused(() => observation.Observe(2,
                new[] { new DesktopStartupRoot(100, 0, otherClass, true, false, true, false) }, true), "startup-class-changed");
        });
        Case("same-wizard-handle-class-mutation-remains-negative", () =>
        {
            var observation = new DesktopStartupObservation();
            var main = new DesktopStartupRoot(100, 0, mainClass, true, true, false, false);
            observation.Observe(1, new[] { main, new DesktopStartupRoot(200, 100, mainClass, true, false, false, true) }, false);
            Refused(() => observation.Revalidate(2,
                new[] { main, new DesktopStartupRoot(200, 100, otherClass, true, false, false, true) }, false), "startup-class-changed");
        });
        Case("actual-policy-failure-publishes-owned-specific-snapshot", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 0;
            var sample = ProjectStartup(model); var capture = new DesktopStartupFailureCapture();
            string reason = null;
            try { sample.Observe(new DesktopStartupObservation(model.Bindings), 10, false, false); }
            catch (InvalidOperationException ex) { reason = ex.Message; }
            Verify(reason == "startup-wizard-owner");
            int reads = model.Reads.Count;
            capture.Record(reason, sample.Roots, model.Bindings);
            Verify(model.Reads.Count == reads && capture.Value != null && capture.Value.Predicate == reason &&
                capture.Value.Roots.Count == 2 && capture.Value.Roots[0].WindowClass != capture.Value.Roots[1].WindowClass);
            startupDiagnosticSamples.Add(capture.Value);
        });
        Case("eight-bound-root-diagnostic", () =>
        {
            var fixture = Snapshot(8); var capture = new DesktopStartupFailureCapture();
            int reads = fixture.model.Reads.Count;
            capture.Record("startup-root-role", fixture.roots, fixture.model.Bindings);
            Verify(capture.Value != null && capture.Value.Roots.Count == 8 && fixture.model.Reads.Count == reads);
            startupDiagnosticSamples.Add(capture.Value);
        });
        Case("maximum-handle-diagnostic-serialization-shape", () =>
        {
            var model = new OwnedModel(); model.SeedNodes.Clear(); model.Native.Clear(); model.Roots.Clear();
            for (int i = 0; i < 8; i++)
            {
                uint handle = uint.MaxValue - (uint)i;
                var node = model.Add(null, 100 + i, handle);
                model.Native.Add(handle, new OwnedNative { Root = handle, Class = otherClass });
                model.Roots.Add(handle, node); model.SeedNodes.Add(node);
            }
            var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            foreach (var root in tree.Windows)
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, true, true, true, true));
            Refused(() => new DesktopStartupObservation(model.Bindings).Observe(10, roots, false), "startup-root-role");
            var capture = new DesktopStartupFailureCapture();
            capture.Record("startup-root-role", roots, model.Bindings);
            Verify(capture.Value != null && capture.Value.Roots.Count == 8);
            startupDiagnosticSamples.Add(capture.Value);
        });
        Case("native-dialog-diagnostic-is-fixed-owned-class", () =>
        {
            var model = new OwnedModel(); AddRoot(model, 300, 10, 100, "#32770");
            var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            foreach (var root in tree.Windows)
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, true, false, false, false));
            var capture = new DesktopStartupFailureCapture();
            capture.Record("startup-root-class", roots, model.Bindings);
            Verify(capture.Value != null && capture.Value.Roots[2].WindowClass == "#32770");
            startupDiagnosticSamples.Add(capture.Value);
        });
        Case("diagnostic-frozen-and-one-attempt", () =>
        {
            var fixture = Snapshot(); var input = new List<DesktopStartupRoot>(fixture.roots);
            var capture = new DesktopStartupFailureCapture(); capture.Record("startup-root-role", input, fixture.model.Bindings);
            var frozen = capture.Value; input.Clear();
            capture.Record("startup-wizard-owner", input, fixture.model.Bindings);
            Verify(frozen != null && ReferenceEquals(frozen, capture.Value) && frozen.Roots.Count == 2 &&
                frozen.Predicate == "startup-root-role");
        });
        Case("diagnostic-does-not-query-after-native-state-changes", () =>
        {
            var fixture = Snapshot(); fixture.model.Native[100].Pid = 8;
            fixture.model.Before = (name, node) => throw new InvalidOperationException("No failure-time adapter access.");
            int reads = fixture.model.Reads.Count; var capture = new DesktopStartupFailureCapture();
            capture.Record("startup-root-role", fixture.roots, fixture.model.Bindings);
            Verify(capture.Value != null && fixture.model.Reads.Count == reads);
        });
        foreach (string reason in new[] { "PRIVATE VALUE", "query-native-pid", "", "Startup-root-role" })
            Case("unknown-diagnostic-predicate-" + reason, () =>
            {
                var fixture = Snapshot(); var capture = new DesktopStartupFailureCapture();
                capture.Record(reason, fixture.roots, fixture.model.Bindings);
                capture.Record("startup-root-role", fixture.roots, fixture.model.Bindings);
                Verify(capture.Value == null);
            });
        Case("null-diagnostic-bindings-refused", () =>
        {
            var fixture = Snapshot(); var capture = new DesktopStartupFailureCapture();
            capture.Record("startup-root-role", fixture.roots, null); Verify(capture.Value == null);
        });
        Case("diagnostic-source-error-never-replaces-failure", () =>
        {
            var fixture = Snapshot(); var capture = new DesktopStartupFailureCapture();
            capture.Record("startup-root-role", new ThrowingStartupRoots(), fixture.model.Bindings);
            Verify(capture.Value == null);
            capture.Record("startup-root-role", fixture.roots, fixture.model.Bindings);
            Verify(capture.Value == null);
        });
        RejectSnapshot("null-diagnostic-roots-refused", roots => null);
        RejectSnapshot("null-diagnostic-row-refused", roots => new DesktopStartupRoot[] { null });
        RejectSnapshot("duplicate-diagnostic-root-refused", roots => new[] { roots[0], roots[0] });
        RejectSnapshot("ninth-diagnostic-root-refused", roots => new DesktopStartupRoot[9]);
        foreach (long handle in new[] { 0L, -1L, (long)uint.MaxValue + 1, 999L })
            RejectSnapshot("unbound-diagnostic-handle-" + handle, roots =>
                new[] { new DesktopStartupRoot(handle, 0, mainClass, true, true, false, false) });
        foreach (long owner in new[] { -1L, (long)uint.MaxValue + 1, 999L })
            RejectSnapshot("unbound-diagnostic-owner-" + owner, roots =>
                new[] { new DesktopStartupRoot(100, owner, mainClass, true, true, false, false) });
        foreach (long owner in new[] { 100L, 200L })
            RejectSnapshot("known-but-unobserved-diagnostic-owner-" + owner, roots =>
                new[] { new DesktopStartupRoot(100, owner, mainClass, true, true, false, false) });
        foreach (string windowClass in new[] { otherClass, "PRIVATE CLASS", mainClass + new string(' ', 5000) })
            RejectSnapshot("unbound-diagnostic-class-" + windowClass.Length, roots =>
                new[] { new DesktopStartupRoot(100, 0, windowClass, true, true, false, false) });
        if (windowIdentityCaseFailures.Count != 0)
            throw new InvalidOperationException("Window-identity cases failed: " + string.Join("; ", windowIdentityCaseFailures));
        return windowIdentityCaseNames.Count;
    }

    public static void AssertOwnedTreeCaseInventory()
    {
        if (ownedTreeCaseNames.Count != 159 || ownedTreeCaseFailures.Count != 0 || ownedTreeScaleProfiles.Count != 3)
            throw new InvalidOperationException("Owned-tree inventory incomplete.");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", ownedTreeCaseNames)))).ToLowerInvariant();
        if (digest != "68e9caf8c11e39b9d066b203861964847dcdf9a77abeb4b40f77b23953e18d6f")
            throw new InvalidOperationException("Owned-tree case names/order changed.");
    }

    public static void AssertStartupCaseInventory()
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", startupCaseNames)))).ToLowerInvariant();
        if (startupCaseNames.Count != 75 || startupCaseFailures.Count != 0 ||
            digest != "0e4ee056e2b08d79ac650bdcd368cc3e4b4a9d3b5f09edc273507a679d5910eb")
            throw new InvalidOperationException("Startup case inventory changed.");
    }

    public static void AssertWindowIdentityCaseInventory()
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", windowIdentityCaseNames)))).ToLowerInvariant();
        if (windowIdentityCaseNames.Count != 57 || windowIdentityCaseFailures.Count != 0 ||
            startupDiagnosticSamples.Count != 4 ||
            digest != "4e9cf5a22747adedbe9d24c13c774d0d8529d249de199c73b5c0567f3c024e8c")
            throw new InvalidOperationException("Window identity case inventory changed.");
    }

    public static Dictionary<string, long> RunOwnedTreeScaleTests() => ScaleProfile(24, 0, false);

    static Dictionary<string, long> ScaleProfile(int depth, int nativeChildren, bool wizardFirst)
    {
        var model = new OwnedModel();
        model.Main.Children.Remove(model.MainButton);
        var parent = model.Main;
        for (int i = 0; i < depth; i++) parent = model.Add(parent, 100 + i);
        parent.Children.Add(model.MainButton);
        model.MainButton.Parent = parent;
        for (int i = 0; i < 10000; i++)
        {
            var irrelevant = model.Add(model.Main, 1000 + i);
            irrelevant.Control = i % 2 == 0 ? DesktopQueryControl.Text : DesktopQueryControl.Button;
            irrelevant.AutomationId = "irrelevant-" + i;
            irrelevant.Name = "Unrelated modeled content " + i;
        }
        for (int i = 0; i < nativeChildren; i++)
        {
            uint handle = (uint)(500 + i);
            model.Add(model.Main, 12000 + i, handle).Control = DesktopQueryControl.Button;
            model.Native.Add(handle, new OwnedNative { Root = 100, Class = "Button" });
        }
        if (wizardFirst) model.SeedNodes.Insert(0, model.Wizard);
        DesktopStartupSample<OwnedNode> sample;
        try { sample = ProjectStartup(model); }
        catch (DesktopTreeException ex)
        {
            ex.Data["nodes"] = ex.Failure.Nodes;
            ex.Data["calls"] = ex.Failure.Calls;
            ex.Data["windows"] = ex.Failure.Windows;
            ex.Data["seedQueries"] = model.Reads.FindAll(x => x == "seeds:0").Count;
            ex.Data["rawNavigationCalls"] = model.Reads.FindAll(x =>
                x.StartsWith("child:", StringComparison.Ordinal) || x.StartsWith("sibling:", StringComparison.Ordinal)).Count;
            throw;
        }
        var observation = new DesktopStartupObservation();
        var decision = sample.Observe(observation, 10, false, false);
        if (!decision.Ready || decision.MainHandle != 100 || decision.WizardHandle != 200 ||
            sample.Tree.Budget.Nodes > 4096 || sample.Tree.Budget.Calls > 65536)
            throw new InvalidOperationException("Representative full-graph startup scale failed.");
        long candidateNodes = sample.Tree.Budget.Nodes, candidateCalls = sample.Tree.Budget.Calls;
        long candidateQueries = model.CandidateQueries, seedQueries = model.SeedQueries;
        var acceptance = ProjectStartup(model);
        if (!acceptance.Observe(observation, 11, false, true).Ready)
            throw new InvalidOperationException("Full-graph acceptance failed.");
        acceptance.RefreshAndCheckRevision();
        foreach (string read in model.Reads)
        {
            int separator = read.LastIndexOf(':');
            if (separator >= 0 && int.TryParse(read.Substring(separator + 1), out int id) &&
                id >= 1000 && id < 11000)
                throw new InvalidOperationException("Irrelevant provider node reached client inspection.");
        }
        if (acceptance.Tree.Budget.Nodes > 4096 || acceptance.Tree.Budget.Calls > 65536 ||
            model.CandidateQueries <= 0 || model.CandidateQueries + model.SeedQueries > 160 ||
            model.ProviderNodes < 10000)
            throw new InvalidOperationException("Scale query or client budget changed.");
        return new Dictionary<string, long>
        {
            ["irrelevantNodes"] = 10000, ["ancestorDepth"] = depth, ["nativeChildren"] = nativeChildren,
            ["wizardFirst"] = wizardFirst ? 1 : 0,
            ["nodes"] = candidateNodes, ["calls"] = candidateCalls,
            ["candidateFrameQueries"] = candidateQueries, ["candidateFrameSeedQueries"] = seedQueries,
            ["acceptanceNodes"] = acceptance.Tree.Budget.Nodes, ["acceptanceCalls"] = acceptance.Tree.Budget.Calls,
            ["windows"] = sample.Tree.Windows.Count,
            ["seedQueries"] = model.SeedQueries, ["candidateQueries"] = model.CandidateQueries,
            ["providerNodes"] = model.ProviderNodes, ["providerProperties"] = model.ProviderProperties
        };
    }

    public static int RunOwnedTreeTests()
    {
        ownedTreeCaseNames.Clear();
        ownedTreeCaseFailures.Clear();
        ownedTreeScaleProfiles.Clear();
        int count = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected owned-tree result.");
        }
        void Case(string name, Action action)
        {
            if (!names.Add(name)) throw new InvalidOperationException("Duplicate owned-tree case.");
            try { action(); }
            catch (Exception ex) { ownedTreeCaseFailures.Add(name + ": " + ex.Message); }
            ownedTreeCaseNames.Add(name);
            count++;
        }
        DesktopQueryFailure Refused(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == predicate) { return ex.Failure; }
            throw new InvalidOperationException("Expected owned-tree refusal: " + predicate);
        }
        void GuardRefused(Action action, string predicate)
        {
            try { action(); }
            catch (DesktopTreeGuardException ex) when (ex.Message == predicate) { return; }
            throw new InvalidOperationException("Expected budget refusal: " + predicate);
        }
        OwnedNode AddRoot(OwnedModel model, uint handle, int id, OwnedNode parent,
            uint owner = 100, string windowClass = null)
        {
            var node = model.Add(parent, id, handle);
            model.Native.Add(handle, new OwnedNative { Root = handle, Owner = owner,
                Class = windowClass ?? model.Native[100].Class });
            model.Roots.Add(handle, node);
            return node;
        }
        Case("main-seed-discovers-owned-wizard", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && tree.Windows[0].Handle == 100 && tree.Windows[1].Handle == 200);
            Verify(tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
            Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            Verify(tree.Find(200, null, DesktopSelector.Wizard, 1)[0] == model.Close);
        });
        foreach (bool reverse in new[] { false, true })
        {
            Case("seed-and-owned-rediscovery-deduplicates-" + reverse, () =>
            {
                var model = new OwnedModel(); model.SeedNodes.Add(model.Wizard);
                if (reverse) model.SeedNodes.Reverse();
                var tree = model.Tree(); tree.Discover();
                Verify(tree.Windows.Count == 2 && tree.Find(200, null, DesktopSelector.Wizard, 1).Count == 1);
            });
            Case("child-order-does-not-lose-owned-root-" + reverse, () =>
            {
                var model = new OwnedModel(); if (reverse) model.Main.Children.Reverse();
                var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 2);
                Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            });
        }
        Case("owner-query-never-matches-wizard-descendant", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            Verify(tree.Find(100, null, DesktopSelector.Wizard, 1).Count == 0);
            Verify(!model.Reads.Contains("match-Wizard:4") && !model.Reads.Contains("property-Name:4") &&
                model.CandidateQueries > 0 && model.ProviderProperties > 0);
        });
        Case("subtree-root-is-excluded", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Seed(model.Main);
            Verify(tree.Find(100, model.MainButton, DesktopSelector.Main, 1).Count == 0);
        });
        Case("same-native-root-child-hwnd-is-not-a-window", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100 });
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
        });
        Case("nearest-nonzero-hwnd-does-not-follow-filtered-parent", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100 });
            var tree = model.Tree(); tree.Discover(); model.Reads.Clear(); tree.Validate(100, model.MainButton);
            Verify(!model.Reads.Contains("parent:2"));
        });
        Case("raw-parent-includes-native-root-through-wrappers", () =>
        {
            var model = new OwnedModel(); model.Main.Children.Remove(model.MainButton);
            var wrapper = model.Add(model.Main, 5); wrapper.Children.Add(model.MainButton);
            model.MainButton.Parent = wrapper;
            var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            tree.Validate(100, model.MainButton);
            Verify(model.Reads.Contains("parent:2") && model.Reads.Contains("parent:5"));
        });
        Case("invisible-owned-seed-still-discovers-visible-wizard", () =>
        {
            var model = new OwnedModel(); model.Native[100].Visible = false;
            var tree = model.Tree(); tree.Discover();
            Verify(!tree.Visible(tree.Windows[0]) && tree.Visible(tree.Windows[1]));
        });
        Case("offscreen-owned-root-not-returned-as-visible", () =>
        {
            var model = new OwnedModel(); model.Wizard.Offscreen = true;
            var tree = model.Tree(); tree.Discover(); Verify(!tree.Visible(tree.Windows[1]));
        });
        Case("nested-owned-confirmation-and-native-picker-discovery", () =>
        {
            var model = new OwnedModel();
            var confirmation = AddRoot(model, 300, 5, model.Wizard, 200);
            var yes = model.Add(confirmation, 6, 0, DesktopSelector.ConfirmationYes);
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var open = model.Add(picker, 8, 401, DesktopSelector.PickerOpen);
            model.Native.Add(401, new OwnedNative { Root = 400, Class = "Button" });
            var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 4);
            Verify(tree.Find(100, null, DesktopSelector.ConfirmationYes, 1).Count == 0);
            Verify(tree.Find(300, null, DesktopSelector.ConfirmationYes, 1)[0] == yes);
            Verify(tree.Find(400, null, DesktopSelector.PickerOpen, 1)[0] == open);
        });
        Case("owned-combo-popup-chain-is-transient", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var popup = AddRoot(model, 500, 8, picker, 400, "ComboLBox");
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 4 && model.Bindings.KnownNativeOwner(400) &&
                !model.Bindings.KnownNativeOwner(500));
            tree.ValidateWindow(popup, DesktopRootKind.TransientPopup, 400);
            Refused(() => tree.ValidateWindow(popup, DesktopRootKind.Avalonia, 400), "query-root-kind");
            Refused(() => tree.ValidateWindow(popup, DesktopRootKind.NativeDialog, 400), "query-root-kind");
        });
        Case("combo-popup-child-owner-normalizes-to-picker-root", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var popup = AddRoot(model, 500, 8, picker, 401, "ComboLBox");
            model.Native.Add(401, new OwnedNative { Root = 400, Class = "ComboBox" });
            var tree = model.Tree(); tree.Discover();
            tree.ValidateWindow(popup, DesktopRootKind.TransientPopup, 400);
            Verify(model.Bindings.KnownNativeOwner(400) && !model.Bindings.KnownNativeOwner(401));
        });
        Case("combo-popup-child-owner-must-be-same-process", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            AddRoot(model, 500, 8, picker, 401, "ComboLBox");
            model.Native.Add(401, new OwnedNative { Root = 400, Pid = 8, Class = "ComboBox" });
            Refused(() => model.Tree().Discover(), "query-owner-pid");
        });
        Case("combo-popup-child-root-must-be-same-process", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var popup = AddRoot(model, 500, 8, picker, 401, "ComboLBox");
            model.Native.Add(401, new OwnedNative { Root = 400, Class = "ComboBox" });
            model.Native[400].Pid = 8;
            model.SeedNodes.Insert(0, popup);
            Refused(() => model.Tree().Discover(), "query-owner-pid");
        });
        Case("combo-popup-child-root-must-be-native-dialog", () =>
        {
            var model = new OwnedModel();
            var other = AddRoot(model, 400, 7, model.Main, 100);
            AddRoot(model, 500, 8, other, 401, "ComboLBox");
            model.Native.Add(401, new OwnedNative { Root = 400, Class = "ComboBox" });
            Refused(() => model.Tree().Discover(), "query-owner-root");
        });
        Case("combo-popup-child-root-cycle-refused", () =>
        {
            var model = new OwnedModel();
            var popup = AddRoot(model, 500, 8, model.Main, 401, "ComboLBox");
            model.Native.Add(401, new OwnedNative { Root = 500, Class = "ComboBox" });
            model.SeedNodes.Insert(0, popup);
            Refused(() => model.Tree().Discover(), "query-owner-bound");
        });
        Case("combo-popup-requires-avalonia-terminus", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 0, "#32770");
            AddRoot(model, 500, 8, picker, 400, "ComboLBox");
            var failure = Refused(() => model.Tree().Discover(), "query-owner-root");
            Verify(failure.OwnerHandle == 400 && failure.OwnerClass == "#32770" &&
                failure.OwnerParent == 0 && failure.OwnerNativeRoot == 400 &&
                failure.OwnerDepth == 0 && failure.OwnerIsSelfRoot == true &&
                failure.OwnerChainCount == null);
        });
        Case("combo-popup-single-avalonia-chain-is-reported", () =>
        {
            var model = new OwnedModel();
            AddRoot(model, 500, 8, model.Main, 100, "ComboLBox");
            var failure = Refused(() => model.Tree().Discover(), "query-owner-root");
            Verify(failure.OwnerChainCount == 1 && failure.OwnerChainTransient == true &&
                failure.OwnerChainFirstHandle == 100 &&
                DesktopPolicy.AvaloniaClass(failure.OwnerChainFirstClass) &&
                failure.OwnerChainLastHandle == 100 &&
                failure.OwnerChainLastClass == failure.OwnerChainFirstClass);
        });
        Case("ownerless-combo-popup-is-inert-transient-root", () =>
        {
            var model = new OwnedModel();
            var popup = AddRoot(model, 500, 8, model.Main, 0, "ComboLBox");
            var tree = model.Tree(); tree.Discover();
            tree.ValidateWindow(popup, DesktopRootKind.TransientPopup, 0);
            Refused(() => tree.ValidateWindow(popup, DesktopRootKind.Avalonia, 0), "query-root-kind");
            Refused(() => tree.ValidateWindow(popup, DesktopRootKind.NativeDialog, 0), "query-root-kind");
            Verify(!model.Bindings.KnownNativeOwner(500));
        });
        Case("combo-popup-foreign-dialog-owner-refused", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            var popup = AddRoot(model, 500, 8, picker, 400, "ComboLBox");
            model.SeedNodes.Insert(0, popup);
            model.Native[400].Pid = 8;
            Refused(() => model.Tree().Discover(), "query-owner-pid");
        });
        Case("combo-popup-dialog-owner-must-be-root", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 100, "#32770");
            AddRoot(model, 500, 8, picker, 400, "ComboLBox");
            model.Native[400].Root = 100;
            var failure = Refused(() => model.Tree().Discover(), "query-owner-root");
            Verify(failure.OwnerHandle == 400 && failure.OwnerClass == null &&
                failure.OwnerParent == 0 && failure.OwnerNativeRoot == 100 &&
                failure.OwnerDepth == 0 && failure.OwnerIsSelfRoot == false);
        });
        Case("combo-popup-owner-cycle-refused", () =>
        {
            var model = new OwnedModel();
            var picker = AddRoot(model, 400, 7, model.Main, 500, "#32770");
            var popup = AddRoot(model, 500, 8, picker, 400, "ComboLBox");
            model.SeedNodes.Insert(0, popup);
            Refused(() => model.Tree().Discover(), "query-owner-bound");
        });
        Case("combo-popup-cannot-own-popup", () =>
        {
            var model = new OwnedModel();
            var owner = AddRoot(model, 400, 7, model.Main, 100, "ComboLBox");
            AddRoot(model, 500, 8, owner, 400, "ComboLBox");
            Refused(() => model.Tree().Discover(), "query-owner-root");
        });
        Case("unowned-nonmodal-editor-root-is-separately-discovered", () =>
        {
            var model = new OwnedModel(); AddRoot(model, 300, 5, model.Main, 0);
            var tree = model.Tree(); tree.Discover(); Verify(tree.Windows.Count == 3);
        });
        Case("native-root-discovered-through-native-child", () =>
        {
            var model = new OwnedModel();
            var root = AddRoot(model, 300, 5, null);
            model.Add(root, 6, 0, DesktopSelector.ConfirmationYes);
            model.Add(model.Main, 7, 301);
            model.Native.Add(301, new OwnedNative { Root = 300 });
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 3 && tree.Find(300, null, DesktopSelector.ConfirmationYes, 1).Count == 1);
        });
        Case("filename-edit-subtree-prunes-owned-window", () =>
        {
            var model = new OwnedModel(); var host = model.Add(model.Main, 5, 0, DesktopSelector.FilenameHost);
            var edit = model.Add(host, 6, 150, DesktopSelector.FilenameEdit);
            model.Native.Add(150, new OwnedNative { Root = 100, Class = "Edit" });
            var other = AddRoot(model, 300, 7, host);
            model.Add(other, 8, 0, DesktopSelector.FilenameEdit);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, host, DesktopSelector.FilenameEdit, 1)[0] == edit);
        });
        Case("row-and-name-query-retains-subtree-semantics", () =>
        {
            var model = new OwnedModel(); var list = model.Add(model.Main, 5, 0, DesktopSelector.List);
            var row = model.Add(list, 6, 0, DesktopSelector.Row);
            var text = model.Add(row, 7, 0, DesktopSelector.RowName);
            model.Add(model.Wizard, 8, 0, DesktopSelector.Row);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, list, DesktopSelector.Row, 1)[0] == row);
            Verify(tree.Find(100, row, DesktopSelector.RowName, 2)[0] == text);
        });
        Case("duplicate-controls-are-not-hwnd-deduplicated", () =>
        {
            var model = new OwnedModel(); model.Add(model.Main, 5, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-match-bound");
        });
        foreach (int maximum in new[] { 0, 17 })
            Case("result-limit-" + maximum, () =>
            {
                var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
                Refused(() => tree.Find(100, null, DesktopSelector.Main, maximum), "query-result-limit");
            });
        Case("sixteen-matches-allowed", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 15; i++) model.Add(model.Main, 20 + i, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, null, DesktopSelector.Main, 16).Count == 16);
        });
        Case("seventeenth-match-refused", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 16; i++) model.Add(model.Main, 20 + i, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 16), "query-match-bound");
        });
        Case("foreign-uia-pid-before-all-other-node-access", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.MainButton.Pid = 8;
            model.CandidateResults = (root, selector, found) =>
                selector == DesktopSelector.Main ? new[] { model.MainButton } : found;
            model.Reads.Clear();
            var failure = Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid");
            foreach (string read in model.Reads)
                if (read.EndsWith(":2", StringComparison.Ordinal)) Verify(read == "pid:2" || read == "candidate-item:2");
            Verify(failure.PreviouslyOwnedHandle == 0 && failure.OwnedRootBefore == 0 &&
                failure.OwnedRootAfter == 0 && failure.ExpectedOwnedRoot == 100);
        });
        Case("foreign-seed-pid-before-handle", () =>
        {
            var model = new OwnedModel(); model.Main.Pid = 8; var tree = model.Tree();
            model.SeedResults = found => new[] { model.Main };
            Refused(() => tree.Discover(), "query-seed-pid");
            Verify(!model.Reads.Contains("handle:1") && !model.Reads.Contains("child:1"));
        });
        Case("foreign-native-pid-before-selector-or-descent", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 100, Pid = 8 });
            var tree = model.Tree(); var failure = Refused(() => tree.Discover(), "query-native-pid");
            Verify(!model.Reads.Contains("child:2") && !model.Reads.Contains("match-Main:2"));
            Verify(failure.PreviouslyOwnedHandle == 0 && failure.OwnedRootBefore == 0);
        });
        Case("foreign-native-root-before-selector-or-root-provider", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 900 });
            model.Native.Add(900, new OwnedNative { Root = 900, Pid = 8 });
            var tree = model.Tree(); var failure = Refused(() => tree.Discover(), "query-native-root-pid");
            Verify(failure.PreviouslyOwnedHandle == 150 && failure.OwnedRootBefore == 0 &&
                failure.OwnedRootAfter == 0 && !model.Reads.Contains("child:2"));
        });
        Case("foreign-raw-ancestor-stops-before-handle", () =>
        {
            var model = new OwnedModel(); var foreign = model.Add(null, 5, 900); foreign.Pid = 8;
            model.MainButton.Parent = foreign; var tree = model.Tree();
            tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid");
            Verify(!model.Reads.Contains("handle:5") && !model.Reads.Contains("parent:5"));
        });
        Case("missing-raw-parent-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Parent = null;
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-raw-parent-missing");
        });
        Case("missing-native-root-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.Native.Add(150, new OwnedNative { Root = 0 });
            Refused(() => model.Tree().Discover(), "query-native-root-pid");
        });
        Case("zero-seed-hwnd-refused", () =>
        {
            var model = new OwnedModel(); model.Main.Handle = 0;
            Refused(() => model.Tree().Discover(), "query-seed-root");
        });
        Case("native-root-must-be-top-level", () =>
        {
            var model = new OwnedModel(); model.Native[200].Root = 100;
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(200, null, DesktopSelector.Wizard, 1), "query-root-self");
        });
        foreach (string windowClass in new[] { "", "other", "Avalonia-invalid" })
            Case("invalid-root-class-" + windowClass, () =>
            {
                var model = new OwnedModel(); model.Native[200].Class = windowClass;
                Refused(() => model.Tree().Discover(), "query-root-class");
            });
        Case("same-hwnd-class-remains-bound-across-trees", () =>
        {
            var model = new OwnedModel(); model.Tree().Discover();
            model.Native[100].Class = "Avalonia-33333333-3333-3333-3333-333333333333";
            Refused(() => model.Tree().Discover(), "query-root-class-changed");
        });
        Case("foreign-owner-refused", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 900;
            model.Native.Add(900, new OwnedNative { Root = 900, Pid = 8 });
            Refused(() => model.Tree().Discover(), "query-owner-pid");
        });
        Case("owner-must-be-own-avalonia-top-level", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 900;
            model.Native.Add(900, new OwnedNative { Root = 900, Class = "#32770" });
            Refused(() => model.Tree().Discover(), "query-owner-root");
        });
        Case("owner-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 200;
            Refused(() => model.Tree().Discover(), "query-owner-bound");
        });
        Case("root-uia-handle-alias-refused", () =>
        {
            var model = new OwnedModel(); model.Roots[200] = model.Main;
            Refused(() => model.Tree().Discover(), "query-root-uia-handle");
        });
        Case("root-uia-pid-refused", () =>
        {
            var model = new OwnedModel(); var alias = model.Add(null, 9, 200); alias.Pid = 8;
            model.Roots[200] = alias;
            Refused(() => model.Tree().Discover(), "query-root-uia-pid");
        });
        Case("changed-canonical-runtime-identity-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Main.RuntimeId = new[] { 99 };
            Refused(() => tree.Validate(100, model.MainButton), "query-root-alias");
        });
        foreach (int length in new[] { 0, 33 })
            Case("runtime-identity-length-" + length, () =>
            {
                var model = new OwnedModel(); model.MainButton.RuntimeId = new int[length];
                var tree = model.Tree(); tree.Discover();
                Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-runtime-id");
            });
        Case("null-runtime-identity-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.NullIdentity = true;
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-runtime-id");
        });
        Case("32-runtime-identity-components-allowed", () =>
        {
            var model = new OwnedModel(); model.MainButton.RuntimeId = new int[32];
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, null, DesktopSelector.Main, 1).Count == 1);
        });
        Case("duplicate-raw-node-identity-refused", () =>
        {
            var model = new OwnedModel();
            model.Add(model.Main, 5, 0, DesktopSelector.Main).RuntimeId = new[] { 2 };
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 16), "query-node-cycle");
        });
        Case("provider-query-root-echo-refused", () =>
        {
            var model = new OwnedModel();
            model.CandidateResults = (root, selector, found) => new[] { root };
            Refused(() => model.Tree().Discover(), "query-node-cycle");
        });
        Case("duplicate-provider-candidate-refused", () =>
        {
            var model = new OwnedModel();
            model.CandidateResults = (root, selector, found) =>
                root == model.Main ? new[] { model.Wizard, model.Wizard } : found;
            Refused(() => model.Tree().Discover(), "query-node-cycle");
        });
        Case("raw-parent-cycle-refused", () =>
        {
            var model = new OwnedModel(); model.MainButton.Parent = model.MainButton;
            var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-parent-cycle");
        });
        foreach (int depth in new[] { 32, 33 })
            Case("raw-depth-" + depth, () =>
            {
                var model = new OwnedModel(); model.Main.Children.Clear(); var parent = model.Main;
                for (int i = 1; i < depth; i++) parent = model.Add(parent, 10 + i);
                model.Add(parent, 100, 0, DesktopSelector.Main);
                var tree = model.Tree(); tree.Discover();
                if (depth == 32) Verify(tree.Find(100, null, DesktopSelector.Main, 1).Count == 1);
                else Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-depth");
            });
        foreach (int windows in new[] { 8, 9 })
            Case("canonical-window-count-" + windows, () =>
            {
                var model = new OwnedModel();
                for (int i = 2; i < windows; i++) AddRoot(model, (uint)(300 + i), 10 + i, model.Main);
                var tree = model.Tree();
                if (windows == 8) { tree.Discover(); Verify(tree.Windows.Count == 8); }
                else Refused(() => tree.Discover(), "query-window-bound");
            });
        Case("nine-seeds-refused-before-node-reads", () =>
        {
            var model = new OwnedModel(); for (int i = 1; i < 9; i++) model.SeedNodes.Add(model.Main);
            Refused(() => model.Tree().Discover(), "query-seed-bound");
            Verify(model.Reads.Count == 2 && model.Reads[0] == "seeds:0" && model.Reads[1] == "seed-count:0");
        });
        Case("node-budget-exact-and-one-over", () =>
        {
            var budget = new DesktopTreeBudget(() => { });
            for (int i = 0; i < 4096; i++) budget.Visit(0);
            Verify(budget.Nodes == 4096);
            GuardRefused(() => budget.Visit(0), "query-node-bound");
            int reads = 0;
            GuardRefused(() => budget.Call(() => ++reads), "query-budget-closed"); Verify(reads == 0);
        });
        Case("irrelevant-wide-provider-tree-does-not-charge-client-nodes", () =>
        {
            var model = new OwnedModel();
            for (int i = 0; i < 1100; i++) model.Add(model.Main, 1000 + i);
            var tree = model.Tree(); tree.Discover();
            Verify(model.ProviderNodes > 1100 && tree.Budget.Nodes < 100);
        });
        Case("actual-candidate-ancestry-exhausts-unchanged-node-budget", () =>
        {
            var model = new OwnedModel(); var parent = model.Main;
            for (int i = 0; i < 20; i++) parent = model.Add(parent, 100 + i);
            for (int i = 0; i < 200; i++) model.Add(parent, 1000 + i).Control = DesktopQueryControl.Window;
            var failure = Refused(() => model.Tree().Discover(), "query-node-bound");
            Verify(failure.Nodes == 4096);
        });
        Case("call-budget-exact-and-one-over", () =>
        {
            var budget = new DesktopTreeBudget(() => { }); int reads = 0;
            for (int i = 0; i < 65536; i++) budget.Call(() => ++reads);
            GuardRefused(() => budget.Call(() => ++reads), "query-call-bound");
            Verify(reads == 65536 && budget.Calls == 65536);
        });
        Case("deadline-before-first-adapter-call", () =>
        {
            var model = new OwnedModel(); model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
            Refused(() => model.Tree().Discover(), "deadline"); Verify(model.Reads.Count == 0);
        });
        Case("cancellation-after-provider-call-no-followup", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) => model.GuardAction = () => { throw new DesktopTreeGuardException("cancelled"); };
            var failure = Refused(() => model.Tree().Discover(), "cancelled");
            Verify(model.Reads.Count == 1 && failure.AliveAfter == null);
        });
        Case("latched-failure-has-no-further-adapter-calls", () =>
        {
            var model = new OwnedModel(); model.MainButton.Pid = 8;
            model.CandidateResults = (root, selector, found) => new[] { model.MainButton };
            var tree = model.Tree();
            var failure = Refused(() => tree.Discover(), "query-uia-pid"); int reads = model.Reads.Count;
            Verify(ReferenceEquals(failure, Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid")));
            Verify(reads == model.Reads.Count);
        });
        Case("provider-message-never-leaks", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "candidates-Discovery" && node == model.Main) throw new Exception("PRIVATE NAME VALUE"); };
            var failure = Refused(() => model.Tree().Discover(), "query-provider-failure");
            Verify(failure.Stage == "loading-handoff" && failure.Selector == "Discovery" &&
                failure.Predicate == "query-provider-failure");
        });
        Case("stale-native-window-refused-not-skipped", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[100].Alive = false;
            Refused(() => tree.Validate(100, model.MainButton), "query-root-gone");
        });
        Case("reassigned-native-window-refused-not-skipped", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[100].Pid = 8;
            Refused(() => tree.Validate(100, model.MainButton), "query-root-pid");
        });
        Case("match-time-uia-reassignment-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "match-Main" && node == model.MainButton) node.Pid = 8; };
            var failure = Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid");
            Verify(failure.Selector == "Main" && failure.ExpectedOwnedRoot == 100);
        });
        Case("match-time-root-change-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "match-Main" && node == model.MainButton) node.Parent = model.Wizard; };
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-match-root-changed");
        });
        Case("root-owner-change-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Native[200].Owner = 0;
            Refused(() => tree.Validate(200, model.Close), "query-root-alias");
        });
        Case("root-change-during-canonical-provider-read-refused", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "from-handle") model.Native[100].Owner = model.Native[100].Owner == 0 ? 900u : 0u; };
            model.Native.Add(900, new OwnedNative { Root = 900 });
            Refused(() => model.Tree().Discover(), "query-root-changed");
        });
        Case("subtree-from-owned-other-root-is-not-owner-member", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Find(100, model.Close, DesktopSelector.Wizard, 1), "query-subtree-root");
        });
        Case("membership-from-owned-other-root-is-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Validate(100, model.Close), "control-native-root");
        });
        Case("read-revalidates-native-root-after-property", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            Refused(() => tree.Read(100, model.MainButton, DesktopSelector.Main,
                () => { model.MainButton.Parent = model.Wizard; return true; }), "query-read-root-changed");
        });
        Case("diagnostic-refresh-is-four-calls-at-most", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            var failure = Refused(() => tree.Validate(100, model.Close), "control-native-root");
            Verify(failure.PreviouslyOwnedHandle == 200 && failure.OwnedRootBefore == 200 &&
                failure.OwnedRootAfter == 200 && failure.AliveBefore == true && failure.AliveAfter == true &&
                failure.OwnPidBefore == true && failure.OwnPidAfter == true && failure.RootMatchesAfter == false);
            int lastParent = model.Reads.LastIndexOf("handle:3");
            Verify(lastParent >= 0 && model.Reads.Count - lastParent - 1 == 9);
        });
        Case("diagnostics-cannot-hide-original-refusal-when-budget-closes", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); bool mismatch = false;
            model.Before = (name, node) =>
            {
                if (name == "handle" && node == model.Wizard) mismatch = true;
                if (mismatch && name == "alive" && model.Reads.Count >= 1)
                {
                    int aliveCount = 0;
                    for (int i = model.Reads.LastIndexOf("handle:3") + 1; i < model.Reads.Count; i++)
                        if (model.Reads[i] == "alive:0") aliveCount++;
                    if (aliveCount == 3) model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
                }
            };
            var failure = Refused(() => tree.Validate(100, model.Close), "control-native-root");
            Verify(failure.Predicate == "control-native-root" && failure.OwnedRootBefore == 200);
        });
        Case("hwnd-low32-sign-extension-equivalence", () =>
        {
            const uint high = 0x80000001;
            Verify(DesktopHwnd.Key(unchecked((int)high)) == high && DesktopHwnd.Key(high) == high);
            Verify(DesktopHwnd.Key(DesktopHwnd.Pointer(high).ToInt64()) == high);
            Verify(DesktopHwnd.Same(new IntPtr(unchecked((int)high)), DesktopHwnd.Pointer(high)));
        });
        Case("hwnd-distinct-low-bits-remain-distinct", () =>
            Verify(DesktopHwnd.Key(0x180000001) != DesktopHwnd.Key(0x180000002) && DesktopHwnd.Key(0) == 0));
        Case("high-bit-owned-window-traversal", () =>
        {
            var model = new OwnedModel(); const uint high = 0x80000001;
            model.Native[high] = model.Native[100]; model.Native.Remove(100); model.Native[high].Root = high;
            model.Main.Handle = unchecked((int)high); model.Roots.Remove(100); model.Roots.Add(high, model.Main);
            model.Native[200].Owner = high;
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(high, null, DesktopSelector.Main, 1)[0] == model.MainButton);
        });
        Case("high-bit-equivalence-never-bypasses-native-pid", () =>
        {
            var model = new OwnedModel(); const uint high = 0x80000001;
            model.MainButton.Handle = unchecked((int)high);
            model.Native.Add(high, new OwnedNative { Root = 100, Pid = 8 });
            Refused(() => model.Tree().Discover(), "query-native-pid");
        });
        Case("query-discovered-root-is-drained-before-refusal", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); bool added = false;
            model.Before = (name, node) =>
            {
                if (!added && name == "match-Main" && node == model.MainButton)
                {
                    added = true; var extra = AddRoot(model, 300, 5, model.Main);
                    model.Add(extra, 6, 0, DesktopSelector.ConfirmationYes);
                }
            };
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-topology-changed");
            Verify(tree.Windows.Count == 3 && model.Reads.Contains("candidates-Discovery:5"));
        });
        Case("discovered-unknown-root-blocks-startup-projection", () =>
        {
            var model = new OwnedModel(); AddRoot(model, 300, 5, model.Main);
            var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            for (int i = 0; i < tree.Windows.Count; i++)
            {
                var root = tree.Windows[i];
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                    tree.Find(root.Handle, null, DesktopSelector.Main, 1).Count == 1, false,
                    tree.Find(root.Handle, null, DesktopSelector.Wizard, 1).Count == 1));
            }
            Verify(roots.Count == 3 && !new DesktopStartupObservation().Observe(1, roots, false).Ready);
        });
        Case("discovered-native-picker-still-refused-at-startup", () =>
        {
            var model = new OwnedModel(); var picker = AddRoot(model, 300, 5, model.Main, 100, "#32770");
            var tree = model.Tree(); tree.Discover();
            bool refused = false;
            try
            {
                new DesktopStartupObservation().Observe(1, new[] {
                    new DesktopStartupRoot(100, 0, model.Native[100].Class, true, true, false, false),
                    new DesktopStartupRoot(300, 100, model.Native[300].Class, true, false, false, false) }, false);
            }
            catch (InvalidOperationException ex) when (ex.Message == "startup-root-class") { refused = true; }
            Verify(refused && tree.Windows.Count == 3 && picker.Handle == 300);
        });
        Case("actual-traversal-wizard-selection-drives-startup", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            var roots = new List<DesktopStartupRoot>();
            foreach (var root in tree.Windows)
                roots.Add(new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                    tree.Find(root.Handle, null, DesktopSelector.Main, 1).Count == 1, false,
                    tree.Find(root.Handle, null, DesktopSelector.Wizard, 1).Count == 1));
            var decision = new DesktopStartupObservation().Observe(1, roots, false);
            Verify(decision.Ready && decision.MainHandle == 100 && decision.WizardHandle == 200);
        });
        Case("cross-native-window-cycle-is-not-consistent-rediscovery", () =>
        {
            var model = new OwnedModel(); model.Wizard.Children.Add(model.Main);
            Refused(() => model.Tree().Discover(), "query-window-cycle");
        });
        Case("unavailable-provider-is-refused-without-retry", () =>
        {
            var model = new OwnedModel(); model.Before = (name, node) =>
            { if (name == "candidates-Discovery" && node == model.Main) throw new ElementNotAvailableException(); };
            var tree = model.Tree(); Refused(() => tree.Discover(), "query-element-unavailable");
            int reads = model.Reads.Count;
            Refused(() => tree.Discover(), "query-element-unavailable"); Verify(reads == model.Reads.Count);
        });
        Case("missing-canonical-uia-root-refused", () =>
        {
            var model = new OwnedModel(); model.Roots.Remove(200);
            Refused(() => model.Tree().Discover(), "query-root-uia-pid");
        });
        Case("window-key-checks-pid-before-native-handle", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Main.Pid = 8; model.Reads.Clear();
            Refused(() => tree.WindowKey(model.Main, DesktopSelector.Main), "query-window-uia-pid");
            Verify(model.Reads.Count == 1 && model.Reads[0] == "pid:1");
        });
        Case("unsupported-selector-refused-before-matching", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            Refused(() => tree.Find(100, null, (DesktopSelector)999, 1), "query-selector");
            Verify(model.Reads.Count == 0);
        });
        Case("owned-pid-must-be-positive", () =>
        {
            var model = new OwnedModel();
            var tree = new DesktopOwnedTree<OwnedNode>(model, 0, null, "loading-handoff", () => { });
            Refused(() => tree.Discover(), "query-owned-pid"); Verify(model.Reads.Count == 0);
        });
        foreach (int depth in new[] { 32, 33 })
            Case("raw-parent-depth-" + depth, () =>
            {
                var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
                var parent = model.Main;
                for (int i = 1; i < depth; i++) parent = model.Add(parent, 100 + i);
                model.MainButton.Parent = parent;
                if (depth == 32) tree.Validate(100, model.MainButton);
                else Refused(() => tree.Validate(100, model.MainButton), "query-depth");
            });
        foreach (int owners in new[] { 8, 9 })
            Case("native-owner-chain-" + owners, () =>
            {
                var model = new OwnedModel(); model.Native[200].Owner = 900;
                for (int i = 0; i < owners; i++)
                    model.Native.Add((uint)(900 + i), new OwnedNative { Root = (uint)(900 + i),
                        Owner = i == owners - 1 ? 0 : (uint)(901 + i) });
                if (owners == 8) model.Tree().Discover();
                else Refused(() => model.Tree().Discover(), "query-owner-bound");
            });
        Case("consistent-rediscovery-preserves-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            tree.Find(100, null, DesktopSelector.Main, 1); tree.CheckRevision(revision);
        });
        Case("new-observed-root-invalidates-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            AddRoot(model, 300, 5, model.Main);
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-topology-changed");
            Refused(() => tree.CheckRevision(revision), "query-topology-changed");
        });
        Case("observed-visibility-change-invalidates-acceptance-revision", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover(); int revision = tree.Revision;
            model.Native[200].Visible = false; Verify(!tree.Visible(tree.Windows[1]));
            Refused(() => tree.CheckRevision(revision), "query-topology-changed");
        });
        Case("foreign-property-node-never-invokes-content-callback", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.MainButton.Pid = 8; int reads = 0;
            Refused(() => tree.Read(100, model.MainButton, DesktopSelector.Main, () => ++reads), "query-uia-pid");
            Verify(reads == 0);
        });
        int total = count + RunProjectionTests() + RunCandidateTests();
        if (ownedTreeCaseFailures.Count != 0)
            throw new InvalidOperationException("Owned-tree cases failed: " + string.Join("; ", ownedTreeCaseFailures));
        return total;
    }

    static DesktopStartupSample<OwnedNode> ProjectStartup(OwnedModel model)
    {
        var tree = model.Tree();
        tree.Discover();
        return ProjectStartup(model, tree);
    }

    static DesktopStartupSample<OwnedNode> ProjectStartup(OwnedModel model, DesktopOwnedTree<OwnedNode> tree)
    {
        return DesktopStartupSample<OwnedNode>.Capture(tree, root =>
        {
            bool Control(DesktopSelector selector, DesktopQueryControl type)
            {
                var found = tree.Find(root.Handle, null, selector, 1);
                if (found.Count == 0) return false;
                var node = found[0];
                var actualType = tree.Read(root.Handle, node, selector, () =>
                {
                    model.Reads.Add("content-type:" + node.Id);
                    return node.Control;
                });
                if (actualType != type) throw new InvalidOperationException("control-type");
                return !tree.Read(root.Handle, node, selector, () =>
                {
                    model.Reads.Add("content-offscreen:" + node.Id);
                    return node.Offscreen;
                });
            }
            return new DesktopStartupRoot(root.Handle, root.Owner, root.Class, tree.Visible(root),
                Control(DesktopSelector.Main, DesktopQueryControl.Button),
                Control(DesktopSelector.Loading, DesktopQueryControl.Text),
                Control(DesktopSelector.Wizard, DesktopQueryControl.Button));
        });
    }

    static int RunCandidateTests()
    {
        int count = 0;
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected candidate-query result.");
        }
        void Case(string name, Action action)
        {
            if (ownedTreeCaseNames.Contains(name)) throw new InvalidOperationException("Duplicate candidate case.");
            try { action(); }
            catch (Exception ex) { ownedTreeCaseFailures.Add(name + ": " + ex.Message); }
            ownedTreeCaseNames.Add(name); count++;
        }
        DesktopQueryFailure Refused(Action action, string code)
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == code) { return ex.Failure; }
            throw new InvalidOperationException("Expected candidate refusal: " + code);
        }
        OwnedNode ExtraRoot(OwnedModel model, bool visible = true)
        {
            var node = model.Add(model.Main, 500, 300);
            model.Native.Add(300, new OwnedNative { Root = 300, Owner = 100, Visible = visible });
            model.Roots.Add(300, node);
            return node;
        }
        for (int value = (int)DesktopSelector.Main; value <= (int)DesktopSelector.ConfirmationMessage; value++)
        {
            var selector = (DesktopSelector)value;
            Case("compiled-selector-subtree-" + selector, () =>
            {
                var model = new OwnedModel();
                var host = model.Add(model.Main, 5);
                var target = model.Add(host, 6, 0, selector);
                model.Add(model.Main, 7, 0, selector);
                var tree = model.Tree(); tree.Discover();
                var found = tree.Find(100, host, selector, 1);
                Verify(found.Count == 1 && found[0] == target && model.CandidateQueries > 0 && model.ProviderNodes > 0);
            });
        }
        Case("compiled-window-predicate-with-default-zero-handle", () =>
        {
            var model = new OwnedModel();
            var windowPeer = model.Add(model.Main, 5); windowPeer.Control = DesktopQueryControl.Window;
            var irrelevant = model.Add(model.Main, 6);
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && model.Reads.Contains("pid:5") && !model.Reads.Contains("pid:6"));
        });
        Case("compiled-native-child-predicate-does-not-create-window", () =>
        {
            var model = new OwnedModel(); model.MainButton.Handle = 150;
            model.MainButton.Control = DesktopQueryControl.Button;
            model.Native.Add(150, new OwnedNative { Root = 100, Class = "Button" });
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 2 && model.Reads.Contains("handle:2"));
        });
        Case("id-filter-preserves-wrong-type-candidate", () =>
        {
            var model = new OwnedModel(); model.MainButton.Control = DesktopQueryControl.Text;
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
            bool refused = false;
            try { ProjectStartup(model); }
            catch (InvalidOperationException ex) when (ex.Message == "control-type") { refused = true; }
            Verify(refused);
        });
        Case("provider-pid-filter-omits-irrelevant-foreign-nodes", () =>
        {
            var model = new OwnedModel(); var foreign = model.Add(model.Main, 5, 0, DesktopSelector.Main);
            foreign.Pid = 8;
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Find(100, null, DesktopSelector.Main, 1)[0] == model.MainButton);
            Verify(!model.Reads.Contains("pid:5") && !model.Reads.Contains("property-Name:5"));
        });
        Case("null-provider-candidate-collection-refused", () =>
        {
            var model = new OwnedModel(); model.CandidateResults = (root, selector, found) => null;
            Refused(() => model.Tree().Discover(), "query-candidate-list");
        });
        Case("null-provider-candidate-refused", () =>
        {
            var model = new OwnedModel(); model.CandidateResults = (root, selector, found) => new OwnedNode[] { null };
            Refused(() => model.Tree().Discover(), "query-candidate-null");
        });
        Case("257-provider-candidates-refused-before-items", () =>
        {
            var model = new OwnedModel();
            model.CandidateResults = (root, selector, found) => new OwnedNode[257];
            Refused(() => model.Tree().Discover(), "query-candidate-bound");
            Verify(!model.Reads.Exists(x => x.StartsWith("candidate-item:", StringComparison.Ordinal)));
        });
        Case("256-provider-candidates-accepted-within-client-budget", () =>
        {
            var model = new OwnedModel(); model.Main.Children.Clear();
            for (int i = 0; i < 256; i++) model.Add(model.Main, 1000 + i).Control = DesktopQueryControl.Window;
            var tree = model.Tree(); tree.Discover();
            Verify(tree.Windows.Count == 1 && tree.Budget.Nodes < 4096 && tree.Budget.Calls < 65536);
        });
        Case("candidate-count-respects-remaining-node-budget", () =>
        {
            var budget = new DesktopTreeBudget(() => { });
            for (int i = 0; i < 4090; i++) budget.Visit(0);
            int reads = 0; bool refused = false;
            try { DesktopCandidateQuery.Collect<object>(budget, () => 7, index => { reads++; return new object(); }); }
            catch (DesktopTreeGuardException ex) when (ex.Message == "query-node-bound") { refused = true; }
            Verify(refused && reads == 0 && budget.Nodes == 4090);
        });
        Case("negative-candidate-count-refused-before-items", () =>
        {
            var budget = new DesktopTreeBudget(() => { }); int reads = 0; bool refused = false;
            try { DesktopCandidateQuery.Collect<object>(budget, () => -1, index => { reads++; return new object(); }); }
            catch (DesktopTreeGuardException ex) when (ex.Message == "query-candidate-bound") { refused = true; }
            Verify(refused && reads == 0);
        });
        Case("same-root-out-of-subtree-candidate-refused-before-match", () =>
        {
            var model = new OwnedModel(); var host = model.Add(model.Main, 5);
            var tree = model.Tree(); tree.Discover(); model.Reads.Clear();
            model.CandidateResults = (root, selector, found) =>
                selector == DesktopSelector.Main ? new[] { model.MainButton } : found;
            Refused(() => tree.Find(100, host, DesktopSelector.Main, 1), "query-candidate-subtree");
            Verify(!model.Reads.Contains("match-Main:2"));
        });
        Case("subtree-reparent-during-match-refused", () =>
        {
            var model = new OwnedModel(); var host = model.Add(model.Main, 5);
            var target = model.Add(host, 6, 0, DesktopSelector.Main);
            var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "match-Main" && node == target) node.Parent = model.Main; };
            Refused(() => tree.Find(100, host, DesktopSelector.Main, 1), "query-candidate-subtree");
        });
        Case("candidate-filter-lie-refused-not-missing", () =>
        {
            var model = new OwnedModel(); var unrelated = model.Add(model.Main, 5);
            var tree = model.Tree(); tree.Discover();
            model.CandidateResults = (root, selector, found) =>
                selector == DesktopSelector.Main ? new[] { unrelated } : found;
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-candidate-filter");
        });
        Case("candidate-runtime-alias-during-match-refused", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) =>
            {
                if (name == "match-Main" && node == model.MainButton) node.RuntimeId = new[] { 999 };
            };
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-node-alias");
        });
        Case("candidate-item-pid-change-refused-before-content", () =>
        {
            var model = new OwnedModel(); var tree = model.Tree(); tree.Discover();
            model.Before = (name, node) => { if (name == "candidate-item" && node == model.MainButton) node.Pid = 8; };
            Refused(() => tree.Find(100, null, DesktopSelector.Main, 1), "query-uia-pid");
            Verify(!model.Reads.Contains("match-Main:2"));
        });
        Case("candidate-rpc-deadline-closes-before-collection-access", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) =>
            {
                if (name == "candidates-Discovery")
                    model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
            };
            Refused(() => model.Tree().Discover(), "deadline");
            Verify(model.CandidateQueries == 1 && !model.Reads.Exists(x => x.StartsWith("candidate-count:", StringComparison.Ordinal)));
        });
        Case("candidate-item-deadline-closes-before-membership", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) =>
            {
                if (name == "candidate-item")
                    model.GuardAction = () => { throw new DesktopTreeGuardException("deadline"); };
            };
            Refused(() => model.Tree().Discover(), "deadline");
            Verify(!model.Reads.Contains("pid:3"));
        });
        Case("candidate-item-provider-failure-is-not-skipped", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) => { if (name == "candidate-item") throw new ElementNotAvailableException(); };
            Refused(() => model.Tree().Discover(), "query-element-unavailable");
        });
        Case("root-relations-refresh-does-not-cache-old-ancestry", () =>
        {
            var model = new OwnedModel(); model.Native[200].Owner = 0;
            var tree = model.Tree(); tree.Discover();
            model.Main.Children.Remove(model.Wizard);
            model.Wizard.Children.Add(model.Main);
            model.Main.Parent = model.Wizard; model.Wizard.Parent = null;
            model.SeedNodes.Clear(); model.SeedNodes.Add(model.Wizard);
            tree.RefreshCatalog();
            Verify(tree.Find(200, null, DesktopSelector.Wizard, 1)[0] == model.Close);
        });
        foreach (bool visible in new[] { false, true })
            Case("unmatched-late-root-invalidates-final-acceptance-" + visible, () =>
            {
                var model = new OwnedModel(); var sample = ProjectStartup(model);
                Verify(sample.Observe(new DesktopStartupObservation(), 10, false, false).Ready);
                ExtraRoot(model, visible);
                Refused(() => sample.RefreshAndCheckRevision(), "query-topology-changed");
            });
        foreach (int depth in new[] { 0, 24, 31 })
            Case("full-graph-scale-depth-" + depth, () =>
                ownedTreeScaleProfiles.Add(ScaleProfile(depth, depth == 31 ? 20 : 0, depth == 31)));
        return count;
    }

    static int RunProjectionTests()
    {
        int count = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Verify(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Unexpected projection result.");
        }
        void Case(string name, Action action)
        {
            if (!names.Add(name)) throw new InvalidOperationException("Duplicate projection case.");
            try { action(); }
            catch (Exception ex) { ownedTreeCaseFailures.Add(name + ": " + ex.Message); }
            ownedTreeCaseNames.Add(name);
            count++;
        }
        void Refused(Action action, string code = "query-topology-changed")
        {
            try { action(); }
            catch (DesktopTreeException ex) when (ex.Message == code) { return; }
            throw new InvalidOperationException("Expected projection refusal: " + code);
        }
        OwnedModel Unknown(bool visible = false, bool first = true)
        {
            var model = new OwnedModel();
            var unknown = model.Add(model.Main, 5, 300);
            model.Native.Add(300, new OwnedNative { Root = 300, Owner = 100, Visible = visible });
            model.Roots.Add(300, unknown);
            if (first) model.SeedNodes.Insert(0, unknown);
            else model.SeedNodes.Add(unknown);
            return model;
        }
        void ChangeDuringMainQuery(OwnedModel model, bool visible)
        {
            model.Before = (name, node) =>
            {
                if (name == "match-Main" && node == model.MainButton) model.Native[300].Visible = visible;
            };
        }
        Case("initial-projection-cannot-omit-earlier-hidden-root", () =>
        {
            var model = Unknown(); ChangeDuringMainQuery(model, true);
            Refused(() => ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("acceptance-refresh-cannot-omit-earlier-hidden-root", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            Verify(ProjectStartup(model).Observe(observation, 10, false, false).Ready);
            ChangeDuringMainQuery(model, true);
            Refused(() =>
            {
                var refresh = ProjectStartup(model);
                var decision = refresh.Observe(observation, 11, false, true);
                refresh.CheckRevision();
                Verify(!decision.Ready);
            });
        });
        Case("later-hidden-root-change-also-refuses", () =>
        {
            var model = Unknown(first: false); ChangeDuringMainQuery(model, true);
            Refused(() => ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("already-projected-root-visibility-change-refuses", () =>
        {
            var model = Unknown(visible: true); ChangeDuringMainQuery(model, false);
            Refused(() => ProjectStartup(model));
        });
        Case("new-root-during-projection-refuses-instead-of-rebasing", () =>
        {
            var model = new OwnedModel(); bool added = false;
            model.Before = (name, node) =>
            {
                if (!added && name == "match-Main" && node == model.MainButton)
                {
                    added = true;
                    var unknown = model.Add(model.Main, 5, 300);
                    model.Native.Add(300, new OwnedNative { Root = 300, Owner = 100 });
                    model.Roots.Add(300, unknown);
                }
            };
            Refused(() => ProjectStartup(model));
        });
        Case("pre-capture-refresh-includes-newly-visible-root", () =>
        {
            var model = Unknown(); var tree = model.Tree(); tree.Discover();
            model.Native[300].Visible = true;
            var sample = DesktopStartupSample<OwnedNode>.Capture(tree, root =>
                new DesktopStartupRoot(root.Handle, root.Owner, root.Class, true, false, false, false));
            Verify(sample.Roots.Count == 3 && !sample.Observe(new DesktopStartupObservation(), 10, false, false).Ready);
        });
        Case("observed-change-between-capture-and-decision-refuses", () =>
        {
            var model = Unknown(); var sample = ProjectStartup(model); int revision = sample.Revision;
            model.Native[300].Visible = true;
            Refused(() => sample.Tree.Find(100, null, DesktopSelector.Main, 1));
            Verify(sample.Revision == revision && sample.Tree.Revision != revision);
            Refused(() => sample.Observe(new DesktopStartupObservation(), 10, false, false));
        });
        Case("observed-change-after-refresh-decision-refuses-final-acceptance", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            Verify(ProjectStartup(model).Observe(observation, 10, false, false).Ready);
            var refresh = ProjectStartup(model);
            Verify(refresh.Observe(observation, 11, false, true).Ready);
            model.Native[300].Visible = true;
            Refused(() => refresh.RefreshAndCheckRevision());
        });
        Case("stable-hidden-root-keeps-honest-candidate-and-refresh", () =>
        {
            var model = Unknown(); var observation = new DesktopStartupObservation();
            var sample = ProjectStartup(model);
            Verify(sample.Windows.Count == 2 && sample.Roots.Count == 2);
            Verify(sample.Observe(observation, 10, false, false).Route == DesktopStartupObservation.UnobservedRoute);
            var refresh = ProjectStartup(model);
            Verify(refresh.Observe(observation, 11, false, true).Ready); refresh.CheckRevision();
        });
        Case("stable-visible-unknown-root-is-present-and-blocks", () =>
        {
            var sample = ProjectStartup(Unknown(visible: true));
            Verify(sample.Windows.Count == 3 && sample.Roots.Count == 3);
            Verify(sample.Roots[0].Handle == 300 &&
                !sample.Observe(new DesktopStartupObservation(), 10, false, false).Ready);
        });
        Case("stable-main-only-projection-remains-valid", () =>
        {
            var model = new OwnedModel(); model.Native[200].Visible = false;
            var sample = ProjectStartup(model);
            var decision = sample.Observe(new DesktopStartupObservation(), 10, false, false);
            Verify(sample.Roots.Count == 1 && decision.Ready && decision.WizardHandle == 0);
        });
        Case("stable-observed-loading-route-remains-strict", () =>
        {
            var model = Unknown(visible: true);
            model.Add(model.Roots[300], 6, 0, DesktopSelector.Loading);
            var observation = new DesktopStartupObservation();
            Verify(!ProjectStartup(model).Observe(observation, 10, false, false).Ready && observation.LoadingObserved);
            model.Main.Children.Remove(model.Roots[300]); model.SeedNodes.Remove(model.Roots[300]);
            model.Native[300].Alive = false;
            var sample = ProjectStartup(model);
            Verify(sample.Observe(observation, 20, false, false).Route == DesktopStartupObservation.ObservedRoute);
            Verify(ProjectStartup(model).Observe(observation, 21, false, true).Ready);
        });
        Case("stable-missing-main-control-does-not-become-ready", () =>
        {
            var model = new OwnedModel(); model.MainButton.AutomationId = "";
            Verify(!ProjectStartup(model).Observe(new DesktopStartupObservation(), 10, false, false).Ready);
        });
        Case("projection-preserves-specific-ownership-refusal", () =>
        {
            var model = new OwnedModel();
            model.Before = (name, node) =>
            {
                if (name == "match-Main" && node == model.MainButton) model.MainButton.Pid = 8;
            };
            Refused(() => ProjectStartup(model), "query-uia-pid");
        });
        return count;
    }

    public static int RunStartupTests()
    {
        startupCaseNames.Clear();
        startupCaseFailures.Clear();
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
            startupCaseNames.Add(name);
            try { action(); }
            catch (Exception ex) { startupCaseFailures.Add(name + ": " + ex.Message); }
            count++;
        }
        DesktopStartupObservation Observed()
        {
            var state = new DesktopStartupObservation();
            Verify(!state.Observe(5, new[] { Loading() }, true).Ready);
            Verify(state.LoadingObserved && state.LoadingAt == 5 && state.LoadingHandle == 100 &&
                state.LoadingWindowClass == firstClass && state.WindowClass == null);
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
                    "startup-wizard-owner"));
        Case("wizard-distinct-class-valid", () =>
            Verify(new DesktopStartupObservation().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false).Ready));
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
        Case("observed-loading-to-distinct-main-class-valid", () =>
            Verify(Observed().Observe(10, new[] { Root(windowClass: otherClass) }, false).Ready));
        Case("observed-owned-wizard-distinct-class-valid", () =>
            Verify(Observed().Observe(10,
                new[] { Root(), Root(300, main: false, wizard: true, owner: 200, windowClass: otherClass) }, false).Ready));
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
                "startup-wizard-owner");
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
                        reject = () => state.Observe(10,
                            new[] { Root(100, main: false, loading: true, windowClass: otherClass) }, true);
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
        if (startupCaseFailures.Count != 0)
            throw new InvalidOperationException("Startup cases failed: " + string.Join("; ", startupCaseFailures));
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
                (session > 0 && state == 0 && interactive && input));

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
        Check(DesktopPolicy.FilenameHost(200, 300, 42, 42, 200, 300));
        Check(!DesktopPolicy.FilenameHost(200, 0, 42, 42, 200, 0));
        Check(!DesktopPolicy.FilenameHost(200, 300, 42, 43, 200, 300));
        Check(!DesktopPolicy.FilenameHost(200, 300, 42, 42, 201, 300));
        Check(!DesktopPolicy.FilenameHost(200, 300, 42, 42, 200, 301));
        Check(!DesktopPolicy.FilenameHost(200, 200, 42, 42, 200, 200));
        Check(DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(false, 5, 10, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 10, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 5, 100, 200, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 100, true, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, false, true));
        Check(!DesktopPolicy.Handoff(true, 5, 10, 100, 200, true, false));

        var confirmationTransition = new DesktopQueryFailure
        {
            Stage = "valid-confirmation-import",
            Selector = DesktopSelector.ConfirmationYes.ToString(),
            Predicate = "query-topology-changed",
            ExpectedOwnedRoot = 300,
            PreviouslyOwnedHandle = 100,
            OwnedRootBefore = 100,
            OwnedRootAfter = 100,
            AliveBefore = true,
            AliveAfter = true,
            OwnPidBefore = true,
            OwnPidAfter = true,
            RootMatchesBefore = false,
            RootMatchesAfter = false,
            Windows = 3,
        };
        Check(DesktopPolicy.RetryConfirmationTopology(confirmationTransition, 0));
        Check(!DesktopPolicy.RetryConfirmationTopology(confirmationTransition, 1));
        confirmationTransition.Stage = "invalid-rejection-preservation";
        Check(!DesktopPolicy.RetryConfirmationTopology(confirmationTransition, 0));
        confirmationTransition.Stage = "valid-confirmation-import";
        confirmationTransition.Selector = DesktopSelector.ConfirmationMessage.ToString();
        Check(!DesktopPolicy.RetryConfirmationTopology(confirmationTransition, 0));
        confirmationTransition.Selector = DesktopSelector.ConfirmationYes.ToString();
        confirmationTransition.OwnPidAfter = false;
        Check(!DesktopPolicy.RetryConfirmationTopology(confirmationTransition, 0));

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
            "CONIN$", "CONOUT$", "NUL .txt", "COM¹.txt", "COM²", "COM³.any",
            "LPT¹.txt", "LPT²", "LPT³.any" })
            Check(!DesktopPolicy.RelativeFile(bad));
        return cases;
    }
}
