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
                Kind = kind, Code = code, Window = handle.ToInt64() });
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
        Require(!app.HasExited, "owned-app-exited");
        Require(DesktopPolicy.Process(pid, startTicks, exe, app.Id,
            app.StartTime.ToUniversalTime().Ticks, app.MainModule.FileName, app.HasExited), "process-identity");
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

    static IntPtr Handle(AutomationElement element) => new IntPtr(element.Current.NativeWindowHandle);
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
        var seen = new HashSet<IntPtr> { window };
        for (int i = 0; i < 8; i++)
        {
            if (Pid(window) != pid) return false;
            window = Native.GetWindow(window, 4);
            if (window == IntPtr.Zero) return true;
            if (!seen.Add(window) || Pid(window) != pid || !Native.IsWindow(window) ||
                Native.GetAncestor(window, 2) != window || !DesktopPolicy.AvaloniaClass(Class(window)))
                return false;
        }
        return false;
    }

    void ValidateWindow(AutomationElement window, string expectedClass = null)
    {
        Identity();
        IntPtr h = Handle(window);
        string actual = Class(h);
        string expected = expectedClass ?? avaloniaClass;
        if (expected == null) { Require(DesktopPolicy.AvaloniaClass(actual), "avalonia-class"); expected = actual; }
        Require(DesktopPolicy.Window(pid, Pid(h), window.Current.ProcessId, h.ToInt64(),
            Native.GetAncestor(h, 2).ToInt64(), actual, expected,
            Native.IsWindow(h) && Native.IsWindowVisible(h) && !window.Current.IsOffscreen,
            OwnerChain(h)), "window-ownership");
    }

    List<AutomationElement> Windows(bool startup = false)
    {
        Identity();
        // The desktop is only a provider query root; no unrelated element is read or traversed.
        var owned = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        Require(owned.Count <= 8, "owned-window-bound");
        var found = new List<AutomationElement>();
        for (int i = 0; i < owned.Count; i++)
        {
            Guard();
            Require(owned[i].Current.ProcessId == pid, "foreign-root");
            IntPtr h = Handle(owned[i]);
            if (h == IntPtr.Zero)
            {
                Require(!startup || owned[i].Current.IsOffscreen, "startup-root-native-handle");
                continue;
            }
            Require(Pid(h) == pid, "owned-root-native-pid");
            if (!Native.IsWindowVisible(h) || owned[i].Current.IsOffscreen) continue;
            string c = Class(h);
            Require(c == "#32770" || DesktopPolicy.AvaloniaClass(c), "unexpected-owned-class");
            ValidateWindow(owned[i], c == "#32770" ? c : avaloniaClass);
            found.Add(owned[i]);
        }
        return found;
    }

    // A provider subtree is visited only after validating its exact native top-level.
    // Every returned descendant must remain inside that same PID/native root.
    List<AutomationElement> Find(AutomationElement window, Condition condition, int max = 16)
    {
        string c = Class(Handle(window));
        ValidateWindow(window, c == "#32770" ? c : avaloniaClass);
        var matches = window.FindAll(TreeScope.Descendants, condition);
        Require(matches.Count <= max, "control-bound");
        var found = new List<AutomationElement>();
        for (int i = 0; i < matches.Count; i++)
        {
            ValidateControl(matches[i], window);
            found.Add(matches[i]);
        }
        return found;
    }

    void ValidateControl(AutomationElement control, AutomationElement window)
    {
        Identity();
        Require(control.Current.ProcessId == pid, "foreign-control");
        AutomationElement current = control;
        IntPtr expected = Handle(window);
        for (int depth = 0; depth < 32 && current != null; depth++)
        {
            Require(current.Current.ProcessId == pid, "foreign-control-ancestor");
            IntPtr h = Handle(current);
            if (h != IntPtr.Zero)
            {
                Require(Pid(h) == pid && Native.GetAncestor(h, 2) == expected, "control-native-root");
                if (h == expected) return;
            }
            current = TreeWalker.ControlViewWalker.GetParent(current);
        }
        throw new Refusal("control-ancestry-bound");
    }

    AutomationElement Control(AutomationElement window, string id, ControlType type, bool active = true)
    {
        var found = Find(window, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        Require(found.Count <= 1, "ambiguous-control");
        if (found.Count == 0) return null;
        var control = found[0];
        Require(control.Current.ControlType == type, "control-type");
        if (active && (!control.Current.IsEnabled || control.Current.IsOffscreen)) return null;
        return control;
    }

    AutomationElement WindowFor(string id, ControlType type, bool active = true)
    {
        AutomationElement result = null;
        foreach (var window in Windows())
        {
            if (Class(Handle(window)) == "#32770") continue;
            if (Control(window, id, type, active) == null) continue;
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
        ValidateControl(control, window);
        Require(control.Current.IsEnabled && !control.Current.IsOffscreen, "action-control-state");
        Dispatch(() => invoke.Invoke());
    }

    sealed class StartupSample
    {
        public readonly List<AutomationElement> Windows = new List<AutomationElement>();
        public readonly List<DesktopStartupRoot> Roots = new List<DesktopStartupRoot>();
    }

    StartupSample ReadStartupSample()
    {
        var sample = new StartupSample();
        foreach (var window in Windows(true))
        {
            IntPtr h = Handle(window);
            string windowClass = Class(h);
            Require(DesktopPolicy.AvaloniaClass(windowClass), "startup-root-class");
            var mainControl = Control(window, MainButton, ControlType.Button, false);
            var wizardControl = Control(window, SetupWizardButton, ControlType.Button, false);
            bool loading = false;
            if (window.Current.Name == "FEBuilderGBA")
            {
                var labels = Find(window, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.NameProperty, "Recovering patch database…")));
                Require(labels.Count <= 1, "startup-ambiguous-loading-control");
                loading = labels.Count == 1 && !labels[0].Current.IsOffscreen;
            }
            sample.Windows.Add(window);
            sample.Roots.Add(new DesktopStartupRoot(h.ToInt64(), Native.GetWindow(h, 4).ToInt64(),
                windowClass, Native.IsWindow(h) && Native.IsWindowVisible(h) && !window.Current.IsOffscreen,
                mainControl != null && !mainControl.Current.IsOffscreen, loading,
                wizardControl != null && !wizardControl.Current.IsOffscreen));
        }
        return sample;
    }

    DesktopStartupDecision ObserveStartup(DesktopStartupObservation observation,
        StartupSample sample, bool revalidate)
    {
        bool loadingExists = observation.LoadingObserved &&
            Native.IsWindow(new IntPtr(observation.LoadingHandle));
        DesktopStartupDecision decision;
        try
        {
            decision = revalidate
                ? observation.Revalidate(clock.ElapsedMilliseconds, sample.Roots, loadingExists)
                : observation.Observe(clock.ElapsedMilliseconds, sample.Roots, loadingExists);
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
                new IntPtr(observation.LoadingHandle));
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
                        if (h.ToInt64() == decision.MainHandle)
                        {
                            var control = Control(window, MainButton, ControlType.Button, false);
                            Require(control != null && !control.Current.IsOffscreen,
                                "startup-main-acceptance-control");
                            acceptedMain = window;
                        }
                        else
                        {
                            Require(h.ToInt64() == decision.WizardHandle &&
                                Native.GetWindow(h, 4).ToInt64() == decision.MainHandle,
                                "startup-setup-wizard");
                            var control = Control(window, SetupWizardButton, ControlType.Button, false);
                            Require(control != null && !control.Current.IsOffscreen,
                                "startup-wizard-acceptance-control");
                        }
                    }
                    Require(acceptedMain != null, "startup-main-acceptance-root");
                    Require(!observation.LoadingObserved ||
                        !Native.IsWindow(new IntPtr(observation.LoadingHandle)),
                        "startup-loading-still-present");
                    ValidateWindow(acceptedMain, decision.WindowClass);
                    var mainControl = Control(acceptedMain, MainButton, ControlType.Button, false);
                    Require(mainControl != null && !mainControl.Current.IsOffscreen,
                        "startup-main-acceptance-control");
                    main = acceptedMain;
                    mainHandle = new IntPtr(decision.MainHandle);
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
                Require(!wizardClosed && Native.GetWindow(Handle(wizard), 4) == mainHandle, "setup-wizard-owner");
                wizardClosed = true;
                Invoke(wizard, "ContentRepoSetupWizard_Close_Button");
            }
            return Control(main, MainButton, ControlType.Button);
        });
        Invoke(main, MainButton);
        editor = Await(() => WindowFor(ImportButton, ControlType.Button));
        editorHandle = Handle(editor);
        Require(editorHandle != mainHandle && Native.GetWindow(editorHandle, 4) == IntPtr.Zero,
            "nonmodal-editor-root");
        Require(Control(editor, PatchList, ControlType.List) != null, "editor-list-missing");
        Record("observation", "real-patch-manager-open", editorHandle);
    }

    AutomationElement Picker()
    {
        AutomationElement selected = null;
        foreach (var window in Windows())
        {
            if (Class(Handle(window)) != "#32770") continue;
            Require(Native.GetWindow(Handle(window), 4) == editorHandle &&
                (window.Current.Name == "Import Patch Database ZIP" || window.Current.Name == "Open"),
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
        var fields = Find(picker, new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"));
        Require(fields.Count == 1, "filename-host");
        var field = fields[0];
        if (field.Current.ControlType != ControlType.Edit)
        {
            var edits = field.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            Require(edits.Count == 1, "filename-edit");
            field = edits[0];
        }
        ValidateControl(field, picker);
        IntPtr fieldHandle = Handle(field);
        Require(fieldHandle != IntPtr.Zero && Class(fieldHandle) == "Edit" &&
            Pid(fieldHandle) == pid && Native.GetAncestor(fieldHandle, 2) == pickerHandle &&
            field.Current.IsEnabled && !field.Current.IsOffscreen, "filename-native-identity");
        var value = (ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern);
        Require(!value.Current.IsReadOnly && Native.GetForegroundWindow() == pickerHandle, "picker-not-active");
        Action("picker-set-exact-" + name + "-fixture", picker);
        Dispatch(() => value.SetValue(path));
        Require(value.Current.Value == path, "filename-value-mismatch");
        var buttons = Find(picker, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
        AutomationElement open = null;
        foreach (var candidate in buttons)
        {
            IntPtr h = Handle(candidate);
            if (candidate.Current.ControlType != ControlType.Button || h == IntPtr.Zero ||
                Class(h) != "Button" || Native.GetDlgCtrlID(h) != 1 ||
                (candidate.Current.Name != "Open" && candidate.Current.Name != "&Open")) continue;
            Require(open == null, "ambiguous-open");
            open = candidate;
        }
        Require(open != null, "logical-open-missing");
        IntPtr button = Handle(open);
        Action("single-owned-active-picker-BM_CLICK", picker);
        ValidateControl(field, picker);
        ValidateControl(open, picker);
        Require(Pid(button) == pid && Pid(fieldHandle) == pid && value.Current.Value == path &&
            DesktopPolicy.Picker(pickerHandle.ToInt64(), Native.GetAncestor(fieldHandle, 2).ToInt64(),
                Native.GetAncestor(button, 2).ToInt64(),
                Native.GetWindow(pickerHandle, 4) == editorHandle && OwnerChain(pickerHandle),
                Native.GetForegroundWindow() == pickerHandle,
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
        var label = Control(editor, StatusLabel, ControlType.Text, false);
        if (label == null || label.Current.IsOffscreen) return "";
        string text = label.Current.Name;
        Require(text != null && text.Length <= 4096, "status-bound");
        return text;
    }

    string Row()
    {
        var list = Control(editor, PatchList, ControlType.List);
        Require(list != null, "patch-list");
        var rows = list.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Require(rows.Count == 1, "expected-single-row");
        ValidateControl(rows[0], editor);
        Require(!rows[0].Current.IsOffscreen, "row-not-visible");
        var names = rows[0].FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, ExpectedRow));
        bool matched = rows[0].Current.Name == ExpectedRow;
        Require(names.Count <= 2, "row-name-bound");
        for (int i = 0; i < names.Count; i++)
        {
            ValidateControl(names[i], editor);
            matched |= !names[i].Current.IsOffscreen;
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
        // Current8ad constructs EditorHostWindow before configuring MessageBoxContent.
        // Its native title stays the default; the actual prompt below identifies the operation.
        Require(Native.GetWindow(Handle(confirmation), 4) == editorHandle &&
            confirmation.Current.Name == "FEBuilderGBA", "confirmation-owner-title");
        var message = Control(confirmation, "MessageBoxContent_Message_Label", ControlType.Text);
        Require(message != null, "confirmation-message");
        string text = message.Current.Name;
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
