using Xunit;
using FEBuilderGBA;
using System.IO;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchMetadataCoreTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly string? _savedLang;

        public PatchMetadataCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedLang = CoreState.Language;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
        }

        [Fact]
        public void ParseByteArray_SimpleHex_ParsesCorrectly()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("0x10 0x00 0xAB 0xFF");
            Assert.Equal(4, result.Length);
            Assert.Equal(0x10, result[0]);
            Assert.Equal(0x00, result[1]);
            Assert.Equal(0xAB, result[2]);
            Assert.Equal(0xFF, result[3]);
        }

        [Fact]
        public void ParseByteArray_EmptyString_ReturnsEmpty()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseByteArray_StopsAtNonHex()
        {
            byte[] result = PatchMetadataCore.ParseByteArray("0x10 0x20 NOTAHEX 0x30");
            Assert.Equal(2, result.Length);
            Assert.Equal(0x10, result[0]);
            Assert.Equal(0x20, result[1]);
        }

        [Fact]
        public void CleanDescription_ReplacesEscapes()
        {
            string result = PatchMetadataCore.CleanDescription("Line1\\r\\nLine2\\nLine3  ");
            Assert.Equal("Line1\nLine2\nLine3", result);
        }

        [Fact]
        public void CheckPatchInstalled_GrepCondition_ReturnsUnknown()
        {
            // Create a minimal ROM
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("$GREP4 0xAB=0xAB", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, status);
        }

        [Fact]
        public void CheckPatchInstalled_FixedAddr_Installed()
        {
            byte[] data = new byte[0x100];
            data[0x10] = 0xAB;
            data[0x11] = 0xCD;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0x10=0xAB 0xCD", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Installed, status);
        }

        [Fact]
        public void CheckPatchInstalled_FixedAddr_NotInstalled()
        {
            byte[] data = new byte[0x100];
            data[0x10] = 0x00;
            data[0x11] = 0x00;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0x10=0xAB 0xCD", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_AddrBeyondRom_NotInstalled()
        {
            byte[] data = new byte[0x10];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("0xFF=0xAB", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, status);
        }

        [Fact]
        public void CheckPatchInstalled_NoEqualsSign_ReturnsUnknown()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var status = PatchMetadataCore.CheckPatchInstalled("noequalssign", rom);
            Assert.Equal(PatchMetadataCore.PatchStatus.Unknown, status);
        }

        [Fact]
        public void GetLanguageSuffix_English_ReturnsEn()
        {
            CoreState.Language = "en";
            Assert.Equal("en", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Chinese_ReturnsZh()
        {
            CoreState.Language = "zh";
            Assert.Equal("zh", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Japanese_ReturnsEmpty()
        {
            CoreState.Language = "ja";
            Assert.Equal("", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void GetLanguageSuffix_Null_DefaultsToEn()
        {
            CoreState.Language = null;
            Assert.Equal("en", PatchMetadataCore.GetLanguageSuffix());
        }

        [Fact]
        public void EnumeratePatches_NonexistentDir_ReturnsEmpty()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var result = PatchMetadataCore.EnumeratePatches("/nonexistent/path", rom, "en");
            Assert.Empty(result);
        }

        [Fact]
        public void ParsePatchFile_WithMetadata_ExtractsFields()
        {
            // Create a temp patch file
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMetadataCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=TestPatchJP",
                    "NAME.en=Test Patch English",
                    "TYPE=BIN",
                    "TAG=#ENGINE #TEST",
                    "AUTHOR=TestAuthor",
                    "INFO=Japanese description\\r\\nLine2",
                    "INFO.en=English description\\r\\nLine2",
                    "PATCHED_IF:0x10=0xAB 0xCD",
                });

                byte[] data = new byte[0x100];
                data[0x10] = 0xAB;
                data[0x11] = 0xCD;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");

                Assert.Equal("Test Patch English", info.Name);
                Assert.Equal("TestDir", info.DirectoryName);
                Assert.Equal("BIN", info.Type);
                Assert.Equal("#ENGINE #TEST", info.Tags);
                Assert.Equal("TestAuthor", info.Author);
                Assert.Contains("English description", info.Description);
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, info.Status);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_JapaneseLanguage_UsesDefaultName()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMetadataCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=TestPatchJP",
                    "NAME.en=Test Patch English",
                    "INFO=Japanese description",
                    "INFO.en=English description",
                });

                byte[] data = new byte[0x100];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                // With empty lang (Japanese), should use NAME= value
                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "");

                Assert.Equal("TestPatchJP", info.Name);
                Assert.Equal("Japanese description", info.Description);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // ===== ApplyPatch tests =====

        [Fact]
        public void ParsePatchParams_ParsesKeyValuePairs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchParamTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=data.bin",
                    "JUMP:0x200:$r3=data.bin",
                    "// comment line",
                    "PATCHED_IF:0x100=0xAB",
                });

                var parms = PatchMetadataCore.ParsePatchParams(patchFile);
                Assert.Equal(4, parms.Count); // TYPE, BIN, JUMP, PATCHED_IF (comment skipped)
                Assert.Equal("BIN", parms[1].Keyword);
                Assert.Equal("0x100", parms[1].KeyParts[1]);
                Assert.Equal("data.bin", parms[1].Value);
                Assert.Equal("JUMP", parms[2].Keyword);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_NullRom_Fails()
        {
            var result = PatchMetadataCore.ApplyPatch(null, "nonexistent.txt");
            Assert.False(result.Success);
            Assert.Contains("No ROM", result.Message);
        }

        [Fact]
        public void ApplyPatch_NonexistentFile_Fails()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);
            var result = PatchMetadataCore.ApplyPatch(rom, "/nonexistent/PATCH_test.txt");
            Assert.False(result.Success);
            Assert.Contains("not found", result.Message);
        }

        [Fact]
        public void ApplyPatch_EAType_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchEATest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=EA",
                    "EA=Installer.event",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);
                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("EA-type", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_FixedAddress_WritesData()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create binary data file
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(4, result.BytesWritten);

                // Verify data was written
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.Equal(0xBBu, rom.u8(0x201));
                Assert.Equal(0xCCu, rom.u8(0x202));
                Assert.Equal(0xDDu, rom.u8(0x203));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_WithUndo_TracksChanges()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUndoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                byte[] binData = new byte[] { 0x11, 0x22 };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=test.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);
                CoreState.ROM = rom;

                var undo = new Undo();
                var undoData = undo.NewUndoData("test");

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile, undoData);
                Assert.True(result.Success);
                // Undo data should have recorded write positions
                Assert.True(undoData.list.Count > 0);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_MultipleBins_WritesAll()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMultiBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[] { 0xAA });
                File.WriteAllBytes(Path.Combine(tempDir, "b.bin"), new byte[] { 0xBB, 0xCC });

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=a.bin",
                    "BIN:0x200=b.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(3, result.BytesWritten);
                Assert.Equal(0xAAu, rom.u8(0x100));
                Assert.Equal(0xBBu, rom.u8(0x200));
                Assert.Equal(0xCCu, rom.u8(0x201));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_MissingBinFile_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchMissingBinTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x100=nonexistent.bin",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("not found", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_FreeArea_FindsSpace()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchFreeAreaTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                byte[] binData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
                File.WriteAllBytes(Path.Combine(tempDir, "hook.dmp"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:$FREEAREA=hook.dmp",
                });

                // Create ROM with free space (0x00 bytes at end)
                var rom = new ROM();
                byte[] romData = new byte[0x10000]; // 64KB
                // Fill first 0x200 bytes with non-zero to simulate used space
                for (int i = 0; i < 0x200; i++) romData[i] = 0x42;
                rom.SwapNewROMDataDirect(romData);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                Assert.Equal(4, result.BytesWritten);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_JumpWithBin_WritesJumpCode()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchJumpTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Small routine to place in free area
                byte[] binData = new byte[] { 0x00, 0x4B, 0x9F, 0x46 };
                File.WriteAllBytes(Path.Combine(tempDir, "routine.dmp"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x1000=routine.dmp",
                    "JUMP:0x200:$r3=routine.dmp",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x10000]);

                var result = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(result.Success);
                // BIN wrote 4 bytes, JUMP wrote some bytes for jump trampoline
                Assert.True(result.BytesWritten > 4);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UninstallPatch_ReturnsNotSupported()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);
            var result = PatchMetadataCore.UninstallPatch(rom, "anything.txt");
            Assert.False(result.Success);
            Assert.Contains("not yet supported", result.Message);
        }

        [Fact]
        public void FindFreeSpace_FindsSpaceInRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x10000];
            // Fill first 0x200 bytes
            for (int i = 0; i < 0x200; i++) data[i] = 0x42;
            rom.SwapNewROMDataDirect(data);

            uint addr = PatchMetadataCore.FindFreeSpace(rom, 100);
            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.True(addr >= 0x200);
        }

        [Fact]
        public void PatchApplyResult_OkAndFail_WorkCorrectly()
        {
            var ok = PatchMetadataCore.PatchApplyResult.Ok("Success", 42);
            Assert.True(ok.Success);
            Assert.Equal("Success", ok.Message);
            Assert.Equal(42, ok.BytesWritten);

            var fail = PatchMetadataCore.PatchApplyResult.Fail("Error");
            Assert.False(fail.Success);
            Assert.Equal("Error", fail.Message);
        }
    }
}
