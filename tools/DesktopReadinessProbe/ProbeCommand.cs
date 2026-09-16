using FEBuilderGBA.E2ETests.Helpers;

namespace FEBuilderGBA.DesktopReadinessProbe;

internal static class ProbeCommand
{
    internal static int Run(
        string[] args, Func<DesktopReadinessResult> probe, TextWriter output)
    {
        (int ExitCode, string State, string Reason) result;
        if (args.Length == 0)
        {
            result = Error("OptInRequired");
        }
        else if (args.Length != 1 || args[0] != "--probe-own-desktop")
        {
            result = Error("InvalidArguments");
        }
        else
        {
            try
            {
                DesktopReadinessResult readiness = probe();
                int exitCode = (readiness.State, readiness.Reason) switch
                {
                    (DesktopReadinessState.Ready,
                        DesktopReadinessReason.ActiveInputDesktop) => 0,
                    (DesktopReadinessState.Blocked,
                        DesktopReadinessReason.SessionZero or
                        DesktopReadinessReason.SessionInactive or
                        DesktopReadinessReason.ThreadDesktopNotInput) => 2,
                    (DesktopReadinessState.Unknown,
                        DesktopReadinessReason.SessionQueryFailed or
                        DesktopReadinessReason.SessionStateUnknown or
                        DesktopReadinessReason.ThreadDesktopUnknown or
                        DesktopReadinessReason.NativeQueryFailed) => 3,
                    _ => 4
                };
                result = exitCode == 4
                    ? Error("InvalidProbeResult")
                    : (exitCode, readiness.State.ToString(), readiness.Reason.ToString());
            }
            catch (Exception)
            {
                result = Error("ProbeFailed");
            }
        }

        try
        {
            output.Write($"State={result.State};Reason={result.Reason}\n");
        }
        catch (Exception)
        {
            return 4;
        }
        return result.ExitCode;
    }

    private static (int ExitCode, string State, string Reason) Error(string reason) =>
        (4, "ExecutionError", reason);
}
