using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class EventAssemblerProcessStubSupportTests
    {
        [Fact]
        public void ReadLaunchRecord_RetriesTransientInvalidJsonUntilComplete()
        {
            string root = Path.Combine(Path.GetTempPath(), "ea-launch-record-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "launch-record.json");
            string tempPath = path + ".tmp";
            File.WriteAllText(path, "");
            File.WriteAllText(tempPath, "pending");

            var expected = new EventAssemblerProcessStubSupport.LaunchRecord
            {
                Mode = EventAssemblerProcessStubSupport.TimeoutParentMode,
                ProcessId = 123,
                ChildProcessId = 456,
                ProcessPath = Path.Combine(root, "ColorzCore.exe"),
                WorkingDirectory = root,
                WrapperPath = Path.Combine(root, "wrapper.event"),
                OutputRomPath = Path.Combine(root, "output.gba"),
                Arguments = new[] { "A", "FE8", "-input:wrapper.event" },
            };

            try
            {
                var writer = new Thread(() =>
                {
                    Thread.Sleep(100);
                    File.WriteAllText(path, JsonSerializer.Serialize(expected));
                    File.Delete(tempPath);
                });
                writer.Start();

                EventAssemblerProcessStubSupport.LaunchRecord actual =
                    EventAssemblerProcessStubSupport.ReadLaunchRecord(path);
                Assert.True(writer.Join(TimeSpan.FromSeconds(5)), "Launch-record writer thread did not finish.");

                Assert.Equal(expected.Mode, actual.Mode);
                Assert.Equal(expected.ProcessId, actual.ProcessId);
                Assert.Equal(expected.ChildProcessId, actual.ChildProcessId);
                EventAssemblerProcessStubSupport.AssertSamePath(expected.ProcessPath, actual.ProcessPath);
                EventAssemblerProcessStubSupport.AssertSamePath(expected.WorkingDirectory, actual.WorkingDirectory);
                EventAssemblerProcessStubSupport.AssertSamePath(expected.WrapperPath, actual.WrapperPath);
                EventAssemblerProcessStubSupport.AssertSamePath(expected.OutputRomPath, actual.OutputRomPath);
                Assert.Equal(expected.Arguments, actual.Arguments);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void ReadLaunchRecord_PersistentInvalidJsonThrows()
        {
            string root = Path.Combine(Path.GetTempPath(), "ea-launch-record-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "launch-record.json");
            File.WriteAllText(path, "{ not valid json");

            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => EventAssemblerProcessStubSupport.ReadLaunchRecord(path));

                Assert.NotNull(ex.InnerException);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
