// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for SkillSystemPatchScanner (#416 gap-sweep).
//
// Each test plants the WF byte signatures into a synthetic ROM and asserts
// the scanner resolves to the expected post-pattern u32 location.
using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class SkillSystemPatchScannerTests
    {
        const uint PlantedClassBase = 0x80000;
        const uint PlantedLevelUpBase = 0x90000;

        static readonly byte[] AssignSig1 = new byte[]
        {
            0x01,0x35,0x02,0x36,0xF1,0xE7,0x00,0x20,
            0x28,0x70,0x29,0x1C,0x02,0x48,0x09,0x1A,
        };

        static readonly byte[] LevelUpSig2 = new byte[]
        {
            0x0A,0xD0,0x1A,0x78,0x00,0x2A,0x07,0xD0,
            0x8A,0x42,0x01,0xD0,0x02,0x33,0xF8,0xE7,
            0x5A,0x78,0x22,0x70,0x01,0x34,0xF9,0xE7,
            0x00,0x20,0x20,0x70,0x31,0xBC,0x70,0x47,
        };

        static readonly byte[] ClassSkillExtendsSig = new byte[]
        {
            0xF0,0xE7,0x02,0x2B,0x12,0xD0,0x03,0x2B,
            0x06,0xD1,0x0D,0x48,0x42,0x21,0x41,0x5C,
            0x20,0x22,0x11,0x42,0x0A,0xD1,0xE5,0xE7,
            0x04,0x2B,0x06,0xD1,0x08,0x48,0x14,0x21,
            0x41,0x5C,0x40,0x22,0x11,0x42,0x01,0xD1,
            0xDC,0xE7,0xDB,0xE7,0x63,0x78,0x33,0x70,
            0x01,0x36,0xD7,0xE7,0x00,0x20,0x30,0x70,
            0x06,0xBC,0xF1,0xBC,0x70,0x47,0x00,0x00,
            0xF0,0xBC,0x02,0x02,
        };

        static ROM MakeRom(int size = 0x1000000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            rom.LoadLow("synth-skillsystem.gba", data, "BE8E01");
            return rom;
        }

        static void WriteBytes(ROM rom, uint addr, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                rom.write_u8(addr + (uint)i, bytes[i]);
        }

        static void WriteU32(ROM rom, uint addr, uint value)
        {
            rom.write_u8(addr + 0, (byte)(value & 0xFF));
            rom.write_u8(addr + 1, (byte)((value >> 8) & 0xFF));
            rom.write_u8(addr + 2, (byte)((value >> 16) & 0xFF));
            rom.write_u8(addr + 3, (byte)((value >> 24) & 0xFF));
        }

        [Fact]
        public void FindAssignClassSkillPointerLocation_ResolvesPlantedSignature()
        {
            ROM rom = MakeRom();
            const uint patternPos = 0xB10000;
            WriteBytes(rom, patternPos, AssignSig1);
            uint expectedLocation = patternPos + (uint)AssignSig1.Length + 16 + 4;
            WriteU32(rom, expectedLocation, PlantedClassBase | 0x08000000u);
            rom.write_u8(PlantedClassBase, 0x42);

            uint resolved = SkillSystemPatchScanner.FindAssignClassSkillPointerLocation(rom);
            Assert.Equal(expectedLocation, resolved);
            Assert.Equal(PlantedClassBase, U.toOffset(rom.u32(resolved)));
        }

        [Fact]
        public void FindAssignClassLevelUpSkillPointerLocation_ResolvesPlantedSignatureAndPostPointerValidation()
        {
            ROM rom = MakeRom();
            const uint patternPos = 0xB20000;
            WriteBytes(rom, patternPos, AssignSig1);
            uint expectedLocation = patternPos + (uint)AssignSig1.Length + 16 + 8;
            WriteU32(rom, expectedLocation, PlantedLevelUpBase | 0x08000000u);
            WriteU32(rom, PlantedLevelUpBase + 0, 0u);
            WriteU32(rom, PlantedLevelUpBase + 4, 0u);
            WriteU32(rom, PlantedLevelUpBase + 8, 0u);

            uint resolved = SkillSystemPatchScanner.FindAssignClassLevelUpSkillPointerLocation(rom);
            Assert.Equal(expectedLocation, resolved);
            Assert.Equal(PlantedLevelUpBase, U.toOffset(rom.u32(resolved)));
        }

        [Fact]
        public void FindAssignClassLevelUpSkillPointerLocation_RejectsInvalidPostPointerEntries()
        {
            ROM rom = MakeRom();
            const uint patternPos = 0xB30000;
            WriteBytes(rom, patternPos, LevelUpSig2);
            uint expectedLocation = patternPos + (uint)LevelUpSig2.Length + 0;
            WriteU32(rom, expectedLocation, PlantedLevelUpBase | 0x08000000u);
            WriteU32(rom, PlantedLevelUpBase + 0, 0u);
            WriteU32(rom, PlantedLevelUpBase + 4, 0xDEADBEEFu);
            WriteU32(rom, PlantedLevelUpBase + 8, 0u);

            uint resolved = SkillSystemPatchScanner.FindAssignClassLevelUpSkillPointerLocation(rom);
            Assert.Equal(U.NOT_FOUND, resolved);
        }

        [Fact]
        public void IsClassSkillExtends_TrueWhenSignaturePlanted()
        {
            ROM rom = MakeRom();
            const uint patternPos = 0xB40000;
            WriteBytes(rom, patternPos, ClassSkillExtendsSig);
            Assert.True(SkillSystemPatchScanner.IsClassSkillExtends(rom));
        }

        [Fact]
        public void IsClassSkillExtends_FalseOnVanillaRom()
        {
            ROM rom = MakeRom();
            Assert.False(SkillSystemPatchScanner.IsClassSkillExtends(rom));
        }

        [Fact]
        public void IsClassSkillExtends_FalseOnNullRom()
        {
            Assert.False(SkillSystemPatchScanner.IsClassSkillExtends(null));
        }

        [Fact]
        public void FindAssignClassSkillPointerLocation_NotFoundOnEmptyRom()
        {
            ROM rom = MakeRom();
            Assert.Equal(U.NOT_FOUND, SkillSystemPatchScanner.FindAssignClassSkillPointerLocation(rom));
        }

        [Fact]
        public void FindAssignClassSkillPointerLocation_HandlesNullRomGracefully()
        {
            Assert.Equal(U.NOT_FOUND, SkillSystemPatchScanner.FindAssignClassSkillPointerLocation(null));
        }

        [Fact]
        public void FindAssignClassLevelUpSkillPointerLocation_HandlesNullRomGracefully()
        {
            Assert.Equal(U.NOT_FOUND, SkillSystemPatchScanner.FindAssignClassLevelUpSkillPointerLocation(null));
        }
    }
}
