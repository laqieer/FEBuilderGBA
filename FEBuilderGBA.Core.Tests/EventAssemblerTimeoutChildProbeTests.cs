using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection(ProcessRunnerTestCollection.Name)]
    public class EventAssemblerTimeoutChildProbeTests
    {
        const string TimeoutChildMarker = "child survived timeout kill";
        const int TimeoutChildProbeObservationSeconds = 15;

        [Fact]
        public void TimeoutChildProbe_ParentExitPublishesSurvivalMarker()
        {
            string root = CreateRoot();
            string parentMarkerPath = Path.Combine(root, "parent-marker.txt");
            string childMarkerPath = Path.Combine(root, "child-survived.txt");
            Process? parent = null;
            Process? child = null;

            try
            {
                parent = StartStubProcess(
                    EventAssemblerProcessStubSupport.ParentExitChildCommand,
                    parentMarkerPath);
                long parentStartTimeUtcTicks =
                    parent.StartTime.ToUniversalTime().Ticks;
                child = StartStubProcess(
                    EventAssemblerProcessStubSupport.TimeoutChildCommand,
                    childMarkerPath,
                    parent.Id.ToString(CultureInfo.InvariantCulture),
                    parentStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
                    "10000");

                parent.Kill(entireProcessTree: false);
                Assert.True(
                    parent.WaitForExit(5000),
                    "The watched parent did not exit.");

                Assert.Equal(
                    TimeoutChildMarker,
                    EventAssemblerProcessStubSupport.ReadChildMarker(
                        childMarkerPath,
                        TimeSpan.FromSeconds(
                            TimeoutChildProbeObservationSeconds)));
                Assert.True(
                    child.WaitForExit(5000),
                    "The timeout child did not exit after publishing its marker.");
                Assert.Equal(0, child.ExitCode);
            }
            finally
            {
                TerminateProcess(child);
                TerminateProcess(parent);
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void TimeoutChildProbe_ParentAliveThroughDeadlinePublishesSurvivalMarker()
        {
            string root = CreateRoot();
            string parentMarkerPath = Path.Combine(root, "parent-marker.txt");
            string childMarkerPath = Path.Combine(root, "child-survived.txt");
            Process? parent = null;
            Process? child = null;

            try
            {
                parent = StartStubProcess(
                    EventAssemblerProcessStubSupport.ParentExitChildCommand,
                    parentMarkerPath);
                long parentStartTimeUtcTicks =
                    parent.StartTime.ToUniversalTime().Ticks;
                child = StartStubProcess(
                    EventAssemblerProcessStubSupport.TimeoutChildCommand,
                    childMarkerPath,
                    parent.Id.ToString(CultureInfo.InvariantCulture),
                    parentStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
                    "500");

                Assert.Equal(
                    TimeoutChildMarker,
                    EventAssemblerProcessStubSupport.ReadChildMarker(
                        childMarkerPath,
                        TimeSpan.FromSeconds(
                            TimeoutChildProbeObservationSeconds)));
                Assert.False(
                    parent.HasExited,
                    "The deadline probe must publish while the watched parent is still alive.");
                Assert.True(
                    child.WaitForExit(5000),
                    "The timeout child did not exit after publishing its marker.");
                Assert.Equal(0, child.ExitCode);
            }
            finally
            {
                TerminateProcess(child);
                TerminateProcess(parent);
                try { Directory.Delete(root, true); } catch { }
            }
        }

        static Process StartStubProcess(params string[] args)
        {
            var startInfo = new ProcessStartInfo(
                EventAssemblerProcessStubSupport.FindExecutable())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Failed to start Event Assembler process stub.");
        }

        static void TerminateProcess(Process? process)
        {
            if (process == null)
                return;

            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        static string CreateRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "ea-timeout-probe-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
