using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class EventAssemblerProcessStubSupportTests
    {
        const string ParentExitChildMarker = "parent-exit helper descendant self-terminated";

        [Fact]
        public void ReadLaunchRecord_RetriesMissingFinalFileUntilAtomicPublish()
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            string tempPath = path + ".tmp";
            EventAssemblerProcessStubSupport.LaunchRecord expected = CreateExpectedLaunchRecord(root);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(expected));

            try
            {
                Exception? writerError = null;
                var writer = new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(100);
                        File.Move(tempPath, path, true);
                    }
                    catch (Exception ex)
                    {
                        writerError = ex;
                    }
                });
                writer.Start();

                EventAssemblerProcessStubSupport.LaunchRecord actual =
                    EventAssemblerProcessStubSupport.ReadLaunchRecord(path);
                Assert.True(writer.Join(TimeSpan.FromSeconds(5)), "Launch-record writer thread did not finish.");
                Assert.Null(writerError);

                AssertLaunchRecord(expected, actual);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("{\"Mode\":\"timeout-parent\"")]
        public void ReadLaunchRecord_RetriesEmptyOrPartialJsonUntilComplete(string initialJson)
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            string tempPath = path + ".tmp";
            EventAssemblerProcessStubSupport.LaunchRecord expected = CreateExpectedLaunchRecord(root);
            File.WriteAllText(path, initialJson);

            try
            {
                Exception? writerError = null;
                var writer = new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(100);
                        File.WriteAllText(tempPath, JsonSerializer.Serialize(expected));
                        File.Move(tempPath, path, true);
                    }
                    catch (Exception ex)
                    {
                        writerError = ex;
                    }
                });
                writer.Start();

                EventAssemblerProcessStubSupport.LaunchRecord actual =
                    EventAssemblerProcessStubSupport.ReadLaunchRecord(path);
                Assert.True(writer.Join(TimeSpan.FromSeconds(5)), "Launch-record writer thread did not finish.");
                Assert.Null(writerError);

                AssertLaunchRecord(expected, actual);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("{ not valid json")]
        public void ReadLaunchRecord_PersistentMissingOrCorruptFails(string? persistentJson)
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            if (persistentJson != null)
                File.WriteAllText(path, persistentJson);

            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => EventAssemblerProcessStubSupport.ReadLaunchRecord(path));

                Assert.Contains(path, ex.Message);
                Assert.NotNull(ex.InnerException);
                if (persistentJson == null)
                    Assert.IsType<FileNotFoundException>(ex.InnerException);
                else
                    Assert.IsType<JsonException>(ex.InnerException);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void ChildMarkerReader_WaitsForAtomicFinalPublishAndNonEmptyContent()
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "child-survived.txt");
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, ParentExitChildMarker);

            try
            {
                Assert.False(
                    EventAssemblerProcessStubSupport.TryReadChildMarker(
                        path,
                        TimeSpan.FromMilliseconds(200),
                        out string? markerBeforePublish));
                Assert.Null(markerBeforePublish);

                File.Move(tempPath, path, true);

                string marker = EventAssemblerProcessStubSupport.ReadChildMarker(
                    path,
                    TimeSpan.FromSeconds(1));

                Assert.Equal(ParentExitChildMarker, marker);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ea-launch-record-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        static EventAssemblerProcessStubSupport.LaunchRecord CreateExpectedLaunchRecord(string root)
        {
            return new EventAssemblerProcessStubSupport.LaunchRecord
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
        }

        static void AssertLaunchRecord(
            EventAssemblerProcessStubSupport.LaunchRecord expected,
            EventAssemblerProcessStubSupport.LaunchRecord actual)
        {
            Assert.Equal(expected.Mode, actual.Mode);
            Assert.Equal(expected.ProcessId, actual.ProcessId);
            Assert.Equal(expected.ChildProcessId, actual.ChildProcessId);
            EventAssemblerProcessStubSupport.AssertSamePath(expected.ProcessPath, actual.ProcessPath);
            EventAssemblerProcessStubSupport.AssertSamePath(expected.WorkingDirectory, actual.WorkingDirectory);
            EventAssemblerProcessStubSupport.AssertSamePath(expected.WrapperPath, actual.WrapperPath);
            EventAssemblerProcessStubSupport.AssertSamePath(expected.OutputRomPath, actual.OutputRomPath);
            Assert.Equal(expected.Arguments, actual.Arguments);
        }
    }
}
