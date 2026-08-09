// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #2050: one contention fact that deliberately races two ProcessRunnerCore.Run(...)
    /// launches against each other to exercise host-global process-tree containment under real
    /// concurrent load, reusing the exact same leader/descendant fixture as
    /// <see cref="ProcessRunnerProcessTreeTests.Run_ParentExitStillTerminatesDescendantHoldingPipes"/>
    /// via <see cref="ProcessRunnerScenarioSupport"/>.
    ///
    /// Unlike that strict single-launch theory, real concurrent contention can legitimately put
    /// the dedicated LongRunning output-capture tasks under scheduler/OS process-thread
    /// pressure for a moment, so the oracle here accepts the documented
    /// <see cref="ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage"/> outcome as a
    /// pass alongside the normal clean exit — any other exit code/error-message shape, or any
    /// exception, is rejected. No cleanup-performance assertion is made here; only correctness
    /// plus a generous post-release liveness bound.
    /// </summary>
    [Collection(ProcessRunnerTestCollection.Name)]
    public class ProcessRunnerContentionTests
    {
        private readonly ITestOutputHelper _output;

        public ProcessRunnerContentionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Contention_TwoConcurrentLaunches()
        {
            RunContentionScenario(nameof(Contention_TwoConcurrentLaunches));
        }

        private void RunContentionScenario(string scenarioName)
        {
            // Preflight before spawning any task: skip up front on hosts with no suitable
            // long-running interpreter instead of discovering that 45s into the handshake.
            if (!OperatingSystem.IsWindows() && !File.Exists("/usr/bin/python3"))
            {
                ProcessRunnerScenarioSupport.EmitMetric(
                    _output,
                    scenarioName,
                    0,
                    -1,
                    null,
                    0,
                    "host-unsupported");
                return;
            }

            var countdown = new CountdownEvent(2);
            var release = new ManualResetEventSlim(false);

            var participant1 = new ContentionParticipant(scenarioName, 1, countdown, release, _output);
            var participant2 = new ContentionParticipant(scenarioName, 2, countdown, release, _output);

            Task task1 = Task.Factory.StartNew(
                participant1.Run,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Task task2 = Task.Factory.StartNew(
                participant2.Run,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            string? rendezvousFailure = null;
            try
            {
                bool bothPrepared = countdown.Wait(TimeSpan.FromSeconds(45));
                if (!bothPrepared)
                {
                    rendezvousFailure =
                        "phase=prepare not all participants signalled preparation within 45s.";
                }
            }
            finally
            {
                // Always release — a stalled participant must never deadlock the other one.
                release.Set();
            }

            bool bothCompleted = Task.WaitAll(new[] { task1, task2 }, TimeSpan.FromSeconds(150));
            if (bothCompleted)
            {
                countdown.Dispose();
                release.Dispose();
            }
            else
            {
                _ = Task.WhenAll(task1, task2).ContinueWith(
                    _ =>
                    {
                        countdown.Dispose();
                        release.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            Assert.True(
                bothCompleted,
                $"{scenarioName}: phase=completion contention participants did not complete within the 150s "
                + "post-release liveness window.");

            participant1.AssertOutcome();
            participant2.AssertOutcome();
            Assert.True(rendezvousFailure == null, $"{scenarioName}: {rendezvousFailure}");
        }

        private sealed class ContentionParticipant
        {
            private readonly string _scenarioName;
            private readonly int _index;
            private readonly CountdownEvent _countdown;
            private readonly ManualResetEventSlim _release;
            private readonly ITestOutputHelper _output;

            private Exception? _failure;
            private string? _rejectReason;
            private bool _readinessObserved;
            private long _readinessMs;
            private long? _cleanupMs;
            private ProcessRunResult _result;
            private bool _resultCaptured;
            private bool _resultShapeAccepted;
            private bool _finalIdentityCaptured;
            private bool _identityConsistent;
            private bool _descendantDied;
            private bool _scenarioPassed;
            private TimeSpan _observedAfterReadiness;

            public ContentionParticipant(
                string scenarioName,
                int index,
                CountdownEvent countdown,
                ManualResetEventSlim release,
                ITestOutputHelper output)
            {
                _scenarioName = scenarioName;
                _index = index;
                _countdown = countdown;
                _release = release;
                _output = output;
            }

            public void Run()
            {
                var totalStopwatch = Stopwatch.StartNew();
                ProcessRunnerScenarioSupport.ScenarioRoot? root = null;
                CancellationTokenSource? cts = null;
                Task<ProcessRunnerScenarioSupport.RunCompletion>? runTask = null;
                string? leaderPath = null;
                string? childIdentityPath = null;
                ProcessRunnerScenarioSupport.ProcessIdentity? childIdentity = null;
                DateTimeOffset scenarioStartUtc = DateTimeOffset.UtcNow;
                try
                {
                    root = new ProcessRunnerScenarioSupport.ScenarioRoot(
                        $"febuildergba_process_contention_{_index}");
                    leaderPath = Path.Combine(root.Path, "leader.pid");
                    childIdentityPath = Path.Combine(root.Path, "child.pid");
                    string childReadyPath = Path.Combine(root.Path, "child.ready");
                    (string command, string[] args) =
                        ProcessRunnerScenarioSupport.BuildDescendantScenarioScript(
                            root.Path, leaderPath, childIdentityPath, childReadyPath);

                    cts = new CancellationTokenSource();

                    // Preparation is complete: signal the coordinator, then wait for the shared
                    // release gate so both participants launch as close to simultaneously as
                    // possible, creating real host-global contention.
                    _countdown.Signal();
                    _release.Wait();

                    scenarioStartUtc = DateTimeOffset.UtcNow;
                    Task<ProcessRunnerScenarioSupport.RunCompletion> localRunTask =
                        ProcessRunnerScenarioSupport.StartRunWithCompletion(
                            command,
                            args,
                            root.Path,
                            cts.Token);
                    runTask = localRunTask;

                    var readinessStopwatch = Stopwatch.StartNew();
                    childIdentity = ProcessRunnerScenarioSupport.PollForIdentity(
                        childReadyPath, TimeSpan.FromSeconds(50), localRunTask);
                    _readinessMs = readinessStopwatch.ElapsedMilliseconds;
                    _readinessObserved = childIdentity != null;
                    if (!_readinessObserved)
                    {
                        _rejectReason = "Child readiness file was never published.";
                        return;
                    }

                    bool completed = localRunTask.Wait(TimeSpan.FromSeconds(50));
                    if (!completed)
                    {
                        _rejectReason = "Run(...) did not complete within the participant's bounded wait.";
                        return;
                    }

                    ProcessRunnerScenarioSupport.ProcessIdentity readyIdentity =
                        TestRequire.HasValue(childIdentity, "child identity");
                    ProcessRunnerScenarioSupport.RunCompletion runCompletion = localRunTask.Result;
                    _cleanupMs = ProcessRunnerScenarioSupport.ComputeCleanupMs(
                        readyIdentity,
                        runCompletion.CompletionUtc);
                    _result = runCompletion.Result;
                    _resultCaptured = true;
                    bool cleanExit = _result.ExitCode == 0 && _result.ErrorMessage == "";
                    bool captureRace = _result.ExitCode == -1
                        && _result.ErrorMessage == ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage;
                    _resultShapeAccepted =
                        _result.Started
                        && !_result.TimedOut
                        && !_result.OutputLimitExceeded
                        && !_result.TerminationFailed
                        && !_result.Cancelled
                        && (cleanExit || captureRace);

                    var finalChildIdentity =
                        ProcessRunnerScenarioSupport.ReadFinalIdentity(childIdentityPath);
                    var finalChildReady =
                        ProcessRunnerScenarioSupport.ReadFinalIdentity(childReadyPath);
                    if (finalChildIdentity != null && finalChildReady != null)
                    {
                        _finalIdentityCaptured = true;
                        _identityConsistent =
                            finalChildIdentity.Value.Pid == finalChildReady.Value.Pid
                            && finalChildReady.Value.RecordedUnixMs
                                >= finalChildIdentity.Value.RecordedUnixMs;
                        if (!_identityConsistent)
                        {
                            _rejectReason =
                                "Child identity/readiness payloads disagreed.";
                        }
                        _descendantDied = ProcessRunnerScenarioSupport.DescendantDiedWithinAcceptedWindow(
                            finalChildReady.Value,
                            DateTimeOffset.FromUnixTimeMilliseconds(
                                finalChildReady.Value.RecordedUnixMs),
                            scenarioStartUtc,
                            out _observedAfterReadiness);
                    }
                    else
                    {
                        _rejectReason = "Child readiness payload was incomplete after Run() completed.";
                    }

                    _scenarioPassed =
                        _resultShapeAccepted
                        && _finalIdentityCaptured
                        && _identityConsistent
                        && _descendantDied;
                }
                catch (Exception ex)
                {
                    _failure = ex;
                }
                finally
                {
                    bool needsCleanup = !_scenarioPassed;
                    if (needsCleanup && cts != null)
                    {
                        bool runQuiesced = ProcessRunnerScenarioSupport.BestEffortCleanup(
                            cts,
                            runTask,
                            ProcessRunnerScenarioSupport.ReadFinalIdentity(leaderPath),
                            childIdentityPath,
                            childIdentity,
                            scenarioStartUtc,
                            SafeLog);
                        if (!runQuiesced && root != null && runTask != null)
                        {
                            ProcessRunnerScenarioSupport.ScenarioRoot retainedRoot = root;
                            CancellationTokenSource retainedCts = cts;
                            root = null;
                            cts = null;
                            ProcessRunnerScenarioSupport.RetainLiveResources(
                                $"{_scenarioName}#{_index}",
                                retainedRoot,
                                retainedCts,
                                runTask,
                                SafeLog);
                        }
                    }

                    if (_cleanupMs == null
                        && childIdentity != null
                        && runTask?.Status == TaskStatus.RanToCompletion)
                    {
                        _cleanupMs = ProcessRunnerScenarioSupport.ComputeCleanupMs(
                            childIdentity.Value,
                            runTask.Result.CompletionUtc);
                    }

                    cts?.Dispose();
                    root?.Dispose();

                    bool captureRace = _resultCaptured
                        && _result.ExitCode == -1
                        && _result.ErrorMessage == ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage;
                    string outcome = _failure != null
                        ? "exception"
                        : (!_readinessObserved
                            ? "readiness-missed"
                            : (!_resultCaptured
                                ? "run-incomplete"
                                : (!_resultShapeAccepted
                                    ? "result-rejected"
                                    : (!_finalIdentityCaptured
                                        ? "final-identity-missing"
                                        : (!_descendantDied
                                            ? "descendant-not-dead"
                                            : (captureRace ? "capture-race" : "pass"))))));
                    ProcessRunnerScenarioSupport.EmitMetric(
                        _output,
                        _scenarioName,
                        _index,
                        _readinessMs,
                        _cleanupMs,
                        totalStopwatch.ElapsedMilliseconds,
                        outcome);
                }
            }

            public void AssertOutcome()
            {
                Assert.True(_failure == null, $"Participant {_index} threw: {_failure}");
                Assert.True(
                    _readinessObserved,
                    $"Participant {_index}: {_rejectReason ?? "child readiness was never observed."}");
                Assert.True(
                    _resultCaptured,
                    $"Participant {_index}: {_rejectReason ?? "Run(...) did not complete."}");
                Assert.True(
                    _finalIdentityCaptured,
                    $"Participant {_index}: {_rejectReason ?? "child readiness payload was incomplete after Run() completed."}");
                Assert.True(
                    _identityConsistent,
                    $"Participant {_index}: {_rejectReason ?? "child identity/readiness payloads disagreed."}");
                Assert.True(_result.Started, $"Participant {_index}: {_result.ErrorMessage}");
                Assert.False(_result.TimedOut, $"Participant {_index}: {_result.ErrorMessage}");
                Assert.False(_result.OutputLimitExceeded, $"Participant {_index}: {_result.ErrorMessage}");
                Assert.False(_result.TerminationFailed, $"Participant {_index}: {_result.ErrorMessage}");
                Assert.False(_result.Cancelled, $"Participant {_index}: {_result.ErrorMessage}");

                bool cleanExit = _result.ExitCode == 0 && _result.ErrorMessage == "";
                bool captureRace = _result.ExitCode == -1
                    && _result.ErrorMessage == ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage;
                // This result is accepted only with the independent descendant-death assertion
                // below; a capture message alone can never satisfy the containment oracle.
                Assert.True(
                    cleanExit || captureRace,
                    $"Participant {_index}: unexpected result shape ExitCode={_result.ExitCode} "
                    + $"ErrorMessage='{_result.ErrorMessage}'.");

                Assert.True(
                    _descendantDied,
                    $"Participant {_index}: descendant outlived containment or died too late "
                    + $"({_observedAfterReadiness}).");
            }

            private void SafeLog(string message)
            {
                try
                {
                    _output.WriteLine($"[{_scenarioName}#{_index}] {message}");
                }
                catch
                {
                    // Never let diagnostic logging mask the primary outcome.
                }
            }
        }
    }
}
