// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Security.Cryptography;

namespace FEBuilderGBA
{
    /// <summary>
    /// Shared local-file identity helper (size + last-write-UTC ticks + SHA-256) used wherever
    /// the FEMapCreator persistence seam (#1978 Slice 2) needs to detect that a file on disk has
    /// changed since it was recorded: <see cref="FEMapCreatorProfileCore"/> (executable identity)
    /// and <see cref="FEMapCreatorTilesetMappingStoreCore"/> (mapped tileset image/generation-data
    /// file identity) both delegate here so the two staleness checks can never silently diverge.
    /// Never throws; read-only — opens the file for reading only, never writes, deletes, or
    /// launches anything, and never touches the network.
    /// </summary>
    internal static class FileContentIdentityCore
    {
        /// <summary>
        /// Stat-only (no hashing) identity check: file size + last-write-UTC ticks. Cheap enough
        /// to call on every keystroke; used by <see cref="FEMapCreatorExecutableIdentityCache"/>
        /// to decide whether a full SHA-256 recompute is actually necessary.
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

        /// <summary>Hash-only (no size/mtime stat): SHA-256 content hash of <paramref name="path"/>, lowercase hex.</summary>
        internal static bool TryComputeHashOnly(string path, out string sha256Hex, out string error)
        {
            sha256Hex = "";
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
                using SHA256 sha = SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                byte[] hash = sha.ComputeHash(stream);
                sha256Hex = Convert.ToHexString(hash).ToLowerInvariant();
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
        /// Always performs a full re-stat + re-hash — callers needing an authoritative, never-cached
        /// identity (e.g. <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>) must keep using
        /// this method directly rather than <see cref="FEMapCreatorExecutableIdentityCache"/>.
        /// </summary>
        internal static bool TryCompute(string path, out long sizeBytes, out long lastWriteUtcTicks, out string sha256Hex, out string error)
        {
            if (!TryStat(path, out sizeBytes, out lastWriteUtcTicks, out error))
            {
                sha256Hex = "";
                return false;
            }
            return TryComputeHashOnly(path, out sha256Hex, out error);
        }
    }
}
