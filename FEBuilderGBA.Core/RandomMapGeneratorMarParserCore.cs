// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Converts FEMapCreator's signed little-endian headerless <c>.mar</c> output
    /// (<c>tileIndex * 32</c>) into FEBuilderGBA MAR values
    /// (<see cref="MapEditorTilesetCore.ChipsetIndexToMar"/> == <c>chipsetIndex * 4</c>).
    /// </summary>
    public static class RandomMapGeneratorMarParserCore
    {
        /// <summary>
        /// Parse FEMapCreator raw <c>.mar</c> bytes using FEBuilderGBA's chipset-count limit.
        /// Returns false + error on any shape or value fault and never throws.
        /// </summary>
        public static bool TryParse(
            byte[] marBytes,
            int width,
            int height,
            out ushort[] mars,
            out string error)
        {
            return TryParse(
                marBytes,
                width,
                height,
                MapEditorTilesetCore.CHIPSET_COUNT,
                out mars,
                out error);
        }

        /// <summary>
        /// Internal test seam (via <c>InternalsVisibleTo("FEBuilderGBA.Core.Tests")</c>) that
        /// lets tests prove the upper-bound guard with a narrowed chipset-count limit.
        /// Production callers should use the 4-argument overload.
        /// </summary>
        internal static bool TryParse(
            byte[] marBytes,
            int width,
            int height,
            int maximumChipsetCount,
            out ushort[] mars,
            out string error)
        {
            mars = Array.Empty<ushort>();
            error = "";

            if (marBytes == null)
            {
                error = "Generated MAR bytes are null.";
                return false;
            }
            if (width <= 0 || height <= 0)
            {
                error = $"Invalid MAR dimensions {width}x{height}.";
                return false;
            }
            if (maximumChipsetCount <= 0)
            {
                error = "Maximum chipset count must be positive.";
                return false;
            }

            long expectedByteCount = (long)width * height * 2;
            if (expectedByteCount > int.MaxValue)
            {
                error = $"Requested MAR dimensions are too large: {width}x{height}.";
                return false;
            }
            if (marBytes.Length != (int)expectedByteCount)
            {
                error = $"Generated MAR length mismatch: expected {(int)expectedByteCount} bytes but got {marBytes.Length}.";
                return false;
            }

            int entryCount = width * height;
            var parsed = new ushort[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                int offset = i * 2;
                short rawValue = (short)(marBytes[offset] | (marBytes[offset + 1] << 8));
                if (rawValue < 0)
                {
                    error = $"Generated MAR entry {i} is negative: {rawValue}.";
                    return false;
                }
                if ((rawValue % 32) != 0)
                {
                    error = $"Generated MAR entry {i} is not divisible by 32: {rawValue}.";
                    return false;
                }

                int chipsetIndex = rawValue / 32;
                if (chipsetIndex >= maximumChipsetCount)
                {
                    error = $"Generated MAR entry {i} resolves to chipset index {chipsetIndex}, which exceeds the supported range 0..{maximumChipsetCount - 1}.";
                    return false;
                }

                parsed[i] = MapEditorTilesetCore.ChipsetIndexToMar(chipsetIndex);
            }

            mars = parsed;
            return true;
        }
    }
}
