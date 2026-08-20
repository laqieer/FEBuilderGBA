using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace EventAssemblerProcessStub;

internal static class Program
{
    const string ModeEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_MODE";
    const string LaunchRecordEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_RECORD";
    const string ChildMarkerEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_CHILD_MARKER";
    const string TimeoutMsEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_TIMEOUT_MS";

    const string ConcurrentOutputMode = "concurrent-output";
    const string ParentExitDescendantHoldsPipesMode = "parent-exit-descendant-holds-pipes";
    const string TimeoutParentMode = "timeout-parent";
    const int ParentExitChildSelfTerminateSeconds = 12;
    const int TimeoutContainmentGraceMs = 5000;
    const int TimeoutPostParentExitGraceMs = 2500;
    const int ParentStartIdentityToleranceMs = 2000;
    const int AtomicPublishRetryCount = 3;
    const int AtomicPublishRetryDelayMs = 50;
    const string ParentExitChildCommand = "__parent-exit-child";
    const string TimeoutChildCommand = "__timeout-child";
    const string WritingBeforeInitializingOffsetWarning =
        "warning: C_Code.lyn.event:11:1: Writing before initializing offset. You may be breaking the ROM!";
    const string ColorzCoreSuccessMarker =
        "No errors. Please continue being awesome.";

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ParentExitChildCommand)
            return RunParentExitChild(args);
        if (args.Length > 0 && args[0] == TimeoutChildCommand)
            return RunTimeoutChild(args);

        string mode = RequireEnvironmentVariable(ModeEnvVar);
        string launchRecordPath = RequireEnvironmentVariable(LaunchRecordEnvVar);
        string wrapperPath = GetArgValue(args, "-input:");
        string outputRomPath = GetArgValue(args, "-output:");

        var launch = new LaunchRecord
        {
            Mode = mode,
            ProcessId = Environment.ProcessId,
            ProcessPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "",
            WorkingDirectory = Environment.CurrentDirectory,
            WrapperPath = wrapperPath,
            OutputRomPath = outputRomPath,
            Arguments = args,
        };

        return mode switch
        {
            ConcurrentOutputMode => RunConcurrentOutput(launch, launchRecordPath),
            ParentExitDescendantHoldsPipesMode => RunParentExitDescendantHoldsPipes(launch, launchRecordPath),
            TimeoutParentMode => RunTimeoutParent(launch, launchRecordPath),
            _ => throw new InvalidOperationException("Unsupported process stub mode: " + mode),
        };
    }

    static int RunConcurrentOutput(LaunchRecord launch, string launchRecordPath)
    {
        WriteLaunchRecord(launchRecordPath, launch);
        MutateOutputRom(launch.OutputRomPath);

        using var warningReady = new ManualResetEventSlim(false);
        var stderrThread = new Thread(() =>
        {
            Console.Error.WriteLine(WritingBeforeInitializingOffsetWarning);
            Console.Error.Flush();
            warningReady.Set();
            for (int i = 0; i < 64; i++)
            {
                Console.Error.WriteLine($"stderr filler {i}");
            }
            Console.Error.Flush();
        });
        var stdoutThread = new Thread(() =>
        {
            warningReady.Wait();
            for (int i = 0; i < 64; i++)
            {
                Console.Out.WriteLine($"stdout filler {i}");
            }
            Console.Out.WriteLine(ColorzCoreSuccessMarker);
            Console.Out.Flush();
        });

        stderrThread.Start();
        stdoutThread.Start();
        stderrThread.Join();
        stdoutThread.Join();
        return 0;
    }

    static int RunParentExitDescendantHoldsPipes(LaunchRecord launch, string launchRecordPath)
    {
        string childMarkerPath = RequireEnvironmentVariable(ChildMarkerEnvVar);
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        MutateOutputRom(launch.OutputRomPath);

        var psi = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(ParentExitChildCommand);
        psi.ArgumentList.Add(childMarkerPath);

        using var child = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start inherited-pipe child.");

        launch.ChildProcessId = child.Id;
        WriteLaunchRecord(launchRecordPath, launch);
        Console.Out.WriteLine(ColorzCoreSuccessMarker);
        Console.Out.Flush();
        return 0;
    }

    static int RunTimeoutParent(LaunchRecord launch, string launchRecordPath)
    {
        string childMarkerPath = RequireEnvironmentVariable(ChildMarkerEnvVar);
        int timeoutMs = RequirePositiveIntEnvironmentVariable(TimeoutMsEnvVar);
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        long parentStartTimeUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        var psi = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(TimeoutChildCommand);
        psi.ArgumentList.Add(childMarkerPath);
        psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(parentStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(timeoutMs.ToString(CultureInfo.InvariantCulture));

        using var child = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start timeout child.");

        launch.ChildProcessId = child.Id;
        WriteLaunchRecord(launchRecordPath, launch);

        Thread.Sleep(TimeSpan.FromSeconds(60));
        return 0;
    }

    static int RunParentExitChild(string[] args)
    {
        if (args.Length != 2)
            throw new InvalidOperationException("Parent-exit child expects a marker path.");

        Thread.Sleep(TimeSpan.FromSeconds(ParentExitChildSelfTerminateSeconds));
        WriteAtomicText(args[1], "parent-exit helper descendant self-terminated");
        return 0;
    }

    static int RunTimeoutChild(string[] args)
    {
        if (args.Length != 5)
            throw new InvalidOperationException(
                "Timeout child expects a marker path, parent PID, parent start time, and timeout.");

        int parentProcessId = ParsePositiveInt(args[2], "parent PID");
        long parentStartTimeUtcTicks = ParsePositiveLong(args[3], "parent start time");
        int timeoutMs = ParsePositiveInt(args[4], "timeout");
        WaitForParentExitOrContainmentDeadline(
            parentProcessId,
            parentStartTimeUtcTicks,
            timeoutMs);
        Thread.Sleep(TimeoutPostParentExitGraceMs);
        WriteAtomicText(args[1], "child survived timeout kill");
        return 0;
    }

    static void WaitForParentExitOrContainmentDeadline(
        int parentProcessId,
        long parentStartTimeUtcTicks,
        int timeoutMs)
    {
        DateTime deadlineUtc = new DateTime(
            parentStartTimeUtcTicks,
            DateTimeKind.Utc).AddMilliseconds(timeoutMs + TimeoutContainmentGraceMs);

        try
        {
            using Process parent = Process.GetProcessById(parentProcessId);
            long actualStartTimeUtcTicks;
            try
            {
                actualStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (Math.Abs(actualStartTimeUtcTicks - parentStartTimeUtcTicks)
                > TimeSpan.FromMilliseconds(ParentStartIdentityToleranceMs).Ticks)
            {
                return;
            }

            int remainingMs = (int)Math.Clamp(
                Math.Ceiling((deadlineUtc - DateTime.UtcNow).TotalMilliseconds),
                0,
                int.MaxValue);
            if (remainingMs > 0)
                parent.WaitForExit(remainingMs);
        }
        catch (ArgumentException)
        {
            // The expected parent already exited before the child obtained a handle.
        }
        catch (InvalidOperationException)
        {
            // The expected parent exited while its process metadata was being read.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Parent metadata became unavailable during the exit race.
        }
    }

    static void MutateOutputRom(string outputRomPath)
    {
        byte[] bytes = File.ReadAllBytes(outputRomPath);
        bytes[0x100] = 0xAA;
        File.WriteAllBytes(outputRomPath, bytes);
    }

    static string GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg.Substring(prefix.Length);
        }
        throw new InvalidOperationException("Missing argument prefix: " + prefix);
    }

    static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException("Missing environment variable: " + name);
    }

    static int RequirePositiveIntEnvironmentVariable(string name)
    {
        return ParsePositiveInt(RequireEnvironmentVariable(name), name);
    }

    static int ParsePositiveInt(string value, string description)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid positive integer for {description}: {value}");
        }
        return parsed;
    }

    static long ParsePositiveLong(string value, string description)
    {
        if (!long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long parsed)
            || parsed <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid positive integer for {description}: {value}");
        }
        return parsed;
    }

    static void WriteLaunchRecord(string path, LaunchRecord launch)
    {
        WriteAtomicText(path, JsonSerializer.Serialize(launch));
    }

    static void WriteAtomicText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        for (int attempt = 0; attempt < AtomicPublishRetryCount; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (IsTransientAtomicPublishFailure(ex))
            {
                if (HasExpectedAtomicPublishContent(path, content))
                    return;

                if (!File.Exists(tempPath) || attempt + 1 == AtomicPublishRetryCount)
                    throw;

                Thread.Sleep(AtomicPublishRetryDelayMs);
            }
        }
    }

    static bool HasExpectedAtomicPublishContent(string path, string expectedContent)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return string.Equals(
                reader.ReadToEnd(),
                expectedContent,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsTransientAtomicPublishFailure(Exception ex)
    {
        return ex is IOException
            || ex is UnauthorizedAccessException;
    }

    sealed class LaunchRecord
    {
        public string Mode { get; init; } = "";
        public int ProcessId { get; init; }
        public int? ChildProcessId { get; set; }
        public string ProcessPath { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public string WrapperPath { get; init; } = "";
        public string OutputRomPath { get; init; } = "";
        public string[] Arguments { get; init; } = Array.Empty<string>();
    }
}
