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
    }
}
