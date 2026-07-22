// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Security.Cryptography;

namespace FEBuilderGBA
{
    /// <summary>
    /// Stable identity for "the tileset currently backing one loaded map", derived only from
    /// the ROM version plus a hash of the map's decoded/decompressed OBJ, PAL, and CFG
    /// (chipset config) bytes — never from mutable ROM pointers alone, so relocating those
    /// tables (e.g. via table expansion) does not change the fingerprint as long as the
    /// decoded bytes are unchanged, and two maps that render identically always compare
    /// equal even if their PLIST indices differ.
    /// </summary>
    public readonly struct TilesetFingerprint : IEquatable<TilesetFingerprint>
    {
        TilesetFingerprint(string value) { Value = value; }

        /// <summary>Lower-case hex SHA-256 digest. Empty for <see cref="Empty"/>.</summary>
        public string Value { get; }

        /// <summary>The default, unset fingerprint. Never equal to a <see cref="Compute"/> result.</summary>
        public static readonly TilesetFingerprint Empty = new TilesetFingerprint("");

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>
        /// Compute the fingerprint from the ROM version identifier and the three decoded
        /// tileset buffers. Each buffer is length-prefixed before hashing so that, e.g., an
        /// OBJ/PAL boundary shift can never collide with a different split of the same
        /// concatenated bytes.
        /// </summary>
        public static TilesetFingerprint Compute(int romVersion, byte[] objData, byte[] paletteData, byte[] configData)
        {
            using SHA256 sha = SHA256.Create();
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(romVersion);
                WriteBlock(writer, objData);
                WriteBlock(writer, paletteData);
                WriteBlock(writer, configData);
            }
            byte[] hash = sha.ComputeHash(stream.ToArray());
            return new TilesetFingerprint(Convert.ToHexString(hash).ToLowerInvariant());
        }

        static void WriteBlock(System.IO.BinaryWriter writer, byte[] data)
        {
            data ??= Array.Empty<byte>();
            writer.Write(data.Length);
            writer.Write(data);
        }

        public bool Equals(TilesetFingerprint other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TilesetFingerprint other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? "");
        public override string ToString() => Value ?? "";
        public static bool operator ==(TilesetFingerprint left, TilesetFingerprint right) => left.Equals(right);
        public static bool operator !=(TilesetFingerprint left, TilesetFingerprint right) => !left.Equals(right);
    }

    /// <summary>
    /// Fully-resolved, decompressed tileset + map data for one map-setting entry. Produced by
    /// <see cref="BuiltInRandomMapTilesetCore.TryResolveMapTileset"/>; read-only snapshot, safe
    /// to cache across calls (never mutated after construction).
    /// </summary>
    public sealed class MapTilesetSnapshot
    {
        internal MapTilesetSnapshot(
            uint mapSettingAddr,
            uint mapDataAddr,
            byte[] mapData,
            int width,
            int height,
            byte[] objData,
            byte[] paletteData,
            byte[] configData,
            TilesetFingerprint fingerprint)
        {
            MapSettingAddr = mapSettingAddr;
            MapDataAddr = mapDataAddr;
            MapData = mapData;
            Width = width;
            Height = height;
            ObjData = objData;
            PaletteData = paletteData;
            ConfigData = configData;
            Fingerprint = fingerprint;
        }

        /// <summary>ROM address of the map-setting struct this snapshot was resolved from.</summary>
        public uint MapSettingAddr { get; }

        /// <summary>ROM offset the decompressed <see cref="MapData"/> came from.</summary>
        public uint MapDataAddr { get; }

        /// <summary>
        /// Decompressed main-map buffer: 2-byte (width, height) header followed by row-major
        /// u16 MAR values, matching <see cref="MapEditorTilesetCore.TryReadMar"/>'s format.
        /// </summary>
        public byte[] MapData { get; }

        /// <summary>Map width in 16x16 tiles (from the decompressed header).</summary>
        public int Width { get; }

        /// <summary>Map height in 16x16 tiles (from the decompressed header).</summary>
        public int Height { get; }

        /// <summary>Decompressed OBJ tile graphics (FE7's secondary tileset, when present, appended).</summary>
        public byte[] ObjData { get; }

        /// <summary>Raw (not decompressed — palettes are never LZ77-compressed) palette bytes.</summary>
        public byte[] PaletteData { get; }

        /// <summary>Decompressed chipset config (TSA sub-tile table + terrain table).</summary>
        public byte[] ConfigData { get; }

        /// <summary>Stable identity of this snapshot's tileset. See <see cref="TilesetFingerprint"/>.</summary>
        public TilesetFingerprint Fingerprint { get; }
    }

    /// <summary>
    /// Pure Core seam that resolves a map-setting entry's OBJ/PAL/CFG/MAP PLIST pointers and
    /// decompresses each buffer. Ported from the read-only resolution logic in
    /// <c>FEBuilderGBA.Avalonia.ViewModels.MapEditorViewModel.LoadMapImage</c> /
    /// <c>MapStyleEditorViewModel</c> so callers that need this data outside the Avalonia
    /// ViewModel layer (this generator, and future callers) do not have to duplicate the
    /// PLIST-table math or take a WinForms/Avalonia dependency.
    /// </summary>
    public static class BuiltInRandomMapTilesetCore
    {
        /// <summary>
        /// Resolve and decompress the OBJ (+ FE7 secondary OBJ), PAL, CFG, and MAP data for
        /// the map-setting entry at <paramref name="mapSettingAddr"/>. Never mutates
        /// <paramref name="rom"/>. Returns false with a human-readable <paramref name="error"/>
        /// on any unresolved PLIST, out-of-range read, or failed LZ77 decompression.
        /// </summary>
        public static bool TryResolveMapTileset(
            ROM rom,
            uint mapSettingAddr,
            out MapTilesetSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = "";

            if (rom?.RomInfo == null || rom.Data == null)
            {
                error = "ROM is not loaded.";
                return false;
            }
            if ((ulong)mapSettingAddr + 9 > (ulong)rom.Data.Length)
            {
                error = "Map setting address is out of range.";
                return false;
            }

            uint objPlistWord = rom.u16(mapSettingAddr + 4);
            uint objPlist = objPlistWord & 0xFF;
            uint obj2Plist = (objPlistWord >> 8) & 0xFF;
            uint palettePlist = rom.u8(mapSettingAddr + 6);
            uint configPlist = rom.u8(mapSettingAddr + 7);
            uint mapPointerPlist = rom.u8(mapSettingAddr + 8);

            uint objOffset = ResolvePlist(rom, rom.RomInfo.map_obj_pointer, objPlist);
            uint palOffset = ResolvePlist(rom, rom.RomInfo.map_pal_pointer, palettePlist);
            uint configOffset = ResolvePlist(rom, rom.RomInfo.map_config_pointer, configPlist);
            uint mapOffset = ResolvePlist(rom, rom.RomInfo.map_map_pointer_pointer, mapPointerPlist);

            if (objOffset == U.NOT_FOUND || palOffset == U.NOT_FOUND
                || configOffset == U.NOT_FOUND || mapOffset == U.NOT_FOUND)
            {
                error = "Could not resolve one or more OBJ/PAL/CFG/MAP PLIST pointers.";
                return false;
            }

            if (!TryDecompressCompleteLz77(
                rom,
                objOffset,
                "Primary OBJ",
                out byte[] objData,
                out error))
            {
                return false;
            }

            // FE7's obj_plist high byte selects a secondary tileset that is appended after
            // the primary OBJ bytes, matching MapEditorViewModel.LoadMapImage's combine step.
            if (obj2Plist > 0)
            {
                uint obj2Offset = ResolvePlist(rom, rom.RomInfo.map_obj_pointer, obj2Plist);
                if (obj2Offset == U.NOT_FOUND)
                {
                    error = $"Secondary OBJ PLIST {obj2Plist} could not be resolved.";
                    return false;
                }
                if (!TryDecompressCompleteLz77(
                    rom,
                    obj2Offset,
                    "Secondary OBJ",
                    out byte[] obj2Data,
                    out error))
                {
                    return false;
                }

                byte[] combined = new byte[objData.Length + obj2Data.Length];
                Array.Copy(objData, 0, combined, 0, objData.Length);
                Array.Copy(obj2Data, 0, combined, objData.Length, obj2Data.Length);
                objData = combined;
            }

            const int paletteBytes = 2 * 16 * 16; // 16 palettes * 16 colors * 2 bytes
            if (palOffset >= (uint)rom.Data.Length || (uint)rom.Data.Length - palOffset < paletteBytes)
            {
                error = "Palette data does not contain the required 512-byte snapshot.";
                return false;
            }
            byte[] paletteData = new byte[paletteBytes];
            Array.Copy(rom.Data, palOffset, paletteData, 0, paletteBytes);

            if (!TryDecompressCompleteLz77(
                rom,
                configOffset,
                "Config",
                out byte[] configData,
                out error))
            {
                return false;
            }

            if (!TryDecompressCompleteLz77(
                rom,
                mapOffset,
                "MAP",
                out byte[] mapData,
                out error))
            {
                return false;
            }

            int width = mapData[0];
            int height = mapData[1];
            if (width <= 0 || height <= 0 || mapData.Length < 2 + width * height * 2)
            {
                error = "Decompressed map data is too small for its declared dimensions.";
                return false;
            }

            TilesetFingerprint fingerprint = TilesetFingerprint.Compute(rom.RomInfo.version, objData, paletteData, configData);

            snapshot = new MapTilesetSnapshot(
                mapSettingAddr, mapOffset, mapData, width, height,
                objData, paletteData, configData, fingerprint);
            return true;
        }

        static bool TryDecompressCompleteLz77(
            ROM rom,
            uint dataOffset,
            string dataName,
            out byte[] data,
            out string error)
        {
            data = Array.Empty<byte>();
            error = "";

            if (LZ77.getCompressedSizeStrict(rom.Data, dataOffset) == 0)
            {
                error = $"{dataName} data is not a complete valid LZ77 stream.";
                return false;
            }

            data = LZ77.decompress(rom.Data, dataOffset);
            return true;
        }

        /// <summary>
        /// True when <paramref name="marValue"/> both (a) falls in the semantic chipset index
        /// range and (b) has an in-bounds TSA sub-tile block in <paramref name="configData"/>
        /// — i.e. it is safe to render through <see cref="MapEditorTilesetCore.RenderChipsetIntoBuffer"/>
        /// for the given tileset. Deliberately does NOT require an in-bounds terrain-table
        /// entry: terrain only affects gameplay movement/combat rules, and this generator's
        /// documented scope is visual coherence only, never gameplay/objective validity.
        /// </summary>
        public static bool IsMarRenderable(ushort marValue, byte[] configData)
        {
            if ((marValue & 0x3) != 0) return false;
            int chipsetIndex = MapEditorTilesetCore.MarToChipsetIndex(marValue);
            if (chipsetIndex < 0 || chipsetIndex >= MapEditorTilesetCore.CHIPSET_COUNT) return false;
            if (configData == null) return false;
            int tsaBase = marValue << 1;
            return tsaBase + 7 < configData.Length;
        }

        /// <summary>
        /// Resolve a PLIST index to a decompressed-buffer ROM offset via
        /// <c>tableBase + plist*4</c> (each table entry is a 4-byte GBA pointer). PLIST 0 is
        /// the FEBuilder "none" sentinel and always resolves to <see cref="U.NOT_FOUND"/>.
        /// </summary>
        static uint ResolvePlist(ROM rom, uint basePointer, uint plist)
        {
            if (basePointer == 0 || plist == 0) return U.NOT_FOUND;
            if (basePointer >= (uint)rom.Data.Length || (uint)rom.Data.Length - basePointer < 4)
                return U.NOT_FOUND;

            uint tableBase = rom.p32(basePointer);
            if (!U.isSafetyOffset(tableBase, rom)) return U.NOT_FOUND;

            ulong entry = tableBase + (ulong)plist * 4;
            if (entry + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;

            uint dataPtr = rom.p32((uint)entry);
            if (!U.isSafetyOffset(dataPtr, rom)) return U.NOT_FOUND;

            return dataPtr;
        }
    }
}
