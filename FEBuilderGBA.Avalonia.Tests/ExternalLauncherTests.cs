// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public sealed class ExternalLauncherTests
{
    [Fact]
    public async Task RunCapturedProcessAsync_ReturnsUnsupportedBeforeCreatingProcessOnBrowserOrIos()
    {
        var adapter = new RecordingProcessAdapter();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: true), adapter);

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest("never-started")
        {
            Arguments = "--bad",
            WorkingDirectory = AppContext.BaseDirectory,
            Timeout = TimeSpan.FromSeconds(1),
        });

        Assert.Equal(ExternalProcessResultKind.Unsupported, result.Kind);
        Assert.False(adapter.WasCalled);
        Assert.Contains("not available", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenPathAndRevealPath_ReturnUnsupportedOnBrowserOrIos()
    {
        var adapter = new RecordingProcessAdapter();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: true), adapter);

        var open = await launcher.OpenPathAsync("sample.txt");
        var reveal = await launcher.RevealPathAsync("sample.txt");

        Assert.Equal(ExternalLaunchResultKind.Unsupported, open.Kind);
        Assert.Equal(ExternalLaunchResultKind.Unsupported, reveal.Kind);
        Assert.False(adapter.WasCalled);
    }

    [Fact]
    public async Task OpenUriAsync_DoesNotUseProcessUnsupportedCapability()
    {
        var adapter = new RecordingProcessAdapter();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: true), adapter);

        var result = await launcher.OpenUriAsync(null, new Uri("https://example.invalid/"));

        Assert.Equal(ExternalLaunchResultKind.Failed, result.Kind);
        Assert.False(adapter.WasCalled);
        Assert.Contains("https://example.invalid/", result.Message);
    }

    [Fact]
    public async Task OpenPathAsync_PreservesArgumentsWorkingDirectoryAndShellExecution()
    {
        var adapter = new RecordingProcessAdapter();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false), adapter);
        string cwd = CreateScratchDirectory();

        var result = await launcher.OpenPathAsync("emulator.exe", "\"rom path.gba\"", cwd);

        Assert.Equal(ExternalLaunchResultKind.Succeeded, result.Kind);
        ProcessStartInfo psi = Assert.Single(adapter.StartInfos);
        Assert.Equal("emulator.exe", psi.FileName);
        Assert.Equal("\"rom path.gba\"", psi.Arguments);
        Assert.Equal(cwd, psi.WorkingDirectory);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public async Task OpenPathAsync_CanPreserveDetachedExecutableWithoutShellExecution()
    {
        var adapter = new RecordingProcessAdapter();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false), adapter);

        var result = await launcher.OpenPathAsync("emulator.exe", "\"rom path.gba\"", useShellExecute: false);

        Assert.Equal(ExternalLaunchResultKind.Succeeded, result.Kind);
        ProcessStartInfo psi = Assert.Single(adapter.StartInfos);
        Assert.Equal("emulator.exe", psi.FileName);
        Assert.Equal("\"rom path.gba\"", psi.Arguments);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public async Task RunCapturedProcessAsync_PreservesArgumentsWorkingDirectoryCaptureAndExit()
    {
        var adapter = new RecordingProcessAdapter
        {
            CapturedResult = ExternalProcessResult.Exited(
                exitCode: 17,
                standardOutput: "captured stdout",
                standardError: "captured stderr")
        };
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false), adapter);
        string cwd = CreateScratchDirectory();

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest("tool")
        {
            ArgumentList = new[] { "--flag", "value with spaces" },
            WorkingDirectory = cwd,
            Timeout = TimeSpan.FromSeconds(5),
            CaptureStandardOutput = true,
            CaptureStandardError = true,
        });

        Assert.Equal(ExternalProcessResultKind.Exited, result.Kind);
        Assert.Equal(17, result.ExitCode);
        Assert.Equal("captured stdout", result.StandardOutput);
        Assert.Equal("captured stderr", result.StandardError);

        ProcessStartInfo psi = Assert.Single(adapter.StartInfos);
        Assert.Equal("tool", psi.FileName);
        Assert.Equal(new[] { "--flag", "value with spaces" }, psi.ArgumentList.ToArray());
        Assert.Equal(cwd, psi.WorkingDirectory);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.Equal(TimeSpan.FromSeconds(5), adapter.Timeouts.Single());
    }

    [Fact]
    public async Task RunCapturedProcessAsync_ReturnsStartFailureForMissingExecutable()
    {
        string missing = "febuildergba-missing-tool-" + Guid.NewGuid().ToString("N");
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false));

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest(missing)
        {
            Timeout = TimeSpan.FromSeconds(5),
        });

        Assert.Equal(ExternalProcessResultKind.StartFailure, result.Kind);
        Assert.Contains(missing, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunCapturedProcessAsync_ExecutesWithArgumentsAndWorkingDirectory()
    {
        string cwd = CreateScratchDirectory();
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false));
        var (exe, args) = EchoCommand("hello-launcher");

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest(exe)
        {
            ArgumentList = args,
            WorkingDirectory = cwd,
            Timeout = TimeSpan.FromSeconds(10),
        });

        Assert.Equal(ExternalProcessResultKind.Exited, result.Kind);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ARG:hello-launcher", result.StandardOutput);
        Assert.Contains(cwd, result.StandardOutput);
    }

    [Fact]
    public async Task RunCapturedProcessAsync_ReturnsTimeoutAndKillsProcess()
    {
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false));
        var (exe, args) = SlowCommand();

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest(exe)
        {
            ArgumentList = args,
            Timeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.Equal(ExternalProcessResultKind.TimedOut, result.Kind);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task RunCapturedProcessAsync_CallerCancellationKillsProcessTree()
    {
        var launcher = new ExternalLauncher(new FakePlatformCapabilities(unsupported: false));
        string cwd = CreateScratchDirectory();
        string marker = Path.Combine(cwd, "cancel-marker.txt");
        var (exe, args) = DelayedMarkerCommand(marker);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await launcher.RunCapturedProcessAsync(new ExternalProcessRequest(exe)
        {
            ArgumentList = args,
            WorkingDirectory = cwd,
            Timeout = TimeSpan.FromSeconds(10),
        }, cts.Token);

        Assert.Equal(ExternalProcessResultKind.StartFailure, result.Kind);
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.False(File.Exists(marker), "Cancelled captured processes must not keep running after RunCapturedProcessAsync returns.");
    }

    [Fact]
    public void ToolAsmEditCompileClickHasReentrancyGuardAroundAwaitedLauncherCalls()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "FEBuilderGBA.Avalonia",
            "Views",
            "ToolASMEditView.axaml.cs"));

        Assert.Contains("_isCompiling", source);
        Assert.Contains("if (_isCompiling) return;", source);
        Assert.Contains("_isCompiling = true;", source);
        Assert.Contains("_isCompiling = false;", source);
    }

    [Fact]
    public void HeadCompiledAvaloniaSourcesHaveNoRawProcessApiOutsideLauncher()
    {
        string root = FindRepositoryRoot();
        string avaloniaRoot = Path.Combine(root, "FEBuilderGBA.Avalonia");
        string launcherFile = Path.Combine(avaloniaRoot, "Services", "ExternalLauncher.cs");
        string[] forbidden =
        {
            "System.Diagnostics.Process",
            "ProcessStartInfo",
            "Process.Start",
            "new Process",
            ".WaitForExit(",
            ".StandardOutput.Read",
            ".StandardError.Read",
            "RedirectStandard",
        };

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(avaloniaRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcludedHeadSource(file, avaloniaRoot)) continue;
            if (Path.GetFullPath(file).Equals(Path.GetFullPath(launcherFile), StringComparison.OrdinalIgnoreCase))
                continue;

            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                int line = LineOf(text, token);
                if (line > 0)
                    violations.Add($"{Path.GetRelativePath(root, file)}:{line}: {token}");
            }
        }

        Assert.True(violations.Count == 0,
            "Raw process APIs must stay inside Services\\ExternalLauncher.cs for browser/iOS capability guarding:\n" +
            string.Join(Environment.NewLine, violations));
    }

    static bool IsExcludedHeadSource(string file, string avaloniaRoot)
    {
        string relative = Path.GetRelativePath(avaloniaRoot, file)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (relative.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) return true;
        if (relative.Equals("App.GapSweep.cs", StringComparison.OrdinalIgnoreCase)) return true;
        return relative.StartsWith("GapSweep" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static int LineOf(string text, string token)
    {
        int index = text.IndexOf(token, StringComparison.Ordinal);
        if (index < 0) return 0;
        int line = 1;
        for (int i = 0; i < index; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    static string FindRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate FEBuilderGBA.sln from " + AppContext.BaseDirectory);
    }

    static string CreateScratchDirectory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "external-launcher-tests");
        string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static (string Exe, IReadOnlyList<string> Args) EchoCommand(string argument)
    {
        if (OperatingSystem.IsWindows())
        {
            string command = $"echo ARG:{argument} & cd";
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                new[] { "/d", "/c", command });
        }

        return ("/bin/sh", new[] { "-c", "printf 'ARG:%s\n' \"$1\"; pwd", "sh", argument });
    }

    static (string Exe, IReadOnlyList<string> Args) SlowCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                new[] { "/d", "/c", "ping -n 5 127.0.0.1 > nul" });
        }

        return ("/bin/sh", new[] { "-c", "sleep 5" });
    }

    static (string Exe, IReadOnlyList<string> Args) DelayedMarkerCommand(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            string quotedMarker = marker.Replace("\"", "\\\"");
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                new[] { "/d", "/c", $"ping -n 3 127.0.0.1 > nul & echo done>\"{quotedMarker}\"" });
        }

        return ("/bin/sh", new[] { "-c", "sleep 2; printf done > \"$1\"", "sh", marker });
    }

    sealed class FakePlatformCapabilities : IPlatformCapabilities
    {
        public FakePlatformCapabilities(bool unsupported) => IsExternalProcessUnsupported = unsupported;
        public bool IsExternalProcessUnsupported { get; }
    }

    sealed class RecordingProcessAdapter : IExternalProcessAdapter
    {
        public bool WasCalled => StartInfos.Count != 0;
        public List<ProcessStartInfo> StartInfos { get; } = new();
        public List<TimeSpan?> Timeouts { get; } = new();
        public ExternalProcessResult CapturedResult { get; set; } = ExternalProcessResult.Exited(0, "", "");

        public Task<ExternalProcessResult> RunCapturedAsync(
            ProcessStartInfo startInfo,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            StartInfos.Add(startInfo);
            Timeouts.Add(timeout);
            return Task.FromResult(CapturedResult);
        }

        public ExternalLaunchResult StartDetached(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return ExternalLaunchResult.Succeeded();
        }
    }
}
