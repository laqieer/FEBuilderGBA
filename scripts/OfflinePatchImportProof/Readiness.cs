using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

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
        return sessionId > 0 && state == 0 && interactive && input && foreground;
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
