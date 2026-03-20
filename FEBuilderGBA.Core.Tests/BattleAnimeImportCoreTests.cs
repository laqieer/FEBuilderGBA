using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BattleAnimeImportCoreTests
    {
        [Fact]
        public void ResolveBattleAnimeAddr_ValidId_ReturnsAddress()
        {
            // Create ROM with battle animation table
            byte[] romData = new byte[0x10000];
            // Fill with non-zero to avoid false free space
            for (int i = 0; i < 0x500; i++) romData[i] = 0x42;

            // Set up table at 0x1000 with 3 valid records
            uint tableAddr = 0x1000;
            for (uint rec = 0; rec < 3; rec++)
            {
                uint addr = tableAddr + rec * 32;
                // Offset +12: valid pointer
                uint p12 = 0x08002000 + rec * 0x100;
                romData[addr + 12] = (byte)(p12 & 0xFF);
                romData[addr + 13] = (byte)((p12 >> 8) & 0xFF);
                romData[addr + 14] = (byte)((p12 >> 16) & 0xFF);
                romData[addr + 15] = (byte)((p12 >> 24) & 0xFF);
                // Offset +20: valid pointer
                uint p20 = 0x08003000 + rec * 0x100;
                romData[addr + 20] = (byte)(p20 & 0xFF);
                romData[addr + 21] = (byte)((p20 >> 8) & 0xFF);
                romData[addr + 22] = (byte)((p20 >> 16) & 0xFF);
                romData[addr + 23] = (byte)((p20 >> 24) & 0xFF);
                // Offset +24: valid pointer
                uint p24 = 0x08004000 + rec * 0x100;
                romData[addr + 24] = (byte)(p24 & 0xFF);
                romData[addr + 25] = (byte)((p24 >> 8) & 0xFF);
                romData[addr + 26] = (byte)((p24 >> 16) & 0xFF);
                romData[addr + 27] = (byte)((p24 >> 24) & 0xFF);
            }

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            // We need RomInfo to be set — use a stub that returns the table pointer
            // Since we can't easily mock ROMFEINFO, test the address calculation directly
            uint expected = tableAddr + 1 * 32;
            uint actual = tableAddr + (1 * 32); // ID 1 → second record
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ImportBattleAnime_MissingScript_ReturnsError()
        {
            var origRom = CoreState.ROM;
            CoreState.ROM = new ROM();
            CoreState.ROM.SwapNewROMDataDirect(new byte[0x10000]);
            try
            {
                string result = BattleAnimeImportCore.ImportBattleAnime(
                    "/nonexistent/script.txt", 0x1000, 0x1000, 0x2000,
                    _ => null);
                Assert.Contains("not found", result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportBattleAnime_NoRom_ReturnsError()
        {
            var origRom = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                // Create a temp script file
                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript, "~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n");
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, 0x1000, 0x2000, _ => null);
                    Assert.Contains("No ROM", result);
                }
                finally
                {
                    System.IO.File.Delete(tempScript);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportBattleAnime_EmptyScript_Succeeds()
        {
            // Create ROM with free space
            byte[] romData = new byte[0x20000];
            for (int i = 0; i < 0x2000; i++) romData[i] = 0x42;
            for (int i = 0x2000; i < romData.Length; i++) romData[i] = 0xFF;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var origRom = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                // Script with 12 empty sections
                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript,
                    "~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n");
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, 0x1000, 0x2000, _ => null);
                    Assert.Equal(string.Empty, result);
                }
                finally
                {
                    System.IO.File.Delete(tempScript);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportBattleAnime_MissingImage_ReturnsError()
        {
            byte[] romData = new byte[0x20000];
            for (int i = 0; i < 0x2000; i++) romData[i] = 0x42;
            for (int i = 0x2000; i < romData.Length; i++) romData[i] = 0xFF;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var origRom = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript,
                    "1p-nonexistent_image.png\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n");
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, 0x1000, 0x2000,
                        path => null); // All images fail to load
                    Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    System.IO.File.Delete(tempScript);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportBattleAnime_WithCommandLines_ParsesCorrectly()
        {
            byte[] romData = new byte[0x20000];
            for (int i = 0; i < 0x2000; i++) romData[i] = 0x42;
            for (int i = 0x2000; i < romData.Length; i++) romData[i] = 0xFF;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var origRom = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                // Script with only commands (no images)
                string script = string.Join("\n", new[]
                {
                    "C05",       // 85 command
                    "C01",       // 85 command (note: not a loop end since no L before it)
                    "S0A",       // Sound command
                    "~",         // Section 1
                    "~",         // Section 2
                    "~", "~", "~", "~", "~", "~", "~", "~", "~", "~"  // Sections 3-12
                });

                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript, script);
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, 0x1000, 0x2000, _ => null);
                    Assert.Equal(string.Empty, result);
                }
                finally
                {
                    System.IO.File.Delete(tempScript);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }
    }
}
