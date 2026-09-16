using System.Diagnostics;

namespace FEBuilderGBA.E2ETests.Helpers;

internal static class StartupCloseDiagnostics
{
    internal static bool CaptureAndCleanup(
        Func<IEnumerable<IntPtr>> windows,
        Action<IntPtr> capture,
        Action<string> report,
        OwnedProcessCleanup cleanup)
    {
        bool exitConfirmed;
        try
        {
            foreach (IntPtr window in windows())
            {
                try
                {
                    capture(window);
                }
                catch (WindowCaptureException ex) when (ex.InnerException is null)
                {
                    report($"Optional diagnostic capture refused; no screenshot evidence: {ex.Message}");
                }
            }
        }
        finally
        {
            exitConfirmed = cleanup.TryCleanup(5_000);
        }
        return exitConfirmed;
    }
}

internal sealed class OwnedProcessCleanup
{
    private readonly Func<bool> _hasExited;
    private readonly Action _kill;
    private readonly Func<int, bool> _waitForExit;
    private bool _attempted;
    private bool _exitConfirmed;

    internal OwnedProcessCleanup(Process process)
        : this(() => process.HasExited, () => process.Kill(),
            timeout => process.WaitForExit(timeout))
    {
        ArgumentNullException.ThrowIfNull(process);
    }

    internal OwnedProcessCleanup(Func<bool> hasExited, Action kill, Func<int, bool> waitForExit)
    {
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(kill);
        ArgumentNullException.ThrowIfNull(waitForExit);
        _hasExited = hasExited;
        _kill = kill;
        _waitForExit = waitForExit;
    }

    internal bool TryCleanup(int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (_attempted)
            return _exitConfirmed;

        _attempted = true;
        if (!_hasExited())
            _kill();
        _exitConfirmed = _waitForExit(timeoutMs);
        return _exitConfirmed;
    }
}
