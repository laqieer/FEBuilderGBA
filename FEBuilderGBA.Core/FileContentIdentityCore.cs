// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Shared local-file identity helper (size + last-write-UTC ticks + SHA-256) used wherever
    /// the FEMapCreator persistence seam (#1978 Slice 2) needs to detect that a file on disk has
    /// changed since it was recorded: <see cref="FEMapCreatorProfileCore"/> (executable identity)
    /// and <see cref="FEMapCreatorTilesetMappingStoreCore"/> (mapped tileset image/generation-data
    /// file identity) both delegate here so the two staleness checks can never silently diverge.
    /// The legacy no-token overloads never throw; cancellation-aware overloads additionally
    /// propagate <see cref="OperationCanceledException"/> when cancellation is requested.
    /// Read-only — opens the file for reading only, never writes, deletes, launches anything,
    /// or touches the network.
    /// </summary>
    internal static class FileContentIdentityCore
    {
        const int HashBufferSize = 64 * 1024;

        /// <summary>
        /// Stat-only (no hashing) identity check: file size + last-write-UTC ticks. Used by live
        /// FEMapCreator status so opening or typing in Options never reads executable content.
        /// </summary>
        internal static bool TryStat(string path, out long sizeBytes, out long lastWriteUtcTicks, out string error)
        {
            sizeBytes = 0;
            lastWriteUtcTicks = 0;
            error = "";

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is empty.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "File does not exist: " + path;
                return false;
            }

            try
            {
                var info = new FileInfo(path);
                sizeBytes = info.Length;
                lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Cancellation-aware SHA-256 hash. Cancellation is checked before opening the file and
        /// between bounded reads so a large local executable or tileset file cannot keep an
        /// awaiting UI workflow unresponsive until the entire hash completes.
        /// </summary>
        internal static bool TryComputeHashOnly(
            string path,
            CancellationToken cancellationToken,
            out string sha256Hex,
            out string error)
        {
            sha256Hex = "";
            error = "";
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is empty.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "File does not exist: " + path;
                return false;
            }

            try
            {
                using FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: HashBufferSize,
                    FileOptions.SequentialScan);
                sha256Hex = ComputeSha256(stream, cancellationToken);
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Stat and hash <paramref name="path"/>. Returns false with <paramref name="error"/> set
        /// when the path is blank, does not exist, or cannot be read (I/O error, access denied).
        /// Always performs a full re-stat + re-hash for authoritative identity checks.
        /// </summary>
        internal static bool TryCompute(string path, out long sizeBytes, out long lastWriteUtcTicks, out string sha256Hex, out string error)
            => TryCompute(path, CancellationToken.None, out sizeBytes, out lastWriteUtcTicks, out sha256Hex, out error);

        /// <summary>
        /// Cancellation-aware stat + SHA-256 identity calculation. The token is observed before
        /// statting and throughout the bounded hash-read loop.
        /// </summary>
        internal static bool TryCompute(
            string path,
            CancellationToken cancellationToken,
            out long sizeBytes,
            out long lastWriteUtcTicks,
            out string sha256Hex,
            out string error)
            => TryCompute(
                path,
                cancellationToken,
                afterMetadataCaptured: null,
                out sizeBytes,
                out lastWriteUtcTicks,
                out sha256Hex,
                out error);

        /// <summary>
        /// Deterministic race seam used by Core tests to replace the pathname after this method
        /// captures metadata from the opened handle but before hashing that same handle.
        /// </summary>
        internal static bool TryCompute(
            string path,
            CancellationToken cancellationToken,
            Action? afterMetadataCaptured,
            out long sizeBytes,
            out long lastWriteUtcTicks,
            out string sha256Hex,
            out string error)
        {
            sizeBytes = 0;
            lastWriteUtcTicks = 0;
            sha256Hex = "";
            error = "";
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is empty.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "File does not exist: " + path;
                return false;
            }

            try
            {
                using FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: HashBufferSize,
                    FileOptions.SequentialScan);
                long openedSize = stream.Length;
                long openedTicks = File.GetLastWriteTimeUtc(stream.SafeFileHandle).Ticks;
                afterMetadataCaptured?.Invoke();

                string openedHash = ComputeSha256(stream, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                long finalSize = stream.Length;
                long finalTicks = File.GetLastWriteTimeUtc(stream.SafeFileHandle).Ticks;
                if (openedSize != finalSize || openedTicks != finalTicks)
                {
                    error = "File changed while its content identity was being computed: " + path;
                    return false;
                }

                sizeBytes = openedSize;
                lastWriteUtcTicks = openedTicks;
                sha256Hex = openedHash;
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string ComputeSha256(FileStream stream, CancellationToken cancellationToken)
        {
            using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[HashBufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                cancellationToken.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
    }
}
