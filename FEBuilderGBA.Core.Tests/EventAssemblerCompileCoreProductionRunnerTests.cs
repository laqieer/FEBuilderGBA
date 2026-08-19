using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Real ProcessRunner-backed EventAssemblerCompileCore coverage. These facts launch the
    /// stub via the production runner, observe host-global process-tree cleanup, and assert
    /// wall-clock bounds, so they run in <see cref="ProcessRunnerTestCollection"/> instead of
    /// the mixed <see cref="EventAssemblerCompileCoreTests"/> SharedState collection.
    /// </summary>
    [Collection(ProcessRunnerTestCollection.Name)]
    public class EventAssemblerCompileCoreProductionRunnerTests
    {
        const string WritingBeforeInitializingOffsetWarning =
            "warning: C_Code.lyn.event:11:1: Writing before initializing offset. You may be breaking the ROM!";
        const string ColorzCoreSuccessMarker =
            "No errors. Please continue being awesome.";

        static ROM CreateTestRom(int size = 512)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        static string CreateProcessStubScenarioDirectory(string prefix)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        [Fact]
        public void CompileAndInsert_ProductionRunner_ParentExitDescendantHoldingPipes_ReturnsWithinBound()
        {
            var rom = CreateTestRom(0x400);
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            string fakeEaDir = "";
            string eaFile = "";
            string launchRecordPath = "";
            string markerPath = "";

            var savedResolveExe = EventAssemblerCompileCore.ResolveExeOverride;
            var savedRunProcess = EventAssemblerCompileCore.RunProcessOverride;
            var savedTimeoutMs = EventAssemblerCompileCore.RunProcessTimeoutMs;
            try
            {
                string fakeExe = EventAssemblerProcessStubSupport.FindExecutable();
                fakeEaDir = CreateProcessStubScenarioDirectory("febuilder-ea-parent-exit-");
                eaFile = Path.Combine(fakeEaDir, "parent-exit.event");
                launchRecordPath = Path.Combine(fakeEaDir, "launch-record.json");
                markerPath = Path.Combine(fakeEaDir, "child-survived.txt");
                File.WriteAllText(eaFile, "// production runner parent-exit pipe-hold test\r\n");

                EventAssemblerCompileCore.ResolveExeOverride = () => fakeExe;
                EventAssemblerCompileCore.RunProcessOverride = null!;
                EventAssemblerCompileCore.RunProcessTimeoutMs = 500;

                var stopwatch = Stopwatch.StartNew();
                EventAssemblerProcessStubSupport.LaunchRecord launch;
                EventAssemblerCompileCore.CompileResult result;
                using (EventAssemblerProcessStubSupport.Configure(
                    EventAssemblerProcessStubSupport.ParentExitDescendantHoldsPipesMode,
                    launchRecordPath,
                    markerPath))
                {
                    result = EventAssemblerCompileCore.CompileAndInsert(
                        rom, eaFile, EventAssemblerCompileCore.FreeAreaMode.None,
                        undo, SymbolUtil.DebugSymbol.None);
                    launch = EventAssemblerProcessStubSupport.ReadLaunchRecord(launchRecordPath);
                }
                stopwatch.Stop();

                Assert.Equal(EventAssemblerProcessStubSupport.ParentExitDescendantHoldsPipesMode, launch.Mode);
                Assert.True(launch.ProcessId > 0);
                Assert.True(launch.ChildProcessId > 0);
                EventAssemblerProcessStubSupport.AssertSamePath(fakeExe, launch.ProcessPath);
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(15),
                    $"Parent-exit inherited-pipe cleanup took too long: {stopwatch.Elapsed}.");

                if (OperatingSystem.IsWindows())
                {
                    Assert.True(result.Success, result.ErrorMessage);
                    Assert.Contains(ColorzCoreSuccessMarker, result.Output);
                    Assert.Equal(0xAA, rom.Data[0x100]);
                    Assert.NotEmpty(undo.list);
                    Assert.False(
                        SpinWait.SpinUntil(() => File.Exists(markerPath), TimeSpan.FromSeconds(3)),
                        "Windows job containment should kill the helper descendant before it self-terminates.");
                    Assert.False(
                        File.Exists(markerPath),
                        "The job-contained descendant wrote its self-termination marker.");
                }
                else
                {
                    bool descendantSelfTerminated = SpinWait.SpinUntil(
                        () => File.Exists(markerPath),
                        TimeSpan.FromSeconds(
                            EventAssemblerProcessStubSupport.ParentExitChildSelfTerminateSeconds));

                    Assert.False(result.Success);
                    Assert.Equal(before, rom.Data);
                    Assert.Empty(undo.list);
                    Assert.Contains(
                        ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage,
                        result.Output);
                    Assert.Contains(
                        ProcessRunnerScenarioSupport.CaptureIncompleteErrorMessage,
                        result.ErrorMessage);
                    Assert.True(
                        descendantSelfTerminated,
                        "The POSIX helper descendant did not self-terminate after the bounded capture failure.");
                    Assert.Equal(
                        "parent-exit helper descendant self-terminated",
                        File.ReadAllText(markerPath));
                }
            }
            finally
            {
                EventAssemblerCompileCore.ResolveExeOverride = savedResolveExe;
                EventAssemblerCompileCore.RunProcessOverride = savedRunProcess;
                EventAssemblerCompileCore.RunProcessTimeoutMs = savedTimeoutMs;
                try { if (fakeEaDir != "") Directory.Delete(fakeEaDir, true); } catch { }
            }
        }

        [Fact]
        public void CompileAndInsert_ProductionRunner_CapturesConcurrentStdoutAndStderrBeforeSwap()
        {
            var rom = CreateTestRom(0x400);
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            string fakeEaDir = "";
            string eaFile = "";
            string launchRecordPath = "";

            var savedResolveExe = EventAssemblerCompileCore.ResolveExeOverride;
            var savedRunProcess = EventAssemblerCompileCore.RunProcessOverride;
            var savedTimeoutMs = EventAssemblerCompileCore.RunProcessTimeoutMs;
            try
            {
                string fakeExe = EventAssemblerProcessStubSupport.FindExecutable();
                fakeEaDir = CreateProcessStubScenarioDirectory("febuilder-ea-concurrent-");
                eaFile = Path.Combine(fakeEaDir, "warning.event");
                launchRecordPath = Path.Combine(fakeEaDir, "launch-record.json");
                File.WriteAllText(eaFile, "// production runner concurrency test\r\n");

                EventAssemblerCompileCore.ResolveExeOverride = () => fakeExe;
                EventAssemblerCompileCore.RunProcessOverride = null!;
                EventAssemblerCompileCore.RunProcessTimeoutMs = EventAssemblerCompileCore.DefaultRunProcessTimeoutMs;

                EventAssemblerProcessStubSupport.LaunchRecord launch;
                EventAssemblerCompileCore.CompileResult result;
                using (EventAssemblerProcessStubSupport.Configure(
                    EventAssemblerProcessStubSupport.ConcurrentOutputMode,
                    launchRecordPath))
                {
                    result = EventAssemblerCompileCore.CompileAndInsert(
                        rom, eaFile, EventAssemblerCompileCore.FreeAreaMode.None,
                        undo, SymbolUtil.DebugSymbol.None);
                    launch = EventAssemblerProcessStubSupport.ReadLaunchRecord(launchRecordPath);
                }

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
                Assert.Equal(EventAssemblerProcessStubSupport.ConcurrentOutputMode, launch.Mode);
                Assert.True(launch.ProcessId > 0);
                EventAssemblerProcessStubSupport.AssertSamePath(fakeExe, launch.ProcessPath);
                EventAssemblerProcessStubSupport.AssertSamePath(Path.GetDirectoryName(fakeExe)!, launch.WorkingDirectory);
                Assert.Contains(launch.Arguments, arg => arg.StartsWith("--nocash-sym:", StringComparison.Ordinal));
                Assert.StartsWith("_FBG_Temp_", Path.GetFileName(launch.WrapperPath));
                Assert.EndsWith(".gba", launch.OutputRomPath, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("stderr filler 0", result.Output);
                Assert.Contains("stdout filler 0", result.Output);

                int warningIndex = result.Output.IndexOf(
                    WritingBeforeInitializingOffsetWarning,
                    StringComparison.Ordinal);
                int successIndex = result.Output.IndexOf(
                    ColorzCoreSuccessMarker,
                    StringComparison.Ordinal);
                Assert.True(warningIndex >= 0, result.Output);
                Assert.True(successIndex >= 0, result.Output);
                Assert.True(warningIndex < successIndex, result.Output);
                Assert.Contains("Writing before initializing offset", result.ErrorMessage);
            }
            finally
            {
                EventAssemblerCompileCore.ResolveExeOverride = savedResolveExe;
                EventAssemblerCompileCore.RunProcessOverride = savedRunProcess;
                EventAssemblerCompileCore.RunProcessTimeoutMs = savedTimeoutMs;
                try { if (fakeEaDir != "") Directory.Delete(fakeEaDir, true); } catch { }
            }
        }

        [Fact]
        public void CompileAndInsert_ProductionRunner_TimeoutKillsProcessTreeBeforeCleanupMarker()
        {
            var rom = CreateTestRom(0x400);
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            string fakeEaDir = "";
            string eaFile = "";
            string launchRecordPath = "";
            string markerPath = "";

            var savedResolveExe = EventAssemblerCompileCore.ResolveExeOverride;
            var savedRunProcess = EventAssemblerCompileCore.RunProcessOverride;
            var savedTimeoutMs = EventAssemblerCompileCore.RunProcessTimeoutMs;
            try
            {
                string fakeExe = EventAssemblerProcessStubSupport.FindExecutable();
                fakeEaDir = CreateProcessStubScenarioDirectory("febuilder-ea-timeout-");
                eaFile = Path.Combine(fakeEaDir, "timeout.event");
                launchRecordPath = Path.Combine(fakeEaDir, "launch-record.json");
                markerPath = Path.Combine(fakeEaDir, "child-survived.txt");
                File.WriteAllText(eaFile, "// production runner timeout cleanup test\r\n");

                EventAssemblerCompileCore.ResolveExeOverride = () => fakeExe;
                EventAssemblerCompileCore.RunProcessOverride = null!;
                EventAssemblerCompileCore.RunProcessTimeoutMs = 500;

                var stopwatch = Stopwatch.StartNew();
                EventAssemblerProcessStubSupport.LaunchRecord launch;
                EventAssemblerCompileCore.CompileResult result;
                using (EventAssemblerProcessStubSupport.Configure(
                    EventAssemblerProcessStubSupport.TimeoutParentMode,
                    launchRecordPath,
                    markerPath))
                {
                    result = EventAssemblerCompileCore.CompileAndInsert(
                        rom, eaFile, EventAssemblerCompileCore.FreeAreaMode.None,
                        undo, SymbolUtil.DebugSymbol.None);
                    launch = EventAssemblerProcessStubSupport.ReadLaunchRecord(launchRecordPath);
                }
                stopwatch.Stop();

                bool childSurvived = SpinWait.SpinUntil(
                    () => File.Exists(markerPath),
                    TimeSpan.FromSeconds(3));

                Assert.False(result.Success);
                Assert.Equal(EventAssemblerProcessStubSupport.TimeoutParentMode, launch.Mode);
                Assert.True(launch.ProcessId > 0);
                Assert.True(launch.ChildProcessId > 0);
                EventAssemblerProcessStubSupport.AssertSamePath(fakeExe, launch.ProcessPath);
                Assert.Contains("Error: Event Assembler timed out after 120 seconds.", result.Output);
                Assert.Contains("Error: Event Assembler timed out after 120 seconds.", result.ErrorMessage);
                Assert.False(childSurvived, "The timeout child outlived the process-tree kill.");
                Assert.False(File.Exists(markerPath), "The timeout child wrote its cleanup marker after the runner returned.");
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(15),
                    $"Timed-out CompileAndInsert took too long to return: {stopwatch.Elapsed}.");
            }
            finally
            {
                EventAssemblerCompileCore.ResolveExeOverride = savedResolveExe;
                EventAssemblerCompileCore.RunProcessOverride = savedRunProcess;
                EventAssemblerCompileCore.RunProcessTimeoutMs = savedTimeoutMs;
                try { if (fakeEaDir != "") Directory.Delete(fakeEaDir, true); } catch { }
            }
        }
    }
}
