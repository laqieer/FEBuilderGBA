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
        /// Stat and hash <paramref name="path"/>. Returns false with <paramref name="error"/> set
        /// when the path is blank, does not exist, or cannot be read (I/O error, access denied).
        /// </summary>
        internal static bool TryCompute(string path, out long sizeBytes, out long lastWriteUtcTicks, out string sha256Hex, out string error)
        {
            sizeBytes = 0;
            lastWriteUtcTicks = 0;
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
                var info = new FileInfo(path);
                sizeBytes = info.Length;
                lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;

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
    }
}
