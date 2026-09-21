using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        return name != null && name.Length == 45 && name.StartsWith("Avalonia-", StringComparison.Ordinal) &&
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

internal enum DesktopRootKind { Avalonia, NativeDialog, TransientPopup }

internal sealed class DesktopRootBindings
{
    sealed class Binding
    {
        internal readonly string Class;
        internal string Identity;
        internal uint Owner;
        internal Binding(string windowClass) { Class = windowClass; }
    }
    readonly Dictionary<uint, Binding> roots = new Dictionary<uint, Binding>();
    internal int Count => roots.Count;

    internal static bool ValidClass(string windowClass) =>
        windowClass == "#32770" || windowClass == "ComboLBox" || DesktopPolicy.AvaloniaClass(windowClass);

    // Only the native/root/owner validation path admits these records.
    internal void BindNativeClass(uint handle, string windowClass)
    {
        if (handle == 0 || !ValidClass(windowClass)) throw new DesktopTreeGuardException("query-root-class");
        if (roots.TryGetValue(handle, out var known))
        {
            if (known.Class != windowClass) throw new DesktopTreeGuardException("query-root-class-changed");
            return;
        }
        if (roots.Count >= 32) throw new DesktopTreeGuardException("query-root-history-bound");
        roots.Add(handle, new Binding(windowClass));
    }

    internal void BindCanonical(uint handle, string windowClass, string identity, uint owner)
    {
        if (string.IsNullOrEmpty(identity) || identity.Length > 512)
            throw new DesktopTreeGuardException("query-runtime-id");
        BindNativeClass(handle, windowClass);
        var known = roots[handle];
        if (known.Identity != null && known.Identity != identity)
            throw new DesktopTreeGuardException("query-root-alias");
        if (known.Identity == null) known.Identity = identity;
        known.Owner = owner;
    }

    internal bool Matches(uint handle, string windowClass) =>
        roots.TryGetValue(handle, out var known) && known.Identity != null && known.Class == windowClass;
    internal bool HasCanonical(uint handle) => roots.TryGetValue(handle, out var known) && known.Identity != null;
    internal bool MatchesProjection(uint handle, string windowClass, uint owner) =>
        roots.TryGetValue(handle, out var known) && known.Identity != null &&
        known.Class == windowClass && known.Owner == owner;
    internal bool KnownNativeOwner(uint handle) => handle == 0 ||
        (roots.TryGetValue(handle, out var known) &&
        (known.Class == "#32770" || DesktopPolicy.AvaloniaClass(known.Class)));
    internal DesktopRootKind Kind(uint handle)
    {
        if (!roots.TryGetValue(handle, out var known) || known.Identity == null)
            throw new DesktopTreeGuardException("query-root-unbound");
        return known.Class == "#32770" ? DesktopRootKind.NativeDialog :
            known.Class == "ComboLBox" ? DesktopRootKind.TransientPopup : DesktopRootKind.Avalonia;
    }
    internal string Expected(uint handle, DesktopRootKind kind)
    {
        if (Kind(handle) != kind) throw new DesktopTreeGuardException("query-root-kind");
        return roots[handle].Class;
    }
}

internal enum DesktopSelector
{
    Discovery, Membership, Main, Wizard, Loading, Import, Status, List,
    FilenameHost, FilenameEdit, PickerOpen, Row, RowName, ConfirmationYes, ConfirmationMessage
}

internal enum DesktopQueryProperty { ProcessId, NativeWindowHandle, ControlType, AutomationId, Name }
internal enum DesktopQueryControl { Unknown, Window, Text, Edit, ListItem, Button, List }

internal interface IDesktopQueryCompiler<TCondition>
{
    TCondition Equal(DesktopQueryProperty property, object value);
    TCondition And(TCondition first, TCondition second);
    TCondition Or(TCondition first, TCondition second);
    TCondition Not(TCondition condition);
}

internal sealed class DesktopPredicateCompiler<TNode> : IDesktopQueryCompiler<Func<TNode, bool>>
{
    readonly Func<TNode, DesktopQueryProperty, object> read;
    internal DesktopPredicateCompiler(Func<TNode, DesktopQueryProperty, object> read) { this.read = read; }
    public Func<TNode, bool> Equal(DesktopQueryProperty property, object value) => node => Equals(read(node, property), value);
    public Func<TNode, bool> And(Func<TNode, bool> first, Func<TNode, bool> second) => node => first(node) && second(node);
    public Func<TNode, bool> Or(Func<TNode, bool> first, Func<TNode, bool> second) => node => first(node) || second(node);
    public Func<TNode, bool> Not(Func<TNode, bool> condition) => node => !condition(node);
}

internal static class DesktopCandidateQuery
{
    internal const string MainButton = "Main_PatchManager_Button";
    internal const string WizardButton = "ContentRepoSetupWizard_Close_Button";
    internal const string ImportButton = "PatchManager_ImportPatchDatabase_Button";
    internal const string StatusLabel = "PatchManager_StatusMessage_Label";
    internal const string PatchList = "PatchManager_PatchList_List";
    internal const string LoadingName = "Recovering patch database…";
    internal const string ExpectedRow = "Offline ZIP Proof";

    internal static TCondition Compile<TCondition>(IDesktopQueryCompiler<TCondition> compiler,
        int pid, DesktopSelector selector)
    {
        if (pid <= 0) throw new DesktopTreeGuardException("query-owned-pid");
        TCondition selected;
        switch (selector)
        {
            case DesktopSelector.Discovery:
                selected = compiler.Or(compiler.Equal(DesktopQueryProperty.ControlType, DesktopQueryControl.Window),
                    compiler.Not(compiler.Equal(DesktopQueryProperty.NativeWindowHandle, 0)));
                break;
            case DesktopSelector.Loading:
                selected = compiler.And(compiler.Equal(DesktopQueryProperty.ControlType, DesktopQueryControl.Text),
                    compiler.Equal(DesktopQueryProperty.Name, LoadingName));
                break;
            case DesktopSelector.FilenameEdit:
                selected = compiler.Equal(DesktopQueryProperty.ControlType, DesktopQueryControl.Edit); break;
            case DesktopSelector.Row:
                selected = compiler.Equal(DesktopQueryProperty.ControlType, DesktopQueryControl.ListItem); break;
            case DesktopSelector.RowName:
                selected = compiler.Equal(DesktopQueryProperty.Name, ExpectedRow); break;
            default:
                string id;
                switch (selector)
                {
                    case DesktopSelector.Main: id = MainButton; break;
                    case DesktopSelector.Wizard: id = WizardButton; break;
                    case DesktopSelector.Import: id = ImportButton; break;
                    case DesktopSelector.Status: id = StatusLabel; break;
                    case DesktopSelector.List: id = PatchList; break;
                    case DesktopSelector.FilenameHost: id = "1148"; break;
                    case DesktopSelector.PickerOpen: id = "1"; break;
                    case DesktopSelector.ConfirmationYes: id = "MessageBoxContent_Yes_Button"; break;
                    case DesktopSelector.ConfirmationMessage: id = "MessageBoxContent_Message_Label"; break;
                    default: throw new DesktopTreeGuardException("query-selector");
                }
                selected = compiler.Equal(DesktopQueryProperty.AutomationId, id);
                break;
        }
        return compiler.And(compiler.Equal(DesktopQueryProperty.ProcessId, pid), selected);
    }

    internal static IReadOnlyList<TNode> Collect<TNode>(DesktopTreeBudget budget,
        Func<int> count, Func<int, TNode> item, int maximum = 256)
    {
        int length = budget.Call(count);
        budget.Candidates(length, maximum);
        var result = new List<TNode>(length);
        for (int i = 0; i < length; i++)
        {
            int index = i;
            result.Add(budget.Call(() => item(index)));
        }
        return result;
    }
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
    public int? SeedOrdinal { get; set; }
    public bool? SeedResolveKeyEqual { get; set; }
    public bool? ResolveAlive { get; set; }
    public string ResolvePidRelation { get; set; }
    public string RejectedRootClass { get; set; }
    public long OwnerHandle { get; set; }
    public string OwnerClass { get; set; }
    public long OwnerParent { get; set; }
    public long OwnerNativeRoot { get; set; }
    public int? OwnerDepth { get; set; }
    public bool? OwnerIsSelfRoot { get; set; }
    public int? OwnerChainCount { get; set; }
    public bool? OwnerChainTransient { get; set; }
    public long OwnerChainFirstHandle { get; set; }
    public string OwnerChainFirstClass { get; set; }
    public long OwnerChainLastHandle { get; set; }
    public string OwnerChainLastClass { get; set; }
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
    internal void Candidates(int count, int maximum = 256)
    {
        Check();
        string error = (maximum != 8 && maximum != 256) || count < 0 || count > maximum ?
            (maximum == 8 ? "query-seed-bound" : "query-candidate-bound") :
            count > 4096 - Nodes ? "query-node-bound" : null;
        if (error != null) { Closed = true; throw new DesktopTreeGuardException(error); }
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
    IReadOnlyList<TNode> Candidates(TNode subtree, DesktopSelector selector);
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
    readonly string stage;
    readonly DesktopRootBindings bindings;
    readonly Dictionary<uint, DesktopOwnedWindow<TNode>> roots = new Dictionary<uint, DesktopOwnedWindow<TNode>>();
    readonly List<DesktopOwnedWindow<TNode>> windows = new List<DesktopOwnedWindow<TNode>>();
    readonly Queue<DesktopOwnedWindow<TNode>> pending = new Queue<DesktopOwnedWindow<TNode>>();
    readonly Dictionary<uint, HashSet<uint>> edges = new Dictionary<uint, HashSet<uint>>();
    DesktopQueryFailure context, failure;
    internal DesktopTreeBudget Budget { get; }
    internal IReadOnlyList<DesktopOwnedWindow<TNode>> Windows => windows;
    internal int Revision { get; private set; }

    internal DesktopOwnedTree(IDesktopOwnedTreeAdapter<TNode> adapter, int pid, DesktopRootBindings bindings,
        string stage, Action guard)
    {
        this.adapter = adapter;
        this.pid = pid;
        this.bindings = bindings ?? new DesktopRootBindings();
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

    void Fail(string code, string rejectedRootClass = null)
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
                        if (code == "query-root-class" && !Budget.Closed && ownedRoot && root == h &&
                            failure.AliveBefore == true && failure.OwnPidBefore == true &&
                            failure.OwnedRootBefore == h && rejectedRootClass != null &&
                            rejectedRootClass.Length >= 1 && rejectedRootClass.Length <= 256)
                        {
                            bool printable = true;
                            foreach (char c in rejectedRootClass) printable &= c >= 32 && c <= 126;
                            if (printable) failure.RejectedRootClass = rejectedRootClass;
                        }

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

    void OwnerDiagnostics(uint handle, string windowClass, uint parent, uint nativeRoot, int depth)
    {
        context.OwnerHandle = handle;
        context.OwnerClass = Printable(windowClass) ? windowClass : null;
        context.OwnerParent = parent;
        context.OwnerNativeRoot = nativeRoot;
        context.OwnerDepth = depth;
        context.OwnerIsSelfRoot = nativeRoot == handle;
    }

    void ClearOwnerDiagnostics()
    {
        context.OwnerHandle = context.OwnerParent = context.OwnerNativeRoot = 0;
        context.OwnerClass = null;
        context.OwnerDepth = null;
        context.OwnerIsSelfRoot = null;
    }

    static bool Printable(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256) return false;
        foreach (char c in value) if (c < 32 || c > 126) return false;
        return true;
    }

    void ChainDiagnostics(List<(uint handle, string windowClass)> owners, bool transient)
    {
        context.OwnerChainCount = owners.Count;
        context.OwnerChainTransient = transient;
        if (owners.Count == 0) return;
        context.OwnerChainFirstHandle = owners[0].handle;
        context.OwnerChainFirstClass = Printable(owners[0].windowClass) ? owners[0].windowClass : null;
        context.OwnerChainLastHandle = owners[owners.Count - 1].handle;
        context.OwnerChainLastClass = Printable(owners[owners.Count - 1].windowClass) ?
            owners[owners.Count - 1].windowClass : null;
    }

    void ClearChainDiagnostics()
    {
        context.OwnerChainCount = null;
        context.OwnerChainTransient = null;
        context.OwnerChainFirstHandle = context.OwnerChainLastHandle = 0;
        context.OwnerChainFirstClass = context.OwnerChainLastClass = null;
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

    void ClearResolveObservations(int? seedOrdinal = null)
    {
        context.SeedOrdinal = seedOrdinal;
        context.SeedResolveKeyEqual = null;
        context.ResolveAlive = null;
        context.ResolvePidRelation = null;
    }

    uint Resolve(TNode node, uint expected, uint? seedKey = null, int? seedOrdinal = null)
    {
        ClearResolveObservations(seedOrdinal);
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; node != null; depth++)
        {
            if (depth != 0) ClearResolveObservations(seedOrdinal);
            Budget.Visit(depth);
            Require(adapter.ProcessId(node) == pid, "query-uia-pid");
            Require(ancestors.Add(NodeIdentity(node)), "query-parent-cycle");
            uint handle = adapter.Handle(node);
            if (depth == 0 && seedKey.HasValue) context.SeedResolveKeyEqual = seedKey.Value == handle;
            if (handle != 0)
            {
                bool alive = adapter.Alive(handle);
                context.ResolveAlive = alive;
                int nativePid = adapter.NativePid(handle);
                context.ResolvePidRelation = nativePid == 0 ? "zero" : nativePid == pid ? "owned" : "foreign";
                bool own = nativePid == pid;
                Require(alive && own, alive ? "query-native-pid" : "query-native-gone");
                context.PreviouslyOwnedHandle = handle;
                context.AliveBefore = alive;
                context.OwnPidBefore = own;
                uint root = adapter.NativeRoot(handle);
                Require(root != 0 && adapter.NativePid(root) == pid, "query-native-root-pid");
                context.OwnedRootBefore = root;
                context.RootMatchesBefore = root == expected;
                Require(adapter.Alive(root), "query-native-root-gone");
                ClearResolveObservations();
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
        if (!DesktopRootBindings.ValidClass(windowClass)) Fail("query-root-class", windowClass);
        bool transientCombo = windowClass == "ComboLBox";
        uint owner = adapter.Owner(handle), next = owner;
        var seen = new HashSet<uint> { handle };
        if (transientCombo && next != 0 && adapter.NativeRoot(next) != next)
        {
            Require(seen.Add(next), "query-owner-bound");
            Require(adapter.Alive(next) && adapter.NativePid(next) == pid, "query-owner-pid");
            uint ownerRoot = adapter.NativeRoot(next);
            Require(ownerRoot != 0 && !seen.Contains(ownerRoot), "query-owner-bound");
            Require(adapter.Alive(ownerRoot) && adapter.NativePid(ownerRoot) == pid, "query-owner-pid");
            uint canonicalRoot = adapter.NativeRoot(ownerRoot);
            OwnerDiagnostics(next, null, 0, ownerRoot, 0);
            Require(canonicalRoot == ownerRoot, "query-owner-root");
            Require(adapter.Class(ownerRoot) == "#32770", "query-owner-root");
            ClearOwnerDiagnostics();
            owner = next = ownerRoot;
        }
        var owners = new List<(uint handle, string windowClass)>();
        for (int depth = 0; next != 0; depth++)
        {
            Require(depth < 8 && seen.Add(next), "query-owner-bound");
            Require(adapter.Alive(next) && adapter.NativePid(next) == pid, "query-owner-pid");
            string ownerClass = adapter.Class(next);
            uint parent = adapter.Owner(next);
            bool avaloniaOwner = DesktopPolicy.AvaloniaClass(ownerClass);
            bool comboDialogOwner = transientCombo && ownerClass == "#32770" && parent != 0;
            uint nativeRoot = adapter.NativeRoot(next);
            OwnerDiagnostics(next, ownerClass, parent, nativeRoot, depth);
            Require(nativeRoot == next && (avaloniaOwner || comboDialogOwner), "query-owner-root");
            if (transientCombo && avaloniaOwner) Require(parent == 0, "query-owner-root");
            ClearOwnerDiagnostics();
            owners.Add((next, ownerClass));
            next = parent;
        }
        if (transientCombo)
        {
            ChainDiagnostics(owners, true);
            bool ownerless = owner == 0 && owners.Count == 0;
            Require(ownerless || (owners.Count >= 2 && owners[0].windowClass == "#32770" &&
                DesktopPolicy.AvaloniaClass(owners[owners.Count - 1].windowClass)), "query-owner-root");
            ClearChainDiagnostics();
        }
        foreach (var parent in owners) bindings.BindNativeClass(parent.handle, parent.windowClass);
        bindings.BindNativeClass(handle, windowClass);
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
        bindings.BindCanonical(handle, after.windowClass, identity, after.owner);
        if (roots.TryGetValue(handle, out var known))
        {
            Require(known.Owner == after.owner && known.Class == after.windowClass &&
                known.Identity == identity, "query-root-alias");
            if (known.Visible != visible) { known.Visible = visible; Revision++; }
            known.Element = element;
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

    void WithinSubtree(TNode node, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; node != null; depth++)
        {
            Budget.Visit(depth);
            Require(adapter.ProcessId(node) == pid, "query-uia-pid");
            string identity = NodeIdentity(node);
            Require(seen.Add(identity), "query-parent-cycle");
            if (identity == ancestor) return;
            node = adapter.Parent(node);
        }
        Fail("query-candidate-subtree");
    }

    void Candidates(TNode parent, uint expected, DesktopSelector selector, List<TNode> matches, int maximum)
    {
        Require(adapter.ProcessId(parent) == pid, "query-uia-pid");
        string parentIdentity = NodeIdentity(parent);
        var candidates = adapter.Candidates(parent, selector);
        Require(candidates != null, "query-candidate-list");
        Budget.Candidates(candidates.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal) { parentIdentity };
        foreach (TNode node in candidates)
        {
            NodeContext(expected);
            Require(node != null, "query-candidate-null");
            uint root = Resolve(node, expected);
            string identity = NodeIdentity(node);
            Require(visited.Add(identity), "query-node-cycle");
            if (root != expected)
            {
                CrossRoot(expected, root);
                Require(Resolve(node, expected) == root, "query-sibling-root-changed");
                continue;
            }
            WithinSubtree(node, parentIdentity);
            bool matched = adapter.Matches(node, selector);
            Require(Resolve(node, expected) == expected, "query-match-root-changed");
            WithinSubtree(node, parentIdentity);
            Require(NodeIdentity(node) == identity, "query-node-alias");
            Require(matched, "query-candidate-filter");
            if (selector != DesktopSelector.Discovery)
            {
                matches.Add(node);
                Require(matches.Count <= maximum, "query-match-bound");
            }
        }
        Require(Resolve(parent, expected) == expected, "query-subtree-root");
        Require(NodeIdentity(parent) == parentIdentity, "query-subtree-changed");
    }

    void Drain()
    {
        var scanned = new HashSet<uint>();
        while (pending.Count != 0)
        {
            var root = pending.Dequeue();
            if (!scanned.Add(root.Handle)) continue;
            Register(root.Handle);
            Candidates(root.Element, root.Handle, DesktopSelector.Discovery, new List<TNode>(), 0);
            Register(root.Handle);
        }
    }

    void Refresh()
    {
        ClearResolveObservations();
        edges.Clear();
        var seeds = adapter.Seeds();
        Require(seeds != null && seeds.Count <= 8, "query-seed-bound");
        int ordinal = 0;
        foreach (var seed in seeds)
        {
            context.SeedOrdinal = ++ordinal;
            Require(seed != null && adapter.ProcessId(seed) == pid, "query-seed-pid");
            uint handle = adapter.Handle(seed);
            Require(handle != 0 && Resolve(seed, 0, handle, ordinal) == handle, "query-seed-root");
            Register(handle);
        }
        foreach (var root in windows) pending.Enqueue(root);
        Drain();
    }

    internal void Discover() => Execute(DesktopSelector.Discovery, 0, () =>
    {
        Refresh();
        return true;
    });

    internal void RefreshCatalog() => Execute(DesktopSelector.Discovery, 0, () =>
    {
        Refresh();
        return true;
    });

    internal void Seed(TNode node) => Execute(DesktopSelector.Discovery, 0, () =>
    {
        Require(node != null && adapter.ProcessId(node) == pid, "query-seed-pid");
        uint handle = adapter.Handle(node);
        Require(handle != 0 && Resolve(node, 0, handle) == handle, "query-seed-root");
        Register(handle);
        Refresh();
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
            int revision = Revision;
            var matches = new List<TNode>();
            Candidates(start, window, selector, matches, maximum);
            Refresh();
            Require(Revision == revision, "query-topology-changed");
            return matches;
        });

    internal void Validate(uint window, TNode node, DesktopSelector selector = DesktopSelector.Membership) => Execute(selector, window, () =>
    {
        Register(window);
        Require(node != null && Resolve(node, window) == window, "control-native-root");
        return true;
    });

    internal void ValidateWindow(TNode node, DesktopRootKind kind, uint? expectedOwner = null) =>
        Execute(DesktopSelector.Membership, 0, () =>
    {
        Require(node != null && adapter.ProcessId(node) == pid, "query-window-uia-pid");
        uint handle = adapter.Handle(node);
        Require(handle != 0 && Resolve(node, handle) == handle, "query-window-native-root");
        var root = Register(handle);
        Require(NodeIdentity(node) == root.Identity, "query-root-alias");
        Require(root.Class == bindings.Expected(handle, kind), "query-root-class-changed");
        Require(!expectedOwner.HasValue || root.Owner == expectedOwner.Value, "query-window-owner");
        Require(adapter.Visible(handle) && !adapter.Offscreen(node), "query-window-hidden");
        Require(Resolve(node, handle) == handle, "query-window-native-root");
        Register(handle);
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

internal sealed class DesktopStartupAcquisition
{
    internal DesktopRootBindings Bindings { get; }
    internal DesktopStartupObservation Observation { get; }
    internal int Used { get; private set; }
    internal bool Recovering { get; private set; }
    internal DesktopTreeException RetainedUnavailable { get; private set; }

    internal DesktopStartupAcquisition(DesktopRootBindings bindings, DesktopStartupObservation observation)
    {
        Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    internal static bool IsUnavailable(DesktopTreeException exception, int currentWindows)
    {
        var failure = exception?.Failure;
        return failure != null && currentWindows == 0 && failure.Windows == 0 &&
            failure.Stage == "loading-handoff" && failure.Selector == "Discovery" &&
            failure.Predicate == "query-native-gone" && failure.SeedOrdinal == 1 &&
            failure.SeedResolveKeyEqual == true && failure.ResolveAlive == false &&
            failure.ResolvePidRelation == "zero" && failure.ExpectedOwnedRoot == 0 &&
            failure.PreviouslyOwnedHandle == 0 && failure.OwnedRootBefore == 0 &&
            failure.OwnedRootAfter == 0 && failure.AliveBefore == null &&
            failure.AliveAfter == null && failure.OwnPidBefore == null &&
            failure.OwnPidAfter == null && failure.RootMatchesBefore == null &&
            failure.RootMatchesAfter == null;
    }

    internal bool TryDiscover<TNode>(IDesktopOwnedTreeAdapter<TNode> adapter, int pid, string stage,
        Action guard, Action<string> record, out DesktopOwnedTree<TNode> tree,
        Action queryGuard = null) where TNode : class
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        if (guard == null) throw new ArgumentNullException(nameof(guard));
        if (record == null) throw new ArgumentNullException(nameof(record));
        guard();
        if (Recovering)
        {
            if (Used >= 3) throw RetainedUnavailable;
            Used++;
        }
        tree = new DesktopOwnedTree<TNode>(adapter, pid, Bindings, stage, queryGuard ?? guard);
        try { tree.Discover(); }
        catch (DesktopTreeException ex)
        {
            guard();
            if (!IsUnavailable(ex, tree.Windows.Count)) throw;
            RetainedUnavailable = ex;
            if (Used >= 3) throw;
            Observation.InvalidateCandidate();
            Recovering = true;
            record("startup-snapshot-unavailable");
            tree = null;
            return false;
        }
        if (tree.Windows.Count == 0)
        {
            // Reacquire empty snapshots through the guarded loop, not capture-time refreshes.
            Observation.InvalidateCandidate();
            Complete(new DesktopStartupDecision(), false);
            tree = null;
            return false;
        }
        return true;
    }

    internal bool Complete(DesktopStartupDecision decision, bool acceptance)
    {
        if (decision == null) throw new ArgumentNullException(nameof(decision));
        if (!decision.Ready || acceptance)
        {
            Recovering = false;
            return decision.Ready;
        }
        if (Recovering && Used >= 3)
        {
            // A complete Ready candidate is not current unavailability.
            // Charge three cannot fund acceptance: require a new ordinary pair.
            Observation.InvalidateCandidate();
            Recovering = false;
            return false;
        }
        return decision.Ready;
    }
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
        tree.RefreshCatalog();
        var sample = new DesktopStartupSample<TNode>(tree);
        sample.CheckRevision();
        for (int i = 0; i < tree.Windows.Count; i++)
        {
            var window = tree.Windows[i];
            bool visible = tree.Visible(window);
            tree.RefreshCatalog();
            sample.CheckRevision();
            if (!visible) continue;
            var projection = project(window);
            // A later query must not invalidate a root already projected or skipped.
            tree.RefreshCatalog();
            sample.CheckRevision();
            sample.windows.Add(window.Element);
            sample.roots.Add(projection);
        }
        sample.RefreshAndCheckRevision();
        return sample;
    }

    internal DesktopStartupDecision Observe(DesktopStartupObservation observation, long at,
        bool loadingExists, bool revalidate)
    {
        RefreshAndCheckRevision();
        return revalidate ? observation.Revalidate(at, Roots, loadingExists) :
            observation.Observe(at, Roots, loadingExists);
    }

    internal void CheckRevision() => Tree.CheckRevision(Revision);
    internal void RefreshAndCheckRevision()
    {
        Tree.RefreshCatalog();
        CheckRevision();
    }
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
    public string LoadingWindowClass { get; private set; }
    readonly Dictionary<long, string> classes = new Dictionary<long, string>();
    readonly DesktopRootBindings bindings;
    long previousAt = -1;
    string failure;
    DesktopStartupDecision candidate;

    public DesktopStartupObservation() { }
    internal DesktopStartupObservation(DesktopRootBindings bindings) { this.bindings = bindings; }

    internal void InvalidateCandidate() { candidate = null; }

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
            Require(root != null && root.Handle > 0 && root.Handle <= uint.MaxValue && root.Visible,
                "startup-root-projection");
            Require(handles.Add(root.Handle), "startup-duplicate-root");
            Require(DesktopPolicy.AvaloniaClass(root.WindowClass), "startup-root-class");
            if (bindings != null)
                Require(bindings.Matches((uint)root.Handle, root.WindowClass), "startup-class-changed");
            if (classes.TryGetValue(root.Handle, out var knownClass))
                Require(knownClass == root.WindowClass, "startup-class-changed");
            else
            {
                Require(classes.Count < 32, "startup-class-history-bound");
                classes.Add(root.Handle, root.WindowClass);
            }
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
                LoadingWindowClass = loading.WindowClass;
            }
            Require(LoadingWindowClass == loading.WindowClass, "startup-class-changed");
        }
        Require(LoadingObserved || !loadingExists, "startup-loading-projection");
        if (main != null && wizard != null)
        {
            Require(wizard.Handle != main.Handle, "startup-wizard-handle");
            Require(wizard.Owner == main.Handle, "startup-wizard-owner");
        }
        if (main == null || unknown != 0 || loading != null || (LoadingObserved && loadingExists))
            return new DesktopStartupDecision();

        if (LoadingObserved)
            Require(DesktopPolicy.Handoff(true, LoadingAt, at, LoadingHandle, main.Handle,
                !loadingExists, main.Visible && main.MainControlVisible), "startup-handoff-refused");
        WindowClass = main.WindowClass;
        return new DesktopStartupDecision(LoadingObserved ? ObservedRoute : UnobservedRoute,
            main.Handle, wizard?.Handle ?? 0, WindowClass);
    }
}

public sealed class DesktopStartupFailureRoot
{
    public long Handle { get; }
    public long Owner { get; }
    public string WindowClass { get; }
    public bool Visible { get; }
    public bool MainControlVisible { get; }
    public bool LoadingLabelVisible { get; }
    public bool SetupWizardControlVisible { get; }
    internal DesktopStartupFailureRoot(DesktopStartupRoot root)
    {
        Handle = root.Handle; Owner = root.Owner; WindowClass = root.WindowClass;
        Visible = root.Visible; MainControlVisible = root.MainControlVisible;
        LoadingLabelVisible = root.LoadingLabelVisible; SetupWizardControlVisible = root.SetupWizardControlVisible;
    }
}

public sealed class DesktopStartupFailure
{
    public string Predicate { get; }
    public ReadOnlyCollection<DesktopStartupFailureRoot> Roots { get; }
    internal DesktopStartupFailure(string predicate, DesktopStartupFailureRoot[] roots)
    {
        Predicate = predicate; Roots = Array.AsReadOnly(roots);
    }
}

internal sealed class DesktopStartupFailureCapture
{
    static readonly HashSet<string> Predicates = new HashSet<string>(StringComparer.Ordinal)
    {
        "startup-observation-order", "startup-acceptance-candidate", "startup-acceptance-changed",
        "startup-root-bound", "startup-root-projection", "startup-duplicate-root", "startup-root-class",
        "startup-root-role", "startup-duplicate-main", "startup-duplicate-loading", "startup-duplicate-wizard",
        "startup-loading-changed", "startup-class-changed", "startup-class-history-bound",
        "startup-loading-projection", "startup-wizard-handle", "startup-wizard-owner", "startup-handoff-refused",
        "startup-main-acceptance-control", "startup-main-acceptance-root", "startup-wizard-acceptance-control",
        "startup-loading-still-present"
    };
    bool attempted;
    internal DesktopStartupFailure Value { get; private set; }

    internal void Record(string predicate, IReadOnlyList<DesktopStartupRoot> roots, DesktopRootBindings bindings)
    {
        if (attempted) return;
        attempted = true;
        // This path copies validated data only; optional diagnostics cannot replace the original failure.
        try
        {
            if (predicate == null || !Predicates.Contains(predicate) || roots == null || roots.Count > 8 ||
                bindings == null) return;
            var seen = new HashSet<long>();
            var copy = new DesktopStartupFailureRoot[roots.Count];
            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null || root.Handle <= 0 || root.Handle > uint.MaxValue || !seen.Add(root.Handle) ||
                    root.Owner < 0 || root.Owner > uint.MaxValue ||
                    !bindings.MatchesProjection((uint)root.Handle, root.WindowClass, (uint)root.Owner) ||
                    !bindings.KnownNativeOwner((uint)root.Owner)) return;
                copy[i] = new DesktopStartupFailureRoot(root);
            }
            // Fixed ASCII predicates/classes, UInt32 handles and eight seven-field rows fit within 4 KiB.
            Value = new DesktopStartupFailure(predicate, copy);
        }
        catch { /* The already-latched refusal remains authoritative. */ }
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
