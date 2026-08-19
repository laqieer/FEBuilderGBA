using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace EventAssemblerProcessStub;

internal static class Program
{
    const string ModeEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_MODE";
    const string LaunchRecordEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_RECORD";
    const string ChildMarkerEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_CHILD_MARKER";

    const string ConcurrentOutputMode = "concurrent-output";
    const string ParentExitDescendantHoldsPipesMode = "parent-exit-descendant-holds-pipes";
    const string TimeoutParentMode = "timeout-parent";
    const int ParentExitChildSelfTerminateSeconds = 12;
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

        WriteLaunchRecord(launchRecordPath, launch);

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
        MutateOutputRom(launch.OutputRomPath);
        WriteLaunchRecord(launchRecordPath, launch);

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
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        var psi = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(TimeoutChildCommand);
        psi.ArgumentList.Add(childMarkerPath);

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
        File.WriteAllText(args[1], "parent-exit helper descendant self-terminated");
        return 0;
    }

    static int RunTimeoutChild(string[] args)
    {
        if (args.Length != 2)
            throw new InvalidOperationException("Timeout child expects a marker path.");

        Thread.Sleep(TimeSpan.FromMilliseconds(2500));
        File.WriteAllText(args[1], "child survived timeout kill");
        return 0;
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

    static void WriteLaunchRecord(string path, LaunchRecord launch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(launch));
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
