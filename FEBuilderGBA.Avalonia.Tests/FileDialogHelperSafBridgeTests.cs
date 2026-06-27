using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.Dialogs;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1639 — the SAF (Storage Access Framework) bridge in FileDialogHelper.
    /// On Android a picked IStorageFile is a content:// document with NO local
    /// filesystem path (TryGetLocalPath() returns null), so the historic
    /// path-returning helpers collapsed a valid pick to null and every caller
    /// read it as "cancelled". The bridge instead reads the SAF stream into a
    /// temp file (ResolveReadPathAsync) and writes back through the SAF stream
    /// (WriteViaAsync). Avalonia forbids implementing IStorageFile in user code,
    /// so these tests exercise the stream-based core (CopyStreamToTempAsync /
    /// WriteViaStreamsAsync) the IStorageFile overloads delegate to — a stream
    /// opener with NO local path is exactly the Android case.
    /// </summary>
    public class FileDialogHelperSafBridgeTests
    {
        // A MemoryStream that is NOT disposed away when the bridge closes it, so
        // the test can read the written bytes afterward (mirrors a SAF document
        // whose content persists past the OpenWriteAsync stream's lifetime).
        sealed class PersistentStream : Stream
        {
            readonly MemoryStream _inner;
            public PersistentStream(MemoryStream inner) { _inner = inner; }
            public byte[] ToArray() => _inner.ToArray();
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => true;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] b, int o, int c) => _inner.Read(b, o, c);
            public override long Seek(long off, SeekOrigin origin) => _inner.Seek(off, origin);
            public override void SetLength(long v) => _inner.SetLength(v);
            public override void Write(byte[] b, int o, int c) => _inner.Write(b, o, c);
            protected override void Dispose(bool disposing) { /* keep _inner alive */ }
        }

        [Fact]
        public async Task CopyStreamToTempAsync_ReturnsReadableTempCopy_WithExtension()
        {
            byte[] payload = Encoding.UTF8.GetBytes("textId\tValue\n0\tHello\n");

            string? path = await FileDialogHelper.CopyStreamToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream(payload, writable: false)),
                "texts.tsv");

            // The whole point: a valid SAF pick is NOT collapsed to null/cancel.
            Assert.False(string.IsNullOrEmpty(path));
            Assert.True(File.Exists(path));
            Assert.Equal(payload, File.ReadAllBytes(path!));
            // Extension preserved so format-by-extension sniffing still works.
            Assert.Equal(".tsv", Path.GetExtension(path));

            try { File.Delete(path!); } catch { }
        }

        [Fact]
        public async Task ResolveReadPathAsync_Null_ReturnsNull()
        {
            Assert.Null(await FileDialogHelper.ResolveReadPathAsync(null));
        }

        [Fact]
        public async Task CopyStreamToTempAsync_DoesNotSweepRecentReadTemps()
        {
            // #1639 review follow-up: the read-temp sweep is AGE-BASED so a
            // DEFERRED flow (browse now, Import/Reduce later — even after picking
            // other SAF files in between) keeps its temp. A second pick must NOT
            // delete the first, recent temp.
            string? first = await FileDialogHelper.CopyStreamToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 })), "a.bin");
            Assert.True(File.Exists(first));

            string? second = await FileDialogHelper.CopyStreamToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream(new byte[] { 2 })), "b.bin");

            Assert.True(File.Exists(second));
            Assert.True(File.Exists(first)); // recent prior read-temp is RETAINED

            try { File.Delete(first!); } catch { }
            try { File.Delete(second!); } catch { }
        }

        [Fact]
        public async Task SweepReadTemps_RemovesStaleReadTemps()
        {
            // The sweep DOES reclaim genuinely old temps (no unbounded leak).
            // Create a read-temp, backdate it past the window, then sweep with a
            // zero max-age so any past-cutoff temp is eligible.
            string? old = await FileDialogHelper.CopyStreamToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 })), "stale.bin");
            Assert.True(File.Exists(old));
            File.SetLastWriteTimeUtc(old!, DateTime.UtcNow.AddHours(-24));

            FileDialogHelper.SweepReadTempsForTest(TimeSpan.Zero);

            Assert.False(File.Exists(old)); // stale temp reclaimed
        }

        [Fact]
        public async Task WriteViaStreamsAsync_WritesBackThroughStream()
        {
            var backing = new MemoryStream();
            byte[] expected = { 1, 2, 3, 4, 5, 6, 7, 8 };

            string? written = await FileDialogHelper.WriteViaStreamsAsync(
                () => Task.FromResult<Stream>(new PersistentStream(backing)),
                "out.bin",
                path => { File.WriteAllBytes(path, expected); return Task.CompletedTask; });

            // Not cancelled, and the bytes the writer produced reached the doc.
            Assert.Equal("out.bin", written);
            Assert.Equal(expected, backing.ToArray());
        }

        [Fact]
        public async Task WriteViaStreamsAsync_TruncatesPreviouslyLargerDocument()
        {
            // A document that already holds MORE bytes than the new content; the
            // bridge must SetLength(0) so no stale trailing bytes remain (#1639
            // Copilot review gap 7).
            var backing = new MemoryStream(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 });
            byte[] shorter = { 1, 2, 3 };

            await FileDialogHelper.WriteViaStreamsAsync(
                () => Task.FromResult<Stream>(new PersistentStream(backing)),
                "out.bin",
                path => { File.WriteAllBytes(path, shorter); return Task.CompletedTask; });

            Assert.Equal(shorter, backing.ToArray()); // exactly 3 bytes, no stale tail
        }

        [Fact]
        public async Task WriteViaStreamsAsync_DeletesTempFile()
        {
            var backing = new MemoryStream();
            string? capturedTemp = null;

            await FileDialogHelper.WriteViaStreamsAsync(
                () => Task.FromResult<Stream>(new PersistentStream(backing)),
                "out.txt",
                path => { capturedTemp = path; File.WriteAllText(path, "hi"); return Task.CompletedTask; });

            Assert.NotNull(capturedTemp);
            // The bridge owns the temp lifetime and cleans it up after write-back.
            Assert.False(File.Exists(capturedTemp!));
        }

        [Fact]
        public async Task WriteViaAsync_Null_ReturnsNull_AndDoesNotRunWriter()
        {
            bool writerRan = false;
            string? written = await FileDialogHelper.WriteViaAsync(
                (global::Avalonia.Platform.Storage.IStorageFile?)null, _ => writerRan = true);
            Assert.Null(written);
            Assert.False(writerRan);
        }

        // #1639 review: the new Android-disable R._() messages must be LOADABLE by
        // the runtime translate loader, which only resets the pending key on a
        // BLANK line — so each :key/value pair needs a blank-line separator. This
        // replicates MyTranslateResourceLow.LoadResource's state machine verbatim
        // (a key swallowed as a prior entry's value is NOT reported), proving the
        // separators are present in both ja.txt and zh.txt.
        static System.Collections.Generic.HashSet<string> LoadableKeys(string path)
        {
            var keys = new System.Collections.Generic.HashSet<string>();
            string? src = null;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0) { src = null; continue; }
                if (src == null)
                {
                    if (line[0] != ':') continue;
                    src = line.Substring(1);
                    keys.Add(src);
                }
                // value line — src is NOT reset (only a blank line resets it).
            }
            return keys;
        }

        static readonly string[] AndroidDisableKeys =
        {
            "Event Assembler needs desktop file-system access and is not available on this device.",
            "The assembler needs desktop file-system access and is not available on this device.",
            "Custom build runs an external tool and needs desktop file-system access; it is not available on this device.",
            "ROM rebuild needs desktop file-system access and is not available on this device.",
            "Opening a decomp project reads a folder tree and requires desktop file-system access; it is not available on this device.",
            "Dev Translate reads a folder tree and requires desktop file-system access; it is not available on this device.",
            "Importing an instrument set reads sibling files and requires desktop file-system access; it is not available on this device.",
            "Instrument-set export writes multiple files and requires desktop file-system access; it is not available on this device.",
            "Exporting all fonts writes sibling glyph PNGs and requires desktop file-system access; it is not available on this device.",
            "Importing all fonts reads sibling glyph PNGs and requires desktop file-system access; it is not available on this device.",
            "This setting configures an external tool path and requires desktop file-system access; it is not available on this device.",
            "This setting configures an external tool directory and requires desktop file-system access; it is not available on this device.",
        };

        [Theory]
        [InlineData("ja")]
        [InlineData("zh")]
        public void NewAndroidDisableMessages_AreLoadableFromTranslateFile(string lang)
        {
            string repoRoot = FindRepoRoot();
            string path = Path.Combine(repoRoot, "config", "translate", lang + ".txt");
            Assert.True(File.Exists(path), $"missing {path}");

            var keys = LoadableKeys(path);
            foreach (string k in AndroidDisableKeys)
                Assert.True(keys.Contains(k),
                    $"'{k}' is not a LOADABLE key in {lang}.txt — likely missing the blank-line separator after the previous entry.");
        }

        static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln");
        }
    }
}
