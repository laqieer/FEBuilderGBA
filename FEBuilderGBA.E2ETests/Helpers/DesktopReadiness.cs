using System.Runtime.InteropServices;

namespace FEBuilderGBA.E2ETests.Helpers;

public enum DesktopReadinessState { Unknown, Blocked, Ready }

public enum DesktopReadinessReason
{
    SessionQueryFailed,
    SessionZero,
    SessionStateUnknown,
    SessionInactive,
    ThreadDesktopUnknown,
    ThreadDesktopNotInput,
    NativeQueryFailed,
    ActiveInputDesktop
}

public readonly record struct DesktopObservation(uint? SessionId, int? ConnectionState, bool? IsInputDesktop);
public readonly record struct DesktopReadinessResult(DesktopReadinessState State, DesktopReadinessReason Reason);

public sealed class DesktopUnavailableException : InvalidOperationException
{
    public DesktopReadinessResult Readiness { get; }

    public DesktopUnavailableException(DesktopReadinessResult readiness)
        : base($"Windows GUI automation rejected: {readiness.State} ({readiness.Reason}). " +
               "See docs/WINDOWS-E2E-DESKTOP.md; the requested launch or capture was not admitted.")
    {
        Readiness = readiness;
    }
}

public static class DesktopReadiness
{
    public static DesktopReadinessResult Evaluate(DesktopObservation observation)
    {
        if (observation.SessionId == 0)
            return new(DesktopReadinessState.Blocked, DesktopReadinessReason.SessionZero);
        if (observation.ConnectionState is >= 1 and <= 9)
            return new(DesktopReadinessState.Blocked, DesktopReadinessReason.SessionInactive);
        if (observation.IsInputDesktop == false)
            return new(DesktopReadinessState.Blocked, DesktopReadinessReason.ThreadDesktopNotInput);
        if (observation.SessionId == null)
            return new(DesktopReadinessState.Unknown, DesktopReadinessReason.SessionQueryFailed);
        if (observation.ConnectionState != 0)
            return new(DesktopReadinessState.Unknown, DesktopReadinessReason.SessionStateUnknown);
        if (observation.IsInputDesktop != true)
            return new(DesktopReadinessState.Unknown, DesktopReadinessReason.ThreadDesktopUnknown);
        return new(DesktopReadinessState.Ready, DesktopReadinessReason.ActiveInputDesktop);
    }

    public static DesktopReadinessResult Probe() => Probe(new WindowsDesktopNative());

    internal static DesktopReadinessResult Probe(IDesktopReadinessNative native)
    {
        try
        {
            if (!native.TryGetOwnSession(out uint session))
                return Evaluate(default);
            if (session == 0)
                return Evaluate(new(session, null, null));

            int? state = null;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (native.QuerySessionState(session, out buffer, out uint bytes) &&
                    buffer != IntPtr.Zero && bytes == sizeof(int))
                {
                    int value = native.ReadState(buffer);
                    if (value is >= 0 and <= 9)
                        state = value;
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    native.FreeSessionBuffer(buffer);
            }
            if (state != 0)
                return Evaluate(new(session, state, null));

            // GetThreadDesktop returns a borrowed handle. Never close or switch it.
            IntPtr desktop = native.GetOwnThreadDesktop();
            bool? input = null;
            if (desktop != IntPtr.Zero &&
                native.QueryInputDesktop(desktop, out int valueInput, out uint inputBytes) &&
                inputBytes == sizeof(int) && valueInput is 0 or 1)
                input = valueInput == 1;
            return Evaluate(new(session, state, input));
        }
        catch (Exception)
        {
            return new(DesktopReadinessState.Unknown, DesktopReadinessReason.NativeQueryFailed);
        }
    }

    internal static void RequireReady(Func<DesktopReadinessResult> probe)
    {
        DesktopReadinessResult result;
        try
        {
            result = probe();
        }
        catch (Exception)
        {
            result = new(DesktopReadinessState.Unknown, DesktopReadinessReason.NativeQueryFailed);
        }
        if (result.State != DesktopReadinessState.Ready)
            throw new DesktopUnavailableException(result);
    }

    private sealed class WindowsDesktopNative : IDesktopReadinessNative
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);
        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr buffer);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(
            IntPtr handle, int index, out int info, uint length, out uint needed);

        public bool TryGetOwnSession(out uint session) => ProcessIdToSessionId(GetCurrentProcessId(), out session);
        public bool QuerySessionState(uint session, out IntPtr buffer, out uint bytes) =>
            WTSQuerySessionInformation(IntPtr.Zero, session, 8 /* WTSConnectState */, out buffer, out bytes);
        public int ReadState(IntPtr buffer) => Marshal.ReadInt32(buffer);
        public void FreeSessionBuffer(IntPtr buffer) => WTSFreeMemory(buffer);
        public IntPtr GetOwnThreadDesktop() => GetThreadDesktop(GetCurrentThreadId());
        public bool QueryInputDesktop(IntPtr desktop, out int input, out uint bytes) =>
            GetUserObjectInformation(desktop, 6 /* UOI_IO */, out input, sizeof(int), out bytes);
    }
}

internal interface IDesktopReadinessNative
{
    bool TryGetOwnSession(out uint session);
    bool QuerySessionState(uint session, out IntPtr buffer, out uint bytes);
    int ReadState(IntPtr buffer);
    void FreeSessionBuffer(IntPtr buffer);
    IntPtr GetOwnThreadDesktop();
    bool QueryInputDesktop(IntPtr desktop, out int input, out uint bytes);
}
