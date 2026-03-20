using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BattleAnimeExportCoreTests
    {
        [Fact]
        public void ExportBattleAnime_NullRom_ReturnsError()
        {
            string result = BattleAnimeExportCore.ExportBattleAnime(null, 0x1000, "/tmp/test.txt");
            Assert.Contains("No ROM", result);
        }

        [Fact]
        public void ExportBattleAnime_InvalidAddress_ReturnsError()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x1000]);
            string result = BattleAnimeExportCore.ExportBattleAnime(rom, 0xFFFF, "/tmp/test.txt");
            Assert.Contains("Invalid", result);
        }

        [Fact]
        public void ExportBattleAnime_InvalidPointers_ReturnsError()
        {
            byte[] romData = new byte[0x10000];
            // Record at 0x1000 with zero pointers (invalid)
            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);
            string result = BattleAnimeExportCore.ExportBattleAnime(rom, 0x1000, "/tmp/test.txt");
            Assert.Contains("invalid pointer", result, StringComparison.OrdinalIgnoreCase);
        }
    }
}
