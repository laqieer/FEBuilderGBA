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
            AssertLaunchRecordReadWaitsForAtomicPublish(initialFinalJson: null);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{\"Mode\":\"timeout-parent\"")]
        public void ReadLaunchRecord_RetriesEmptyOrPartialJsonUntilComplete(string initialJson)
        {
            AssertLaunchRecordReadWaitsForAtomicPublish(initialJson);
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
            Exception? publisherError = null;
            string? marker = null;
            Exception? readerError = null;

            using var publishReady = new ManualResetEventSlim(false);
            using var allowPublish = new ManualResetEventSlim(false);
            using var readerStarted = new ManualResetEventSlim(false);

            var publisher = new Thread(() =>
            {
                try
                {
                    File.WriteAllText(tempPath, ParentExitChildMarker);
                    publishReady.Set();
                    if (!allowPublish.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Child-marker publisher timed out before the final atomic publish.");

                    EventAssemblerProcessStubSupport.PublishStagedAtomicText(
                        path,
                        ParentExitChildMarker);
                }
                catch (Exception ex)
                {
                    publisherError = ex;
                }
            });

            Thread? reader = null;

            try
            {
                publisher.Start();
                Assert.True(
                    publishReady.Wait(TimeSpan.FromSeconds(5)),
                    "Child-marker publisher did not stage the atomic publish.");
                Assert.True(File.Exists(tempPath), "The child-marker publisher did not stage the temporary file.");
                Assert.False(File.Exists(path), "The child-marker final path should remain unpublished while staging.");

                reader = new Thread(() =>
                {
                    try
                    {
                        readerStarted.Set();
                        marker = EventAssemblerProcessStubSupport.ReadChildMarker(
                            path,
                            TimeSpan.FromSeconds(1));
                    }
                    catch (Exception ex)
                    {
                        readerError = ex;
                    }
                });
                reader.Start();

                Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(5)), "Child-marker reader did not start.");
                Assert.False(
                    reader.Join(TimeSpan.FromMilliseconds(200)),
                    "Reader should keep retrying while the child-marker publish is still in progress.");

                allowPublish.Set();
                Assert.True(publisher.Join(TimeSpan.FromSeconds(5)), "Child-marker publisher thread did not finish.");
                Assert.Null(publisherError);
                Assert.True(reader.Join(TimeSpan.FromSeconds(5)), "Child-marker reader thread did not finish.");
                Assert.Null(readerError);

                Assert.Equal(ParentExitChildMarker, marker);
            }
            finally
            {
                allowPublish.Set();
                if (publisher.IsAlive)
                    publisher.Join(TimeSpan.FromSeconds(5));
                if (reader?.IsAlive == true)
                    reader.Join(TimeSpan.FromSeconds(5));
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void AtomicPublisher_RetriesTransientMoveFailuresWhileTempExists()
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            EventAssemblerProcessStubSupport.LaunchRecord expected = CreateExpectedLaunchRecord(root);
            string expectedJson = JsonSerializer.Serialize(expected);
            File.WriteAllText(path + ".tmp", expectedJson);
            int moveCalls = 0;
            int delayCalls = 0;

            try
            {
                EventAssemblerProcessStubSupport.PublishStagedAtomicText(
                    path,
                    expectedJson,
                    (sourcePath, destinationPath) =>
                    {
                        moveCalls++;
                        if (moveCalls < EventAssemblerProcessStubSupport.AtomicPublishRetryCount)
                            throw new IOException("sharing violation");

                        File.Move(sourcePath, destinationPath, overwrite: true);
                    },
                    _ => delayCalls++);

                Assert.Equal(EventAssemblerProcessStubSupport.AtomicPublishRetryCount, moveCalls);
                Assert.Equal(EventAssemblerProcessStubSupport.AtomicPublishRetryCount - 1, delayCalls);
                Assert.False(File.Exists(path + ".tmp"));
                AssertLaunchRecord(expected, EventAssemblerProcessStubSupport.ReadLaunchRecord(path));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void AtomicPublisher_AcceptsCompletedThenThrewMoveWhenFinalContentMatches()
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            EventAssemblerProcessStubSupport.LaunchRecord expected = CreateExpectedLaunchRecord(root);
            string expectedJson = JsonSerializer.Serialize(expected);
            File.WriteAllText(path + ".tmp", expectedJson);
            int moveCalls = 0;
            int delayCalls = 0;

            try
            {
                EventAssemblerProcessStubSupport.PublishStagedAtomicText(
                    path,
                    expectedJson,
                    (sourcePath, destinationPath) =>
                    {
                        moveCalls++;
                        File.Move(sourcePath, destinationPath, overwrite: true);
                        throw new IOException("simulated completed-then-threw");
                    },
                    _ => delayCalls++);

                Assert.Equal(1, moveCalls);
                Assert.Equal(0, delayCalls);
                Assert.False(File.Exists(path + ".tmp"));
                AssertLaunchRecord(expected, EventAssemblerProcessStubSupport.ReadLaunchRecord(path));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void AtomicPublisher_PersistentUnauthorizedAccessThrows()
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            string expectedJson = JsonSerializer.Serialize(CreateExpectedLaunchRecord(root));
            File.WriteAllText(path + ".tmp", expectedJson);
            int moveCalls = 0;
            int delayCalls = 0;

            try
            {
                UnauthorizedAccessException ex = Assert.Throws<UnauthorizedAccessException>(
                    () => EventAssemblerProcessStubSupport.PublishStagedAtomicText(
                        path,
                        expectedJson,
                        (sourcePath, destinationPath) =>
                        {
                            moveCalls++;
                            throw new UnauthorizedAccessException(
                                $"sharing violation {moveCalls}: {sourcePath} -> {destinationPath}");
                        },
                        _ => delayCalls++));

                Assert.Contains("sharing violation", ex.Message);
                Assert.Equal(EventAssemblerProcessStubSupport.AtomicPublishRetryCount, moveCalls);
                Assert.Equal(EventAssemblerProcessStubSupport.AtomicPublishRetryCount - 1, delayCalls);
                Assert.True(File.Exists(path + ".tmp"));
                Assert.False(File.Exists(path));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        static void AssertLaunchRecordReadWaitsForAtomicPublish(string? initialFinalJson)
        {
            string root = CreateRoot();
            string path = Path.Combine(root, "launch-record.json");
            string tempPath = path + ".tmp";
            EventAssemblerProcessStubSupport.LaunchRecord expected = CreateExpectedLaunchRecord(root);
            string expectedJson = JsonSerializer.Serialize(expected);
            EventAssemblerProcessStubSupport.LaunchRecord? actual = null;
            Exception? publisherError = null;
            Exception? readerError = null;

            using var publishReady = new ManualResetEventSlim(false);
            using var allowPublish = new ManualResetEventSlim(false);
            using var readerStarted = new ManualResetEventSlim(false);

            if (initialFinalJson != null)
                File.WriteAllText(path, initialFinalJson);

            var publisher = new Thread(() =>
            {
                try
                {
                    File.WriteAllText(tempPath, expectedJson);
                    publishReady.Set();
                    if (!allowPublish.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Launch-record publisher timed out before the final atomic publish.");

                    EventAssemblerProcessStubSupport.PublishStagedAtomicText(
                        path,
                        expectedJson);
                }
                catch (Exception ex)
                {
                    publisherError = ex;
                }
            });

            Thread? reader = null;

            try
            {
                publisher.Start();
                Assert.True(
                    publishReady.Wait(TimeSpan.FromSeconds(5)),
                    "Launch-record publisher did not stage the atomic publish.");
                Assert.True(File.Exists(tempPath), "The launch-record publisher did not stage the temporary file.");
                if (initialFinalJson == null)
                {
                    Assert.False(File.Exists(path), "The launch-record final path should remain unpublished while staging.");
                }
                else
                {
                    Assert.True(File.Exists(path), "The launch-record final path should contain the pre-existing incomplete file.");
                    Assert.Equal(initialFinalJson, File.ReadAllText(path));
                }

                reader = new Thread(() =>
                {
                    try
                    {
                        readerStarted.Set();
                        actual = EventAssemblerProcessStubSupport.ReadLaunchRecord(path);
                    }
                    catch (Exception ex)
                    {
                        readerError = ex;
                    }
                });
                reader.Start();

                Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(5)), "Launch-record reader did not start.");
                bool completedEarly = reader.Join(TimeSpan.FromMilliseconds(200));
                Assert.False(
                    completedEarly,
                    readerError == null
                        ? "Reader should keep retrying while the launch-record publish is still in progress."
                        : "Reader should keep retrying while the launch-record publish is still in progress."
                            + Environment.NewLine
                            + readerError);

                allowPublish.Set();
                Assert.True(publisher.Join(TimeSpan.FromSeconds(5)), "Launch-record publisher thread did not finish.");
                Assert.Null(publisherError);
                Assert.True(reader.Join(TimeSpan.FromSeconds(5)), "Launch-record reader thread did not finish.");
                Assert.Null(readerError);
                Assert.NotNull(actual);

                AssertLaunchRecord(expected, actual!);
            }
            finally
            {
                allowPublish.Set();
                if (publisher.IsAlive)
                    publisher.Join(TimeSpan.FromSeconds(5));
                if (reader?.IsAlive == true)
                    reader.Join(TimeSpan.FromSeconds(5));
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
