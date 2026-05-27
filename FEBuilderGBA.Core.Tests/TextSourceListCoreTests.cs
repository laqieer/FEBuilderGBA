// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for TextSourceListCore — specifically the WU3 extension for
// non-multibyte ROM terrain-name iteration added for issue #671.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class TextSourceListCoreTests
    {
        // -------------------------------------------------------------
        // Map terrain names — non-multibyte (FE7U/FE8U) path. WF
        // MapTerrainNameEngForm.Init iterates 2-byte text-id entries
        // and stops only when the u16 is 0x0000. 0xFFFF is a valid
        // entry, NOT a terminator.
        // -------------------------------------------------------------

        [Fact]
        public void MakeMapTerrainNameList_NonMultibyte_ReturnsEntries()
        {
            var rom = MakeFe8uRom();
            uint terrainTableBase = 0x00A00000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_terrain_name_pointer, terrainTableBase | 0x08000000u);

            // Plant 5 text-ids (2 bytes each), then a zero terminator.
            ushort[] textIds = { 0x1000, 0x1001, 0x1002, 0x1003, 0x1004 };
            for (int i = 0; i < textIds.Length; i++)
                WriteU16(rom.Data, (int)(terrainTableBase + i * 2u), textIds[i]);
            WriteU16(rom.Data, (int)(terrainTableBase + textIds.Length * 2u), 0x0000);

            var list = TextSourceListCore.MakeMapTerrainNameList(rom);
            Assert.Equal(textIds.Length, list.Count);
            // Each entry sits at stride 2 starting at the table base.
            for (int i = 0; i < textIds.Length; i++)
                Assert.Equal(terrainTableBase + (uint)(i * 2), list[i].addr);
        }

        /// <summary>
        /// 0xFFFF is a valid 2-byte text-id and must NOT terminate
        /// iteration (Copilot v6 review item 2: the WF Eng form stops
        /// only on u16 == 0).
        /// </summary>
        [Fact]
        public void MakeMapTerrainNameList_NonMultibyte_StopsOnlyOnZero()
        {
            var rom = MakeFe8uRom();
            uint terrainTableBase = 0x00A00000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_terrain_name_pointer, terrainTableBase | 0x08000000u);

            // Layout: 0x1000, 0xFFFF, 0x1001, 0x0000 (terminator).
            WriteU16(rom.Data, (int)(terrainTableBase + 0u), 0x1000);
            WriteU16(rom.Data, (int)(terrainTableBase + 2u), 0xFFFF);
            WriteU16(rom.Data, (int)(terrainTableBase + 4u), 0x1001);
            WriteU16(rom.Data, (int)(terrainTableBase + 6u), 0x0000);

            var list = TextSourceListCore.MakeMapTerrainNameList(rom);
            Assert.Equal(3, list.Count);
            // Final entry's address is the 0x1001 slot at offset +4.
            Assert.Equal(terrainTableBase + 4u, list[2].addr);
        }

        /// <summary>
        /// Multibyte path is unchanged by WU3 — confirms the FE6/7J/8J
        /// branch still iterates 4-byte pointer entries.
        /// </summary>
        [Fact]
        public void MakeMapTerrainNameList_Multibyte_StillIterates4ByteEntries()
        {
            var rom = MakeFe7jRom();
            Assert.True(rom.RomInfo.is_multibyte);
            uint terrainTableBase = 0x00A00000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_terrain_name_pointer, terrainTableBase | 0x08000000u);

            // 3 valid pointers, then a non-pointer terminator.
            WriteU32(rom.Data, (int)(terrainTableBase + 0u), 0x08200000u);
            WriteU32(rom.Data, (int)(terrainTableBase + 4u), 0x08200010u);
            WriteU32(rom.Data, (int)(terrainTableBase + 8u), 0x08200020u);
            WriteU32(rom.Data, (int)(terrainTableBase + 12u), 0x12345678u);

            var list = TextSourceListCore.MakeMapTerrainNameList(rom);
            Assert.Equal(3, list.Count);
            // Stride is 4 bytes on the multibyte path.
            Assert.Equal(terrainTableBase + 0u, list[0].addr);
            Assert.Equal(terrainTableBase + 4u, list[1].addr);
            Assert.Equal(terrainTableBase + 8u, list[2].addr);
        }

        // -------------------------------------------------------------
        // Helpers.
        // -------------------------------------------------------------

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeFe7jRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe7j.gba", new byte[0x1000000], "AE7J01");
            return rom;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
