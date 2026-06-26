using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1467 — Core seam used by the Avalonia Log Viewer (and WinForms LogForm):
    /// <see cref="Log.GetLogFilePath"/> resolves the same <c>config/log/log.txt</c>
    /// path as the private writer, and <see cref="Log.LogToString"/> round-trips
    /// the logged lines back out of the file. Uses a temp
    /// <see cref="CoreState.BaseDirectory"/> so the suite does not pollute the
    /// real output log; restores state in <c>finally</c> and serialises via the
    /// shared-state collection.
    /// </summary>
    [Collection("SharedState")]
    public class LogViewerCoreTests
    {
        [Fact]
        public void GetLogFilePath_ResolvesUnderBaseDirectory_ConfigLog()
        {
            string original = CoreState.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_logview_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                CoreState.BaseDirectory = tempDir;

                string expected = Path.Combine(tempDir, "config", "log", "log.txt");
                Assert.Equal(expected, Log.GetLogFilePath());
            }
            finally
            {
                CoreState.BaseDirectory = original;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void LogToString_RoundTripsLoggedLines()
        {
            string original = CoreState.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_logview_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                CoreState.BaseDirectory = tempDir;

                // Unique marker so we are immune to anything else in the in-memory
                // buffer from earlier tests in this shared-state collection.
                string marker = "#1467 log-viewer marker " + Guid.NewGuid().ToString("N");
                Log.Notify(marker);
                Log.SyncLog();

                string text = Log.LogToString();
                Assert.False(string.IsNullOrEmpty(text), "LogToString returned empty after a Notify+SyncLog.");
                Assert.Contains(marker, text);

                // The file the viewer Save/Open-dir buttons target must actually exist.
                Assert.True(File.Exists(Log.GetLogFilePath()));
            }
            finally
            {
                CoreState.BaseDirectory = original;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void UpdateEvent_FiresOnLog()
        {
            string original = CoreState.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_logview_" + Guid.NewGuid().ToString("N"));
            int fired = 0;
            EventHandler handler = (_, _) => fired++;
            try
            {
                Directory.CreateDirectory(tempDir);
                CoreState.BaseDirectory = tempDir;

                Log.UpdateEvent += handler;
                Log.Notify("#1467 update-event probe " + Guid.NewGuid().ToString("N"));
                Assert.True(fired >= 1, "Log.UpdateEvent did not fire on a logged message.");

                // Flush the buffered line into the TEMP log NOW, while
                // BaseDirectory still points at tempDir, so it can't later be
                // flushed into the real log path after the restore below (and so
                // it can't leak across the SharedState collection).
                Log.SyncLog();
            }
            finally
            {
                Log.UpdateEvent -= handler;
                CoreState.BaseDirectory = original;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
