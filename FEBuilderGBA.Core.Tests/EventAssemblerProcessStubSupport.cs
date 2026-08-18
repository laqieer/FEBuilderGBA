using System.Text.Json;

namespace FEBuilderGBA.Core.Tests;

internal static class EventAssemblerProcessStubSupport
{
    internal const string ModeEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_MODE";
    internal const string LaunchRecordEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_RECORD";
    internal const string ChildMarkerEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_CHILD_MARKER";
    internal const string ConcurrentOutputMode = "concurrent-output";
    internal const string TimeoutParentMode = "timeout-parent";

    internal static IDisposable Configure(string mode, string launchRecordPath, string? childMarkerPath = null)
    {
        return new EnvironmentVariableScope(new (string Name, string? Value)[]
        {
            (ModeEnvVar, mode),
            (LaunchRecordEnvVar, launchRecordPath),
            (ChildMarkerEnvVar, childMarkerPath),
        });
    }

    internal static LaunchRecord ReadLaunchRecord(string path)
    {
        Assert.True(File.Exists(path), "Production runner helper record was not written: " + path);
        return JsonSerializer.Deserialize<LaunchRecord>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Could not deserialize process stub launch record.");
    }

    internal static string FindExecutable()
    {
        string repositoryRoot = FindRepositoryRoot();
        string stubRoot = Path.Combine(repositoryRoot, "FEBuilderGBA.Core.Tests", "Helpers", "EventAssemblerProcessStub");
        string executableName = OperatingSystem.IsWindows() ? "ColorzCore.exe" : "ColorzCore";
        string runtimeConfigName = "ColorzCore.runtimeconfig.json";

        string outputRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string targetFramework = Path.GetFileName(outputRoot);
        string? configuration = Path.GetFileName(Path.GetDirectoryName(outputRoot));
        if (!string.IsNullOrEmpty(configuration) && !string.IsNullOrEmpty(targetFramework))
        {
            string candidate = Path.Combine(stubRoot, "bin", configuration, targetFramework, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        string[] runtimeConfigs = Directory.GetFiles(stubRoot, runtimeConfigName, SearchOption.AllDirectories);
        Array.Sort(runtimeConfigs, StringComparer.OrdinalIgnoreCase);
        for (int i = runtimeConfigs.Length - 1; i >= 0; i--)
        {
            string candidate = Path.Combine(Path.GetDirectoryName(runtimeConfigs[i])!, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Runnable ColorzCore test stub was not built.");
    }

    internal static void AssertSamePath(string expected, string actual)
    {
        string normalizedExpected = Path.GetFullPath(expected);
        string normalizedActual = Path.GetFullPath(actual);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        Assert.True(
            string.Equals(normalizedExpected, normalizedActual, comparison),
            $"Expected path '{normalizedExpected}', got '{normalizedActual}'.");
    }

    static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null
            && !File.Exists(Path.Combine(directory, "FEBuilderGBA.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new InvalidOperationException("Could not locate FEBuilderGBA.sln.");
    }

    internal sealed class LaunchRecord
    {
        public string Mode { get; init; } = "";
        public int ProcessId { get; init; }
        public int? ChildProcessId { get; init; }
        public string ProcessPath { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public string WrapperPath { get; init; } = "";
        public string OutputRomPath { get; init; } = "";
        public string[] Arguments { get; init; } = Array.Empty<string>();
    }

    sealed class EnvironmentVariableScope : IDisposable
    {
        readonly (string Name, string? OriginalValue)[] _originalValues;

        internal EnvironmentVariableScope((string Name, string? Value)[] entries)
        {
            _originalValues = new (string Name, string? OriginalValue)[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                _originalValues[i] = (entries[i].Name, Environment.GetEnvironmentVariable(entries[i].Name));
                Environment.SetEnvironmentVariable(entries[i].Name, entries[i].Value);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Name, entry.OriginalValue);
            }
        }
    }
}
