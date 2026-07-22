// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Builds fully synthetic (non-copyrighted, zero-filled base) ROMs with one or more
    /// resolvable map-setting entries, for <c>BuiltInRandomMap*Core</c> tests. Every table
    /// and blob is placed at a fixed offset far away from the real ROMFE8U default pointer
    /// targets (all below 0x40000) so nothing collides with <see cref="ROM.RomInfo"/>'s own
    /// resolved addresses.
    /// </summary>
    static class BuiltInRandomMapTestFixture
    {
        const uint MapSettingTableAddr = 0x00600000;
        const uint ObjTableAddr = 0x00610000;
        const uint PalTableAddr = 0x00611000;
        const uint ConfigTableAddr = 0x00612000;
        const uint MapTableAddr = 0x00613000;
        const uint BlobRegionStart = 0x00620000;
        const uint BlobStride = 0x00004000; // 16KB per map's private blob slot

        public static ROM CreateRom(string gameCode = "BE8E01")
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], gameCode);

            WriteU32(rom, rom.RomInfo.map_setting_pointer, 0x08000000 + MapSettingTableAddr);
            WriteU32(rom, rom.RomInfo.map_obj_pointer, 0x08000000 + ObjTableAddr);
            WriteU32(rom, rom.RomInfo.map_pal_pointer, 0x08000000 + PalTableAddr);
            WriteU32(rom, rom.RomInfo.map_config_pointer, 0x08000000 + ConfigTableAddr);
            WriteU32(rom, rom.RomInfo.map_map_pointer_pointer, 0x08000000 + MapTableAddr);
            return rom;
        }

        /// <summary>
        /// Write one map-setting entry (row <paramref name="mapIndex"/>, 0-based) whose OBJ/PAL/CFG
        /// share PLIST slot <paramref name="tilesetSlot"/> (so maps using the same slot are
        /// guaranteed fingerprint-identical) but has its own private MAP blob built from
        /// <paramref name="mars"/>. Returns the map-setting entry's ROM address.
        /// </summary>
        public static uint WriteMap(
            ROM rom,
            int mapIndex,
            int tilesetSlot,
            byte[] objRaw,
            byte[] palRaw,
            byte[] configRaw,
            int width,
            int height,
            ushort[] mars,
            int? secondaryObjSlot = null,
            byte[] secondaryObjRaw = null)
        {
            uint dataSize = rom.RomInfo.map_setting_datasize;
            uint entryAddr = MapSettingTableAddr + (uint)mapIndex * dataSize;

            // Shared tileset blobs: only populate the PLIST slot the first time it's used
            // for this ROM instance so repeated calls with the same tilesetSlot legitimately
            // point at byte-identical data (this is what makes two maps fingerprint-equal).
            EnsureBlob(rom, ObjTableAddr, tilesetSlot, () => LZ77.compress(objRaw), (uint)tilesetSlot * BlobStride);
            EnsureBlob(rom, PalTableAddr, tilesetSlot, () => palRaw, (uint)tilesetSlot * BlobStride + 0x1000);
            EnsureBlob(rom, ConfigTableAddr, tilesetSlot, () => LZ77.compress(configRaw), (uint)tilesetSlot * BlobStride + 0x2000);

            uint mapBlobAddr = BlobRegionStart + (uint)mapIndex * BlobStride;
            byte[] mapRaw = BuildMapBuffer(width, height, mars);
            byte[] mapCompressed = LZ77.compress(mapRaw);
            WriteBlob(rom, mapCompressed, mapBlobAddr);
            WriteTableEntry(rom, MapTableAddr, mapIndex + 1, 0x08000000 + mapBlobAddr);

            WriteU32(rom, entryAddr + 0, 0x08000001); // dummy valid pointer: bypasses further IsMapSettingValid checks
            ushort objPlistWord = (ushort)(tilesetSlot & 0xFF);
            if (secondaryObjSlot.HasValue)
            {
                EnsureBlob(rom, ObjTableAddr, secondaryObjSlot.Value, () => LZ77.compress(secondaryObjRaw), (uint)secondaryObjSlot.Value * BlobStride);
                objPlistWord |= (ushort)((secondaryObjSlot.Value & 0xFF) << 8);
            }
            WriteU16(rom, entryAddr + 4, objPlistWord);
            rom.Data[entryAddr + 6] = (byte)(tilesetSlot & 0xFF);
            rom.Data[entryAddr + 7] = (byte)(tilesetSlot & 0xFF);
            rom.Data[entryAddr + 8] = (byte)((mapIndex + 1) & 0xFF);

            return entryAddr;
        }

        static void EnsureBlob(ROM rom, uint tableAddr, int slot, Func<byte[]> makeBlob, uint blobOffset)
        {
            byte[] blob = makeBlob();
            WriteBlob(rom, blob, BlobRegionStart + 0x00100000 + blobOffset);
            WriteTableEntry(rom, tableAddr, slot, 0x08000000 + BlobRegionStart + 0x00100000 + blobOffset);
        }

        static void WriteBlob(ROM rom, byte[] blob, uint addr) =>
            Array.Copy(blob, 0, rom.Data, addr, blob.Length);

        static void WriteTableEntry(ROM rom, uint tableAddr, int slot, uint gbaPointerValue) =>
            WriteU32(rom, tableAddr + (uint)slot * 4, gbaPointerValue);

        public static byte[] BuildMapBuffer(int width, int height, ushort[] mars)
        {
            byte[] buffer = new byte[2 + width * height * 2];
            buffer[0] = (byte)width;
            buffer[1] = (byte)height;
            for (int i = 0; i < width * height; i++)
            {
                buffer[2 + i * 2] = (byte)(mars[i] & 0xFF);
                buffer[2 + i * 2 + 1] = (byte)((mars[i] >> 8) & 0xFF);
            }
            return buffer;
        }

        public static void WriteU32(ROM rom, uint addr, uint value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static void WriteU16(ROM rom, uint addr, ushort value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
