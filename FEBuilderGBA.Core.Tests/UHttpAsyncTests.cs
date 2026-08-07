#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class UHttpAsyncTests : IDisposable
    {
        readonly string _root;

        public UHttpAsyncTests()
        {
            _root = Path.Combine(AppContext.BaseDirectory, "u-http-async-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public async Task HttpHeadLastModifiedAsync_UsesHeadAndUserAgent()
        {
            var lastModified = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
            HttpMethod? method = null;
            string userAgent = "";
            var handler = new RecordingHandler((request, _) =>
            {
                method = request.Method;
                userAgent = string.Join(" ", request.Headers.UserAgent.Select(x => x.ToString()));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>()),
                };
                response.Content.Headers.LastModified = lastModified;
                return Task.FromResult(response);
            });

            string? value = await U.HttpHeadLastModifiedAsync(
                "https://example.test/build.ups", handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(HttpMethod.Head, method);
            Assert.Contains("FEBuilderGBA/1.0", userAgent);
            Assert.Equal(lastModified.ToString(), value);
        }

        [Fact]
        public async Task HttpGetAsync_ReturnsUtf8TextAndSendsHeaders()
        {
            HttpMethod? method = null;
            Uri? referrer = null;
            string userAgent = "";
            var handler = new RecordingHandler((request, _) =>
            {
                method = request.Method;
                referrer = request.Headers.Referrer;
                userAgent = string.Join(" ", request.Headers.UserAgent.Select(x => x.ToString()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("héllo", System.Text.Encoding.UTF8),
                });
            });

            string text = await U.HttpGetAsync(
                "https://example.test/page", "https://example.test/from", null,
                handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(HttpMethod.Get, method);
            Assert.Equal(new Uri("https://example.test/from"), referrer);
            Assert.Contains("FEBuilderGBA/1.0", userAgent);
            Assert.Equal("héllo", text);
        }

        [Fact]
        public async Task HttpGetAsync_NonSuccess_ReturnsEmptyString()
        {
            var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found"),
                }));

            string text = await U.HttpGetAsync(
                "https://example.test/missing", "", null,
                handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal("", text);
        }

        [Fact]
        public async Task HttpGetAsync_Timeout_ReturnsEmptyString()
        {
            bool handlerSawCancellation = false;
            var handler = new RecordingHandler(async (_, token) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                catch (OperationCanceledException)
                {
                    handlerSawCancellation = token.IsCancellationRequested;
                    throw;
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("late"),
                };
            });

            string text = await U.HttpGetAsync(
                "https://example.test/slow", "", null,
                handler, TimeSpan.FromMilliseconds(20), CancellationToken.None);

            Assert.Equal("", text);
            Assert.True(handlerSawCancellation);
        }

        [Fact]
        public async Task HttpHeadLastModifiedAsync_Timeout_ReturnsNull()
        {
            bool handlerSawCancellation = false;
            var handler = new RecordingHandler(async (_, token) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                catch (OperationCanceledException)
                {
                    handlerSawCancellation = token.IsCancellationRequested;
                    throw;
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            string? value = await U.HttpHeadLastModifiedAsync(
                "https://example.test/slow", handler, TimeSpan.FromMilliseconds(20), CancellationToken.None);

            Assert.Null(value);
            Assert.True(handlerSawCancellation);
        }

        [Fact]
        public async Task HttpHeadLastModifiedAsync_CallerCancellation_Throws()
        {
            var handler = new RecordingHandler(async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                U.HttpHeadLastModifiedAsync(
                    "https://example.test/slow", handler, TimeSpan.FromSeconds(5), cts.Token));
        }

        [Fact]
        public async Task HttpGetAsync_CallerCancellation_Throws()
        {
            var handler = new RecordingHandler(async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("late"),
                };
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                U.HttpGetAsync(
                    "https://example.test/slow", "", null,
                    handler, TimeSpan.FromSeconds(5), cts.Token));
        }

        [Fact]
        public async Task HttpDownloadFileAsync_WritesFileReplacesExistingAndReportsProgress()
        {
            byte[] bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
            string dest = Path.Combine(_root, "download.bin");
            File.WriteAllText(dest, "old");
            var progress = new ListProgress();
            var handler = new RecordingHandler((request, _) =>
            {
                Assert.Contains("FEBuilderGBA/1.0", string.Join(" ", request.Headers.UserAgent.Select(x => x.ToString())));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                });
            });

            (bool ok, string error) = await U.HttpDownloadFileAsync(
                "https://example.test/download.bin", dest, "", progress,
                handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.True(ok, error);
            Assert.Equal("", error);
            Assert.Equal(bytes, File.ReadAllBytes(dest));
            Assert.Contains(bytes.Length, progress.Values);
        }

        [Fact]
        public async Task HttpDownloadFileAsync_NonSuccess_ReturnsFalseAndLeavesNoPartialFile()
        {
            string dest = Path.Combine(_root, "missing.bin");
            var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("nope"),
                }));

            (bool ok, string error) = await U.HttpDownloadFileAsync(
                "https://example.test/missing.bin", dest, "", null,
                handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(File.Exists(dest));
        }

        [Fact]
        public async Task HttpDownloadFileAsync_StreamFailureDeletesPartialOutput()
        {
            string dest = Path.Combine(_root, "partial.bin");
            var handler = new RecordingHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new FailingAfterFirstReadStream(new byte[] { 1, 2, 3, 4 })),
                }));

            (bool ok, string error) = await U.HttpDownloadFileAsync(
                "https://example.test/partial.bin", dest, "", null,
                handler, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(File.Exists(dest));
        }

        [Fact]
        public async Task HttpDownloadFileAsync_CallerCancellation_ThrowsAndDeletesPartialOutput()
        {
            string dest = Path.Combine(_root, "cancelled.bin");
            using var cts = new CancellationTokenSource();
            var handler = new RecordingHandler((_, token) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new DelayedSecondReadStream(new byte[] { 1, 2, 3, 4 }, cts)),
                }));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                U.HttpDownloadFileAsync(
                    "https://example.test/cancelled.bin", dest, "", null,
                    handler, TimeSpan.FromSeconds(5), cts.Token));

            Assert.False(File.Exists(dest));
        }

        sealed class RecordingHandler : HttpMessageHandler
        {
            readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
            {
                _send = send;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _send(request, cancellationToken);
            }
        }

        sealed class ListProgress : IProgress<long>
        {
            public List<long> Values { get; } = new List<long>();
            public void Report(long value) => Values.Add(value);
        }

        sealed class FailingAfterFirstReadStream : Stream
        {
            readonly byte[] _firstChunk;
            bool _returnedFirstChunk;

            public FailingAfterFirstReadStream(byte[] firstChunk)
            {
                _firstChunk = firstChunk;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_returnedFirstChunk)
                {
                    throw new IOException("copy failed");
                }
                int toCopy = Math.Min(count, _firstChunk.Length);
                Array.Copy(_firstChunk, 0, buffer, offset, toCopy);
                _returnedFirstChunk = true;
                return toCopy;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_returnedFirstChunk)
                {
                    throw new IOException("copy failed");
                }
                int toCopy = Math.Min(buffer.Length, _firstChunk.Length);
                _firstChunk.AsMemory(0, toCopy).CopyTo(buffer);
                _returnedFirstChunk = true;
                return ValueTask.FromResult(toCopy);
            }
        }

        sealed class DelayedSecondReadStream : Stream
        {
            readonly byte[] _firstChunk;
            readonly CancellationTokenSource _cancellation;
            bool _returnedFirstChunk;

            public DelayedSecondReadStream(byte[] firstChunk, CancellationTokenSource cancellation)
            {
                _firstChunk = firstChunk;
                _cancellation = cancellation;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_returnedFirstChunk)
                {
                    _cancellation.Cancel();
                    return ValueTask.FromCanceled<int>(_cancellation.Token);
                }
                int toCopy = Math.Min(buffer.Length, _firstChunk.Length);
                _firstChunk.AsMemory(0, toCopy).CopyTo(buffer);
                _returnedFirstChunk = true;
                return ValueTask.FromResult(toCopy);
            }
        }
    }
}
