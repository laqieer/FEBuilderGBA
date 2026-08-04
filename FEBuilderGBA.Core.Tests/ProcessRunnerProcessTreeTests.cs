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

        // #2050: ProcessRunnerCore.Run executes on a dedicated LongRunning task while this
        // method polls externally (<=25 ms cadence) for atomically published child readiness.
        // The old whole-run 10s budget is replaced by an explicit max-25s wait after observed
        // readiness plus a payload-timestamp-to-wrapper-completion phase assertion.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Run_ParentExitStillTerminatesDescendantHoldingPipes(int iteration)
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
            DateTimeOffset scenarioStartUtc = DateTimeOffset.UtcNow;
            ProcessRunnerScenarioSupport.ProcessIdentity? childIdentity = null;
            ProcessRunResult result = default;
            bool scenarioPassed = false;
            long readinessMs = 0;
            long? cleanupMs = null;
            string outcome = "failed";
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                root = new ProcessRunnerScenarioSupport.ScenarioRoot(
                    "febuildergba_process_tree");
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
                childIdentity = ProcessRunnerScenarioSupport.PollForIdentity(
                    childReadyPath, TimeSpan.FromSeconds(25), runTask);
                readinessMs = readinessStopwatch.ElapsedMilliseconds;
                Assert.True(childIdentity != null, "Child readiness file was never published.");

                bool completed = runTask.Wait(TimeSpan.FromSeconds(25));
                Assert.True(
                    completed,
                    "Run(...) did not complete within 25s of observed child readiness.");
                ProcessRunnerScenarioSupport.RunCompletion runCompletion = runTask.Result;
                cleanupMs = ProcessRunnerScenarioSupport.ComputeCleanupMs(
                    childIdentity.Value,
                    runCompletion.CompletionUtc);
                Assert.InRange(cleanupMs.Value, 0, 24_999);

                result = runCompletion.Result;
                Assert.True(result.Started, result.ErrorMessage);
                Assert.False(result.TimedOut, result.ErrorMessage);
                Assert.False(result.OutputLimitExceeded, result.ErrorMessage);
                Assert.False(result.TerminationFailed, result.ErrorMessage);
                Assert.False(result.Cancelled, result.ErrorMessage);
                Assert.True(
                    result.ExitCode == 0,
                    $"Expected exit 0, got {result.ExitCode}. "
                    + $"ErrorMessage: {result.ErrorMessage}");
                Assert.Equal("", result.ErrorMessage);

                var finalLeader = ProcessRunnerScenarioSupport.ReadFinalIdentity(leaderPath);
                Assert.True(finalLeader != null, "Leader did not record a final atomic identity payload.");
                var finalChildIdentity =
                    ProcessRunnerScenarioSupport.ReadFinalIdentity(childIdentityPath);
                Assert.True(
                    finalChildIdentity != null,
                    "Child did not record an immediate atomic identity payload.");
                var finalChildReady =
                    ProcessRunnerScenarioSupport.ReadFinalIdentity(childReadyPath);
                Assert.True(
                    finalChildReady != null,
                    "Child readiness payload was incomplete after Run() completed.");
                Assert.Equal(finalChildIdentity.Value.Pid, finalChildReady.Value.Pid);
                Assert.True(
                    finalChildReady.Value.RecordedUnixMs
                        >= finalChildIdentity.Value.RecordedUnixMs,
                    "Child readiness timestamp preceded its spawn identity.");

                bool died = ProcessRunnerScenarioSupport.DescendantDiedWithinAcceptedWindow(
                    finalChildReady.Value,
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        finalChildReady.Value.RecordedUnixMs),
                    scenarioStartUtc,
                    out TimeSpan observedAfter);
                Assert.True(died, $"Descendant outlived containment or died too late ({observedAfter}).");

                scenarioPassed = true;
                outcome = "pass";
            }
            finally
            {
                try
                {
                    if (!scenarioPassed && cts != null)
                    {
                        bool runQuiesced = ProcessRunnerScenarioSupport.BestEffortCleanup(
                            cts,
                            runTask,
                            ProcessRunnerScenarioSupport.ReadFinalIdentity(leaderPath),
                            childIdentityPath,
                            childIdentity,
                            scenarioStartUtc,
                            msg => _output.WriteLine(msg));
                        if (!runQuiesced && root != null && runTask != null)
                        {
                            ProcessRunnerScenarioSupport.ScenarioRoot retainedRoot = root;
                            CancellationTokenSource retainedCts = cts;
                            root = null;
                            cts = null;
                            ProcessRunnerScenarioSupport.RetainLiveResources(
                                $"{nameof(Run_ParentExitStillTerminatesDescendantHoldingPipes)}[{iteration}]",
                                retainedRoot,
                                retainedCts,
                                runTask,
                                msg => _output.WriteLine(msg));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup after failure itself failed: {ex}");
                }
                finally
                {
                    cts?.Dispose();
                    root?.Dispose();
                }

                if (cleanupMs == null
                    && childIdentity != null
                    && runTask?.Status == TaskStatus.RanToCompletion)
                {
                    cleanupMs = ProcessRunnerScenarioSupport.ComputeCleanupMs(
                        childIdentity.Value,
                        runTask.Result.CompletionUtc);
                }

                ProcessRunnerScenarioSupport.EmitMetric(
                    _output,
                    nameof(Run_ParentExitStillTerminatesDescendantHoldingPipes),
                    iteration,
                    readinessMs,
                    cleanupMs,
                    totalStopwatch.ElapsedMilliseconds,
                    outcome);
            }
        }
    }
}
