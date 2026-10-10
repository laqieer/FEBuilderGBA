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
    /// Strict single-launch process-tree oracle. This class is isolated from ordinary
    /// Core.Tests because it observes host-global PID liveness and wall-clock phase timing.
    /// Pure ProcessRunnerCore unit tests remain in <see cref="ProcessRunnerCoreTests"/>.
    /// </summary>
    [Collection(ProcessRunnerTestCollection.Name)]
    public class ProcessRunnerProcessTreeTests
    {
        private readonly ITestOutputHelper _output;

        public ProcessRunnerProcessTreeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        internal static ProcessRunnerScenarioSupport.FailureEvidence CaptureFailurePhaseEvidence(
            ProcessRunnerScenarioSupport.ReadinessEvidence readiness, string phase) =>
            ProcessRunnerScenarioSupport.CreateObservedFailureEvidence(
                null, readiness, phase, ProcessRunnerScenarioSupport.CreateCleanupObservations());

        // #2050: ProcessRunnerCore.Run executes on a dedicated LongRunning task while this
        // method polls externally (<=25 ms cadence) for atomically published child readiness.
        // The old whole-run 10s budget is replaced by an explicit max-25s wait after observed
        // readiness plus a payload-timestamp-to-wrapper-completion phase assertion.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Run_ParentExitStillTerminatesDescendantHoldingPipes(int iteration)
        {
            if (!OperatingSystem.IsWindows() && !File.Exists("/usr/bin/python3"))
            {
                ProcessRunnerScenarioSupport.EmitMetric(
                    _output,
                    nameof(Run_ParentExitStillTerminatesDescendantHoldingPipes),
                    iteration,
                    -1,
                    null,
                    0,
                    "host-unsupported");
                return;
            }

            ProcessRunnerScenarioSupport.ScenarioRoot? root = null;
            CancellationTokenSource? cts = null;
            string? leaderPath = null;
            string? childIdentityPath = null;
            string? childReadyPath = null;
            Task<ProcessRunnerScenarioSupport.RunCompletion>? runTask = null;
            ProcessRunnerScenarioSupport.RunCompletion? runCompletion = null;
            DateTimeOffset scenarioStartUtc = DateTimeOffset.UtcNow;
            ProcessRunnerScenarioSupport.ProcessIdentity? childIdentity = null;
            ProcessRunnerScenarioSupport.ReadinessPollEvidence? poll = null;
            ProcessRunnerScenarioSupport.ReadinessEvidence? evidence = null;
            Exception? primaryFailure = null;
            Exception? secondaryFailure = null;
            var cleanup = ProcessRunnerScenarioSupport.CreateCleanupObservations();
            string phase = "prepare";
            ProcessRunResult result = default;
            bool scenarioPassed = false;
            long readinessMs = 0;
            long? cleanupMs = null;
            string outcome = "failed";
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                root = new ProcessRunnerScenarioSupport.ScenarioRoot(
                    "febuildergba_process_tree", msg => _output.WriteLine(msg));
                leaderPath = Path.Combine(root.Path, "leader.pid");
                childIdentityPath = Path.Combine(root.Path, "child.pid");
                childReadyPath = Path.Combine(root.Path, "child.ready");
                (string command, string[] args) =
                    ProcessRunnerScenarioSupport.BuildDescendantScenarioScript(
                        root.Path, leaderPath, childIdentityPath, childReadyPath);

                cts = new CancellationTokenSource();
                scenarioStartUtc = DateTimeOffset.UtcNow;
                runTask = ProcessRunnerScenarioSupport.StartRunWithCompletion(
                    command,
                    args,
                    root.Path,
                    cts.Token);

                var readinessStopwatch = Stopwatch.StartNew();
                phase = "readiness";
                poll = ProcessRunnerScenarioSupport.PollForIdentityWithEvidence(
                    childReadyPath, TimeSpan.FromSeconds(25), runTask);
                childIdentity = poll.Identity;
                readinessMs = readinessStopwatch.ElapsedMilliseconds;
                evidence = ProcessRunnerScenarioSupport.FreezeReadinessEvidence(
                    poll, phase, childIdentity != null, runTask, readinessMs);
                Assert.True(
                    childIdentity != null,
                    "No valid child readiness identity was observed. "
                    + CaptureFailurePhaseEvidence(evidence, phase).Text);

                phase = "run-incomplete";
                Task completedTask = await Task.WhenAny(
                    runTask,
                    Task.Delay(TimeSpan.FromSeconds(25)));
                Assert.True(
                    ReferenceEquals(runTask, completedTask),
                    "Run(...) did not complete within 25s of observed child readiness. "
                    + CaptureFailurePhaseEvidence(evidence, phase).Text);
                runCompletion = await runTask;
                evidence = ProcessRunnerScenarioSupport.FillReadinessResultBeforeCleanup(
                    evidence, runTask, cleanupStarted: false);
                phase = "assertion";
                cleanupMs = ProcessRunnerScenarioSupport.ComputeCleanupMs(
                    childIdentity.Value,
                    runCompletion.Value.CompletionUtc);
                Assert.InRange(cleanupMs.Value, 0, 24_999);

                result = runCompletion.Value.Result;
                phase = "result-rejected";
                string resultEvidence = CaptureFailurePhaseEvidence(evidence, phase).Text;
                Assert.True(result.Started, resultEvidence);
                Assert.False(result.TimedOut, resultEvidence);
                Assert.False(result.OutputLimitExceeded, resultEvidence);
                Assert.False(result.TerminationFailed, resultEvidence);
                Assert.False(result.Cancelled, resultEvidence);
                Assert.True(
                    result.ExitCode == 0,
                    "Expected exit 0. " + resultEvidence);
                Assert.True(result.ErrorMessage == "", "Expected empty error. " + resultEvidence);

                phase = "assertion";
                resultEvidence = CaptureFailurePhaseEvidence(evidence, phase).Text;
                var finalLeader = ProcessRunnerScenarioSupport.ReadFinalIdentity(leaderPath);
                Assert.True(finalLeader != null, "Leader did not record a final atomic identity payload. " + resultEvidence);
                var finalChildIdentity =
                    ProcessRunnerScenarioSupport.ReadFinalIdentity(childIdentityPath);
                Assert.True(
                    finalChildIdentity != null,
                    "Child did not record an immediate atomic identity payload. " + resultEvidence);
                var finalChildReady =
                    ProcessRunnerScenarioSupport.ReadFinalIdentity(childReadyPath);
                Assert.True(
                    finalChildReady != null,
                    "Child readiness payload was incomplete after Run() completed. " + resultEvidence);
                Assert.True(
                    finalChildIdentity.Value.Pid == finalChildReady.Value.Pid,
                    "Child identity/readiness payloads disagreed. " + resultEvidence);
                Assert.True(
                    finalChildReady.Value.RecordedUnixMs
                        >= finalChildIdentity.Value.RecordedUnixMs,
                    "Child readiness timestamp preceded its spawn identity. " + resultEvidence);

                bool died = ProcessRunnerScenarioSupport.DescendantDiedWithinAcceptedWindow(
                    finalChildReady.Value,
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        finalChildReady.Value.RecordedUnixMs),
                    scenarioStartUtc,
                    out TimeSpan observedAfter);
                Assert.True(died, $"Descendant outlived containment or died too late ({observedAfter}). " + resultEvidence);

                scenarioPassed = true;
                outcome = "pass";
            }
            catch (Exception ex)
            {
                primaryFailure = ex;
                evidence ??= ProcessRunnerScenarioSupport.FreezeReadinessEvidence(
                    poll, phase, childIdentity != null, runTask, readinessMs);
                if (ex is Xunit.Sdk.XunitException)
                    throw;
                throw new ProcessRunnerScenarioSupport.ScenarioFailureException(
                    ProcessRunnerScenarioSupport.CreateObservedFailureEvidence(
                        ex, evidence, phase, cleanup).Text,
                    ex);
            }
            finally
            {
                evidence ??= ProcessRunnerScenarioSupport.FreezeReadinessEvidence(
                    poll, phase, childIdentity != null, runTask, readinessMs);
                bool runQuiesced = runTask == null || runTask.IsCompleted;
                try
                {
                    if (!scenarioPassed && cts != null)
                    {
                        runQuiesced = ProcessRunnerScenarioSupport.BestEffortCleanup(
                            cts,
                            runTask,
                            ProcessRunnerScenarioSupport.ReadFinalIdentity(leaderPath),
                            childIdentityPath,
                            childIdentity,
                            scenarioStartUtc,
                            msg => _output.WriteLine(msg),
                            fault =>
                            {
                                cleanup.Add(fault);
                                secondaryFailure ??= fault.Exception;
                            });
                    }
                }
                catch (Exception ex)
                {
                    secondaryFailure ??= ex;
                    ProcessRunnerScenarioSupport.RecordCleanupFault(cleanup, "owned-kill", ex);
                    ProcessRunnerScenarioSupport.LogEvidence(
                        msg => _output.WriteLine(msg),
                        ProcessRunnerScenarioSupport.FormatCleanupEvidence("owned-kill", "fault", ex));
                    runQuiesced = runTask == null || runTask.IsCompleted;
                }
                finally
                {
                    if (!runQuiesced && root != null && cts != null && runTask != null)
                    {
                        ProcessRunnerScenarioSupport.ScenarioRoot retainedRoot = root;
                        CancellationTokenSource retainedCts = cts;
                        root = null;
                        cts = null;
                        ProcessRunnerScenarioSupport.RecordCleanupRetention(cleanup);
                        ProcessRunnerScenarioSupport.RetainLiveResources(
                            $"{nameof(Run_ParentExitStillTerminatesDescendantHoldingPipes)}[{iteration}]",
                            retainedRoot, retainedCts, runTask, msg => _output.WriteLine(msg));
                    }
                    ProcessRunnerScenarioSupport.ReleaseEvidence releaseEvidence =
                        ProcessRunnerScenarioSupport.ReleaseResourcesWithEvidence(
                            cts == null ? null : cts.Dispose,
                            root == null ? null : root.DeleteContents,
                            continueAfterCtsFault: false, msg => _output.WriteLine(msg));
                    ProcessRunnerScenarioSupport.RecordReleaseEvidence(cleanup, releaseEvidence);
                    secondaryFailure ??= releaseEvidence.PolicySecondaryFailure;
                }

                try
                {
                    if (cleanupMs == null)
                    {
                        cleanupMs = await ResolveCleanupMsAsync(
                            runTask,
                            runCompletion,
                            childIdentity);
                    }
                }
                catch (Exception ex)
                {
                    secondaryFailure ??= ex;
                    ProcessRunnerScenarioSupport.RecordCleanupFault(cleanup, "wait", ex);
                    ProcessRunnerScenarioSupport.LogEvidence(
                        msg => _output.WriteLine(msg),
                        ProcessRunnerScenarioSupport.FormatCleanupEvidence("wait", "fault", ex));
                }

                if (!scenarioPassed || cleanup.FirstSecondary != null)
                {
                    ProcessRunnerScenarioSupport.LogEvidence(
                        msg => _output.WriteLine(msg),
                        ProcessRunnerScenarioSupport.CreateObservedFailureEvidence(
                            primaryFailure, evidence, phase, cleanup).Text);
                }

                if (primaryFailure == null && secondaryFailure != null)
                    outcome = "cleanup-fault";
                ProcessRunnerScenarioSupport.EmitMetric(
                    _output,
                    nameof(Run_ParentExitStillTerminatesDescendantHoldingPipes),
                    iteration,
                    readinessMs,
                    cleanupMs,
                    totalStopwatch.ElapsedMilliseconds,
                    outcome);
                if (primaryFailure == null && secondaryFailure != null)
                {
                    string secondaryStage = "unavailable";
                    foreach (ProcessRunnerScenarioSupport.CleanupFault fault in cleanup.Faults)
                    {
                        if (ReferenceEquals(fault.Exception, secondaryFailure))
                        {
                            secondaryStage = fault.Stage;
                            break;
                        }
                    }
                    throw new ProcessRunnerScenarioSupport.ScenarioFailureException(
                        ProcessRunnerScenarioSupport.FormatCleanupEvidence(
                            secondaryStage, "fault", secondaryFailure),
                        secondaryFailure);
                }
            }
        }

        [Fact]
        public async Task ResolveCleanupMsAsync_ReturnsCompletedTaskCleanup_WhenCompletionWasNotCaptured()
        {
            ProcessRunnerScenarioSupport.ProcessIdentity childIdentity =
                new ProcessRunnerScenarioSupport.ProcessIdentity(123, 1_000);
            ProcessRunnerScenarioSupport.RunCompletion completion =
                new ProcessRunnerScenarioSupport.RunCompletion(
                    default,
                    DateTimeOffset.FromUnixTimeMilliseconds(1_250));
            Task<ProcessRunnerScenarioSupport.RunCompletion> runTask =
                Task.FromResult(completion);

            long? cleanupMs = await ResolveCleanupMsAsync(
                runTask,
                null,
                childIdentity);

            Assert.Equal(250, cleanupMs);
        }

        private static async Task<long?> ResolveCleanupMsAsync(
            Task<ProcessRunnerScenarioSupport.RunCompletion>? runTask,
            ProcessRunnerScenarioSupport.RunCompletion? runCompletion,
            ProcessRunnerScenarioSupport.ProcessIdentity? childIdentity)
        {
            if (childIdentity == null)
                return null;

            if (runCompletion != null)
            {
                return ProcessRunnerScenarioSupport.ComputeCleanupMs(
                    childIdentity.Value,
                    runCompletion.Value.CompletionUtc);
            }

            if (runTask?.IsCompletedSuccessfully == true)
            {
                ProcessRunnerScenarioSupport.RunCompletion completed = await runTask;
                return ProcessRunnerScenarioSupport.ComputeCleanupMs(
                    childIdentity.Value,
                    completed.CompletionUtc);
            }

            return null;
        }
    }
}
