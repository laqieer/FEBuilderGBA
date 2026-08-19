using System.IO;
using System.Text.Json;
using System.Threading;

namespace FEBuilderGBA.Core.Tests;

internal static class EventAssemblerProcessStubSupport
{
    const int LaunchRecordReadRetryCount = 20;
    const int LaunchRecordReadRetryDelayMs = 50;
    const int ChildMarkerReadRetryDelayMs = 50;

    internal const string ModeEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_MODE";
    internal const string LaunchRecordEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_RECORD";
    internal const string ChildMarkerEnvVar = "FEBUILDERGBA_EA_PROCESS_STUB_CHILD_MARKER";
    internal const string ConcurrentOutputMode = "concurrent-output";
    internal const string ParentExitDescendantHoldsPipesMode = "parent-exit-descendant-holds-pipes";
    internal const string TimeoutParentMode = "timeout-parent";
    internal const int ParentExitChildSelfTerminateSeconds = 12;

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
        Exception? lastError = null;
        for (int attempt = 0; attempt < LaunchRecordReadRetryCount; attempt++)
        {
            try
            {
                string json = ReadRequiredNonEmptyText(
                    path,
                    "Process stub launch record was empty.");

                return JsonSerializer.Deserialize<LaunchRecord>(json)
                    ?? throw new InvalidDataException("Could not deserialize process stub launch record.");
            }
            catch (Exception ex) when (IsTransientLaunchRecordReadFailure(ex))
            {
                lastError = ex;
                if (attempt + 1 == LaunchRecordReadRetryCount)
                    break;

                Thread.Sleep(LaunchRecordReadRetryDelayMs);
            }
        }

        throw new InvalidOperationException(
            "Could not read a complete process stub launch record: " + path,
            lastError);
    }

    internal static string ReadChildMarker(string path, TimeSpan timeout)
    {
        if (TryReadChildMarker(path, timeout, out string? marker, out Exception? lastError))
            return marker!;

        throw new InvalidOperationException(
            "Could not read a complete process stub child marker: " + path,
            lastError);
    }

    internal static bool TryReadChildMarker(string path, TimeSpan timeout, out string? marker)
    {
        return TryReadChildMarker(path, timeout, out marker, out _);
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

    static bool TryReadChildMarker(
        string path,
        TimeSpan timeout,
        out string? marker,
        out Exception? lastError)
    {
        return TryReadNonEmptyText(
            path,
            GetRetryCount(timeout, ChildMarkerReadRetryDelayMs),
            ChildMarkerReadRetryDelayMs,
            "Process stub child marker was empty.",
            out marker,
            out lastError);
    }

    static bool TryReadNonEmptyText(
        string path,
        int retryCount,
        int retryDelayMs,
        string emptyMessage,
        out string? text,
        out Exception? lastError)
    {
        if (retryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(retryCount));
        if (retryDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(retryDelayMs));

        text = null;
        lastError = null;

        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                text = ReadRequiredNonEmptyText(path, emptyMessage);
                return true;
            }
            catch (Exception ex) when (IsTransientMarkerReadFailure(ex))
            {
                lastError = ex;
                if (attempt + 1 == retryCount)
                    break;

                Thread.Sleep(retryDelayMs);
            }
        }

        return false;
    }

    static string ReadRequiredNonEmptyText(string path, string emptyMessage)
    {
        string text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException(emptyMessage);

        return text;
    }

    static int GetRetryCount(TimeSpan timeout, int retryDelayMs)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (retryDelayMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(retryDelayMs));

        return Math.Max(
            1,
            1 + (int)Math.Ceiling(timeout.TotalMilliseconds / retryDelayMs));
    }

    static bool IsTransientLaunchRecordReadFailure(Exception ex)
    {
        return ex is IOException
            || ex is JsonException
            || ex is InvalidDataException;
    }

    static bool IsTransientMarkerReadFailure(Exception ex)
    {
        return ex is IOException
            || ex is InvalidDataException;
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
