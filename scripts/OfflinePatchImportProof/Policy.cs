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
