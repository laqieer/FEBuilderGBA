using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BattleAnimeImportCoreTests
    {
        [Fact]
        public void GetTableBounds_NoRomInfo_ReturnsZero()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x10000]);
            // ROM has no RomInfo set, so the pointer will be 0
            var (baseAddr, endAddr) = BattleAnimeImportCore.GetTableBounds(rom);
            // Without RomInfo, pointer is 0 → returns (0,0)
            Assert.Equal(0u, baseAddr);
        }

        [Fact]
        public void ResolveBattleAnimeAddr_NoRomInfo_ReturnsNotFound()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x10000]);
            uint result = BattleAnimeImportCore.ResolveBattleAnimeAddr(rom, 0);
            Assert.Equal(U.NOT_FOUND, result);
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
                    "/nonexistent/script.txt", 0x1000,
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
                System.IO.File.WriteAllText(tempScript, "~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n");
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, _ => null);
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
                // With isMode1=true: first ~ generates 2 modes, second ~ generates 2 modes,
                // remaining 8 ~ generate 1 mode each = 12 total. So 10 ~ lines needed.
                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript,
                    "~\n~\n~\n~\n~\n~\n~\n~\n~\n~\n");
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, _ => null);
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
                        tempScript, 0x1000,
                        _ => null); // All images fail to load
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
                    "~",         // Modes 0+1 (isMode1=true)
                    "~",         // Modes 2+3 (isMode1=true)
                    "~", "~", "~", "~", "~", "~", "~", "~"  // Modes 4-11 (8 sections)
                });

                string tempScript = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"febuilder_test_{Guid.NewGuid():N}.txt");
                System.IO.File.WriteAllText(tempScript, script);
                try
                {
                    string result = BattleAnimeImportCore.ImportBattleAnime(
                        tempScript, 0x1000, _ => null);
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
