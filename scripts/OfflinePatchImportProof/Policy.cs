using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class DesktopPolicy
{
    public static bool Process(int pid, long ticks, string exe, int actualPid, long actualTicks,
        string actualExe, bool exited)
    {
        return !exited && pid > 0 && ticks > 0 && !string.IsNullOrEmpty(exe) &&
            pid == actualPid && ticks == actualTicks && string.Equals(exe, actualExe, StringComparison.Ordinal);
    }

    public static bool AvaloniaClass(string name)
    {
        Guid parsed;
        return name != null && name.StartsWith("Avalonia-", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.Substring(9), "D", out parsed);
    }

    public static bool Window(int pid, int nativePid, int uiaPid, long handle, long root,
        string nativeClass, string expectedClass, bool visible, bool ownerChain)
    {
        return pid > 0 && pid == nativePid && pid == uiaPid && handle != 0 && handle == root &&
            !string.IsNullOrEmpty(expectedClass) && nativeClass == expectedClass && visible && ownerChain;
    }

    public static bool Picker(long expectedRoot, long editRoot, long buttonRoot,
        bool ownedChain, bool foreground, bool enabled, string editClass, string buttonClass, int buttonId)
    {
        return expectedRoot != 0 && editRoot == expectedRoot && buttonRoot == expectedRoot &&
            ownedChain && foreground && enabled && editClass == "Edit" && buttonClass == "Button" && buttonId == 1;
    }

    public static bool Handoff(bool observedLoading, long loadingAt, long mainAt,
        long loading, long main, bool loadingGone, bool mainReady)
    {
        return observedLoading && loadingAt >= 0 && mainAt > loadingAt && loading != 0 &&
            main != 0 && main != loading && loadingGone && mainReady;
    }

    // ROM, valid ZIP, invalid ZIP, installed database; row is a separate UI observation.
    public static bool Preserved(string[] before, string[] after, string rowBefore, string rowAfter, bool rejected)
    {
        if (!rejected || before == null || after == null || before.Length != 4 || after.Length != 4 ||
            string.IsNullOrEmpty(rowBefore) || rowBefore != rowAfter) return false;
        for (int i = 0; i < before.Length; i++)
            if (!Sha256(before[i]) || before[i] != after[i]) return false;
        return true;
    }

    public static bool Sha256(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$");
    public static bool Expired(long elapsed, long limit) => elapsed < 0 || limit <= 0 || elapsed >= limit;

    public static bool NormalClose(bool requested, bool editorWasOpen, bool mainGone, bool editorGone,
        bool exited, int exitCode, bool killed, bool timedOut)
    {
        return requested && editorWasOpen && mainGone && editorGone && exited && exitCode == 0 && !killed && !timedOut;
    }

    public static bool MayCleanupApp(bool executionBoundaryExited, bool retainedIdentity, bool killAttempted)
        => executionBoundaryExited && retainedIdentity && !killAttempted;

    public static bool RelativeFile(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 220 || value.IndexOfAny(new[] { '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            return false;
        foreach (string part in value.Split('\\'))
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" "))
                return false;
            foreach (char c in part) if (c < 32) return false;
            string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|CONIN\\$|CONOUT\\$|COM[0-9]|LPT[0-9])$")) return false;
        }
        return true;
    }
}

internal static class DesktopHwnd
{
    internal static uint Key(long value) => unchecked((uint)value);
    internal static IntPtr Pointer(uint value) => new IntPtr(unchecked((int)value));
    internal static bool Same(IntPtr first, IntPtr second) => Key(first.ToInt64()) == Key(second.ToInt64());
}

internal enum DesktopSelector
{
    Discovery, Membership, Main, Wizard, Loading, Import, Status, List,
    FilenameHost, FilenameEdit, PickerOpen, Row, RowName, ConfirmationYes, ConfirmationMessage
}

public sealed class DesktopQueryFailure
{
    public string Stage { get; set; }
    public string Selector { get; set; }
    public string Predicate { get; set; }
    public long ExpectedOwnedRoot { get; set; }
    public long PreviouslyOwnedHandle { get; set; }
    public long OwnedRootBefore { get; set; }
    public long OwnedRootAfter { get; set; }
    public bool? AliveBefore { get; set; }
    public bool? AliveAfter { get; set; }
    public bool? OwnPidBefore { get; set; }
    public bool? OwnPidAfter { get; set; }
    public bool? RootMatchesBefore { get; set; }
    public bool? RootMatchesAfter { get; set; }
    public int Nodes { get; set; }
    public int Calls { get; set; }
    public int Windows { get; set; }
}

internal sealed class DesktopTreeGuardException : Exception
{
    internal DesktopTreeGuardException(string code) : base(code) { }
}

internal sealed class DesktopTreeException : InvalidOperationException
{
    internal DesktopQueryFailure Failure { get; }
    internal DesktopTreeException(DesktopQueryFailure failure) : base(failure.Predicate) { Failure = failure; }
}

internal sealed class DesktopTreeBudget
{
    readonly Action guard;
    internal int Nodes { get; private set; }
    internal int Calls { get; private set; }
    internal bool Closed { get; private set; }
    internal DesktopTreeBudget(Action guard) { this.guard = guard; }
    internal void Check()
    {
        if (Closed) throw new DesktopTreeGuardException("query-budget-closed");
        try { guard(); }
        catch { Closed = true; throw; }
    }
    internal void Close() { Closed = true; }
    internal void Visit(int depth)
    {
        Check();
        string error = depth < 0 || depth > 32 ? "query-depth" : Nodes >= 4096 ? "query-node-bound" : null;
        if (error != null) { Closed = true; throw new DesktopTreeGuardException(error); }
        Nodes++;
    }
    internal TValue Call<TValue>(Func<TValue> read)
    {
        Check();
        if (Calls >= 65536) { Closed = true; throw new DesktopTreeGuardException("query-call-bound"); }
        Calls++;
        TValue value = read();
        Check();
        return value;
    }
}

internal interface IDesktopOwnedTreeAdapter<TNode> where TNode : class
{
    DesktopTreeBudget Budget { set; }
    IReadOnlyList<TNode> Seeds();
    int ProcessId(TNode node);
    int[] Identity(TNode node);
    uint Handle(TNode node);
    TNode Parent(TNode node);
    TNode FirstChild(TNode node);
    TNode NextSibling(TNode node);
    TNode FromHandle(uint handle);
    bool Alive(uint handle);
    int NativePid(uint handle);
    uint NativeRoot(uint handle);
    uint Owner(uint handle);
    string Class(uint handle);
    bool Visible(uint handle);
    bool Offscreen(TNode node);
    bool Matches(TNode node, DesktopSelector selector);
}

internal sealed class DesktopOwnedWindow<TNode> where TNode : class
{
    internal uint Handle;
    internal uint Owner;
    internal string Class;
    internal string Identity;
    internal TNode Element;
    internal bool Visible;
}

internal sealed class DesktopOwnedTree<TNode> where TNode : class
{
    readonly IDesktopOwnedTreeAdapter<TNode> adapter;
    readonly int pid;
    readonly string expectedClass, stage;
    readonly Dictionary<uint, DesktopOwnedWindow<TNode>> roots = new Dictionary<uint, DesktopOwnedWindow<TNode>>();
    readonly List<DesktopOwnedWindow<TNode>> windows = new List<DesktopOwnedWindow<TNode>>();
    readonly Queue<DesktopOwnedWindow<TNode>> pending = new Queue<DesktopOwnedWindow<TNode>>();
    readonly Dictionary<uint, HashSet<uint>> edges = new Dictionary<uint, HashSet<uint>>();
    DesktopQueryFailure context, failure;
    internal DesktopTreeBudget Budget { get; }
    internal IReadOnlyList<DesktopOwnedWindow<TNode>> Windows => windows;
    internal int Revision { get; private set; }

    internal DesktopOwnedTree(IDesktopOwnedTreeAdapter<TNode> adapter, int pid, string expectedClass,
        string stage, Action guard)
    {
        this.adapter = adapter;
        this.pid = pid;
        this.expectedClass = expectedClass;
        this.stage = stage;
        Budget = new DesktopTreeBudget(guard);
        adapter.Budget = Budget;
    }

    void Begin(DesktopSelector selector, uint expected = 0)
    {
        if (failure != null) throw new DesktopTreeException(failure);
        context = new DesktopQueryFailure { Stage = stage, Selector = selector.ToString(),
            ExpectedOwnedRoot = roots.ContainsKey(expected) ? expected : 0 };
    }

    void NodeContext(uint expected)
    {
        string selector = context.Selector;
        context = new DesktopQueryFailure { Stage = stage, Selector = selector,
            ExpectedOwnedRoot = roots.ContainsKey(expected) ? expected : 0 };
    }

    void Fail(string code)
    {
        if (failure == null)
        {
            failure = context ?? new DesktopQueryFailure { Stage = stage, Selector = DesktopSelector.Discovery.ToString() };
            failure.Predicate = code;
            // Only refresh a handle positively owned earlier in this query, within the same budget.
            if (failure.PreviouslyOwnedHandle != 0 && !Budget.Closed)
            {
                try
                {
                    uint h = (uint)failure.PreviouslyOwnedHandle;
                    failure.AliveAfter = adapter.Alive(h);
                    failure.OwnPidAfter = adapter.NativePid(h) == pid;
                    if (failure.AliveAfter == true && failure.OwnPidAfter == true)
                    {
                        uint root = adapter.NativeRoot(h);
                        bool ownedRoot = root != 0 && adapter.NativePid(root) == pid;
                        if (ownedRoot) failure.OwnedRootAfter = root;
                        failure.RootMatchesAfter = ownedRoot && root == failure.ExpectedOwnedRoot;
                    }
                }
                catch { /* Original refusal and containment take precedence over diagnostics. */ }
            }
            Budget.Close();
            failure.Nodes = Budget.Nodes;
            failure.Calls = Budget.Calls;
            failure.Windows = windows.Count;
        }
        throw new DesktopTreeException(failure);
    }

    void Require(bool condition, string code) { if (!condition) Fail(code); }

    TValue Execute<TValue>(DesktopSelector selector, uint expected, Func<TValue> operation)
    {
        Begin(selector, expected);
        try { Budget.Check(); Require(pid > 0, "query-owned-pid"); return operation(); }
        catch (DesktopTreeException) { throw; }
        catch (DesktopTreeGuardException ex) { Fail(ex.Message); }
        catch (Exception ex)
        {
            Fail(ex.GetType().Name == "ElementNotAvailableException" ? "query-element-unavailable" : "query-provider-failure");
        }
        throw new InvalidOperationException();
    }

    string NodeIdentity(TNode node)
    {
        int[] id = adapter.Identity(node);
        Require(id != null && id.Length > 0 && id.Length <= 32, "query-runtime-id");
        return string.Join(",", id);
    }

    uint Resolve(TNode node, uint expected)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; node != null; depth++)
        {
            Budget.Visit(depth);
            Require(adapter.ProcessId(node) == pid, "query-uia-pid");
            Require(ancestors.Add(NodeIdentity(node)), "query-parent-cycle");
            uint handle = adapter.Handle(node);
            if (handle != 0)
            {
                bool alive = adapter.Alive(handle);
                bool own = adapter.NativePid(handle) == pid;
                Require(alive && own, alive ? "query-native-pid" : "query-native-gone");
                context.PreviouslyOwnedHandle = handle;
                context.AliveBefore = alive;
                context.OwnPidBefore = own;
                uint root = adapter.NativeRoot(handle);
                Require(root != 0 && adapter.NativePid(root) == pid, "query-native-root-pid");
                context.OwnedRootBefore = root;
                context.RootMatchesBefore = root == expected;
                Require(adapter.Alive(root), "query-native-root-gone");
                return root;
            }
            node = adapter.Parent(node);
        }
        Fail("query-raw-parent-missing");
        return 0;
    }

    (uint owner, string windowClass) RootFacts(uint handle)
    {
        Require(handle != 0 && adapter.Alive(handle), "query-root-gone");
        Require(adapter.NativePid(handle) == pid, "query-root-pid");
        context.PreviouslyOwnedHandle = handle;
        context.AliveBefore = context.OwnPidBefore = true;
        Require(adapter.NativeRoot(handle) == handle, "query-root-self");
        context.OwnedRootBefore = handle;
        context.RootMatchesBefore = handle == context.ExpectedOwnedRoot;
        string windowClass = adapter.Class(handle);
        Require(windowClass == "#32770" || (DesktopPolicy.AvaloniaClass(windowClass) &&
            (expectedClass == null || windowClass == expectedClass)), "query-root-class");
        uint owner = adapter.Owner(handle), next = owner;
        var seen = new HashSet<uint> { handle };
        for (int depth = 0; next != 0; depth++)
        {
            Require(depth < 8 && seen.Add(next), "query-owner-bound");
            Require(adapter.Alive(next) && adapter.NativePid(next) == pid, "query-owner-pid");
            Require(adapter.NativeRoot(next) == next && DesktopPolicy.AvaloniaClass(adapter.Class(next)),
                "query-owner-root");
            next = adapter.Owner(next);
        }
        return (owner, windowClass);
    }

    DesktopOwnedWindow<TNode> Register(uint handle)
    {
        var before = RootFacts(handle);
        TNode element = adapter.FromHandle(handle);
        Require(element != null && adapter.ProcessId(element) == pid, "query-root-uia-pid");
        Require(adapter.Handle(element) == handle, "query-root-uia-handle");
        string identity = NodeIdentity(element);
        bool visible = adapter.Visible(handle) && !adapter.Offscreen(element);
        var after = RootFacts(handle);
        Require(before == after, "query-root-changed");
        if (roots.TryGetValue(handle, out var known))
        {
            Require(known.Owner == after.owner && known.Class == after.windowClass &&
                known.Identity == identity, "query-root-alias");
            if (known.Visible != visible) { known.Visible = visible; Revision++; }
            return known;
        }
        Require(windows.Count < 8, "query-window-bound");
        var root = new DesktopOwnedWindow<TNode> { Handle = handle, Owner = after.owner,
            Class = after.windowClass, Identity = identity, Element = element, Visible = visible };
        roots.Add(handle, root);
        windows.Add(root);
        pending.Enqueue(root);
        Revision++;
        return root;
    }

    void CrossRoot(uint expected, uint root)
    {
        Register(root);
        var todo = new Stack<uint>();
        var seen = new HashSet<uint>();
        todo.Push(root);
        while (todo.Count != 0)
        {
            Budget.Check();
            uint current = todo.Pop();
            Require(current != expected, "query-window-cycle");
            if (!seen.Add(current) || !edges.TryGetValue(current, out var children)) continue;
            foreach (uint child in children) todo.Push(child);
        }
        if (!edges.TryGetValue(expected, out var outgoing))
            edges.Add(expected, outgoing = new HashSet<uint>());
        outgoing.Add(root);
    }

    void Walk(TNode parent, uint expected, int depth, DesktopSelector selector,
        List<TNode> matches, int maximum, HashSet<string> visited)
    {
        TNode node = adapter.FirstChild(parent);
        while (node != null)
        {
            Budget.Visit(depth);
            NodeContext(expected);
            Require(adapter.ProcessId(node) == pid, "query-uia-pid");
            Require(visited.Add(NodeIdentity(node)), "query-node-cycle");
            uint root = Resolve(node, expected);
            if (root != expected)
            {
                CrossRoot(expected, root);
                Require(Resolve(node, expected) == root, "query-sibling-root-changed");
                node = adapter.NextSibling(node);
                continue;
            }
            if (selector != DesktopSelector.Discovery && adapter.Matches(node, selector))
            {
                Require(Resolve(node, expected) == expected, "query-match-root-changed");
                matches.Add(node);
                Require(matches.Count <= maximum, "query-match-bound");
            }
            Require(Resolve(node, expected) == expected, "query-descent-root-changed");
            Walk(node, expected, depth + 1, selector, matches, maximum, visited);
            Require(Resolve(node, expected) == expected, "query-sibling-root-changed");
            node = adapter.NextSibling(node);
        }
    }

    void Drain()
    {
        while (pending.Count != 0)
        {
            var root = pending.Dequeue();
            Register(root.Handle);
            Walk(root.Element, root.Handle, 1, DesktopSelector.Discovery, new List<TNode>(), 0,
                new HashSet<string>(StringComparer.Ordinal) { root.Identity });
        }
    }

    internal void Discover() => Execute(DesktopSelector.Discovery, 0, () =>
    {
        var seeds = adapter.Seeds();
        Require(seeds != null && seeds.Count <= 8, "query-seed-bound");
        foreach (var seed in seeds)
        {
            Require(seed != null && adapter.ProcessId(seed) == pid, "query-seed-pid");
            uint handle = adapter.Handle(seed);
            Require(handle != 0 && Resolve(seed, 0) == handle, "query-seed-root");
            Register(handle);
        }
        Drain();
        return true;
    });

    internal void Seed(TNode node) => Execute(DesktopSelector.Discovery, 0, () =>
    {
        Require(node != null && adapter.ProcessId(node) == pid, "query-seed-pid");
        uint handle = adapter.Handle(node);
        Require(handle != 0 && Resolve(node, 0) == handle, "query-seed-root");
        Register(handle);
        Drain();
        return true;
    });

    internal List<TNode> Find(uint window, TNode subtree, DesktopSelector selector, int maximum) =>
        Execute(selector, window, () =>
        {
            Require(maximum > 0 && maximum <= 16, "query-result-limit");
            Require(selector >= DesktopSelector.Main && selector <= DesktopSelector.ConfirmationMessage, "query-selector");
            var root = Register(window);
            TNode start = subtree ?? root.Element;
            Require(Resolve(start, window) == window, "query-subtree-root");
            var matches = new List<TNode>();
            Walk(start, window, 1, selector, matches, maximum,
                new HashSet<string>(StringComparer.Ordinal) { NodeIdentity(start) });
            Drain();
            return matches;
        });

    internal void Validate(uint window, TNode node, DesktopSelector selector = DesktopSelector.Membership) => Execute(selector, window, () =>
    {
        Register(window);
        Require(node != null && Resolve(node, window) == window, "control-native-root");
        return true;
    });

    internal bool Visible(DesktopOwnedWindow<TNode> root) => Execute(DesktopSelector.Discovery, root.Handle, () =>
    {
        Register(root.Handle);
        return root.Visible;
    });

    internal TValue Read<TValue>(uint window, TNode node, DesktopSelector selector, Func<TValue> read) =>
        Execute(selector, window, () =>
        {
            Register(window);
            Require(node != null && Resolve(node, window) == window, "control-native-root");
            TValue value = Budget.Call(read);
            Require(Resolve(node, window) == window, "query-read-root-changed");
            return value;
        });

    internal uint WindowKey(TNode node, DesktopSelector selector) => Execute(selector, 0, () =>
    {
        Require(node != null && adapter.ProcessId(node) == pid, "query-window-uia-pid");
        uint handle = adapter.Handle(node);
        Require(handle != 0 && Resolve(node, handle) == handle, "query-window-native-root");
        Register(handle);
        return handle;
    });

    internal void CheckRevision(int revision) => Execute(DesktopSelector.Discovery, 0, () =>
    {
        Require(Revision == revision, "query-topology-changed");
        return true;
    });
}

internal sealed class DesktopStartupSample<TNode> where TNode : class
{
    readonly List<TNode> windows = new List<TNode>();
    readonly List<DesktopStartupRoot> roots = new List<DesktopStartupRoot>();
    internal DesktopOwnedTree<TNode> Tree { get; }
    internal IReadOnlyList<TNode> Windows => windows;
    internal IReadOnlyList<DesktopStartupRoot> Roots => roots;
    internal int Revision { get; }

    DesktopStartupSample(DesktopOwnedTree<TNode> tree) { Tree = tree; Revision = tree.Revision; }

    internal static DesktopStartupSample<TNode> Capture(DesktopOwnedTree<TNode> tree,
        Func<DesktopOwnedWindow<TNode>, DesktopStartupRoot> project)
    {
        var sample = new DesktopStartupSample<TNode>(tree);
        sample.CheckRevision();
        for (int i = 0; i < tree.Windows.Count; i++)
        {
            var window = tree.Windows[i];
            bool visible = tree.Visible(window);
            sample.CheckRevision();
            if (!visible) continue;
            var projection = project(window);
            // A later query must not invalidate a root already projected or skipped.
            sample.CheckRevision();
            sample.windows.Add(window.Element);
            sample.roots.Add(projection);
        }
        sample.CheckRevision();
        return sample;
    }

    internal DesktopStartupDecision Observe(DesktopStartupObservation observation, long at,
        bool loadingExists, bool revalidate)
    {
        CheckRevision();
        return revalidate ? observation.Revalidate(at, Roots, loadingExists) :
            observation.Observe(at, Roots, loadingExists);
    }

    internal void CheckRevision() => Tree.CheckRevision(Revision);
}

// Native/UIA ownership and control ancestry are checked before these projections.
public sealed class DesktopStartupRoot
{
    public long Handle { get; }
    public long Owner { get; }
    public string WindowClass { get; }
    public bool Visible { get; }
    public bool MainControlVisible { get; }
    public bool LoadingLabelVisible { get; }
    public bool SetupWizardControlVisible { get; }

    public DesktopStartupRoot(long handle, long owner, string windowClass, bool visible,
        bool mainControlVisible, bool loadingLabelVisible, bool setupWizardControlVisible)
    {
        Handle = handle;
        Owner = owner;
        WindowClass = windowClass;
        Visible = visible;
        MainControlVisible = mainControlVisible;
        LoadingLabelVisible = loadingLabelVisible;
        SetupWizardControlVisible = setupWizardControlVisible;
    }
}

public sealed class DesktopStartupDecision
{
    public bool Ready { get; }
    public string Route { get; }
    public long MainHandle { get; }
    public long WizardHandle { get; }
    public string WindowClass { get; }

    internal DesktopStartupDecision(string route = null, long main = 0, long wizard = 0,
        string windowClass = null)
    {
        Ready = route != null;
        Route = route;
        MainHandle = main;
        WizardHandle = wizard;
        WindowClass = windowClass;
    }
}

public sealed class DesktopStartupObservation
{
    public const string ObservedRoute = "real-main-visible-and-loading-destroyed";
    public const string UnobservedRoute = "main-visible-loading-not-observed";
    public bool LoadingObserved { get; private set; }
    public long LoadingHandle { get; private set; }
    public long LoadingAt { get; private set; } = -1;
    public string WindowClass { get; private set; }
    long previousAt = -1;
    string failure;
    DesktopStartupDecision candidate;

    void Require(bool condition, string reason)
    {
        if (failure != null) throw new InvalidOperationException(failure);
        if (condition) return;
        failure = reason;
        throw new InvalidOperationException(reason);
    }

    public DesktopStartupDecision Observe(long at, IReadOnlyList<DesktopStartupRoot> roots,
        bool loadingExists)
    {
        Require(at >= 0 && at > previousAt, "startup-observation-order");
        candidate = Evaluate(at, roots, loadingExists);
        return candidate;
    }

    public DesktopStartupDecision Revalidate(long at, IReadOnlyList<DesktopStartupRoot> roots,
        bool loadingExists)
    {
        Require(candidate != null && candidate.Ready, "startup-acceptance-candidate");
        // The acceptance refresh may share a clock tick with its candidate.
        Require(at >= previousAt, "startup-observation-order");
        var expected = candidate;
        candidate = Evaluate(at, roots, loadingExists);
        Require(!candidate.Ready || (candidate.MainHandle == expected.MainHandle &&
            candidate.WindowClass == expected.WindowClass && candidate.Route == expected.Route),
            "startup-acceptance-changed");
        return candidate;
    }

    DesktopStartupDecision Evaluate(long at, IReadOnlyList<DesktopStartupRoot> roots,
        bool loadingExists)
    {
        previousAt = at;
        Require(roots != null && roots.Count <= 8, "startup-root-bound");
        var handles = new HashSet<long>();
        DesktopStartupRoot main = null, loading = null, wizard = null;
        int unknown = 0;
        foreach (var root in roots)
        {
            Require(root != null && root.Handle != 0 && root.Visible, "startup-root-projection");
            Require(handles.Add(root.Handle), "startup-duplicate-root");
            Require(DesktopPolicy.AvaloniaClass(root.WindowClass), "startup-root-class");
            int roles = (root.MainControlVisible ? 1 : 0) + (root.LoadingLabelVisible ? 1 : 0) +
                (root.SetupWizardControlVisible ? 1 : 0);
            Require(roles <= 1, "startup-root-role");
            if (root.MainControlVisible)
            {
                Require(main == null, "startup-duplicate-main");
                main = root;
            }
            else if (root.LoadingLabelVisible)
            {
                Require(loading == null, "startup-duplicate-loading");
                loading = root;
            }
            else if (root.SetupWizardControlVisible)
            {
                Require(wizard == null, "startup-duplicate-wizard");
                wizard = root;
            }
            else unknown++;
        }
        if (loading != null)
        {
            Require(!LoadingObserved || loading.Handle == LoadingHandle, "startup-loading-changed");
            if (!LoadingObserved)
            {
                LoadingObserved = true;
                LoadingHandle = loading.Handle;
                LoadingAt = at;
                Require(WindowClass == null || WindowClass == loading.WindowClass, "startup-class-changed");
                WindowClass = loading.WindowClass;
            }
        }
        if (WindowClass != null)
            foreach (var root in roots)
                Require(root.WindowClass == WindowClass, "startup-class-changed");
        Require(LoadingObserved || !loadingExists, "startup-loading-projection");
        if (main != null && wizard != null)
            Require(wizard.Handle != main.Handle && wizard.Owner == main.Handle &&
                wizard.WindowClass == main.WindowClass, "startup-setup-wizard");
        if (main == null || unknown != 0 || loading != null || (LoadingObserved && loadingExists))
            return new DesktopStartupDecision();

        if (LoadingObserved)
            Require(DesktopPolicy.Handoff(true, LoadingAt, at, LoadingHandle, main.Handle,
                !loadingExists, main.Visible && main.MainControlVisible), "startup-handoff-refused");
        WindowClass = main.WindowClass;
        foreach (var root in roots)
            Require(root.WindowClass == WindowClass, "startup-class-changed");
        return new DesktopStartupDecision(LoadingObserved ? ObservedRoute : UnobservedRoute,
            main.Handle, wizard?.Handle ?? 0, WindowClass);
    }
}

public static class InstalledDatabaseSnapshot
{
    const string MarkerName = ".febuilder-patch-import.json";
    const string MarkerOwner = "FEBuilderGBA.PatchDatabaseImport";

    static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    static void Plain(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "reparse-path");
    }

    static byte[] ReadMarker(string path)
    {
        int length = MarkerOwner.Length + 1 + 32 + 1 + "FE8U".Length + 1;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length == length, "installed-marker");
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1, "installed-marker");
        foreach (byte value in bytes) Require(value <= 0x7F, "installed-marker");
        string[] parts = Encoding.ASCII.GetString(bytes).Split('\n');
        Require(parts.Length == 4 && parts[0] == MarkerOwner && parts[2] == "FE8U" &&
            parts[3] == "" && Guid.TryParseExact(parts[1], "N", out Guid id) &&
            id.ToString("N") == parts[1], "installed-marker");
        return bytes;
    }

    public static string Capture(string database, Action guard, Func<string, string> hash)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(hash);
        Require(Path.GetFullPath(database) == database, "installed-tree-root");
        var rows = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(database);
        int entries = 0, files = 0;
        while (pending.Count != 0)
        {
            guard();
            string directory = pending.Dequeue();
            Plain(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                guard();
                Require(++entries <= 16, "installed-tree-bound");
                Plain(entry);
                string relative = Path.GetRelativePath(database, entry).Replace(Path.DirectorySeparatorChar, '\\');
                Require(DesktopPolicy.RelativeFile(relative), "installed-tree-path");
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    Require(relative == "proof", "unexpected-installed-directory");
                    rows.Add("D:" + relative);
                    pending.Enqueue(entry);
                }
                else
                {
                    Require(relative == @"proof\PATCH_offline.txt" || relative == @"proof\payload.bin" ||
                        relative == MarkerName,
                        "unexpected-installed-file");
                    long length = new FileInfo(entry).Length;
                    Require(length <= 16777216, "hash-size-bound");
                    string digest;
                    if (relative == MarkerName)
                    {
                        byte[] marker = ReadMarker(entry);
                        guard();
                        length = marker.Length;
                        digest = Convert.ToHexString(SHA256.HashData(marker)).ToLowerInvariant();
                    }
                    else digest = hash(entry);
                    Require(DesktopPolicy.Sha256(digest), "installed-file-hash");
                    files++;
                    rows.Add("F:" + relative + ":" + length + ":" + digest);
                }
            }
        }
        Require(files == 3 && rows.Count == 4, "installed-tree-shape");
        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\n", rows)))).ToLowerInvariant();
    }
}

// Closing admission cannot revoke a call already admitted. Cleanup needs an exited
// execution boundary; returning from the observer additionally requires a joined worker.
public sealed class DesktopDispatchGate
{
    readonly object sync = new object();
    bool closed, inFlight;

    public bool IsClosed { get { lock (sync) return closed; } }

    public bool TryBegin()
    {
        lock (sync)
        {
            if (closed || inFlight) return false;
            inFlight = true;
            return true;
        }
    }

    public void End()
    {
        lock (sync)
        {
            if (!inFlight) throw new InvalidOperationException("No admitted dispatch.");
            inFlight = false;
        }
    }

    public void Close() { lock (sync) closed = true; }
    public bool CanReturn(bool workerJoined)
    {
        lock (sync) return closed && !inFlight && workerJoined;
    }
}
