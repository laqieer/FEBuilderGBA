using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1637 — behavioural coverage for the new <c>Log.ErrorF/NotifyF/DebugF</c>
    /// <c>string.Format</c> overloads. The plain <c>Log.Error(params string[])</c> sink only
    /// does <c>string.Join(" ", args)</c>, so a <c>{0}</c>-bearing call recorded the literal
    /// token; the <c>*F</c> overloads must SUBSTITUTE it, and the malformed-format fallback must
    /// never leave a literal <c>{N}</c> token behind.
    ///
    /// Uses a temp <see cref="CoreState.BaseDirectory"/> + the in-memory
    /// <see cref="Log.NonWriteStringB"/> buffer so the suite does not pollute the real log;
    /// serialises via the shared-state collection.
    /// </summary>
    [Collection("SharedState")]
    public class LogFormatOverloadTests
    {
        private static string CaptureLast(Action logAction)
        {
            // Flush any pending content, then capture only what logAction writes.
            Log.NonWriteStringB.Clear();
            logAction();
            string captured = Log.NonWriteStringB.ToString();
            Log.NonWriteStringB.Clear();
            return captured.TrimEnd('\r', '\n');
        }

        [Fact]
        public void ErrorF_SubstitutesPlaceholders_NoLiteralToken()
        {
            string original = CoreState.BaseDirectory;
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_logfmt_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                CoreState.BaseDirectory = tempDir;

                string line = CaptureLast(() => Log.ErrorF("Auto-save failed: {0}", "disk full"));

                Assert.Contains("Auto-save failed: disk full", line);
                Assert.DoesNotContain("{0}", line);
            }
            finally
            {
                CoreState.BaseDirectory = original;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void NotifyF_SubstitutesMultiplePlaceholders()
        {
            string line = CaptureLast(() => Log.NotifyF("{0}/{1} done", "3", "10"));
            Assert.Contains("3/10 done", line);
            Assert.DoesNotContain("{0}", line);
            Assert.DoesNotContain("{1}", line);
        }

        [Fact]
        public void ErrorF_MalformedFormat_DoesNotLeakLiteralPlaceholder()
        {
            // Too few args for {1}: must NOT throw and must NOT leave a literal {N} token.
            string line = CaptureLast(() => Log.ErrorF("need {0} and {1}", "onlyone"));
            Assert.DoesNotContain("{0}", line);
            Assert.DoesNotContain("{1}", line);
            Assert.Contains("onlyone", line);
        }

        [Fact]
        public void ErrorF_NullArgs_DoesNotThrow_AndStripsPlaceholder()
        {
            // args == null path (explicit null array) — falls back, strips {0}.
            string line = CaptureLast(() => Log.ErrorF("value {0}", (object[])null));
            Assert.DoesNotContain("{0}", line);
        }
    }
}
