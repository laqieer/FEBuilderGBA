// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ProcessRunnerScenarioSupportTests
    {
        private const string PrivateText =
            @"C:\private\fixture --secret=do-not-report ENV_PRIVATE=do-not-report";
        private const string IdentityPath = @"C:\inert\child.ready";

        [Fact]
        public void ObserveIdentity_MissingUsesOneExistsAndNoRead()
        {
            var reader = new InertReader(false, PrivateText);

            object observation = Observe(reader);

            AssertObservation(observation, "Missing", null);
            Assert.Equal(new[] { "exists" }, reader.Calls);
            Assert.Null(Property(observation, "Identity"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("123")]
        [InlineData("123|")]
        [InlineData("0|1000")]
        [InlineData("-1|1000")]
        [InlineData("123|not-a-timestamp")]
        [InlineData("123|1000|extra")]
        [InlineData("2147483648|1000")]
        [InlineData("123|9223372036854775808")]
        [InlineData(PrivateText)]
        public void ObserveIdentity_MalformedPreservesParserAndSingleRead(string payload)
        {
            var reader = new InertReader(true, payload);

            object observation = Observe(reader);

            AssertObservation(observation, "Malformed", null);
            Assert.Null(ProcessRunnerScenarioSupport.TryParseIdentity(payload));
            Assert.Null(Property(observation, "Identity"));
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
        }

        [Theory]
        [InlineData("123|1000")]
        [InlineData(" 123 | -1 ")]
        [InlineData("123|-9223372036854775808")]
        [InlineData("123|9223372036854775807")]
        public void ObserveIdentity_ValidKeepsExistingPidAndTimestamp(string payload)
        {
            var reader = new InertReader(true, payload);
            ProcessRunnerScenarioSupport.ProcessIdentity? expected =
                ProcessRunnerScenarioSupport.TryParseIdentity(payload);
            Assert.True(expected.HasValue);

            object observation = Observe(reader);

            AssertObservation(observation, "Valid", null);
            ProcessRunnerScenarioSupport.ProcessIdentity actual =
                Value<ProcessRunnerScenarioSupport.ProcessIdentity>(observation, "Identity");
            Assert.Equal(expected.Value.Pid, actual.Pid);
            Assert.Equal(expected.Value.RecordedUnixMs, actual.RecordedUnixMs);
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
        }

        [Theory]
        [InlineData(false, "read-fault")]
        [InlineData(true, "io-not-found")]
        public void ObserveIdentity_IoFaultAfterExistsIsNotMissing(
            bool fileNotFound, string category)
        {
            IOException fault = fileNotFound
                ? new FileNotFoundException(PrivateText, PrivateText)
                : new IOException(PrivateText);
            var reader = new InertReader(true, "", fault);

            object observation = Observe(reader);

            AssertObservation(observation, "ReadFault", category);
            Assert.Null(Property(observation, "Identity"));
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
        }

        [Fact]
        public void ObserveIdentity_UnauthorizedAccessPropagatesOriginalException()
        {
            var fault = new UnauthorizedAccessException(PrivateText);
            var reader = new InertReader(true, "", fault);

            UnauthorizedAccessException actual =
                Assert.Throws<UnauthorizedAccessException>(() => Observe(reader));

            Assert.Same(fault, actual);
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
        }

        [Fact]
        public void Poll_CompletedTaskPerformsExactlyExistingFinalReread()
        {
            var reader = new InertReader(true, "malformed");
            var clock = new InertClock();

            object poll = Poll(reader, clock, CompletedTask(), TimeSpan.Zero);

            Assert.Equal("TaskCompleted", Token(poll, "StopReason"));
            AssertObservation(Required(poll, "Observation"), "Malformed", null);
            Assert.Equal(new[] { "exists", "read", "exists", "read" }, reader.Calls);
            Assert.Empty(clock.Delays);
        }

        [Fact]
        public void Poll_CompletedTaskFinalRereadCanObserveValidIdentity()
        {
            var reader = new InertReader(true, "malformed")
            {
                ReadOverride = count => count == 1 ? "malformed" : "123|1000"
            };
            var clock = new InertClock();

            object poll = Poll(reader, clock, CompletedTask(), TimeSpan.Zero);

            Assert.Equal("TaskCompleted", Token(poll, "StopReason"));
            AssertObservation(Required(poll, "Observation"), "Valid", null);
            Assert.IsType<ProcessRunnerScenarioSupport.ProcessIdentity>(
                Property(poll, "Identity"));
            Assert.Equal(new[] { "exists", "read", "exists", "read" }, reader.Calls);
            Assert.Empty(clock.Delays);
        }

        [Fact]
        public void Poll_ValidIdentityWinsBeforeCompletedTaskOrExpiredDeadline()
        {
            var reader = new InertReader(true, "123|1000");
            var clock = new InertClock();

            object poll = Poll(reader, clock, CompletedTask(), TimeSpan.Zero);

            Assert.Equal("Ready", Token(poll, "StopReason"));
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
            Assert.Empty(clock.Delays);
        }

        [Fact]
        public void Poll_LiveTaskDeadlineUsesOneObservationPerTickAndExistingCadence()
        {
            var source = NewSource();
            var reader = new InertReader(true, "malformed");
            var clock = new InertClock();

            object poll = Poll(reader, clock, source.Task, TimeSpan.FromMilliseconds(50));

            Assert.Equal("Deadline", Token(poll, "StopReason"));
            Assert.Equal(
                new[] { "exists", "read", "exists", "read", "exists", "read" },
                reader.Calls);
            Assert.Equal(
                new[]
                {
                    ProcessRunnerScenarioSupport.ReadinessPollMs,
                    ProcessRunnerScenarioSupport.ReadinessPollMs
                },
                clock.Delays);
            Assert.False(source.Task.IsCompleted);
        }

        [Fact]
        public void Poll_EarlierReadFaultSurvivesMissingFinalObservation()
        {
            var source = NewSource();
            var reader = new InertReader(true, "", new IOException(PrivateText))
            {
                ExistsOverride = count => count == 1
            };
            var clock = new InertClock();
            clock.OnDelay = () => source.SetResult(Completion(default));

            object poll = Poll(reader, clock, source.Task, TimeSpan.FromMilliseconds(50));

            Assert.Equal("TaskCompleted", Token(poll, "StopReason"));
            AssertObservation(Required(poll, "Observation"), "Missing", null);
            AssertObservation(Required(poll, "EarlierReadFault"), "ReadFault", "read-fault");
            Assert.Equal(new[] { "exists", "read", "exists", "exists" }, reader.Calls);
            Assert.Single(clock.Delays);
            object snapshot = Freeze(poll, "readiness", false, source.Task);
            string text = Format(snapshot);
            Assert.Contains("Missing", text);
            Assert.Contains("read-fault", text);
            AssertPrivateTextAbsent(text);
        }

        [Fact]
        public void Poll_UnauthorizedAccessStillPropagatesWithoutRetry()
        {
            var fault = new UnauthorizedAccessException(PrivateText);
            var reader = new InertReader(true, "", fault);
            var clock = new InertClock();

            UnauthorizedAccessException actual = Assert.Throws<UnauthorizedAccessException>(
                () => Poll(reader, clock, CompletedTask(), TimeSpan.Zero));

            Assert.Same(fault, actual);
            Assert.Equal(new[] { "exists", "read" }, reader.Calls);
            Assert.Empty(clock.Delays);
        }

        [Theory]
        [InlineData("readiness")]
        [InlineData("run-incomplete")]
        [InlineData("result-rejected")]
        [InlineData("assertion")]
        [InlineData("unexpected-fault")]
        public void Freeze_CompletedResultRetainsActualFlagsAndFailurePhase(string phase)
        {
            ProcessRunResult result = new ProcessRunResult
            {
                Started = true,
                TimedOut = true,
                OutputLimitExceeded = true,
                TerminationFailed = true,
                Cancelled = true,
                ExitCode = 98,
                Stdout = PrivateText,
                Stderr = PrivateText,
                ErrorMessage = PrivateText
            };
            Task<ProcessRunnerScenarioSupport.RunCompletion> task = CompletedTask(result);
            object poll = MissingPoll(task);

            object snapshot = Freeze(poll, phase, false, task);

            Assert.Equal(phase, Token(snapshot, "Phase"));
            Assert.Equal("RanToCompletion", Token(snapshot, "TaskStatus"));
            Assert.True(Value<bool>(snapshot, "ResultAvailable"));
            ProcessRunResult captured = Value<ProcessRunResult>(snapshot, "Result");
            Assert.True(captured.Started);
            Assert.True(captured.TimedOut);
            Assert.True(captured.OutputLimitExceeded);
            Assert.True(captured.TerminationFailed);
            Assert.True(captured.Cancelled);
            Assert.Equal(98, captured.ExitCode);
            string text = Format(snapshot);
            Assert.Contains("phase=" + phase, text);
            Assert.Contains("stopReason=TaskCompleted", text);
            Assert.Contains("readinessMs=25", text);
            Assert.Contains("exitCode=98", text);
            AssertPrivateTextAbsent(text);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(31)]
        public void Format_CompletedFlagsAreIndependentNotDefaultedOrInterchanged(int mask)
        {
            ProcessRunResult result = new ProcessRunResult
            {
                Started = (mask & 1) != 0,
                TimedOut = (mask & 2) != 0,
                OutputLimitExceeded = (mask & 4) != 0,
                TerminationFailed = (mask & 8) != 0,
                Cancelled = (mask & 16) != 0,
                ExitCode = -1
            };
            var task = CompletedTask(result);

            string text = Format(Freeze(MissingPoll(task), "readiness", false, task));

            Assert.Contains("started=" + BoolToken(result.Started), text);
            Assert.Contains("timedOut=" + BoolToken(result.TimedOut), text);
            Assert.Contains("outputLimitExceeded=" + BoolToken(result.OutputLimitExceeded), text);
            Assert.Contains("terminationFailed=" + BoolToken(result.TerminationFailed), text);
            Assert.Contains("cancelled=" + BoolToken(result.Cancelled), text);
        }

        [Theory]
        [InlineData("Missing")]
        [InlineData("Malformed")]
        [InlineData("ReadFault")]
        public void Freeze_CompletedReadinessMissKeepsTypedObservationAndActualResult(
            string status)
        {
            var task = CompletedTask(new ProcessRunResult
            {
                Started = true,
                TimedOut = true,
                ExitCode = -1
            });
            var reader = new InertReader(
                status != "Missing", "malformed",
                status == "ReadFault" ? new IOException(PrivateText) : null);
            object poll = Poll(reader, new InertClock(), task, TimeSpan.Zero);

            object snapshot = Freeze(poll, "readiness", false, task);

            AssertObservation(
                Required(snapshot, "Observation"), status,
                status == "ReadFault" ? "read-fault" : null);
            Assert.Equal("TaskCompleted", Token(snapshot, "StopReason"));
            Assert.True(Value<bool>(snapshot, "ResultAvailable"));
            Assert.True(Value<ProcessRunResult>(snapshot, "Result").TimedOut);
            AssertPrivateTextAbsent(Format(snapshot));
        }

        [Theory]
        [InlineData("live", "WaitingForActivation")]
        [InlineData("faulted", "Faulted")]
        [InlineData("cancelled", "Canceled")]
        [InlineData("absent", "unavailable")]
        public void Freeze_UnavailableResultNeverFormatsDefaultSuccess(
            string kind, string expectedStatus)
        {
            Task<ProcessRunnerScenarioSupport.RunCompletion>? task = kind switch
            {
                "live" => NewSource().Task,
                "faulted" => Task.FromException<ProcessRunnerScenarioSupport.RunCompletion>(
                    new InvalidOperationException(PrivateText)),
                "cancelled" => Task.FromCanceled<ProcessRunnerScenarioSupport.RunCompletion>(
                    new CancellationToken(true)),
                _ => null
            };

            object snapshot = Freeze(MissingPoll(task), "readiness", false, task);

            Assert.Equal(expectedStatus, Token(snapshot, "TaskStatus"));
            Assert.False(Value<bool>(snapshot, "ResultAvailable"));
            Assert.Null(Property(snapshot, "Result"));
            string text = Format(snapshot);
            Assert.Contains("result=unavailable", text);
            Assert.DoesNotContain("started=", text);
            Assert.DoesNotContain("exitCode=", text);
            AssertPrivateTextAbsent(text);
            if (kind == "live")
                Assert.False(task!.IsCompleted);
            if (kind == "faulted")
                Assert.NotNull(task!.Exception);
        }

        [Fact]
        public void Freeze_FailedReadinessCannotBeFilledEvenBeforeCleanup()
        {
            var source = NewSource();
            object poll = MissingPoll(source.Task);
            object snapshot = Freeze(poll, "readiness", false, source.Task);
            string original = Format(snapshot);
            source.SetResult(Completion(new ProcessRunResult { Started = true, ExitCode = 0 }));

            object filled = Fill(snapshot, source.Task, cleanupStarted: false);

            Assert.Same(snapshot, filled);
            Assert.Equal(original, Format(filled));
            Assert.False(Value<bool>(filled, "ResultAvailable"));
        }

        [Fact]
        public void Freeze_FailedReadinessIsImmutableWhenCancellationCompletesTask()
        {
            var source = NewSource();
            using var cts = new CancellationTokenSource();
            using CancellationTokenRegistration registration = cts.Token.Register(
                () => source.SetResult(Completion(
                    new ProcessRunResult { Started = true, Cancelled = true, ExitCode = -1 })));
            object snapshot = Freeze(MissingPoll(source.Task), "readiness", false, source.Task);
            string beforeCancellation = Format(snapshot);
            Assert.False(source.Task.IsCompleted);

            cts.Cancel();
            object afterCancellation = Fill(snapshot, source.Task, cleanupStarted: true);

            Assert.True(source.Task.IsCompletedSuccessfully);
            Assert.Same(snapshot, afterCancellation);
            Assert.Equal(beforeCancellation, Format(afterCancellation));
            Assert.Equal("WaitingForActivation", Token(snapshot, "TaskStatus"));
            Assert.False(Value<bool>(snapshot, "ResultAvailable"));
        }

        [Fact]
        public void Fill_SuccessfulReadinessAllowsExactlyOneCompletedPreCleanupResult()
        {
            var source = NewSource();
            object poll = ReadyPoll(source.Task);
            object snapshot = Freeze(poll, "readiness", true, source.Task);
            object observation = Required(snapshot, "Observation");
            source.SetResult(Completion(new ProcessRunResult { Started = true, ExitCode = 0 }));

            object filled = Fill(snapshot, source.Task, cleanupStarted: false);
            object secondFill = Fill(
                filled,
                CompletedTask(new ProcessRunResult { Started = true, TimedOut = true, ExitCode = 99 }),
                cleanupStarted: false);

            Assert.False(Value<bool>(snapshot, "ResultAvailable"));
            Assert.True(Value<bool>(filled, "ResultAvailable"));
            Assert.Equal(0, Value<ProcessRunResult>(filled, "Result").ExitCode);
            Assert.Equal(observation, Required(filled, "Observation"));
            Assert.Equal(Token(snapshot, "Phase"), Token(filled, "Phase"));
            Assert.Equal(Token(snapshot, "StopReason"), Token(filled, "StopReason"));
            Assert.Same(filled, secondFill);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Fill_SuccessfulReadinessRejectsUnavailableOrPostCleanupResult(
            bool cleanupStarted)
        {
            var source = NewSource();
            object snapshot = Freeze(ReadyPoll(source.Task), "readiness", true, source.Task);
            if (cleanupStarted)
                source.SetResult(Completion(new ProcessRunResult { Started = true, ExitCode = 0 }));

            object filled = Fill(snapshot, source.Task, cleanupStarted);

            Assert.Same(snapshot, filled);
            Assert.False(Value<bool>(filled, "ResultAvailable"));
        }

        [Fact]
        public void Fill_SuccessfulReadinessAlreadyCapturedResultCannotBeReplaced()
        {
            var task = CompletedTask(new ProcessRunResult { Started = true, ExitCode = 0 });
            object snapshot = Freeze(ReadyPoll(task), "readiness", true, task);

            object filled = Fill(
                snapshot,
                CompletedTask(new ProcessRunResult { Started = true, TimedOut = true, ExitCode = 99 }),
                cleanupStarted: false);

            Assert.Same(snapshot, filled);
            Assert.Equal(0, Value<ProcessRunResult>(filled, "Result").ExitCode);
            Assert.False(Value<ProcessRunResult>(filled, "Result").TimedOut);
        }

        [Theory]
        [InlineData(97, "leader-publish")]
        [InlineData(98, "child-identity-publish")]
        [InlineData(99, "child-exited-before-ready")]
        [InlineData(100, "readiness-publish")]
        public void Format_OnlyStartedObservedFixtureExitGetsFixedCategory(
            int exitCode, string category)
        {
            var startedTask = CompletedTask(new ProcessRunResult { Started = true, ExitCode = exitCode });
            var notStartedTask = CompletedTask(
                new ProcessRunResult { Started = false, ExitCode = exitCode });

            string started = Format(Freeze(MissingPoll(startedTask), "readiness", false, startedTask));
            string notStarted = Format(
                Freeze(MissingPoll(notStartedTask), "readiness", false, notStartedTask));

            Assert.Contains("fixtureExit=" + category, started);
            Assert.DoesNotContain("fixtureExit=" + category, notStarted);
        }

        [Fact]
        public void Format_UnknownExitDoesNotInventFixturePhase()
        {
            var task = CompletedTask(new ProcessRunResult { Started = true, ExitCode = 123456 });

            string text = Format(Freeze(MissingPoll(task), "readiness", false, task));

            Assert.Contains("exitCode=123456", text);
            Assert.DoesNotContain("fixtureExit=leader-publish", text);
            Assert.DoesNotContain("fixtureExit=readiness-publish", text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("x")]
        [InlineData(PrivateText)]
        [InlineData("Process output capture did not finish. extra")]
        public void Format_StreamsAndUnknownErrorTextAreAlwaysWithheld(string privateText)
        {
            var task = CompletedTask(new ProcessRunResult
            {
                Started = true,
                ExitCode = -1,
                Stdout = privateText,
                Stderr = privateText,
                ErrorMessage = privateText
            });

            string text = Format(Freeze(MissingPoll(task), "readiness", false, task));

            Assert.Contains("stdoutLength=" + privateText.Length, text);
            Assert.Contains("stderrLength=" + privateText.Length, text);
            Assert.Contains("stdout=<withheld>", text);
            Assert.Contains("stderr=<withheld>", text);
            Assert.Contains(
                privateText.Length == 0 ? "error=empty" : "error=<withheld>", text);
            if (privateText.Length > 0)
            {
                Assert.DoesNotContain("stdout=" + privateText, text);
                Assert.DoesNotContain("stderr=" + privateText, text);
                Assert.DoesNotContain("error=" + privateText, text);
            }
            AssertPrivateTextAbsent(text);
        }

        [Fact]
        public void Format_CaptureIncompleteIsAllowedOnlyAsExactErrorTokenNotStreamText()
        {
            const string allowed = ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage;
            var task = CompletedTask(new ProcessRunResult
            {
                Started = true,
                ExitCode = -1,
                Stdout = allowed,
                Stderr = allowed,
                ErrorMessage = allowed
            });

            string text = Format(Freeze(MissingPoll(task), "readiness", false, task));

            Assert.Contains("error=" + allowed, text);
            Assert.Contains("stdout=<withheld>", text);
            Assert.Contains("stderr=<withheld>", text);
            Assert.DoesNotContain("stdout=" + allowed, text);
            Assert.DoesNotContain("stderr=" + allowed, text);
        }

        [Fact]
        public void Format_PathIdentityArbitraryPhaseAndLongStreamsCannotEscapeProjection()
        {
            string stream = PrivateText + new string('x', 4096);
            var task = CompletedTask(new ProcessRunResult
            {
                Started = true,
                ExitCode = 0,
                Stdout = stream,
                Stderr = stream,
                ErrorMessage = stream
            });
            object poll = Poll(
                new InertReader(true, "7654321|1234567890123"),
                new InertClock(), task, TimeSpan.Zero);

            string text = Format(Freeze(poll, PrivateText, true, task));

            Assert.Contains("phase=unavailable", text);
            Assert.Contains("stdoutLength=" + stream.Length, text);
            Assert.DoesNotContain(IdentityPath, text);
            Assert.DoesNotContain("7654321", text);
            Assert.DoesNotContain("1234567890123", text);
            Assert.DoesNotContain(new string('x', 256), text);
            AssertPrivateTextAbsent(text);
            Assert.InRange(text.Length, 1, 2048);
        }

        [Theory]
        [InlineData(249, 255, false)]
        [InlineData(250, 256, false)]
        [InlineData(251, 256, true)]
        [InlineData(4096, 256, true)]
        public void FormatDiagnosticField_CapIncludesCompleteLabelAndTruncationMarker(
            int valueLength, int expectedLength, bool truncated)
        {
            string field = Invoke<string>(
                "FormatDiagnosticField", "phase", new string('1', valueLength));

            Assert.StartsWith("phase=", field);
            Assert.Equal(expectedLength, field.Length);
            Assert.Equal(truncated, field.Contains("<truncated>", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(7, 1798, false)]
        [InlineData(8, 2048, true)]
        [InlineData(20, 2048, true)]
        public void FormatDiagnosticRecord_CapIncludesFieldsSeparatorsAndTruncation(
            int count, int expectedLength, bool truncated)
        {
            var fields = new List<string>();
            for (int i = 0; i < count; i++)
                fields.Add(Invoke<string>("FormatDiagnosticField", "phase", new string('1', 250)));

            string record = Invoke<string>(
                "FormatDiagnosticRecord", (IReadOnlyList<string>)fields);

            Assert.Equal(expectedLength, record.Length);
            Assert.Equal(truncated, record.Contains("<truncated>", StringComparison.Ordinal));
        }

        [Fact]
        public void FormatDiagnosticRecord_Exact2048BoundaryDoesNotTruncate()
        {
            var fields = new List<string>();
            for (int i = 0; i < 7; i++)
                fields.Add(Invoke<string>("FormatDiagnosticField", "phase", new string('1', 250)));
            fields.Add(Invoke<string>("FormatDiagnosticField", "phase", new string('1', 243)));

            string record = Invoke<string>(
                "FormatDiagnosticRecord", (IReadOnlyList<string>)fields);

            Assert.Equal(2048, record.Length);
            Assert.DoesNotContain("<truncated>", record);
        }

        [Theory]
        [InlineData("io", "read-fault")]
        [InlineData("not-found", "io-not-found")]
        [InlineData("access", "access-denied")]
        [InlineData("other", "unexpected-fault")]
        public void ClassifyDiagnosticFault_UsesClosedTokensNotTypeMessageOrHResult(
            string kind, string category)
        {
            Exception fault = kind switch
            {
                "io" => new IOException(PrivateText),
                "not-found" => new FileNotFoundException(PrivateText, PrivateText),
                "access" => new UnauthorizedAccessException(PrivateText),
                _ => new InvalidOperationException(PrivateText)
            };

            string actual = Invoke<string>("ClassifyDiagnosticFault", fault);

            Assert.Equal(category, actual);
            AssertPrivateTextAbsent(actual);
            Assert.DoesNotContain(fault.GetType().Name, actual);
        }

        [Theory]
        [InlineData("cancel")]
        [InlineData("wait")]
        [InlineData("owned-kill")]
        [InlineData("owned-wait")]
        [InlineData("retain")]
        [InlineData("release")]
        public void FailureEvidence_PreservesPrimaryAndExplicitSecondaryCleanup(
            string stage)
        {
            var primary = new InvalidOperationException(PrivateText);
            var secondary = new IOException(PrivateText);
            object snapshot = Freeze(MissingPoll(null), "assertion", false, null);

            object evidence = Invoke<object>(
                "CreateFailureEvidence", primary, snapshot, stage, "fault", secondary, true);

            Assert.Same(primary, Required(evidence, "PrimaryFailure"));
            Assert.Same(secondary, Required(evidence, "SecondaryFailure"));
            Assert.Same(snapshot, Required(evidence, "Readiness"));
            string text = Value<string>(evidence, "Text");
            Assert.Contains("cleanupStage=" + stage, text);
            Assert.Contains("cleanupOutcome=fault", text);
            Assert.Contains("cleanupFault=read-fault", text);
            Assert.Contains("resources=retained", text);
            AssertPrivateTextAbsent(text);
            Assert.DoesNotContain(nameof(IOException), text);
            Assert.DoesNotContain(nameof(InvalidOperationException), text);
            Assert.InRange(text.Length, 1, 2048);
        }

        [Theory]
        [InlineData("success")]
        [InlineData("fault")]
        [InlineData("retained")]
        [InlineData("unavailable")]
        public void FailureEvidence_ReportsFixedCleanupOutcomeWithoutInventingFault(
            string outcome)
        {
            var primary = new InvalidOperationException("primary");
            object snapshot = Freeze(MissingPoll(null), "readiness", false, null);

            object evidence = Invoke<object>(
                "CreateFailureEvidence", primary, snapshot, "wait", outcome, null, false);

            Assert.Same(primary, Required(evidence, "PrimaryFailure"));
            Assert.Null(Property(evidence, "SecondaryFailure"));
            string text = Value<string>(evidence, "Text");
            Assert.Contains("cleanupOutcome=" + outcome, text);
            Assert.Contains("cleanupFault=unavailable", text);
            Assert.DoesNotContain("resources=retained", text);
            Assert.DoesNotContain("primary", text);
        }

        [Fact]
        public void FailureEvidence_UntrustedCleanupLabelsAreWithheld()
        {
            var primary = new InvalidOperationException(PrivateText);
            object snapshot = Freeze(MissingPoll(null), "unexpected-fault", false, null);

            object evidence = Invoke<object>(
                "CreateFailureEvidence", primary, snapshot, PrivateText, PrivateText, null, true);

            string text = Value<string>(evidence, "Text");
            Assert.Contains("cleanupStage=unavailable", text);
            Assert.Contains("cleanupOutcome=unavailable", text);
            Assert.Contains("resources=retained", text);
            AssertPrivateTextAbsent(text);
        }

        [Theory]
        [InlineData("Malformed")]
        [InlineData("Valid")]
        public void Poll_EarlierReadFaultSurvivesFinalPayload(string finalStatus)
        {
            var task = CompletedTask();
            var reader = new InertReader(true, "")
            {
                ReadOverride = count => count == 1
                    ? throw new FileNotFoundException(PrivateText, PrivateText)
                    : finalStatus == "Valid" ? "123|1000" : "malformed"
            };

            object poll = Poll(reader, new InertClock(), task, TimeSpan.Zero);

            AssertObservation(Required(poll, "Observation"), finalStatus, null);
            AssertObservation(Required(poll, "EarlierReadFault"), "ReadFault", "io-not-found");
            Assert.Equal(new[] { "exists", "read", "exists", "read" }, reader.Calls);
            AssertPrivateTextAbsent(Format(Freeze(
                poll, "readiness", finalStatus == "Valid", task)));
        }

        [Fact]
        public void Poll_DifferentFinalReadFaultCannotEraseEarlierFaultCategory()
        {
            var reader = new InertReader(true, "")
            {
                ReadOverride = count => count == 1
                    ? throw new FileNotFoundException(PrivateText, PrivateText)
                    : throw new IOException(PrivateText)
            };

            object poll = Poll(reader, new InertClock(), CompletedTask(), TimeSpan.Zero);

            AssertObservation(Required(poll, "Observation"), "ReadFault", "read-fault");
            AssertObservation(Required(poll, "EarlierReadFault"), "ReadFault", "io-not-found");
            Assert.Equal(new[] { "exists", "read", "exists", "read" }, reader.Calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Fill_SuccessfulReadinessDoesNotReadFaultedOrCancelledTask(bool cancelled)
        {
            var source = NewSource();
            object snapshot = Freeze(ReadyPoll(source.Task), "readiness", true, source.Task);
            if (cancelled)
                source.SetCanceled();
            else
                source.SetException(new InvalidOperationException(PrivateText));

            object filled = Fill(snapshot, source.Task, cleanupStarted: false);

            Assert.Same(snapshot, filled);
            Assert.False(Value<bool>(filled, "ResultAvailable"));
            Assert.Equal("WaitingForActivation", Token(filled, "TaskStatus"));
            AssertPrivateTextAbsent(Format(filled));
            if (!cancelled)
                Assert.NotNull(source.Task.Exception);
        }

        [Fact]
        public void ScenarioFailureBoundary_PreservesOriginalDispatchWithoutRenderedInnerException()
        {
            var original = new UnauthorizedAccessException(PrivateText);
            try
            {
                throw original;
            }
            catch (UnauthorizedAccessException ex)
            {
                var snapshot = ProcessRunnerScenarioSupport.FreezeReadinessEvidence(
                    null, "unexpected-fault", false, null, 0);
                ProcessRunnerScenarioSupport.FailureEvidence evidence =
                    ProcessRunnerScenarioSupport.CreateFailureEvidence(
                        ex, snapshot, "wait", "unavailable", null, false);
                var boundary = new ProcessRunnerScenarioSupport.ScenarioFailureException(
                    evidence.Text, ex);

                Assert.Same(original, boundary.OriginalFailure.SourceException);
                Assert.NotNull(boundary.OriginalFailure.SourceException.StackTrace);
                Assert.Null(boundary.InnerException);
                Assert.Contains("failureFault=access-denied", boundary.Message);
                AssertPrivateTextAbsent(boundary.ToString());
            }
        }

        [Fact]
        public void LogEvidence_ClosedSinkCannotMaskPrimaryOrPreventFollowingCleanup()
        {
            var primary = new InvalidOperationException(PrivateText);
            var secondary = new IOException(PrivateText);
            var snapshot = ProcessRunnerScenarioSupport.FreezeReadinessEvidence(
                null, "assertion", false, null, 0);
            ProcessRunnerScenarioSupport.FailureEvidence evidence =
                ProcessRunnerScenarioSupport.CreateFailureEvidence(
                    primary, snapshot, "wait", "fault", secondary, true);
            bool cleanupReached = false;

            ProcessRunnerScenarioSupport.LogEvidence(
                _ => throw new InvalidOperationException("closed inert sink"), evidence.Text);
            cleanupReached = true;

            Assert.True(cleanupReached);
            Assert.Same(primary, evidence.PrimaryFailure);
            Assert.Same(secondary, evidence.SecondaryFailure);
            Assert.Contains("cleanupOutcome=fault", evidence.Text);
            Assert.Contains("resources=retained", evidence.Text);
            AssertPrivateTextAbsent(evidence.Text);
        }

        private static object Observe(InertReader reader) =>
            Invoke<object>(
                "ObserveIdentity", IdentityPath,
                (Func<string, bool>)reader.Exists,
                (Func<string, string>)reader.Read);

        private static object Poll(
            InertReader reader, InertClock clock, Task? task, TimeSpan maximumWait) =>
            Invoke<object>(
                "PollForIdentityWithEvidence", IdentityPath, maximumWait, task,
                (Func<string, bool>)reader.Exists,
                (Func<string, string>)reader.Read,
                (Func<TimeSpan>)(() => clock.Elapsed),
                (Action<int>)clock.Delay);

        private static object MissingPoll(Task? task) =>
            Poll(new InertReader(false, ""), new InertClock(), task, TimeSpan.Zero);

        private static object ReadyPoll(Task? task) =>
            Poll(new InertReader(true, "123|1000"), new InertClock(), task, TimeSpan.Zero);

        private static object Freeze(
            object poll, string phase, bool readinessSucceeded,
            Task<ProcessRunnerScenarioSupport.RunCompletion>? task) =>
            Invoke<object>(
                "FreezeReadinessEvidence", poll, phase, readinessSucceeded, task, 25L);

        private static object Fill(
            object snapshot, Task<ProcessRunnerScenarioSupport.RunCompletion> task,
            bool cleanupStarted) =>
            Invoke<object>("FillReadinessResultBeforeCleanup", snapshot, task, cleanupStarted);

        private static string Format(object snapshot) =>
            Invoke<string>("FormatReadinessEvidence", snapshot);

        private static TaskCompletionSource<ProcessRunnerScenarioSupport.RunCompletion> NewSource() =>
            new TaskCompletionSource<ProcessRunnerScenarioSupport.RunCompletion>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        private static Task<ProcessRunnerScenarioSupport.RunCompletion> CompletedTask(
            ProcessRunResult result = default) =>
            Task.FromResult(Completion(result));

        private static ProcessRunnerScenarioSupport.RunCompletion Completion(ProcessRunResult result) =>
            new ProcessRunnerScenarioSupport.RunCompletion(
                result, DateTimeOffset.FromUnixTimeMilliseconds(1250));

        private static void AssertObservation(object observation, string status, string? category)
        {
            object typedStatus = Required(observation, "Status");
            Assert.True(typedStatus.GetType().IsEnum, "Identity status must be typed, not free text.");
            Assert.Equal(status, typedStatus.ToString());
            Assert.Equal(category, Property(observation, "FaultCategory"));
        }

        private static void AssertPrivateTextAbsent(string text)
        {
            Assert.DoesNotContain(PrivateText, text);
            Assert.DoesNotContain(@"C:\private", text);
            Assert.DoesNotContain("do-not-report", text);
            Assert.DoesNotContain("--secret", text);
            Assert.DoesNotContain("ENV_PRIVATE", text);
        }

        // Reflection keeps this tests-only stage compilable; absent APIs fail with named RED assertions.
        private static T Invoke<T>(string methodName, params object?[] arguments)
        {
            MethodInfo[] methods = Array.FindAll(
                typeof(ProcessRunnerScenarioSupport).GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                method => method.Name == methodName
                    && method.GetParameters().Length == arguments.Length);
            Assert.True(methods.Length == 1,
                $"Expected one ProcessRunnerScenarioSupport.{methodName} API with "
                + $"{arguments.Length} arguments; found {methods.Length}.");
            try
            {
                return Assert.IsAssignableFrom<T>(methods[0].Invoke(null, arguments));
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static object? Property(object instance, string name)
        {
            PropertyInfo? property = instance.GetType().GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);
            return property.GetValue(instance);
        }

        private static object Required(object instance, string name)
        {
            object? value = Property(instance, name);
            Assert.NotNull(value);
            return value;
        }

        private static T Value<T>(object instance, string name) =>
            Assert.IsType<T>(Required(instance, name));

        private static string Token(object instance, string name) =>
            Required(instance, name).ToString()!;

        private static string BoolToken(bool value) => value ? "true" : "false";

        private sealed class InertReader
        {
            private readonly bool _exists;
            private readonly string _payload;
            private readonly Exception? _readFault;
            private int _existsCount;
            private int _readCount;

            internal List<string> Calls { get; } = new List<string>();
            internal Func<int, bool>? ExistsOverride { get; set; }
            internal Func<int, string>? ReadOverride { get; set; }

            internal InertReader(bool exists, string payload, Exception? readFault = null)
            {
                _exists = exists;
                _payload = payload;
                _readFault = readFault;
            }

            internal bool Exists(string path)
            {
                Assert.Equal(IdentityPath, path);
                Calls.Add("exists");
                return ExistsOverride?.Invoke(++_existsCount) ?? _exists;
            }

            internal string Read(string path)
            {
                Assert.Equal(IdentityPath, path);
                Calls.Add("read");
                ++_readCount;
                if (ReadOverride != null)
                    return ReadOverride(_readCount);
                if (_readFault != null)
                    ExceptionDispatchInfo.Capture(_readFault).Throw();
                return _payload;
            }
        }

        private sealed class InertClock
        {
            internal TimeSpan Elapsed { get; private set; }
            internal List<int> Delays { get; } = new List<int>();
            internal Action? OnDelay { get; set; }

            internal void Delay(int milliseconds)
            {
                Assert.True(Delays.Count < 4, "Poll exceeded the deterministic tick budget.");
                Assert.Equal(ProcessRunnerScenarioSupport.ReadinessPollMs, milliseconds);
                Delays.Add(milliseconds);
                Elapsed += TimeSpan.FromMilliseconds(milliseconds);
                OnDelay?.Invoke();
            }
        }
    }
}
