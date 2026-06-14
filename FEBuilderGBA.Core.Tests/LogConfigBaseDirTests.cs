using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1124 — guard that the log file resolves under CoreState.BaseDirectory
    /// (which is the exe dir on desktop and Context.FilesDir / app-private on
    /// Android via #1123). No behaviour change is introduced — this pins the
    /// existing redirect so a regression on Android storage would be caught here.
    /// </summary>
    [Collection("SharedState")]
    public class LogConfigBaseDirTests
    {
        [Fact]
        public void Log_WritesUnderBaseDirectory_ConfigLog()
        {
            string original = CoreState.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_log_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                CoreState.BaseDirectory = tempDir;

                Log.Notify("#1124 base-dir guard log line");
                Log.SyncLog();

                string expected = Path.Combine(tempDir, "config", "log", "log.txt");
                Assert.True(File.Exists(expected),
                    $"Expected log file under BaseDirectory at {expected}");
            }
            finally
            {
                CoreState.BaseDirectory = original;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
