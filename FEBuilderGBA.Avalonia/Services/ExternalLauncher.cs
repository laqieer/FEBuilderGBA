// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    public interface IPlatformCapabilities
    {
        bool IsExternalProcessUnsupported { get; }
    }

    public sealed class RuntimePlatformCapabilities : IPlatformCapabilities
    {
        public bool IsExternalProcessUnsupported => PlatformCapabilities.IsExternalProcessUnsupported;
    }

    public static class PlatformCapabilities
    {
        [UnsupportedOSPlatformGuard("browser")]
        [UnsupportedOSPlatformGuard("ios")]
        public static bool IsExternalProcessUnsupported
            => OperatingSystem.IsBrowser() || OperatingSystem.IsIOS();
    }

    public interface IExternalLauncher
    {
        Task<ExternalLaunchResult> OpenUriAsync(
            TopLevel? topLevel,
            Uri uri,
            CancellationToken cancellationToken = default);

        Task<ExternalLaunchResult> OpenPathAsync(
            string path,
            string? arguments = null,
            string? workingDirectory = null,
            bool useShellExecute = true,
            CancellationToken cancellationToken = default);

        Task<ExternalLaunchResult> RevealPathAsync(
            string path,
            CancellationToken cancellationToken = default);

        Task<ExternalProcessResult> RunCapturedProcessAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default);
    }

    public enum ExternalLaunchResultKind
    {
        Succeeded,
        Failed,
        Unsupported,
    }

    public sealed class ExternalLaunchResult
    {
        ExternalLaunchResult(ExternalLaunchResultKind kind, string? message = null, Exception? exception = null)
        {
            Kind = kind;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public ExternalLaunchResultKind Kind { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public bool IsSucceeded => Kind == ExternalLaunchResultKind.Succeeded;

        public static ExternalLaunchResult Succeeded() => new(ExternalLaunchResultKind.Succeeded);
        public static ExternalLaunchResult Failed(string message, Exception? exception = null) => new(ExternalLaunchResultKind.Failed, message, exception);
        public static ExternalLaunchResult Unsupported(string message) => new(ExternalLaunchResultKind.Unsupported, message);
    }

    public enum ExternalProcessResultKind
    {
        Exited,
        TimedOut,
        StartFailure,
        Unsupported,
    }

    public sealed class ExternalProcessResult
    {
        ExternalProcessResult(
            ExternalProcessResultKind kind,
            int? exitCode,
            string standardOutput,
            string standardError,
            bool timedOut,
            string? message,
            Exception? exception)
        {
            Kind = kind;
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
            TimedOut = timedOut;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public ExternalProcessResultKind Kind { get; }
        public int? ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool TimedOut { get; }
        public string Message { get; }
        public Exception? Exception { get; }

        public static ExternalProcessResult Exited(int exitCode, string standardOutput, string standardError)
            => new(ExternalProcessResultKind.Exited, exitCode, standardOutput, standardError, false, null, null);

        public static ExternalProcessResult TimedOutResult(string standardOutput, string standardError, string message)
            => new(ExternalProcessResultKind.TimedOut, null, standardOutput, standardError, true, message, null);

        public static ExternalProcessResult StartFailure(string message, Exception? exception = null)
            => new(ExternalProcessResultKind.StartFailure, null, string.Empty, string.Empty, false, message, exception);

        public static ExternalProcessResult Unsupported(string message)
            => new(ExternalProcessResultKind.Unsupported, null, string.Empty, string.Empty, false, message, null);
    }

    public sealed class ExternalProcessRequest
    {
        public ExternalProcessRequest(string executable)
        {
            Executable = executable;
        }

        public string Executable { get; }
        public string? Arguments { get; init; }
        public IReadOnlyList<string> ArgumentList { get; init; } = Array.Empty<string>();
        public string? WorkingDirectory { get; init; }
        public TimeSpan? Timeout { get; init; }
        public bool CaptureStandardOutput { get; init; } = true;
        public bool CaptureStandardError { get; init; } = true;
    }

    internal interface IExternalProcessAdapter
    {
        [UnsupportedOSPlatform("browser")]
        [UnsupportedOSPlatform("ios")]
        Task<ExternalProcessResult> RunCapturedAsync(
            ProcessStartInfo startInfo,
            TimeSpan? timeout,
            CancellationToken cancellationToken);

        [UnsupportedOSPlatform("browser")]
        [UnsupportedOSPlatform("ios")]
        ExternalLaunchResult StartDetached(ProcessStartInfo startInfo);
    }

    public sealed class ExternalLauncher : IExternalLauncher
    {
        static readonly RuntimePlatformCapabilities DefaultCapabilities = new();
        static readonly DefaultExternalProcessAdapter DefaultAdapter = new();

        readonly IPlatformCapabilities _capabilities;
        readonly IExternalProcessAdapter _adapter;

        public ExternalLauncher()
            : this(DefaultCapabilities, DefaultAdapter)
        {
        }

        internal ExternalLauncher(IPlatformCapabilities capabilities)
            : this(capabilities, DefaultAdapter)
        {
        }

        internal ExternalLauncher(IPlatformCapabilities capabilities, IExternalProcessAdapter adapter)
        {
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public static IExternalLauncher Current { get; set; } = new ExternalLauncher();

        public async Task<ExternalLaunchResult> OpenUriAsync(
            TopLevel? topLevel,
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            if (cancellationToken.IsCancellationRequested)
                return ExternalLaunchResult.Failed("Launch cancelled.");
            if (topLevel == null)
                return ExternalLaunchResult.Failed(R._("Couldn't open link:") + " " + uri);

            try
            {
                bool launched = await topLevel.Launcher.LaunchUriAsync(uri);
                return launched
                    ? ExternalLaunchResult.Succeeded()
                    : ExternalLaunchResult.Failed(R._("Couldn't open link:") + " " + uri);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return ExternalLaunchResult.Failed(ex.Message, ex);
            }
        }

        public Task<ExternalLaunchResult> OpenPathAsync(
            string path,
            string? arguments = null,
            string? workingDirectory = null,
            bool useShellExecute = true,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(ExternalLaunchResult.Failed("Launch cancelled."));
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(ExternalLaunchResult.Failed("No path specified."));
            if (_capabilities.IsExternalProcessUnsupported)
                return Task.FromResult(ExternalLaunchResult.Unsupported(UnsupportedProcessMessage()));
            if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
                return Task.FromResult(ExternalLaunchResult.Unsupported(UnsupportedProcessMessage()));

            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = useShellExecute,
            };
            if (!string.IsNullOrEmpty(arguments))
                psi.Arguments = arguments;
            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            return Task.FromResult(_adapter.StartDetached(psi));
        }

        public Task<ExternalLaunchResult> RevealPathAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(ExternalLaunchResult.Failed("Launch cancelled."));
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(ExternalLaunchResult.Failed("No path specified."));
            if (_capabilities.IsExternalProcessUnsupported)
                return Task.FromResult(ExternalLaunchResult.Unsupported(UnsupportedProcessMessage()));
            if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
                return Task.FromResult(ExternalLaunchResult.Unsupported(UnsupportedProcessMessage()));

            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new ProcessStartInfo("open")
                {
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(path);
            }
            else
            {
                string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return Task.FromResult(ExternalLaunchResult.Failed(R._("Folder not found") + ": " + path));
                psi = new ProcessStartInfo("xdg-open")
                {
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add(dir);
            }

            return Task.FromResult(_adapter.StartDetached(psi));
        }

        public Task<ExternalProcessResult> RunCapturedProcessAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(ExternalProcessResult.StartFailure("Launch cancelled."));
            if (string.IsNullOrWhiteSpace(request.Executable))
                return Task.FromResult(ExternalProcessResult.StartFailure("No executable specified."));
            if (_capabilities.IsExternalProcessUnsupported)
                return Task.FromResult(ExternalProcessResult.Unsupported(UnsupportedProcessMessage()));
            if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
                return Task.FromResult(ExternalProcessResult.Unsupported(UnsupportedProcessMessage()));

            var psi = new ProcessStartInfo(request.Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = request.CaptureStandardOutput,
                RedirectStandardError = request.CaptureStandardError,
            };
            if (!string.IsNullOrEmpty(request.Arguments))
                psi.Arguments = request.Arguments;
            foreach (string arg in request.ArgumentList)
                psi.ArgumentList.Add(arg);
            if (!string.IsNullOrEmpty(request.WorkingDirectory))
                psi.WorkingDirectory = request.WorkingDirectory;

            return _adapter.RunCapturedAsync(psi, request.Timeout, cancellationToken);
        }

        static string UnsupportedProcessMessage()
            => R._("External process launching is not available on this device.");

        sealed class DefaultExternalProcessAdapter : IExternalProcessAdapter
        {
            [UnsupportedOSPlatform("browser")]
            [UnsupportedOSPlatform("ios")]
            public async Task<ExternalProcessResult> RunCapturedAsync(
                ProcessStartInfo startInfo,
                TimeSpan? timeout,
                CancellationToken cancellationToken)
            {
                if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
                    return ExternalProcessResult.Unsupported(UnsupportedProcessMessage());

                using var process = new Process { StartInfo = startInfo };
                try
                {
                    if (!process.Start())
                        return ExternalProcessResult.StartFailure("Failed to start process: " + startInfo.FileName);
                }
                catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
                {
                    return ExternalProcessResult.StartFailure(
                        $"Failed to start process '{startInfo.FileName}': {ex.Message}",
                        ex);
                }

                Task<string> stdoutTask = startInfo.RedirectStandardOutput
                    ? process.StandardOutput.ReadToEndAsync(cancellationToken)
                    : Task.FromResult(string.Empty);
                Task<string> stderrTask = startInfo.RedirectStandardError
                    ? process.StandardError.ReadToEndAsync(cancellationToken)
                    : Task.FromResult(string.Empty);

                using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
                using var linkedCts = timeoutCts == null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                    }

                    try { await process.WaitForExitAsync(CancellationToken.None); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }

                    string stdout = await ReadCompletedOutput(stdoutTask);
                    string stderr = await ReadCompletedOutput(stderrTask);
                    return ExternalProcessResult.TimedOutResult(
                        stdout,
                        stderr,
                        $"Process '{startInfo.FileName}' timed out.");
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                    }

                    try { await process.WaitForExitAsync(CancellationToken.None); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }

                    _ = await ReadCompletedOutput(stdoutTask);
                    _ = await ReadCompletedOutput(stderrTask);
                    return ExternalProcessResult.StartFailure("Launch cancelled.");
                }

                string standardOutput = await stdoutTask;
                string standardError = await stderrTask;
                return ExternalProcessResult.Exited(process.ExitCode, standardOutput, standardError);
            }

            [UnsupportedOSPlatform("browser")]
            [UnsupportedOSPlatform("ios")]
            public ExternalLaunchResult StartDetached(ProcessStartInfo startInfo)
            {
                if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
                    return ExternalLaunchResult.Unsupported(UnsupportedProcessMessage());

                try
                {
                    using Process? process = Process.Start(startInfo);
                    return process != null
                        ? ExternalLaunchResult.Succeeded()
                        : ExternalLaunchResult.Failed("Failed to start process: " + startInfo.FileName);
                }
                catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
                {
                    return ExternalLaunchResult.Failed(
                        $"Failed to start process '{startInfo.FileName}': {ex.Message}",
                        ex);
                }
            }

            static async Task<string> ReadCompletedOutput(Task<string> task)
            {
                try { return await task; }
                catch (OperationCanceledException) { return string.Empty; }
                catch (ObjectDisposedException) { return string.Empty; }
                catch (InvalidOperationException) { return string.Empty; }
            }
        }
    }
}
