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
        // ------------------------------------------------------------------ FEditor .bin import tests

        [Fact]
        public void ImportFEditorBin_MissingFile_ReturnsError()
        {
            var origRom = CoreState.ROM;
            CoreState.ROM = new ROM();
            CoreState.ROM.SwapNewROMDataDirect(new byte[0x10000]);
            try
            {
                string result = BattleAnimeImportCore.ImportFEditorBin(
                    "/nonexistent/anim.bin", 0x1000, _ => null);
                Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportFEditorBin_NoRom_ReturnsError()
        {
            var origRom = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                string tempFile = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"febuilder_test_{Guid.NewGuid():N}.bin");
                System.IO.File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x01, 0x02 });
                try
                {
                    string result = BattleAnimeImportCore.ImportFEditorBin(
                        tempFile, 0x1000, _ => null);
                    Assert.Contains("No ROM", result);
                }
                finally
                {
                    System.IO.File.Delete(tempFile);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportFEditorBin_InvalidHeader_ReturnsError()
        {
            byte[] romData = new byte[0x20000];
            for (int i = 0x2000; i < romData.Length; i++) romData[i] = 0xFF;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var origRom = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                string tempFile = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"febuilder_test_{Guid.NewGuid():N}.bin");
                // Write garbage data (no valid FEditor header)
                System.IO.File.WriteAllBytes(tempFile, new byte[100]);
                try
                {
                    string result = BattleAnimeImportCore.ImportFEditorBin(
                        tempFile, 0x1000, _ => null);
                    Assert.Contains("header", result, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    System.IO.File.Delete(tempFile);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }
        // ------------------------------------------------------------------ UpdateFrameDataAddresses tests

        [Fact]
        public void UpdateFrameDataAddresses_SingleSheet_MapsAllFramesToSheet()
        {
            // Build frame data: two 0x86 commands referencing the same graphics pointer
            byte[] frameData = new byte[24];
            // Frame 0: 0x86 command with gfxPtr = 0xDEAD0000
            frameData[3] = 0x86;
            frameData[4] = 0x00; frameData[5] = 0x00; frameData[6] = 0xAD; frameData[7] = 0xDE;
            // Frame 1: 0x86 command with same gfxPtr
            frameData[15] = 0x86;
            frameData[16] = 0x00; frameData[17] = 0x00; frameData[18] = 0xAD; frameData[19] = 0xDE;

            var sheetAddrs = new List<uint> { 0x1000 };
            BattleAnimeImportCore.UpdateFrameDataAddresses(frameData, sheetAddrs);

            uint expectedPtr = U.toPointer(0x1000);
            uint frame0Ptr = (uint)(frameData[4] | (frameData[5] << 8) | (frameData[6] << 16) | (frameData[7] << 24));
            uint frame1Ptr = (uint)(frameData[16] | (frameData[17] << 8) | (frameData[18] << 16) | (frameData[19] << 24));

            Assert.Equal(expectedPtr, frame0Ptr);
            Assert.Equal(expectedPtr, frame1Ptr);
        }

        [Fact]
        public void UpdateFrameDataAddresses_MultiSheet_MapsCorrectly()
        {
            // Build frame data: three 0x86 commands referencing two different graphics pointers
            byte[] frameData = new byte[36];
            // Frame 0: gfxPtr = 0xAAAA0000 (sheet 0)
            frameData[3] = 0x86;
            frameData[4] = 0x00; frameData[5] = 0x00; frameData[6] = 0xAA; frameData[7] = 0xAA;
            // Frame 1: gfxPtr = 0xBBBB0000 (sheet 1)
            frameData[15] = 0x86;
            frameData[16] = 0x00; frameData[17] = 0x00; frameData[18] = 0xBB; frameData[19] = 0xBB;
            // Frame 2: gfxPtr = 0xAAAA0000 (back to sheet 0)
            frameData[27] = 0x86;
            frameData[28] = 0x00; frameData[29] = 0x00; frameData[30] = 0xAA; frameData[31] = 0xAA;

            var sheetAddrs = new List<uint> { 0x2000, 0x3000 };
            BattleAnimeImportCore.UpdateFrameDataAddresses(frameData, sheetAddrs);

            uint sheet0Ptr = U.toPointer(0x2000);
            uint sheet1Ptr = U.toPointer(0x3000);

            uint frame0Ptr = (uint)(frameData[4] | (frameData[5] << 8) | (frameData[6] << 16) | (frameData[7] << 24));
            uint frame1Ptr = (uint)(frameData[16] | (frameData[17] << 8) | (frameData[18] << 16) | (frameData[19] << 24));
            uint frame2Ptr = (uint)(frameData[28] | (frameData[29] << 8) | (frameData[30] << 16) | (frameData[31] << 24));

            Assert.Equal(sheet0Ptr, frame0Ptr);  // First unique pointer → sheet 0
            Assert.Equal(sheet1Ptr, frame1Ptr);  // Second unique pointer → sheet 1
            Assert.Equal(sheet0Ptr, frame2Ptr);  // Same as first → sheet 0
        }

        [Fact]
        public void UpdateFrameDataAddresses_EmptyFrameData_DoesNotThrow()
        {
            byte[] frameData = new byte[0];
            var sheetAddrs = new List<uint> { 0x1000 };
            BattleAnimeImportCore.UpdateFrameDataAddresses(frameData, sheetAddrs);
            // Should not throw
        }

        [Fact]
        public void UpdateFrameDataAddresses_SkipsNonFrameCommands()
        {
            // Build frame data: 0x85 command followed by 0x86 command
            byte[] frameData = new byte[16];
            // 0x85 command (4 bytes)
            frameData[3] = 0x85;
            // 0x86 command with gfxPtr = 0x11110000
            frameData[7] = 0x86;
            frameData[8] = 0x00; frameData[9] = 0x00; frameData[10] = 0x11; frameData[11] = 0x11;

            var sheetAddrs = new List<uint> { 0x4000 };
            BattleAnimeImportCore.UpdateFrameDataAddresses(frameData, sheetAddrs);

            uint expectedPtr = U.toPointer(0x4000);
            uint framePtrResult = (uint)(frameData[8] | (frameData[9] << 8) | (frameData[10] << 16) | (frameData[11] << 24));
            Assert.Equal(expectedPtr, framePtrResult);
        }
    }
}
