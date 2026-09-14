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
