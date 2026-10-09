// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #2050: shared support for ProcessRunnerCore process-tree tests. Both the strict
    /// single-launch theory (<see cref="ProcessRunnerProcessTreeTests"/>) and the concurrent
    /// contention scenarios (<see cref="ProcessRunnerContentionTests"/>) spawn the exact same
    /// "leader process spawns a detached long-running descendant, atomically publishes both
    /// identities, then exits" fixture and need the exact same PID-safe readiness/cleanup
    /// plumbing. Centralizing it here means individual test methods only assert outcomes.
    ///
    /// Wire contract (see docs/ENGINEERING-NOTES.md #1993 / #2050):
    /// - The leader script writes an atomic `leader.pid` payload, then spawns a detached
    ///   long-running descendant (self-watchdog <see cref="ChildWatchdogSeconds"/> seconds, so a
    ///   containment bug can never hang a CI host forever) and immediately publishes its
    ///   atomic `child.pid` identity. After <see cref="IdentitySettleSeconds"/> seconds it
    ///   verifies that child is still alive before publishing a separate atomic `child.ready`
    ///   readiness payload. Every payload is `&lt;pid&gt;|&lt;unixMs&gt;`.
    /// - Windows (PowerShell 5.1): each payload is written with `[IO.File]::WriteAllText` to a
    ///   `&lt;file&gt;.tmp` sibling, then published via the two-argument `[IO.File]::Move`
    ///   (fails rather than silently overwriting a stale file). If `Move` throws, the helper
    ///   re-reads the destination and accepts a completed-then-threw publish when the final
    ///   payload is already complete; retries (bounded 3×50 ms) happen only while the temp
    ///   source still exists, to absorb transient sharing violations.
    /// - POSIX (`/usr/bin/python3`): the script's <c>ArgumentList[0]</c> basename is exactly
    ///   `process_worker.py` (required by <c>ProcessRunnerCore.Run</c>'s POSIX process-group
    ///   containment gate) and it calls <c>os.setsid()</c> before creating the descendant. Each
    ///   payload is written to a `.tmp` sibling then published via `os.replace` (atomic rename).
    /// </summary>
    internal static class ProcessRunnerScenarioSupport
    {
        /// <summary>ProcessRunnerCore.Run timeoutMs used by every scenario.</summary>
        internal const int ScenarioTimeoutMs = 30_000;

        /// <summary>ProcessRunnerCore.Run maximumOutputChars used by every scenario.</summary>
        internal const int OutputCapChars = 4096;

        /// <summary>Readiness-file poll cadence; never coarser than this.</summary>
        internal const int ReadinessPollMs = 25;

        /// <summary>
        /// The descendant's own safety-net lifetime: if containment never terminates it, it
        /// exits on its own instead of hanging a CI host indefinitely.
        /// </summary>
        internal const int ChildWatchdogSeconds = 330;

        /// <summary>
        /// How long the leader sleeps after capturing the descendant's PID/timestamp but
        /// before publishing `child.ready`, so the descendant's OS-level identity has settled.
        /// </summary>
        internal const int IdentitySettleSeconds = 2;

        /// <summary>Maximum total time spent polling for descendant death.</summary>
        internal const int DescendantDeathPollSeconds = 5;

        /// <summary>
        /// Clock-sanity ceiling for a descendant-death observation relative to the atomically
        /// recorded child-readiness timestamp. The much tighter
        /// <see cref="DescendantDeathPollSeconds"/> poll is what prevents the 330-second
        /// watchdog from satisfying the containment oracle; this wider bound rejects stale
        /// payloads or implausible wall-clock jumps.
        /// </summary>
        internal const int ReadinessAcceptanceWindowMs = 120_000;

        /// <summary>On-failure cleanup: how long to wait for the Run(...) task after cancelling.</summary>
        internal const int CancelGraceMs = 25_000;

        /// <summary>On-failure cleanup: how long to wait for a best-effort kill to take effect.</summary>
        internal const int FinalKillWaitMs = 5_000;

        /// <summary>
        /// Best-effort-kill start-time acceptance tolerance (Windows): PowerShell's own
        /// `[DateTimeOffset]::UtcNow` reading and the OS's actual process-creation timestamp can
        /// disagree by a small amount; this bounds how much earlier than the scenario's own
        /// start a live process's StartTime may be and still be considered "ours".
        /// </summary>
        internal const int WindowsStartTimeToleranceMs = 250;

        /// <summary>Same tolerance as <see cref="WindowsStartTimeToleranceMs"/>, but for POSIX.</summary>
        internal const int PosixStartTimeToleranceMs = 2_000;

        /// <summary>
        /// The exact ProcessRunnerCore.cs message emitted when the dedicated LongRunning
        /// output-capture tasks could not be joined within their bounded wait under real
        /// scheduler/OS process-thread pressure — a legitimate, documented outcome under
        /// concurrent contention, never under a single isolated launch.
        /// </summary>
        internal const string CaptureIncompleteErrorMessage =
            "Process output capture did not finish.";

        /// <summary>An atomically-published `&lt;pid&gt;|&lt;unixMs&gt;` identity record.</summary>
        internal readonly struct ProcessIdentity
        {
            public int Pid { get; }
            public long RecordedUnixMs { get; }

            public ProcessIdentity(int pid, long recordedUnixMs)
            {
                Pid = pid;
                RecordedUnixMs = recordedUnixMs;
            }
        }

        /// <summary>
        /// A synchronous <see cref="ProcessRunnerCore.Run"/> result plus the UTC captured
        /// immediately inside the dedicated LongRunning wrapper task once that synchronous call
        /// returned.
        /// </summary>
        internal readonly struct RunCompletion
        {
            public ProcessRunResult Result { get; }
            public DateTimeOffset CompletionUtc { get; }

            public RunCompletion(ProcessRunResult result, DateTimeOffset completionUtc)
            {
                Result = result;
                CompletionUtc = completionUtc;
            }
        }

        internal enum IdentityObservationStatus
        {
            Valid,
            Missing,
            Malformed,
            ReadFault
        }

        internal enum ReadinessStopReason
        {
            Ready,
            TaskCompleted,
            Deadline
        }

        internal sealed class IdentityObservation
        {
            public IdentityObservationStatus Status { get; }
            public ProcessIdentity? Identity { get; }
            public string? FaultCategory { get; }

            internal IdentityObservation(
                IdentityObservationStatus status, ProcessIdentity? identity = null,
                string? faultCategory = null)
            {
                Status = status;
                Identity = identity;
                FaultCategory = faultCategory;
            }
        }

        internal sealed class ReadinessPollEvidence
        {
            public ProcessIdentity? Identity => Observation.Identity;
            public IdentityObservation Observation { get; }
            public IdentityObservation? EarlierReadFault { get; }
            public ReadinessStopReason StopReason { get; }

            internal ReadinessPollEvidence(
                IdentityObservation observation, IdentityObservation? earlierReadFault,
                ReadinessStopReason stopReason)
            {
                Observation = observation;
                EarlierReadFault = earlierReadFault;
                StopReason = stopReason;
            }
        }

        internal sealed class ReadinessEvidence
        {
            public IdentityObservation? Observation { get; }
            public IdentityObservation? EarlierReadFault { get; }
            public string StopReason { get; }
            public string Phase { get; }
            public string TaskStatus { get; }
            public bool ReadinessSucceeded { get; }
            public long ReadinessMs { get; }
            public ProcessRunResult? Result { get; }
            public bool ResultAvailable => Result.HasValue;

            internal ReadinessEvidence(
                IdentityObservation? observation, IdentityObservation? earlierReadFault,
                string stopReason, string phase, string taskStatus, bool readinessSucceeded,
                long readinessMs, ProcessRunResult? result)
            {
                Observation = observation;
                EarlierReadFault = earlierReadFault;
                StopReason = stopReason;
                Phase = phase;
                TaskStatus = taskStatus;
                ReadinessSucceeded = readinessSucceeded;
                ReadinessMs = readinessMs;
                Result = result;
            }
        }

        internal sealed class FailureEvidence
        {
            public Exception? PrimaryFailure { get; }
            public Exception? SecondaryFailure { get; }
            public ReadinessEvidence Readiness { get; }
            public string Text { get; }
            public string FailurePhase { get; }

            internal FailureEvidence(
                Exception? primaryFailure, ReadinessEvidence readiness,
                Exception? secondaryFailure, string text, string failurePhase = "unavailable")
            {
                PrimaryFailure = primaryFailure;
                Readiness = readiness;
                SecondaryFailure = secondaryFailure;
                Text = text;
                FailurePhase = SafePhase(failurePhase);
            }
        }

        internal sealed class CleanupFault
        {
            public string Stage { get; }
            public Exception Exception { get; }

            internal CleanupFault(string stage, Exception exception)
            {
                Stage = SafeCleanupStage(stage);
                Exception = exception;
            }
        }

        internal sealed class CleanupObservations
        {
            private readonly List<CleanupFault> _faults = new List<CleanupFault>();
            public IReadOnlyList<CleanupFault> Faults => _faults;
            public CleanupFault? FirstSecondary => _faults.Count == 0 ? null : _faults[0];
            public bool Retained { get; private set; }
            public string ReleaseOutcome { get; private set; } = "unavailable";
            public string CtsOutcome { get; private set; } = "unavailable";
            public string RootOutcome { get; private set; } = "unavailable";
            public bool RootFaultSwallowed { get; private set; }
            public string Text => FormatDiagnosticRecord(ObservedCleanupFields(this));

            internal void Add(CleanupFault fault) => _faults.Add(fault);
            internal void Retain() => Retained = true;
            internal void Released(ReleaseEvidence release)
            {
                ReleaseOutcome = release.Outcome;
                CtsOutcome = release.CtsOutcome;
                RootOutcome = release.RootOutcome;
                RootFaultSwallowed = release.RootFaultSwallowed;
            }
        }

        internal sealed class RootDisposalEvidence
        {
            public Exception? Exception { get; }
            public bool FaultSwallowed => Exception != null;
            public string Outcome => Exception == null ? "success" : "fault";
            public string Text => FormatCleanupEvidence("release", Outcome, Exception);

            internal RootDisposalEvidence(Exception? exception) => Exception = exception;
        }

        internal sealed class ReleaseEvidence
        {
            private readonly List<CleanupFault> _faults = new List<CleanupFault>();
            public IReadOnlyList<CleanupFault> Faults => _faults;
            public CleanupFault? FirstSecondary => _faults.Count == 0 ? null : _faults[0];
            public Exception? PolicySecondaryFailure { get; private set; }
            public string CtsOutcome { get; internal set; } = "unavailable";
            public string RootOutcome { get; internal set; } = "unavailable";
            public bool RootFaultSwallowed { get; internal set; }
            public string Outcome => CtsOutcome == "fault" || RootOutcome == "fault"
                ? "fault" : CtsOutcome == "success" && RootOutcome == "success"
                    ? "success" : "unavailable";

            internal void Add(Exception fault, bool swallowed)
            {
                _faults.Add(new CleanupFault("release", fault));
                if (!swallowed)
                    PolicySecondaryFailure ??= fault;
            }
        }

        // Keep the original unexpected failure/dispatch privately, not in xUnit's rendered inner exception.
        internal sealed class ScenarioFailureException : Xunit.Sdk.XunitException
        {
            internal ExceptionDispatchInfo OriginalFailure { get; }

            internal ScenarioFailureException(string message, Exception originalFailure)
                : base(message)
            {
                OriginalFailure = ExceptionDispatchInfo.Capture(originalFailure);
            }
        }

        internal static ReadinessEvidence FreezeReadinessEvidence(
            ReadinessPollEvidence? poll, string phase, bool readinessSucceeded,
            Task<RunCompletion>? runTask, long readinessMs)
        {
            TaskStatus? status = runTask?.Status;
            ProcessRunResult? result = status == TaskStatus.RanToCompletion
                ? runTask!.GetAwaiter().GetResult().Result
                : null;
            return new ReadinessEvidence(
                poll?.Observation, poll?.EarlierReadFault,
                poll?.StopReason.ToString() ?? "unavailable",
                SafePhase(phase), status?.ToString() ?? "unavailable",
                readinessSucceeded, readinessMs, result);
        }

        internal static ReadinessEvidence FillReadinessResultBeforeCleanup(
            ReadinessEvidence snapshot, Task<RunCompletion> runTask, bool cleanupStarted)
        {
            if (cleanupStarted || !snapshot.ReadinessSucceeded || snapshot.ResultAvailable)
                return snapshot;
            TaskStatus status = runTask.Status;
            if (status != TaskStatus.RanToCompletion)
                return snapshot;
            return new ReadinessEvidence(
                snapshot.Observation, snapshot.EarlierReadFault, snapshot.StopReason,
                snapshot.Phase, status.ToString(), snapshot.ReadinessSucceeded,
                snapshot.ReadinessMs, runTask.GetAwaiter().GetResult().Result);
        }

        private static string SafePhase(string phase) => phase switch
        {
            "prepare" or "readiness" or "run-incomplete" or "result-rejected"
                or "assertion" or "unexpected-fault" or "complete" => phase,
            _ => "unavailable"
        };

        internal static string ClassifyDiagnosticFault(Exception fault) => fault switch
        {
            FileNotFoundException => "io-not-found",
            IOException => "read-fault",
            UnauthorizedAccessException => "access-denied",
            _ => "unexpected-fault"
        };

        private const int DiagnosticFieldChars = 256;
        private const int DiagnosticRecordChars = 2048;
        private const string Truncated = "<truncated>";

        // These two primitives accept projected values only; never pass raw external text.
        private static string FormatDiagnosticField(string label, string value)
        {
            string field = label + "=" + value;
            return field.Length <= DiagnosticFieldChars
                ? field
                : field.Substring(0, DiagnosticFieldChars - Truncated.Length) + Truncated;
        }

        private static string FormatDiagnosticRecord(IReadOnlyList<string> fields)
        {
            var record = new System.Text.StringBuilder(DiagnosticRecordChars);
            bool truncated = false;
            for (int i = 0; i < fields.Count; i++)
            {
                string separator = i == 0 ? "" : " ";
                int remaining = DiagnosticRecordChars - record.Length;
                if (separator.Length + fields[i].Length > remaining)
                {
                    record.Append(separator);
                    remaining = DiagnosticRecordChars - record.Length;
                    record.Append(fields[i], 0, Math.Max(0, remaining));
                    truncated = true;
                    break;
                }
                record.Append(separator);
                record.Append(fields[i]);
            }
            if (truncated)
            {
                record.Length = DiagnosticRecordChars - Truncated.Length;
                record.Append(Truncated);
            }
            return record.ToString();
        }

        private static List<string> ReadinessFields(ReadinessEvidence snapshot)
        {
            var fields = new List<string>
            {
                FormatDiagnosticField("phase", snapshot.Phase),
                FormatDiagnosticField("stopReason", snapshot.StopReason),
                FormatDiagnosticField("identity", snapshot.Observation?.Status.ToString() ?? "unavailable"),
                FormatDiagnosticField("readFault", snapshot.Observation?.FaultCategory ?? "unavailable"),
                FormatDiagnosticField("earlierReadFault", snapshot.EarlierReadFault?.FaultCategory ?? "unavailable"),
                FormatDiagnosticField("readinessMs", snapshot.ReadinessMs.ToString(CultureInfo.InvariantCulture)),
                FormatDiagnosticField("taskStatus", snapshot.TaskStatus)
            };
            if (snapshot.Result is not ProcessRunResult result)
            {
                fields.Add(FormatDiagnosticField("result", "unavailable"));
                return fields;
            }
            fields.Add(FormatDiagnosticField("result", "available"));
            fields.Add(FormatDiagnosticField("started", BoolToken(result.Started)));
            fields.Add(FormatDiagnosticField("timedOut", BoolToken(result.TimedOut)));
            fields.Add(FormatDiagnosticField("outputLimitExceeded", BoolToken(result.OutputLimitExceeded)));
            fields.Add(FormatDiagnosticField("terminationFailed", BoolToken(result.TerminationFailed)));
            fields.Add(FormatDiagnosticField("cancelled", BoolToken(result.Cancelled)));
            fields.Add(FormatDiagnosticField("exitCode", result.ExitCode.ToString(CultureInfo.InvariantCulture)));
            string fixtureExit = result.Started ? result.ExitCode switch
            {
                97 => "leader-publish",
                98 => "child-identity-publish",
                99 => "child-exited-before-ready",
                100 => "readiness-publish",
                _ => "unavailable"
            } : "unavailable";
            fields.Add(FormatDiagnosticField("fixtureExit", fixtureExit));
            fields.Add(FormatDiagnosticField("stdout", "<withheld>"));
            fields.Add(FormatDiagnosticField("stdoutLength", (result.Stdout?.Length ?? 0).ToString(CultureInfo.InvariantCulture)));
            fields.Add(FormatDiagnosticField("stderr", "<withheld>"));
            fields.Add(FormatDiagnosticField("stderrLength", (result.Stderr?.Length ?? 0).ToString(CultureInfo.InvariantCulture)));
            string error = result.ErrorMessage == "" ? "empty"
                : result.ErrorMessage == CaptureIncompleteErrorMessage
                    ? CaptureIncompleteErrorMessage : "<withheld>";
            fields.Add(FormatDiagnosticField("error", error));
            fields.Add(FormatDiagnosticField("errorLength", (result.ErrorMessage?.Length ?? 0).ToString(CultureInfo.InvariantCulture)));
            return fields;
        }

        private static string BoolToken(bool value) => value ? "true" : "false";

        internal static string FormatReadinessEvidence(ReadinessEvidence snapshot) =>
            FormatDiagnosticRecord(ReadinessFields(snapshot));

        private static List<string> CleanupFields(
            string stage, string outcome, Exception? fault, bool retained)
        {
            string safeStage = SafeCleanupStage(stage);
            string safeOutcome = outcome switch
            {
                "success" or "fault" or "retained" or "unavailable" => outcome,
                _ => "unavailable"
            };
            return new List<string>
            {
                FormatDiagnosticField("cleanupStage", safeStage),
                FormatDiagnosticField("cleanupOutcome", safeOutcome),
                FormatDiagnosticField("cleanupFault", fault == null ? "unavailable" : ClassifyDiagnosticFault(fault)),
                FormatDiagnosticField("resources", retained ? "retained" : "unavailable")
            };
        }

        internal static string FormatCleanupEvidence(
            string stage, string outcome, Exception? fault = null, bool retained = false) =>
            FormatDiagnosticRecord(CleanupFields(stage, outcome, fault, retained));

        internal static FailureEvidence CreateFailureEvidence(
            Exception? primary, ReadinessEvidence readiness, string cleanupStage,
            string cleanupOutcome, Exception? secondary, bool retained)
        {
            List<string> fields = ReadinessFields(readiness);
            fields.Add(FormatDiagnosticField(
                "failureFault", primary == null ? "unavailable" : ClassifyDiagnosticFault(primary)));
            fields.AddRange(CleanupFields(cleanupStage, cleanupOutcome, secondary, retained));
            return new FailureEvidence(primary, readiness, secondary, FormatDiagnosticRecord(fields));
        }

        private static string SafeCleanupStage(string stage) => stage switch
        {
            "cancel" or "wait" or "owned-kill" or "owned-wait" or "retain" or "release" => stage,
            _ => "unavailable"
        };

        internal static CleanupObservations CreateCleanupObservations() => new CleanupObservations();

        internal static CleanupObservations RecordCleanupFault(
            CleanupObservations observations, string stage, Exception fault)
        {
            observations.Add(new CleanupFault(stage, fault));
            return observations;
        }

        internal static CleanupObservations RecordCleanupRetention(CleanupObservations observations)
        {
            observations.Retain();
            return observations;
        }

        internal static void RecordReleaseEvidence(
            CleanupObservations observations, ReleaseEvidence release)
        {
            foreach (CleanupFault fault in release.Faults)
                observations.Add(fault);
            observations.Released(release);
        }

        private static List<string> ObservedCleanupFields(CleanupObservations observations)
        {
            CleanupFault? first = observations.FirstSecondary;
            var fields = CleanupFields(
                first?.Stage ?? (observations.Retained ? "retain" : "release"),
                first != null ? "fault" : observations.Retained ? "retained" : observations.ReleaseOutcome,
                first?.Exception, observations.Retained);
            fields.Add(FormatDiagnosticField(
                "secondaryCount", observations.Faults.Count.ToString(CultureInfo.InvariantCulture)));
            fields.Add(FormatDiagnosticField("releaseOutcome", observations.ReleaseOutcome));
            fields.Add(FormatDiagnosticField("ctsOutcome", observations.CtsOutcome));
            fields.Add(FormatDiagnosticField("rootOutcome", observations.RootOutcome));
            fields.Add(FormatDiagnosticField("rootFaultSwallowed", BoolToken(observations.RootFaultSwallowed)));
            for (int i = 1; i < observations.Faults.Count; i++)
            {
                fields.Add(FormatDiagnosticField(
                    "secondaryStage" + i.ToString(CultureInfo.InvariantCulture), observations.Faults[i].Stage));
                fields.Add(FormatDiagnosticField(
                    "secondaryFault" + i.ToString(CultureInfo.InvariantCulture),
                    ClassifyDiagnosticFault(observations.Faults[i].Exception)));
            }
            return fields;
        }

        internal static FailureEvidence CreateObservedFailureEvidence(
            Exception? primary, ReadinessEvidence readiness, string failurePhase,
            CleanupObservations observations)
        {
            List<string> fields = ReadinessFields(readiness);
            string safePhase = SafePhase(failurePhase);
            fields.Add(FormatDiagnosticField("failurePhase", safePhase));
            fields.Add(FormatDiagnosticField(
                "failureFault", primary == null ? "unavailable" : ClassifyDiagnosticFault(primary)));
            fields.AddRange(ObservedCleanupFields(observations));
            return new FailureEvidence(
                primary, readiness, observations.FirstSecondary?.Exception,
                FormatDiagnosticRecord(fields), safePhase);
        }

        internal static bool RunCleanupCancelOperationWithEvidence(
            Action cancel, Action<string> log, Action<CleanupFault>? secondaryFault)
        {
            try
            {
                cancel();
                return true;
            }
            catch (ObjectDisposedException ex)
            {
                secondaryFault?.Invoke(new CleanupFault("cancel", ex));
                SafeDiagnosticsLog(log, FormatCleanupEvidence("cancel", "fault", ex));
                return false;
            }
        }

        internal static bool RunCleanupWaitOperationWithEvidence(
            string stage, Func<bool> wait, Action<string> log, Action<CleanupFault>? secondaryFault)
        {
            try
            {
                return wait();
            }
            catch (AggregateException ex)
            {
                secondaryFault?.Invoke(new CleanupFault(stage, ex));
                SafeDiagnosticsLog(log, FormatCleanupEvidence(stage, "fault", ex));
                return true;
            }
        }

        internal static RootDisposalEvidence DisposeRootWithEvidence(Action dispose)
        {
            try
            {
                dispose();
                return new RootDisposalEvidence(null);
            }
            catch (IOException ex)
            {
                return new RootDisposalEvidence(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new RootDisposalEvidence(ex);
            }
        }

        internal static ReleaseEvidence ReleaseResourcesWithEvidence(
            Action? disposeCts, Action? disposeRoot, bool continueAfterCtsFault, Action<string> log)
        {
            var release = new ReleaseEvidence();
            if (disposeCts != null)
            {
                try
                {
                    disposeCts();
                    release.CtsOutcome = "success";
                }
                catch (Exception ex)
                {
                    release.CtsOutcome = "fault";
                    release.Add(ex, swallowed: false);
                }
            }
            if (disposeRoot != null && (release.CtsOutcome != "fault" || continueAfterCtsFault))
            {
                try
                {
                    RootDisposalEvidence root = DisposeRootWithEvidence(disposeRoot);
                    release.RootOutcome = root.Outcome;
                    release.RootFaultSwallowed = root.FaultSwallowed;
                    if (root.Exception != null)
                        release.Add(root.Exception, swallowed: true);
                }
                catch (Exception ex)
                {
                    release.RootOutcome = "fault";
                    release.Add(ex, swallowed: false);
                }
            }
            if (release.Faults.Count == 0)
                SafeDiagnosticsLog(log, FormatReleaseEvidence(release, null));
            else
            {
                foreach (CleanupFault fault in release.Faults)
                    SafeDiagnosticsLog(log, FormatReleaseEvidence(release, fault));
            }
            return release;
        }

        private static string FormatReleaseEvidence(ReleaseEvidence release, CleanupFault? fault)
        {
            List<string> fields = CleanupFields("release", release.Outcome, fault?.Exception, false);
            fields.Add(FormatDiagnosticField("ctsOutcome", release.CtsOutcome));
            fields.Add(FormatDiagnosticField("rootOutcome", release.RootOutcome));
            fields.Add(FormatDiagnosticField("rootFaultSwallowed", BoolToken(release.RootFaultSwallowed)));
            return FormatDiagnosticRecord(fields);
        }

        internal static void LogEvidence(Action<string> log, string projectedEvidence) =>
            SafeDiagnosticsLog(log, projectedEvidence);

        /// <summary>
        /// A unique, disposable scenario root directory. Callers must ensure the scenario's
        /// Run(...) task has actually finished (normally or via <see cref="BestEffortCleanup"/>)
        /// before disposing, or hand ownership to the retained-resource lease via
        /// <see cref="RetainLiveResources"/> — never delete the root while that task might still
        /// be reading or writing under it.
        /// </summary>
        internal sealed class ScenarioRoot : IDisposable
        {
            public string Path { get; }
            private readonly Action<string>? _diagnosticsLog;

            public ScenarioRoot(string prefix, Action<string>? diagnosticsLog = null)
            {
                _diagnosticsLog = diagnosticsLog;
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"{prefix}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                RootDisposalEvidence evidence = DisposeRootWithEvidence(DeleteContents);
                if (evidence.Exception != null && _diagnosticsLog != null)
                    SafeDiagnosticsLog(_diagnosticsLog, evidence.Text);
            }

            internal void DeleteContents()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
        }

        private static readonly object RetainedScenarioLock = new object();
        private static readonly List<RetainedScenarioLease> RetainedScenarioLeases =
            new List<RetainedScenarioLease>();

        private sealed class RetainedScenarioLease
        {
            private readonly ScenarioRoot _root;
            private readonly CancellationTokenSource _cts;
            private readonly Task _runTask;
            private readonly Action<string> _diagnosticsLog;
            private int _released;

            internal RetainedScenarioLease(
                string scenarioLabel,
                ScenarioRoot root,
                CancellationTokenSource cts,
                Task runTask,
                Action<string> diagnosticsLog)
            {
                _root = root;
                _cts = cts;
                _runTask = runTask;
                _diagnosticsLog = diagnosticsLog;
            }

            internal void Activate()
            {
                lock (RetainedScenarioLock)
                {
                    RetainedScenarioLeases.Add(this);
                }

                SafeDiagnosticsLog(_diagnosticsLog, FormatCleanupEvidence("retain", "retained", retained: true));

                _runTask.ContinueWith(
                    _ => ReleaseAfterQuiesced(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            private void ReleaseAfterQuiesced()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;

                ReleaseResourcesWithEvidence(
                    _cts.Dispose, _root.DeleteContents, continueAfterCtsFault: true, _diagnosticsLog);

                lock (RetainedScenarioLock)
                {
                    RetainedScenarioLeases.Remove(this);
                }
            }
        }

        internal static void RetainLiveResources(
            string scenarioLabel,
            ScenarioRoot root,
            CancellationTokenSource cts,
            Task runTask,
            Action<string> diagnosticsLog)
        {
            new RetainedScenarioLease(
                scenarioLabel,
                root,
                cts,
                runTask,
                diagnosticsLog).Activate();
        }

        internal static Task<RunCompletion> StartRunWithCompletion(
            string command,
            string[] args,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                () =>
                {
                    ProcessRunResult result = ProcessRunnerCore.Run(
                        command,
                        args,
                        workingDirectory,
                        ScenarioTimeoutMs,
                        OutputCapChars,
                        cancellationToken);
                    return new RunCompletion(result, DateTimeOffset.UtcNow);
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Build the leader+descendant fixture under <paramref name="root"/> and return the
        /// (command, args) pair ready to hand to <c>ProcessRunnerCore.Run</c>.
        /// </summary>
        internal static (string Command, string[] Args) BuildDescendantScenarioScript(
            string root,
            string leaderFinalPath,
            string childIdentityFinalPath,
            string childReadyFinalPath)
        {
            if (OperatingSystem.IsWindows())
            {
                string scriptPath = Path.Combine(root, "process_worker.ps1");
                string script = string.Join("\n", new[]
                {
                    "$ErrorActionPreference = 'Stop'",
                    "function Test-IdentityPayload([string]$Path, [string]$ExpectedPayload) {",
                    "    try {",
                    "        return [IO.File]::Exists($Path) -and ([IO.File]::ReadAllText($Path) -ceq $ExpectedPayload)",
                    "    }",
                    "    catch {",
                    "        return $false",
                    "    }",
                    "}",
                    "function Write-AtomicIdentity([string]$FinalPath, [string]$Payload) {",
                    "    $TempPath = \"$FinalPath.tmp\"",
                    "    [IO.File]::WriteAllText($TempPath, $Payload)",
                    "    for ($i = 0; $i -lt 3; $i++) {",
                    "        try { [IO.File]::Move($TempPath, $FinalPath); return $true }",
                    "        catch {",
                    "            if (Test-IdentityPayload $FinalPath $Payload) { return $true }",
                    "            if (-not [IO.File]::Exists($TempPath)) { return $false }",
                    "            Start-Sleep -Milliseconds 50",
                    "        }",
                    "    }",
                    "    return (Test-IdentityPayload $FinalPath $Payload)",
                    "}",
                    "$leaderNow = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()",
                    "if (-not (Write-AtomicIdentity "
                        + PowerShellStringLiteral(leaderFinalPath)
                        + " \"$PID|$leaderNow\")) { exit 97 }",
                    "$child = Start-Process -FilePath 'powershell.exe' -ArgumentList @("
                        + "'-NoProfile','-Command','Start-Sleep -Seconds "
                        + ChildWatchdogSeconds.ToString(CultureInfo.InvariantCulture)
                        + "') -NoNewWindow -PassThru",
                    "$childIdentityNow = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()",
                    "if (-not (Write-AtomicIdentity "
                        + PowerShellStringLiteral(childIdentityFinalPath)
                        + " \"$($child.Id)|$childIdentityNow\")) { exit 98 }",
                    "Start-Sleep -Seconds "
                        + IdentitySettleSeconds.ToString(CultureInfo.InvariantCulture),
                    "$child.Refresh()",
                    "if ($child.HasExited) { exit 99 }",
                    "$childNow = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()",
                    "if (-not (Write-AtomicIdentity "
                        + PowerShellStringLiteral(childReadyFinalPath)
                        + " \"$($child.Id)|$childNow\")) { exit 100 }",
                    "",
                });
                File.WriteAllText(scriptPath, script);
                return (
                    "powershell.exe",
                    new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath });
            }

            string pyScriptPath = Path.Combine(root, "process_worker.py");
            string pyScript = string.Join("\n", new[]
            {
                "import os,subprocess,sys,time",
                "def _atomic_write(path,payload):",
                "    tmp=path+'.tmp'",
                "    with open(tmp,'w') as fh:",
                "        fh.write(payload)",
                "    os.replace(tmp,path)",
                "os.setsid()",
                "leader_now=int(time.time()*1000)",
                "_atomic_write("
                    + PythonStringLiteral(leaderFinalPath)
                    + ",'%d|%d'%(os.getpid(),leader_now))",
                "child=subprocess.Popen([sys.executable,'-c',"
                    + "'import time;time.sleep("
                    + ChildWatchdogSeconds.ToString(CultureInfo.InvariantCulture)
                    + ")'])",
                "child_identity_now=int(time.time()*1000)",
                "_atomic_write("
                    + PythonStringLiteral(childIdentityFinalPath)
                    + ",'%d|%d'%(child.pid,child_identity_now))",
                "time.sleep(" + IdentitySettleSeconds.ToString(CultureInfo.InvariantCulture) + ")",
                "if child.poll() is not None:",
                "    sys.exit(99)",
                "child_now=int(time.time()*1000)",
                "_atomic_write("
                    + PythonStringLiteral(childReadyFinalPath)
                    + ",'%d|%d'%(child.pid,child_now))",
                "",
            });
            File.WriteAllText(pyScriptPath, pyScript);
            return ("/usr/bin/python3", new[] { pyScriptPath });
        }

        private static string PowerShellStringLiteral(string value) =>
            "'" + value.Replace("'", "''") + "'";

        private static string PythonStringLiteral(string value) =>
            "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        /// <summary>Parse a `&lt;pid&gt;|&lt;unixMs&gt;` payload. Null on any malformed content.</summary>
        internal static ProcessIdentity? TryParseIdentity(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;
            string[] parts = payload.Split('|');
            if (parts.Length != 2)
                return null;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
                return null;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixMs))
                return null;
            if (pid <= 0)
                return null;
            return new ProcessIdentity(pid, unixMs);
        }

        /// <summary>
        /// Poll for a readiness file at ≤<see cref="ReadinessPollMs"/> ms intervals until it
        /// exists and parses as a complete identity payload, or <paramref name="maxWait"/>
        /// elapses.
        /// </summary>
        internal static ProcessIdentity? PollForIdentity(
            string path,
            TimeSpan maxWait,
            Task? runTask = null) =>
            PollForIdentityWithEvidence(path, maxWait, runTask).Identity;

        internal static IdentityObservation ObserveIdentity(
            string? path, Func<string, bool> exists, Func<string, string> read)
        {
            if (string.IsNullOrEmpty(path) || !exists(path))
                return new IdentityObservation(IdentityObservationStatus.Missing);
            try
            {
                ProcessIdentity? identity = TryParseIdentity(read(path));
                return identity.HasValue
                    ? new IdentityObservation(IdentityObservationStatus.Valid, identity)
                    : new IdentityObservation(IdentityObservationStatus.Malformed);
            }
            catch (IOException ex)
            {
                return new IdentityObservation(
                    IdentityObservationStatus.ReadFault, faultCategory: ClassifyDiagnosticFault(ex));
            }
        }

        internal static ReadinessPollEvidence PollForIdentityWithEvidence(
            string path, TimeSpan maxWait, Task? runTask = null,
            Func<string, bool>? exists = null, Func<string, string>? read = null,
            Func<TimeSpan>? elapsed = null, Action<int>? delay = null)
        {
            exists ??= File.Exists;
            read ??= File.ReadAllText;
            if (elapsed == null)
            {
                var stopwatch = Stopwatch.StartNew();
                elapsed = () => stopwatch.Elapsed;
            }
            delay ??= Thread.Sleep;
            IdentityObservation? earlierReadFault = null;
            while (true)
            {
                IdentityObservation observation = ObserveIdentity(path, exists, read);
                if (observation.Status == IdentityObservationStatus.ReadFault)
                    earlierReadFault ??= observation;
                if (observation.Identity != null)
                    return new ReadinessPollEvidence(observation, earlierReadFault, ReadinessStopReason.Ready);
                if (runTask?.IsCompleted == true)
                {
                    observation = ObserveIdentity(path, exists, read);
                    return new ReadinessPollEvidence(
                        observation, earlierReadFault, ReadinessStopReason.TaskCompleted);
                }
                if (elapsed() >= maxWait)
                    return new ReadinessPollEvidence(
                        observation, earlierReadFault, ReadinessStopReason.Deadline);
                delay(ReadinessPollMs);
            }
        }

        /// <summary>Final authoritative re-read after the run completed; must be a complete payload.</summary>
        internal static ProcessIdentity? ReadFinalIdentity(string? path) =>
            ObserveIdentity(path, File.Exists, File.ReadAllText).Identity;

        private static bool IsExpectedProcessName(string name)
        {
            return name.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("python", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecordedProcessAlive(
            ProcessIdentity identity,
            DateTimeOffset scenarioStartUtc)
        {
            try
            {
                using Process process = Process.GetProcessById(identity.Pid);
                if (process.HasExited)
                    return false;
                if (!IsExpectedProcessName(process.ProcessName))
                    return false;

                int toleranceMs = OperatingSystem.IsWindows()
                    ? WindowsStartTimeToleranceMs
                    : PosixStartTimeToleranceMs;
                long windowStartMs =
                    scenarioStartUtc.ToUnixTimeMilliseconds() - toleranceMs;
                long windowEndMs = identity.RecordedUnixMs + toleranceMs;
                long startUnixMs =
                    new DateTimeOffset(process.StartTime.ToUniversalTime())
                        .ToUnixTimeMilliseconds();
                if (startUnixMs > windowEndMs)
                    return false; // PID was reused by a newer process.
                if (startUnixMs < windowStartMs)
                    return true; // Identity is implausibly old; fail closed.
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
        }

        internal static long ComputeCleanupMs(
            ProcessIdentity childIdentity,
            DateTimeOffset completionUtc)
        {
            return (long)(
                completionUtc
                - DateTimeOffset.FromUnixTimeMilliseconds(childIdentity.RecordedUnixMs))
                .TotalMilliseconds;
        }

        /// <summary>
        /// Poll (≤<see cref="DescendantDeathPollSeconds"/> s total) for the descendant identified
        /// by <paramref name="identity"/> to die, then accept it as containment evidence only if
        /// death was observed within <see cref="ReadinessAcceptanceWindowMs"/> of
        /// <paramref name="readinessRecordedAtUtc"/> (the atomically-recorded child-readiness
        /// timestamp) — see <see cref="ReadinessAcceptanceWindowMs"/> for why a later death is
        /// rejected rather than accepted.
        /// </summary>
        internal static bool DescendantDiedWithinAcceptedWindow(
            ProcessIdentity identity,
            DateTimeOffset readinessRecordedAtUtc,
            DateTimeOffset scenarioStartUtc,
            out TimeSpan observedAfterReadiness)
        {
            var pollStopwatch = Stopwatch.StartNew();
            bool alive = IsRecordedProcessAlive(identity, scenarioStartUtc);
            while (alive && pollStopwatch.Elapsed < TimeSpan.FromSeconds(DescendantDeathPollSeconds))
            {
                Thread.Sleep(10);
                alive = IsRecordedProcessAlive(identity, scenarioStartUtc);
            }
            observedAfterReadiness = DateTimeOffset.UtcNow - readinessRecordedAtUtc;
            return !alive
                && observedAfterReadiness < TimeSpan.FromMilliseconds(ReadinessAcceptanceWindowMs);
        }

        /// <summary>
        /// On-failure cleanup only: cancel the scenario's own <paramref name="cts"/>, wait
        /// <see cref="CancelGraceMs"/> ms for <paramref name="runTask"/>, best-effort-kill the
        /// recorded leader/child identities, then wait a final <see cref="FinalKillWaitMs"/> ms
        /// for <paramref name="runTask"/>. The child identity is re-read from
        /// <paramref name="childIdentityPath"/> after cancellation grace so a child spawned just
        /// before failure is still owned even when readiness was never published. Kills are
        /// attempted ONLY when the PID still exists,
        /// its process name (OrdinalIgnoreCase) starts with "powershell" or "python", and its
        /// actual live <c>Process.StartTime</c> (UTC, as unix ms) falls inside
        /// [<paramref name="scenarioStartUtc"/> − tolerance,
        /// identity.RecordedUnixMs + tolerance]. This
        /// bounded window is what prevents a PID-reuse false-kill of an unrelated process that
        /// happened to be assigned the same PID after ours already exited. Returns true only
        /// when the Run task is quiesced; callers must retain CTS/root/task if false. Never
        /// throws; cleanup-only exceptions are logged via <paramref name="diagnosticsLog"/>,
        /// never allowed to replace/mask the caller's primary failure.
        /// </summary>
        internal static bool BestEffortCleanup(
            CancellationTokenSource cts,
            Task? runTask,
            ProcessIdentity? leaderIdentity,
            string? childIdentityPath,
            ProcessIdentity? childIdentity,
            DateTimeOffset scenarioStartUtc,
            Action<string> diagnosticsLog,
            Action<CleanupFault>? secondaryFault = null)
        {
            bool runQuiesced = runTask == null || runTask.IsCompleted;
            string cleanupStage = "cancel";
            try
            {
                RunCleanupCancelOperationWithEvidence(cts.Cancel, diagnosticsLog, secondaryFault);

                cleanupStage = "wait";
                bool quiescedAfterCancel = WaitForRunTaskQuiescence(
                    runTask,
                    CancelGraceMs,
                    "wait",
                    diagnosticsLog,
                    secondaryFault);

                cleanupStage = "owned-kill";
                ProcessIdentity? refreshedChildIdentity =
                    ReadFinalIdentity(childIdentityPath) ?? childIdentity;
                TryBestEffortKill(leaderIdentity, scenarioStartUtc, diagnosticsLog, secondaryFault);
                TryBestEffortKill(refreshedChildIdentity, scenarioStartUtc, diagnosticsLog, secondaryFault);

                cleanupStage = "owned-wait";
                bool quiescedAfterKill = WaitForRunTaskQuiescence(
                    runTask,
                    FinalKillWaitMs,
                    "owned-wait",
                    diagnosticsLog,
                    secondaryFault);

                runQuiesced = quiescedAfterCancel || quiescedAfterKill;
                if (!runQuiesced)
                {
                    SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("retain", "retained", retained: true));
                }
            }
            catch (Exception ex)
            {
                // Cleanup must never replace the primary test failure with one of its own.
                secondaryFault?.Invoke(new CleanupFault(cleanupStage, ex));
                SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence(cleanupStage, "fault", ex));
                runQuiesced = runQuiesced || (runTask == null || runTask.IsCompleted);
            }
            return runQuiesced;
        }

        private static bool WaitForRunTaskQuiescence(
            Task? runTask,
            int waitMs,
            string phase,
            Action<string> diagnosticsLog,
            Action<CleanupFault>? secondaryFault)
        {
            if (runTask == null)
                return true;

            return RunCleanupWaitOperationWithEvidence(
                phase, () => runTask.Wait(TimeSpan.FromMilliseconds(waitMs)),
                diagnosticsLog, secondaryFault);
        }

        private static void SafeDiagnosticsLog(Action<string> diagnosticsLog, string message)
        {
            try
            {
                diagnosticsLog(message);
            }
            catch
            {
                // Never let diagnostic logging mask the primary outcome.
            }
        }

        private static void TryBestEffortKill(
            ProcessIdentity? identity,
            DateTimeOffset scenarioStartUtc,
            Action<string> diagnosticsLog,
            Action<CleanupFault>? secondaryFault)
        {
            if (identity == null)
                return;
            ProcessIdentity id = identity.Value;
            int toleranceMs = OperatingSystem.IsWindows()
                ? WindowsStartTimeToleranceMs
                : PosixStartTimeToleranceMs;
            long windowStartMs = scenarioStartUtc.ToUnixTimeMilliseconds() - toleranceMs;
            long windowEndMs = id.RecordedUnixMs + toleranceMs;
            try
            {
                using Process process = Process.GetProcessById(id.Pid);
                string name = process.ProcessName;
                bool nameMatches = IsExpectedProcessName(name);
                if (!nameMatches)
                {
                    SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "unavailable"));
                    return;
                }
                long startUnixMs =
                    new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                if (startUnixMs < windowStartMs || startUnixMs > windowEndMs)
                {
                    SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "unavailable"));
                    return;
                }
                process.Kill(entireProcessTree: true);
                process.WaitForExit(FinalKillWaitMs);
            }
            catch (ArgumentException ex)
            {
                secondaryFault?.Invoke(new CleanupFault("owned-kill", ex));
                SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "fault", ex));
            }
            catch (InvalidOperationException ex)
            {
                secondaryFault?.Invoke(new CleanupFault("owned-kill", ex));
                SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "fault", ex));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                secondaryFault?.Invoke(new CleanupFault("owned-kill", ex));
                SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "fault", ex));
            }
            catch (PlatformNotSupportedException ex)
            {
                secondaryFault?.Invoke(new CleanupFault("owned-kill", ex));
                SafeDiagnosticsLog(diagnosticsLog, FormatCleanupEvidence("owned-kill", "fault", ex));
            }
        }

        /// <summary>Emit one grep-able PROCESS_RUNNER_METRIC line per scenario iteration.</summary>
        internal static void EmitMetric(
            ITestOutputHelper output,
            string scenario,
            int iteration,
            long readinessMs,
            long? cleanupMs,
            long totalMs,
            string outcome)
        {
            string cleanupValue = cleanupMs?.ToString(CultureInfo.InvariantCulture)
                ?? "unknown";
            SafeDiagnosticsLog(
                message => output.WriteLine(message),
                "PROCESS_RUNNER_METRIC "
                + $"scenario={scenario} iteration={iteration} "
                + $"readinessMs={readinessMs} cleanupMs={cleanupValue} totalMs={totalMs} "
                + $"outcome={outcome}");
        }
    }
}
