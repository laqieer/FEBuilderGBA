using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

public sealed class ProcessImageRead
{
    public string Path { get; }
    public string Code { get; }
    public int? NativeError { get; }
    public int Queries { get; }
    public bool HandleValid { get; }
    public int? Characters { get; }

    public ProcessImageRead(string path, string code, int? error, int queries, bool handleValid, int? characters)
    {
        Path = path; Code = code; NativeError = error; Queries = queries;
        HandleValid = handleValid; Characters = characters;
    }
}

public static class BoundedProcessImage
{
    public const int Capacity = 32768;
    public const int PreviewLimit = 256;
    internal delegate bool QueryImage(IntPtr handle, uint flags, char[] buffer, ref uint characters, out int error);
    static readonly HashSet<string> Roles = new HashSet<string>(StringComparer.Ordinal)
    {
        "prepare-initial", "prepare-cleanup", "launch-runner-initial", "launch-runner-cleanup",
        "launch-app-retain", "launch-app-cleanup", "run-self", "run-app-initial", "desktop-app",
        "supervision-self", "supervision-child-initial", "supervision-child-cleanup"
    };

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] image, ref uint characters);

    static bool NativeQuery(IntPtr handle, uint flags, char[] buffer, ref uint characters, out int error)
    {
        bool succeeded = QueryFullProcessImageNameW(handle, flags, buffer, ref characters);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public static ProcessImageRead Read(SafeProcessHandle retainedHandle) => Read(retainedHandle, NativeQuery);

    internal static ProcessImageRead Read(SafeProcessHandle retainedHandle, QueryImage query)
    {
        if (retainedHandle == null || retainedHandle.IsClosed || retainedHandle.IsInvalid)
            return new ProcessImageRead(null, "invalid-handle", null, 0, false, null);
        bool borrowed = false;
        try
        {
            retainedHandle.DangerousAddRef(ref borrowed);
            var buffer = new char[Capacity];
            uint characters = Capacity;
            int error;
            bool succeeded;
            try { succeeded = query(retainedHandle.DangerousGetHandle(), 0, buffer, ref characters, out error); }
            catch { return new ProcessImageRead(null, "query-exception", null, 1, true, null); }
            if (!succeeded)
                return new ProcessImageRead(null, "native-error", error, 1, true, null);
            if (characters == 0 || characters >= Capacity || buffer[characters] != '\0')
                return new ProcessImageRead(null, "invalid-image", null, 1, true, null);
            for (int i = 0; i < characters; i++)
            {
                char value = buffer[i];
                if (value == '\0' || char.IsLowSurrogate(value) ||
                    (char.IsHighSurrogate(value) && (++i >= characters || !char.IsLowSurrogate(buffer[i]))))
                    return new ProcessImageRead(null, "invalid-image", null, 1, true, (int)characters);
            }
            return new ProcessImageRead(new string(buffer, 0, (int)characters),
                "image-observed", null, 1, true, (int)characters);
        }
        catch (ObjectDisposedException) { return new ProcessImageRead(null, "invalid-handle", null, 0, false, null); }
        finally { if (borrowed) retainedHandle.DangerousRelease(); }
    }

    static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value))).ToLowerInvariant();

    public static Dictionary<string, object> NewObservation(string role, string expectedPath)
    {
        if (!Roles.Contains(role) || string.IsNullOrEmpty(expectedPath) || expectedPath.Length >= Capacity)
            throw new ArgumentException("Invalid fixed image observation binding.");
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["schema"] = "retained-process-image-observation-v1", ["role"] = role,
            ["method"] = "QueryFullProcessImageNameW", ["flags"] = 0, ["code"] = "not-queried",
            ["queries"] = 0, ["handleValid"] = null, ["nativeError"] = null, ["capacity"] = Capacity,
            ["returnedChars"] = null, ["expectedPathSha256"] = Digest(expectedPath),
            ["observedPathSha256"] = null, ["observedPathLength"] = null, ["observedPathPreview"] = null,
            ["previewTruncated"] = null, ["remainingBeforeMs"] = null, ["remainingAfterMs"] = null
        };
    }

    public static Dictionary<string, object> Describe(ProcessImageRead read, string expectedPath,
        StringComparison comparison, string role)
    {
        if (read == null || (comparison != StringComparison.Ordinal && comparison != StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid image observation source/comparison.");
        var observation = NewObservation(role, expectedPath);
        observation["code"] = read.Code;
        observation["queries"] = read.Queries;
        observation["handleValid"] = read.HandleValid;
        observation["nativeError"] = read.NativeError;
        observation["returnedChars"] = read.Characters;
        if (read.Code == "image-observed")
        {
            if (string.IsNullOrEmpty(read.Path) || read.Path.Length >= Capacity || read.Characters != read.Path.Length)
                throw new ArgumentException("Invalid image result shape.");
            int preview = Math.Min(PreviewLimit, read.Path.Length);
            if (preview < read.Path.Length && char.IsHighSurrogate(read.Path[preview - 1])) preview--;
            observation["observedPathLength"] = read.Path.Length;
            observation["observedPathSha256"] = Digest(read.Path);
            observation["observedPathPreview"] = read.Path.Substring(0, preview);
            observation["previewTruncated"] = preview < read.Path.Length;
            if (!string.Equals(read.Path, expectedPath, comparison)) observation["code"] = "image-mismatch";
        }
        return observation;
    }
}

public sealed class BoundedWindowsReadiness
{
    public int SessionId { get; private set; }
    public int ConnectionState { get; private set; }
    public bool UserInteractive { get; private set; }
    public bool ThreadDesktopReceivesInput { get; private set; }
    public bool ForegroundWindowPresent { get; private set; }
    public bool Ready
    {
        get
        {
            return Decide(SessionId, ConnectionState, UserInteractive,
                ThreadDesktopReceivesInput, ForegroundWindowPresent);
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(
        IntPtr handle, int index, out int value, uint length, out uint needed);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("wtsapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server, int sessionId, int informationClass, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr buffer);

    public static bool Decide(int sessionId, int state, bool interactive, bool input, bool foreground)
    {
        return sessionId > 0 && state == 0 && interactive && input;
    }

    public static BoundedWindowsReadiness Capture()
    {
        var result = new BoundedWindowsReadiness();
        uint sessionId;
        if (!ProcessIdToSessionId(GetCurrentProcessId(), out sessionId))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Current process session query failed.");
        result.SessionId = checked((int)sessionId);
        result.UserInteractive = Environment.UserInteractive;
        IntPtr buffer = IntPtr.Zero;
        uint bytes;
        try
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, result.SessionId, 8, out buffer, out bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSConnectState query failed.");
            if (buffer == IntPtr.Zero || bytes != sizeof(int))
                throw new InvalidOperationException("Unexpected WTSConnectState response.");
            result.ConnectionState = Marshal.ReadInt32(buffer);
            if (result.ConnectionState < 0 || result.ConnectionState > 9)
                throw new InvalidOperationException("Unknown WTS connection state.");
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                WTSFreeMemory(buffer);
        }

        // This borrowed handle must not be closed or used to change desktops.
        IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetThreadDesktop failed.");
        int input;
        uint needed;
        if (!GetUserObjectInformationW(desktop, 6, out input, sizeof(int), out needed))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Thread desktop UOI_IO query failed.");
        if (needed != sizeof(int) || (input != 0 && input != 1))
            throw new InvalidOperationException("Unexpected UOI_IO response.");
        result.ThreadDesktopReceivesInput = input != 0;
        result.ForegroundWindowPresent = GetForegroundWindow() != IntPtr.Zero;
        return result;
    }
}
