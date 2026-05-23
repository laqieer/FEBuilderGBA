// SPDX-License-Identifier: GPL-3.0-or-later
// Core round-trip tests for SkillAssignmentClassSkillSystemCore (#416).
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class SkillAssignmentClassSkillSystemCoreTests
    {
        const uint ClassBaseAddr = 0x80000;
        const uint LevelUpPointerLocation = 0x70000;
        const uint LevelUpBaseAddr = 0x90000;
        const uint ClassCount = 4;

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("synth-skillassign.gba", data, "BE8E01");
            return rom;
        }

        static void PlantInitial(ROM rom)
        {
            rom.write_u8(ClassBaseAddr + 0, 0x10);
            rom.write_u8(ClassBaseAddr + 1, 0x11);
            rom.write_u8(ClassBaseAddr + 2, 0x12);
            rom.write_u8(ClassBaseAddr + 3, 0x13);

            rom.write_u32(LevelUpPointerLocation, LevelUpBaseAddr | 0x08000000u);
            rom.write_u32(LevelUpBaseAddr + 0, (LevelUpBaseAddr + 0x100) | 0x08000000u);
            rom.write_u32(LevelUpBaseAddr + 4, (LevelUpBaseAddr + 0x100) | 0x08000000u);
            rom.write_u32(LevelUpBaseAddr + 8, (LevelUpBaseAddr + 0x200) | 0x08000000u);
            rom.write_u32(LevelUpBaseAddr + 12, 0u);

            uint shared = LevelUpBaseAddr + 0x100;
            rom.write_u8(shared + 0, 0x05);
            rom.write_u8(shared + 1, 0x20);
            rom.write_u8(shared + 2, 0x0A);
            rom.write_u8(shared + 3, 0x21);
            rom.write_u16(shared + 4, 0x0000);

            uint indep = LevelUpBaseAddr + 0x200;
            rom.write_u8(indep + 0, 0x15);
            rom.write_u8(indep + 1, 0x30);
            rom.write_u16(indep + 2, 0x0000);
        }

        [Fact]
        public void Export_RoundTrip_PreservesClassSkillAndLevelUpEntries()
        {
            ROM rom = MakeRom();
            PlantInitial(rom);

            string path = Path.GetTempFileName();
            try
            {
                bool exportOk = SkillAssignmentClassSkillSystemCore.ExportAllData(
                    rom, ClassBaseAddr, LevelUpPointerLocation, ClassCount, path);
                Assert.True(exportOk);
                Assert.True(File.Exists(path));

                string[] lines = File.ReadAllLines(path);
                Assert.Equal((int)ClassCount, lines.Length);

                string[] sp0 = lines[0].Split('\t');
                Assert.Equal("10", sp0[0]);
                Assert.True(sp0.Length >= 6);
                Assert.Equal("05", sp0[2]);
                Assert.Equal("20", sp0[3]);
                Assert.Equal("0A", sp0[4]);
                Assert.Equal("21", sp0[5]);

                string[] sp3 = lines[3].Split('\t');
                Assert.Equal("13", sp3[0]);
                Assert.Equal("00", sp3[1]);
                Assert.Equal(2, sp3.Length);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Import_PointerInVanillaROM_WritesLevelSkillBytesInPlace()
        {
            ROM rom = MakeRom();
            PlantInitial(rom);

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "10\t" + (LevelUpBaseAddr + 0x100).ToString("X8") + "\t05\t20\t0A\t21",
                    "11\t" + (LevelUpBaseAddr + 0x100).ToString("X8") + "\t05\t20\t0A\t21",
                    "22\t" + (LevelUpBaseAddr + 0x200).ToString("X8") + "\t18\t33",
                    "13\t0",
                });

                bool importOk = SkillAssignmentClassSkillSystemCore.ImportAllData(
                    rom, ClassBaseAddr, LevelUpPointerLocation, ClassCount, path);
                Assert.True(importOk);

                Assert.Equal((byte)0x22, rom.u8(ClassBaseAddr + 2));

                uint indep = LevelUpBaseAddr + 0x200;
                Assert.Equal((byte)0x18, rom.u8(indep + 0));
                Assert.Equal((byte)0x33, rom.u8(indep + 1));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Import_PointerZero_RewritesPerClassPointerSlot()
        {
            ROM rom = MakeRom();
            PlantInitial(rom);

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "AA\t0",
                    "11\t" + (LevelUpBaseAddr + 0x100).ToString("X8") + "\t05\t20\t0A\t21",
                    "12\t" + (LevelUpBaseAddr + 0x200).ToString("X8") + "\t15\t30",
                    "13\t0",
                });
                bool importOk = SkillAssignmentClassSkillSystemCore.ImportAllData(
                    rom, ClassBaseAddr, LevelUpPointerLocation, ClassCount, path);
                Assert.True(importOk);

                Assert.Equal((byte)0xAA, rom.u8(ClassBaseAddr + 0));
                Assert.Equal(0u, rom.u32(LevelUpBaseAddr + 0));
                Assert.Equal(0u, rom.u32(LevelUpBaseAddr + 12));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Export_NullRom_ReturnsFalse()
        {
            Assert.False(SkillAssignmentClassSkillSystemCore.ExportAllData(
                null, 0, 0, 0, "ignored.tsv"));
        }

        [Fact]
        public void Import_NullRom_ReturnsFalse()
        {
            Assert.False(SkillAssignmentClassSkillSystemCore.ImportAllData(
                null, 0, 0, 0, "ignored.tsv"));
        }

        [Fact]
        public void Import_MissingFile_ReturnsFalse()
        {
            ROM rom = MakeRom();
            PlantInitial(rom);
            string path = Path.Combine(Path.GetTempPath(), "definitely-does-not-exist-" + System.Guid.NewGuid() + ".tsv");
            Assert.False(File.Exists(path));
            Assert.False(SkillAssignmentClassSkillSystemCore.ImportAllData(
                rom, ClassBaseAddr, LevelUpPointerLocation, ClassCount, path));
        }
    }
}
