using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;

public sealed class DesktopEvent
{
    public string Utc { get; set; }
    public long ElapsedMs { get; set; }
    public string Kind { get; set; }
    public string Code { get; set; }
    public long Window { get; set; }
}

public sealed class DesktopResult
{
    public bool Passed { get; set; }
    public string Failure { get; set; }
    public int Pid { get; set; }
    public long StartUtcTicks { get; set; }
    public string AvaloniaWindowClass { get; set; }
    public string StartupRoute { get; set; }
    public bool LoadingObserved { get; set; }
    public long LoadingObservedAtMs { get; set; } = -1;
    public bool TimedOut { get; set; }
    public bool NormalCloseRequested { get; set; }
    public bool NormalCloseObserved { get; set; }
    public bool DispatchAdmissionClosed { get; set; }
    public bool WorkerJoined { get; set; }
    public bool TerminationBoundaryRequired { get; set; }
    public string CancellationRequestedUtc { get; set; }
    public bool ImageReviewRequired { get; set; } = true;
    public string ScreenshotSha256 { get; set; }
    public string[] BeforeInvalid { get; set; }
    public string[] AfterInvalid { get; set; }
    public DesktopEvent[] Events { get; set; }
    public DesktopQueryFailure QueryFailure { get; set; }
    public Dictionary<string, object> InitialImageObservation { get; set; }
    public Dictionary<string, object> ImageFailure { get; set; }
}

// One ordinary app, one worker, one attempt. A stalled UIA/PrintWindow call cannot
// extend its stage: cancellation closes admission and requests termination of this
// runner process. Only the outer launcher may clean up the app, after runner exit.
public sealed class BoundedDesktopSmoke
{
    const string MainButton = "Main_PatchManager_Button";
    const string SetupWizardButton = "ContentRepoSetupWizard_Close_Button";
    const string ImportButton = "PatchManager_ImportPatchDatabase_Button";
    const string StatusLabel = "PatchManager_StatusMessage_Label";
    const string PatchList = "PatchManager_PatchList_List";
    const string ExpectedRow = "Offline ZIP Proof";
    const string Success = "Imported patch database for FE8U; list refreshed. No patches were applied. Restart recommended for cached data.";
    readonly Process app;
    readonly SafeProcessHandle appHandle;
    readonly string exe, root, rom, valid, invalid, database, descriptorHash, payloadHash;
    readonly int pid;
    readonly long startTicks;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly object sync = new object();
    readonly List<DesktopEvent> events = new List<DesktopEvent>();
    readonly DesktopResult result = new DesktopResult();
    readonly DesktopDispatchGate dispatch = new DesktopDispatchGate();
    string stage = "loading-handoff";
    long stageAt, stageLimit = 45000;
    string avaloniaClass;
    AutomationElement main, editor;
    IntPtr mainHandle, editorHandle;
    bool workerPassed;
    string workerFailure;

    BoundedDesktopSmoke(Process process, string executable, string outputRoot, string descriptor, string payload)
    {
        app = process;
        appHandle = app.SafeHandle;
        exe = executable;
        root = outputRoot;
        rom = Path.Combine(root, "fixtures", "zipdb-proof.gba");
        valid = Path.Combine(root, "fixtures", "zipdb-valid.zip");
        invalid = Path.Combine(root, "fixtures", "zipdb-invalid.zip");
        database = Path.Combine(root, "app", "config", "patch2", "FE8U");
        descriptorHash = descriptor;
        payloadHash = payload;
        pid = app.Id;
        startTicks = app.StartTime.ToUniversalTime().Ticks;
        result.Pid = pid;
        result.StartUtcTicks = startTicks;
    }

    public static DesktopResult Run(Process retainedProcess, string executable, string outputRoot,
        string descriptorSha256, string payloadSha256, System.Action<DesktopResult> requestStop)
    {
        Require(requestStop != null, "missing-termination-boundary");
        return new BoundedDesktopSmoke(retainedProcess, executable, outputRoot,
            descriptorSha256, payloadSha256).RunOnce(requestStop);
    }

    DesktopResult RunOnce(System.Action<DesktopResult> requestStop)
    {
        var worker = new Thread(() =>
        {
            try { Workflow(); lock (sync) workerPassed = true; }
            catch (Exception ex)
            {
                lock (sync)
                {
                    workerFailure = ex is Refusal ? ex.Message : ex.GetType().Name;
                    if (ex is Refusal && ex.Message == "deadline") result.TimedOut = true;
                }
            }
        });
        worker.IsBackground = true;
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        bool joined;
        while (!(joined = worker.Join(100)))
        {
            bool stop = false;
            lock (sync)
            {
                if (DesktopPolicy.Expired(clock.ElapsedMilliseconds, 240000) ||
                    DesktopPolicy.Expired(clock.ElapsedMilliseconds - stageAt, stageLimit))
                {
                    result.TimedOut = true;
                    result.Failure = "timeout:" + stage;
                    dispatch.Close();
                    result.DispatchAdmissionClosed = true;
                    result.TerminationBoundaryRequired = true;
                    result.CancellationRequestedUtc = DateTime.UtcNow.ToString("o");
                    result.Events = events.ToArray();
                    stop = true;
                }
            }
            if (!stop) continue;
            // Do not hold either lock while publishing or while the provider is blocked.
            // A failed publication falls back to the launcher's existing outer deadline.
            try { requestStop(result); }
            catch { lock (sync) result.Failure = "stop-receipt-unpublished"; }
            // Never return a live worker to run.ps1. The separately retained PS7 process
            // is the termination boundary if an admitted UIA/native call cannot finish.
            while (!(joined = worker.Join(100))) { }
            break;
        }
        dispatch.Close();
        Require(dispatch.CanReturn(joined), "worker-not-joined");
        lock (sync)
        {
            result.DispatchAdmissionClosed = true;
            result.WorkerJoined = true;
            result.Passed = workerPassed && !result.TimedOut && result.NormalCloseObserved;
            if (!result.Passed && result.Failure == null) result.Failure = workerFailure ?? "incomplete";
            result.Events = events.ToArray();
        }
        return result;
    }

    sealed class Refusal : Exception { public Refusal(string code) : base(code) { } }
    static void Require(bool condition, string code) { if (!condition) throw new Refusal(code); }

    void Record(string kind, string code, IntPtr handle = default(IntPtr))
    {
        lock (sync)
        {
            Require(!dispatch.IsClosed, "cancelled");
            Require(events.Count < 240, "event-bound");
            events.Add(new DesktopEvent { Utc = DateTime.UtcNow.ToString("o"), ElapsedMs = clock.ElapsedMilliseconds,
                Kind = kind, Code = code, Window = Key(handle) });
        }
    }

    void Stage(string name, int milliseconds)
    {
        Guard();
        lock (sync) { stage = name; stageAt = clock.ElapsedMilliseconds; stageLimit = milliseconds; }
        Record("stage", name + ":" + milliseconds);
        Require(BoundedWindowsReadiness.Capture().Ready, "readiness-refused");
        Record("observation", "own-session-ready-at-sample-only");
    }

    void Guard()
    {
        lock (sync)
        {
            Require(!dispatch.IsClosed, "cancelled");
            Require(!DesktopPolicy.Expired(clock.ElapsedMilliseconds, 240000) &&
                !DesktopPolicy.Expired(clock.ElapsedMilliseconds - stageAt, stageLimit), "deadline");
        }
    }

    void Identity()
    {
        Guard();
        double before = ImageRemaining();
        var observation = BoundedProcessImage.NewObservation("desktop-app", exe);
        observation["remainingBeforeMs"] = before;
        try
        {
            Require(!app.HasExited && app.Id == pid && app.StartTime.ToUniversalTime().Ticks == startTicks,
                "process-identity");
        }
        catch
        {
            observation["code"] = "identity-refused";
            CaptureImage(observation);
            throw new Refusal("process-identity");
        }
        var read = BoundedProcessImage.Read(appHandle);
        observation = BoundedProcessImage.Describe(read, exe, StringComparison.Ordinal, "desktop-app");
        observation["remainingBeforeMs"] = before;
        if ((string)observation["code"] == "image-observed")
        {
            bool bound;
            try { bound = DesktopPolicy.Process(pid, startTicks, exe, app.Id,
                app.StartTime.ToUniversalTime().Ticks, read.Path, app.HasExited); }
            catch { bound = false; }
            if (!bound) observation["code"] = "identity-refused";
        }
        observation["remainingAfterMs"] = ImageRemaining();
        if ((double)observation["remainingAfterMs"] <= 0 && (string)observation["code"] == "image-observed")
            observation["code"] = "image-deadline";
        CaptureImage(observation);
        Guard();
        Require((string)observation["code"] == "image-observed", "process-identity");
    }

    double ImageRemaining()
    {
        lock (sync) return Math.Min(240000 - clock.ElapsedMilliseconds,
            stageLimit - (clock.ElapsedMilliseconds - stageAt));
    }

    void CaptureImage(Dictionary<string, object> observation)
    {
        lock (sync)
        {
            if (result.InitialImageObservation == null) result.InitialImageObservation = observation;
            if ((string)observation["code"] != "image-observed" && result.ImageFailure == null)
                result.ImageFailure = observation;
        }
    }

    void Action(string code, AutomationElement window)
    {
        Identity();
        Require(BoundedWindowsReadiness.Capture().Ready, "readiness-lost");
        ValidateWindow(window, Class(Handle(window)) == "#32770" ? "#32770" : avaloniaClass);
        Record("action", code, Handle(window));
        Guard();
    }

    void Dispatch(System.Action operation)
    {
        Guard();
        Require(dispatch.TryBegin(), "dispatch-admission-closed");
        try { Guard(); operation(); }
        finally { dispatch.End(); }
    }

    static uint Key(IntPtr handle) => DesktopHwnd.Key(handle.ToInt64());
    static bool Same(IntPtr first, IntPtr second) => DesktopHwnd.Same(first, second);
    IntPtr Handle(AutomationElement element)
    {
        Require(element != null && element.Current.ProcessId == pid, "uia-handle-pid");
        return DesktopHwnd.Pointer(DesktopHwnd.Key(element.Current.NativeWindowHandle));
    }
    static int Pid(IntPtr window) { uint id; Native.GetWindowThreadProcessId(window, out id); return checked((int)id); }
    string Class(IntPtr window)
    {
        Require(window != IntPtr.Zero && Pid(window) == pid, "native-class-pid");
        var text = new StringBuilder(128);
        Require(Native.GetClassNameW(window, text, text.Capacity) > 0, "native-class");
        return text.ToString();
    }

    bool OwnerChain(IntPtr window)
    {
        var seen = new HashSet<uint> { Key(window) };
        for (int i = 0; i < 8; i++)
        {
            if (Pid(window) != pid) return false;
            window = DesktopHwnd.Pointer(Key(Native.GetWindow(window, 4)));
            if (window == IntPtr.Zero) return true;
            if (!seen.Add(Key(window)) || Pid(window) != pid || !Native.IsWindow(window) ||
                !Same(Native.GetAncestor(window, 2), window) || !DesktopPolicy.AvaloniaClass(Class(window)))
                return false;
        }
        return false;
    }

    void ValidateWindow(AutomationElement window, string expectedClass = null)
    {
        Identity();
        Require(window != null && window.Current.ProcessId == pid, "window-uia-pid");
        IntPtr h = Handle(window);
        string actual = Class(h);
        string expected = expectedClass ?? avaloniaClass;
        if (expected == null) { Require(DesktopPolicy.AvaloniaClass(actual), "avalonia-class"); expected = actual; }
        Require(DesktopPolicy.Window(pid, Pid(h), window.Current.ProcessId, Key(h),
            Key(Native.GetAncestor(h, 2)), actual, expected,
            Native.IsWindow(h) && Native.IsWindowVisible(h) && !window.Current.IsOffscreen,
            OwnerChain(h)), "window-ownership");
    }

    sealed class OwnedTreeAdapter : IDesktopOwnedTreeAdapter<AutomationElement>
    {
        readonly BoundedDesktopSmoke owner;
        public DesktopTreeBudget Budget { private get; set; }
        internal OwnedTreeAdapter(BoundedDesktopSmoke owner) { this.owner = owner; }
        public IReadOnlyList<AutomationElement> Seeds()
        {
            var desktop = Budget.Call(() => AutomationElement.RootElement);
            var seeds = Budget.Call(() => desktop.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, owner.pid)));
            if (seeds.Count > 8) throw new DesktopTreeGuardException("query-seed-bound");
            var result = new List<AutomationElement>();
            for (int i = 0; i < seeds.Count; i++) result.Add(seeds[i]);
            return result;
        }
        public int ProcessId(AutomationElement node) => Budget.Call(() => node.Current.ProcessId);
        public int[] Identity(AutomationElement node) => Budget.Call(() => node.GetRuntimeId());
        public uint Handle(AutomationElement node) => Budget.Call(() => DesktopHwnd.Key(node.Current.NativeWindowHandle));
        public AutomationElement Parent(AutomationElement node) => Budget.Call(() => TreeWalker.RawViewWalker.GetParent(node));
        public AutomationElement FirstChild(AutomationElement node) => Budget.Call(() => TreeWalker.RawViewWalker.GetFirstChild(node));
        public AutomationElement NextSibling(AutomationElement node) => Budget.Call(() => TreeWalker.RawViewWalker.GetNextSibling(node));
        public AutomationElement FromHandle(uint handle) => Budget.Call(() => AutomationElement.FromHandle(DesktopHwnd.Pointer(handle)));
        public bool Alive(uint handle) => Budget.Call(() => Native.IsWindow(DesktopHwnd.Pointer(handle)));
        public int NativePid(uint handle) => Budget.Call(() => Pid(DesktopHwnd.Pointer(handle)));
        public uint NativeRoot(uint handle) => Budget.Call(() => Key(Native.GetAncestor(DesktopHwnd.Pointer(handle), 2)));
        public uint Owner(uint handle) => Budget.Call(() => Key(Native.GetWindow(DesktopHwnd.Pointer(handle), 4)));
        public string Class(uint handle) => Budget.Call(() =>
        {
            var text = new StringBuilder(128);
            if (Native.GetClassNameW(DesktopHwnd.Pointer(handle), text, text.Capacity) <= 0)
                throw new DesktopTreeGuardException("query-native-class-unavailable");
            return text.ToString();
        });
        public bool Visible(uint handle) => Budget.Call(() => Native.IsWindowVisible(DesktopHwnd.Pointer(handle)));
        public bool Offscreen(AutomationElement node) => Budget.Call(() => node.Current.IsOffscreen);
        public bool Matches(AutomationElement node, DesktopSelector selector)
        {
            switch (selector)
            {
                case DesktopSelector.Loading:
                    return Budget.Call(() => node.Current.ControlType) == ControlType.Text &&
                        Budget.Call(() => node.Current.Name) == "Recovering patch database…";
                case DesktopSelector.FilenameEdit:
                    return Budget.Call(() => node.Current.ControlType) == ControlType.Edit;
                case DesktopSelector.Row:
                    return Budget.Call(() => node.Current.ControlType) == ControlType.ListItem;
                case DesktopSelector.RowName:
                    return Budget.Call(() => node.Current.Name) == ExpectedRow;
                default:
                    string id;
                    switch (selector)
                    {
                        case DesktopSelector.Main: id = MainButton; break;
                        case DesktopSelector.Wizard: id = SetupWizardButton; break;
                        case DesktopSelector.Import: id = ImportButton; break;
                        case DesktopSelector.Status: id = StatusLabel; break;
                        case DesktopSelector.List: id = PatchList; break;
                        case DesktopSelector.FilenameHost: id = "1148"; break;
                        case DesktopSelector.PickerOpen: id = "1"; break;
                        case DesktopSelector.ConfirmationYes: id = "MessageBoxContent_Yes_Button"; break;
                        case DesktopSelector.ConfirmationMessage: id = "MessageBoxContent_Message_Label"; break;
                        default: throw new DesktopTreeGuardException("query-selector");
                    }
                    return Budget.Call(() => node.Current.AutomationId) == id;
            }
        }
    }

    TValue TreeCall<TValue>(Func<TValue> operation)
    {
        try { return operation(); }
        catch (DesktopTreeException ex)
        {
            lock (sync) { if (result.QueryFailure == null) result.QueryFailure = ex.Failure; }
            throw new Refusal(ex.Message);
        }
    }

    DesktopOwnedTree<AutomationElement> NewTree(AutomationElement seed = null)
    {
        Identity();
        var tree = new DesktopOwnedTree<AutomationElement>(new OwnedTreeAdapter(this), pid, avaloniaClass, stage, () =>
        {
            try { Guard(); }
            catch (Refusal ex) { throw new DesktopTreeGuardException(ex.Message); }
        });
        TreeCall(() => { if (seed == null) tree.Discover(); else tree.Seed(seed); return true; });
        return tree;
    }

    static DesktopSelector Selector(string id)
    {
        switch (id)
        {
            case MainButton: return DesktopSelector.Main;
            case SetupWizardButton: return DesktopSelector.Wizard;
            case ImportButton: return DesktopSelector.Import;
            case StatusLabel: return DesktopSelector.Status;
            case PatchList: return DesktopSelector.List;
            case "MessageBoxContent_Yes_Button": return DesktopSelector.ConfirmationYes;
            case "MessageBoxContent_Message_Label": return DesktopSelector.ConfirmationMessage;
            default: throw new Refusal("query-selector");
        }
    }

    List<AutomationElement> Find(AutomationElement window, DesktopSelector selector, int max = 16,
        AutomationElement subtree = null, DesktopOwnedTree<AutomationElement> tree = null)
    {
        tree = tree ?? NewTree(window);
        return TreeCall(() => tree.Find(tree.WindowKey(window, selector), subtree, selector, max));
    }

    TValue QueryRead<TValue>(DesktopOwnedTree<AutomationElement> tree, AutomationElement window,
        AutomationElement node, DesktopSelector selector, Func<TValue> read)
    {
        return TreeCall(() => tree.Read(tree.WindowKey(window, selector), node, selector, read));
    }

    void ValidateControl(AutomationElement control, AutomationElement window,
        DesktopSelector selector = DesktopSelector.Membership)
    {
        var tree = NewTree(window);
        TreeCall(() => { tree.Validate(tree.WindowKey(window, selector), control, selector); return true; });
    }

    AutomationElement Control(AutomationElement window, string id, ControlType type, bool active = true,
        DesktopOwnedTree<AutomationElement> tree = null)
    {
        tree = tree ?? NewTree(window);
        var selector = Selector(id);
        var found = Find(window, selector, tree: tree);
        Require(found.Count <= 1, "ambiguous-control");
        if (found.Count == 0) return null;
        var control = found[0];
        Require(QueryRead(tree, window, control, selector, () => control.Current.ControlType) == type, "control-type");
        if (active && (!QueryRead(tree, window, control, selector, () => control.Current.IsEnabled) ||
            QueryRead(tree, window, control, selector, () => control.Current.IsOffscreen))) return null;
        return control;
    }

    AutomationElement WindowFor(string id, ControlType type, bool active = true)
    {
        AutomationElement result = null;
        var tree = NewTree();
        for (int i = 0; i < tree.Windows.Count; i++)
        {
            var owned = tree.Windows[i];
            if (!TreeCall(() => tree.Visible(owned)) || owned.Class == "#32770") continue;
            var window = owned.Element;
            if (Control(window, id, type, active, tree) == null) continue;
            Require(result == null, "ambiguous-window");
            result = window;
        }
        return result;
    }

    T Await<T>(Func<T> probe) where T : class
    {
        while (true)
        {
            Identity();
            T value = probe();
            if (value != null) return value;
            Thread.Sleep(50);
        }
    }

    void Invoke(AutomationElement window, string id)
    {
        var control = Control(window, id, ControlType.Button);
        Require(control != null, "action-control-missing");
        var invoke = (InvokePattern)control.GetCurrentPattern(InvokePattern.Pattern);
        Action("invoke:" + id, window);
        ValidateControl(control, window, Selector(id));
        Require(control.Current.IsEnabled && !control.Current.IsOffscreen, "action-control-state");
        Dispatch(() => invoke.Invoke());
    }

    DesktopStartupSample<AutomationElement> ReadStartupSample()
    {
        var tree = NewTree();
        return TreeCall(() => DesktopStartupSample<AutomationElement>.Capture(tree, owned =>
        {
            var window = owned.Element;
            string windowClass = owned.Class;
            Require(DesktopPolicy.AvaloniaClass(windowClass), "startup-root-class");
            var mainControl = Control(window, MainButton, ControlType.Button, false, tree);
            var wizardControl = Control(window, SetupWizardButton, ControlType.Button, false, tree);
            bool loading = false;
            if (QueryRead(tree, window, window, DesktopSelector.Loading, () => window.Current.Name) == "FEBuilderGBA")
            {
                var labels = Find(window, DesktopSelector.Loading, tree: tree);
                Require(labels.Count <= 1, "startup-ambiguous-loading-control");
                loading = labels.Count == 1 &&
                    !QueryRead(tree, window, labels[0], DesktopSelector.Loading, () => labels[0].Current.IsOffscreen);
            }
            return new DesktopStartupRoot(owned.Handle, owned.Owner,
                windowClass, TreeCall(() => tree.Visible(owned)),
                mainControl != null && !QueryRead(tree, window, mainControl, DesktopSelector.Main,
                    () => mainControl.Current.IsOffscreen), loading,
                wizardControl != null && !QueryRead(tree, window, wizardControl, DesktopSelector.Wizard,
                    () => wizardControl.Current.IsOffscreen));
        }));
    }

    DesktopStartupDecision ObserveStartup(DesktopStartupObservation observation,
        DesktopStartupSample<AutomationElement> sample, bool revalidate)
    {
        bool loadingExists = observation.LoadingObserved &&
            Native.IsWindow(DesktopHwnd.Pointer((uint)observation.LoadingHandle));
        DesktopStartupDecision decision;
        try
        {
            decision = TreeCall(() => sample.Observe(observation, clock.ElapsedMilliseconds, loadingExists, revalidate));
        }
        catch (InvalidOperationException ex) { throw new Refusal(ex.Message); }
        bool firstLoading;
        lock (sync)
        {
            firstLoading = observation.LoadingObserved && !result.LoadingObserved;
            result.LoadingObserved = observation.LoadingObserved;
            result.LoadingObservedAtMs = observation.LoadingAt;
            if (observation.WindowClass != null)
            {
                avaloniaClass = observation.WindowClass;
                result.AvaloniaWindowClass = avaloniaClass;
            }
        }
        if (firstLoading)
            Record("observation", "loading-visible:Recovering-patch-database",
                DesktopHwnd.Pointer((uint)observation.LoadingHandle));
        return decision;
    }

    void Handoff()
    {
        Stage("loading-handoff", 45000);
        var observation = new DesktopStartupObservation();
        while (true)
        {
            var sample = ReadStartupSample();
            var decision = ObserveStartup(observation, sample, false);
            if (decision.Ready)
            {
                var acceptance = ReadStartupSample();
                decision = ObserveStartup(observation, acceptance, true);
                if (decision.Ready)
                {
                    AutomationElement acceptedMain = null;
                    foreach (var window in acceptance.Windows)
                    {
                        ValidateWindow(window, decision.WindowClass);
                        IntPtr h = Handle(window);
                        if (Key(h) == decision.MainHandle)
                        {
                            var control = Control(window, MainButton, ControlType.Button, false, acceptance.Tree);
                            Require(control != null && !QueryRead(acceptance.Tree, window, control,
                                DesktopSelector.Main, () => control.Current.IsOffscreen),
                                "startup-main-acceptance-control");
                            acceptedMain = window;
                        }
                        else
                        {
                            Require(Key(h) == decision.WizardHandle &&
                                Key(Native.GetWindow(h, 4)) == decision.MainHandle,
                                "startup-setup-wizard");
                            var control = Control(window, SetupWizardButton, ControlType.Button, false, acceptance.Tree);
                            Require(control != null && !QueryRead(acceptance.Tree, window, control,
                                DesktopSelector.Wizard, () => control.Current.IsOffscreen),
                                "startup-wizard-acceptance-control");
                        }
                    }
                    Require(acceptedMain != null, "startup-main-acceptance-root");
                    Require(!observation.LoadingObserved ||
                        !Native.IsWindow(DesktopHwnd.Pointer((uint)observation.LoadingHandle)),
                        "startup-loading-still-present");
                    ValidateWindow(acceptedMain, decision.WindowClass);
                    var mainControl = Control(acceptedMain, MainButton, ControlType.Button, false, acceptance.Tree);
                    Require(mainControl != null && !QueryRead(acceptance.Tree, acceptedMain, mainControl,
                        DesktopSelector.Main, () => mainControl.Current.IsOffscreen),
                        "startup-main-acceptance-control");
                    TreeCall(() => { acceptance.CheckRevision(); return true; });
                    main = acceptedMain;
                    mainHandle = DesktopHwnd.Pointer((uint)decision.MainHandle);
                    lock (sync) result.StartupRoute = decision.Route;
                    Record("observation", decision.Route, mainHandle);
                    return;
                }
            }
            Identity();
            Thread.Sleep(25);
        }
    }

    void OpenEditor()
    {
        Stage("open-editor", 30000);
        bool wizardClosed = false;
        Await(() =>
        {
            var wizard = WindowFor("ContentRepoSetupWizard_Close_Button", ControlType.Button);
            if (wizard != null)
            {
                Require(!wizardClosed && Same(Native.GetWindow(Handle(wizard), 4), mainHandle), "setup-wizard-owner");
                wizardClosed = true;
                Invoke(wizard, "ContentRepoSetupWizard_Close_Button");
            }
            return Control(main, MainButton, ControlType.Button);
        });
        Invoke(main, MainButton);
        editor = Await(() => WindowFor(ImportButton, ControlType.Button));
        editorHandle = Handle(editor);
        Require(!Same(editorHandle, mainHandle) && Key(Native.GetWindow(editorHandle, 4)) == 0,
            "nonmodal-editor-root");
        Require(Control(editor, PatchList, ControlType.List) != null, "editor-list-missing");
        Record("observation", "real-patch-manager-open", editorHandle);
    }

    AutomationElement Picker()
    {
        AutomationElement selected = null;
        var tree = NewTree();
        for (int i = 0; i < tree.Windows.Count; i++)
        {
            var owned = tree.Windows[i];
            if (!TreeCall(() => tree.Visible(owned)) || owned.Class != "#32770") continue;
            var window = owned.Element;
            string title = QueryRead(tree, window, window, DesktopSelector.PickerOpen, () => window.Current.Name);
            Require(owned.Owner == Key(editorHandle) &&
                (title == "Import Patch Database ZIP" || title == "Open"),
                "picker-title-owner");
            Require(selected == null, "ambiguous-picker");
            selected = window;
        }
        return selected;
    }

    void Choose(string path, string name)
    {
        Stage(name + "-picker", 30000);
        Invoke(editor, ImportButton);
        var picker = Await(Picker);
        IntPtr pickerHandle = Handle(picker);
        var tree = NewTree(picker);
        var fields = Find(picker, DesktopSelector.FilenameHost, tree: tree);
        Require(fields.Count == 1, "filename-host");
        var field = fields[0];
        if (QueryRead(tree, picker, field, DesktopSelector.FilenameHost, () => field.Current.ControlType) != ControlType.Edit)
        {
            var edits = Find(picker, DesktopSelector.FilenameEdit, 1, field, tree);
            Require(edits.Count == 1, "filename-edit");
            field = edits[0];
        }
        ValidateControl(field, picker, DesktopSelector.FilenameEdit);
        IntPtr fieldHandle = Handle(field);
        Require(fieldHandle != IntPtr.Zero && Class(fieldHandle) == "Edit" &&
            Pid(fieldHandle) == pid && Same(Native.GetAncestor(fieldHandle, 2), pickerHandle) &&
            field.Current.IsEnabled && !field.Current.IsOffscreen, "filename-native-identity");
        var value = (ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern);
        Require(!value.Current.IsReadOnly && Same(Native.GetForegroundWindow(), pickerHandle), "picker-not-active");
        Action("picker-set-exact-" + name + "-fixture", picker);
        Dispatch(() => value.SetValue(path));
        Require(value.Current.Value == path, "filename-value-mismatch");
        var buttons = Find(picker, DesktopSelector.PickerOpen, tree: tree);
        AutomationElement open = null;
        foreach (var candidate in buttons)
        {
            IntPtr h = Handle(candidate);
            if (QueryRead(tree, picker, candidate, DesktopSelector.PickerOpen,
                () => candidate.Current.ControlType) != ControlType.Button || h == IntPtr.Zero ||
                Class(h) != "Button" || Native.GetDlgCtrlID(h) != 1) continue;
            string title = QueryRead(tree, picker, candidate, DesktopSelector.PickerOpen, () => candidate.Current.Name);
            if (title != "Open" && title != "&Open") continue;
            Require(open == null, "ambiguous-open");
            open = candidate;
        }
        Require(open != null, "logical-open-missing");
        IntPtr button = Handle(open);
        Action("single-owned-active-picker-BM_CLICK", picker);
        ValidateControl(field, picker, DesktopSelector.FilenameEdit);
        ValidateControl(open, picker, DesktopSelector.PickerOpen);
        Require(Pid(button) == pid && Pid(fieldHandle) == pid && value.Current.Value == path &&
            DesktopPolicy.Picker(Key(pickerHandle), Key(Native.GetAncestor(fieldHandle, 2)),
                Key(Native.GetAncestor(button, 2)),
                Same(Native.GetWindow(pickerHandle, 4), editorHandle) && OwnerChain(pickerHandle),
                Same(Native.GetForegroundWindow(), pickerHandle),
                Native.IsWindowEnabled(pickerHandle) && Native.IsWindowEnabled(button) &&
                Native.IsWindowVisible(button) && open.Current.IsEnabled && !open.Current.IsOffscreen,
                Class(fieldHandle), Class(button), Native.GetDlgCtrlID(button)), "open-predicates-refused");
        Dispatch(() =>
        {
            UIntPtr response;
            Require(Native.SendMessageTimeoutW(button, 0x00F5, UIntPtr.Zero, IntPtr.Zero, 3, 2000, out response) != IntPtr.Zero,
                "owned-open-message-failed");
        });
        Await(() => !Native.IsWindow(pickerHandle) ? "dismissed" : null);
        Record("observation", name + "-picker-destroyed-after-single-open", pickerHandle);
    }

    string Status()
    {
        var tree = NewTree(editor);
        var label = Control(editor, StatusLabel, ControlType.Text, false, tree);
        if (label == null || QueryRead(tree, editor, label, DesktopSelector.Status, () => label.Current.IsOffscreen)) return "";
        string text = QueryRead(tree, editor, label, DesktopSelector.Status, () => label.Current.Name);
        Require(text != null && text.Length <= 4096, "status-bound");
        return text;
    }

    string Row()
    {
        var tree = NewTree(editor);
        var list = Control(editor, PatchList, ControlType.List, tree: tree);
        Require(list != null, "patch-list");
        var rows = Find(editor, DesktopSelector.Row, 1, list, tree);
        Require(rows.Count == 1, "expected-single-row");
        ValidateControl(rows[0], editor, DesktopSelector.Row);
        Require(!QueryRead(tree, editor, rows[0], DesktopSelector.Row, () => rows[0].Current.IsOffscreen), "row-not-visible");
        var names = Find(editor, DesktopSelector.RowName, 2, rows[0], tree);
        bool matched = QueryRead(tree, editor, rows[0], DesktopSelector.RowName, () => rows[0].Current.Name) == ExpectedRow;
        Require(names.Count <= 2, "row-name-bound");
        for (int i = 0; i < names.Count; i++)
        {
            ValidateControl(names[i], editor, DesktopSelector.RowName);
            matched |= !QueryRead(tree, editor, names[i], DesktopSelector.RowName, () => names[i].Current.IsOffscreen);
        }
        Require(matched, "expected-row-missing");
        return ExpectedRow;
    }

    void Workflow()
    {
        Handoff();
        OpenEditor();
        Choose(valid, "valid");
        Stage("valid-confirmation-import", 30000);
        var confirmation = Await(() => WindowFor("MessageBoxContent_Yes_Button", ControlType.Button));
        var confirmationTree = NewTree(confirmation);
        // Current8ad constructs EditorHostWindow before configuring MessageBoxContent.
        // Its native title stays the default; the actual prompt below identifies the operation.
        Require(Same(Native.GetWindow(Handle(confirmation), 4), editorHandle) &&
            QueryRead(confirmationTree, confirmation, confirmation, DesktopSelector.ConfirmationYes,
                () => confirmation.Current.Name) == "FEBuilderGBA", "confirmation-owner-title");
        var message = Control(confirmation, "MessageBoxContent_Message_Label", ControlType.Text, tree: confirmationTree);
        Require(message != null, "confirmation-message");
        string text = QueryRead(confirmationTree, confirmation, message, DesktopSelector.ConfirmationMessage,
            () => message.Current.Name);
        Require(text != null && text.Length <= 4096 &&
            text.StartsWith("Import the validated patch database for FE8U?", StringComparison.Ordinal) &&
            text.Contains("Target: " + database) && text.Contains("Files: 2;") &&
            text.Contains("No patches will be applied to the ROM."), "confirmation-content");
        Record("observation", "real-valid-two-file-confirmation", Handle(confirmation));
        Invoke(confirmation, "MessageBoxContent_Yes_Button");
        Await(() => Status() == Success ? "success" : null);
        string rowBefore = Row();
        Require(Hash(Path.Combine(database, "proof", "PATCH_offline.txt")) == descriptorHash &&
            Hash(Path.Combine(database, "proof", "payload.bin")) == payloadHash, "installed-files");
        string[] before = Snapshot();
        lock (sync) result.BeforeInvalid = before;
        Record("assertion", "valid-import-success-exact-row-and-installed-files", editorHandle);
        Stage("owned-editor-capture", 10000);
        Capture();
        Choose(invalid, "invalid");
        Stage("invalid-rejection-preservation", 30000);
        Await(() => Status().StartsWith("Patch database import failed:", StringComparison.OrdinalIgnoreCase)
            ? "rejected" : null);
        string rowAfter = Row();
        string[] after = Snapshot();
        lock (sync) result.AfterInvalid = after;
        Require(DesktopPolicy.Preserved(before, after, rowBefore, rowAfter, true), "rejection-preservation");
        Record("assertion", "invalid-rejected-row-database-ROM-and-both-ZIPs-unchanged", editorHandle);
        Stage("normal-main-close-editor-still-open", 20000);
        ValidateWindow(main);
        ValidateWindow(editor);
        Require(Control(editor, ImportButton, ControlType.Button) != null && Row() == ExpectedRow,
            "editor-not-open-before-close");
        var close = (WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern);
        Action("normal-main-WindowPattern.Close-with-editor-open", main);
        lock (sync) result.NormalCloseRequested = true;
        Dispatch(() => close.Close());
        while (!app.HasExited) { Guard(); Thread.Sleep(50); }
        bool mainGone = !Native.IsWindow(mainHandle), editorGone = !Native.IsWindow(editorHandle);
        Require(DesktopPolicy.NormalClose(true, true, mainGone, editorGone, app.HasExited, app.ExitCode, false, false),
            "normal-close-not-proved");
        // A close handler must not silently save or mutate the source files either.
        Require(DesktopPolicy.Preserved(before, Snapshot(), rowBefore, rowAfter, true), "close-mutated-files");
        Record("assertion", "normal-main-and-editor-destroyed-app-exit-zero");
        lock (sync) { Guard(); result.NormalCloseObserved = true; }
    }

    static void Plain(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "reparse-path");
    }

    string Hash(string path)
    {
        Guard();
        Require(Path.GetFullPath(path).StartsWith(root + "\\", StringComparison.Ordinal), "hash-outside-owned-root");
        Plain(path);
        Require(new FileInfo(path).Length <= 16777216, "hash-size-bound");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = SHA256.Create())
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    string[] Snapshot()
    {
        string fingerprint = InstalledDatabaseSnapshot.Capture(database, Guard, Hash);
        return new[] { Hash(rom), Hash(valid), Hash(invalid), fingerprint };
    }

    void Capture()
    {
        Require(Row() == ExpectedRow && Status() == Success, "capture-editor-state");
        Action("PrintWindow-owned-editor-only", editor);
        Native.Rect rect;
        Require(Native.GetWindowRect(editorHandle, out rect), "capture-rectangle");
        int width = rect.R - rect.L, height = rect.B - rect.T;
        Require(width > 0 && height > 0 && width <= 4096 && height <= 4096, "capture-size");
        using (var bitmap = new Bitmap(width, height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr dc = graphics.GetHdc();
                try
                {
                    ValidateWindow(editor);
                    Dispatch(() => Require(Native.PrintWindow(editorHandle, dc, 2), "PrintWindow-failed-no-fallback"));
                }
                finally { graphics.ReleaseHdc(dc); }
            }
            Guard();
            Plain(root);
            string path = Path.Combine(root, "imported.png");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                bitmap.Save(stream, ImageFormat.Png);
            string hash = Hash(path);
            lock (sync) { Guard(); result.ScreenshotSha256 = hash; }
        }
        Record("observation", "owned-editor-image-written-independent-review-required", editorHandle);
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L, T, R, B; }
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr GetWindow(IntPtr h, uint command);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int GetClassNameW(IntPtr h, StringBuilder name, int count);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern bool GetWindowRect(IntPtr h, out Rect rect);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr SendMessageTimeoutW(IntPtr h, uint message, UIntPtr w, IntPtr l,
            uint flags, uint timeout, out UIntPtr response);
    }
}
