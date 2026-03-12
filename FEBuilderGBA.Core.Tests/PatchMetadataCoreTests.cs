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
        public void UninstallPatch_NoBackup_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallNoBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[] { "TYPE=BIN" });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                var result = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("No backup file", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UninstallPatch_NullRom_Fails()
        {
            var result = PatchMetadataCore.UninstallPatch(null, "anything.txt");
            Assert.False(result.Success);
            Assert.Contains("No ROM", result.Message);
        }

        [Fact]
        public void SaveBackup_And_ParseBackupFile_RoundTrips()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBackupRoundTrip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");

                byte[] romData = new byte[0x1000];
                romData[0x100] = 0xAA;
                romData[0x101] = 0xBB;
                romData[0x200] = 0xCC;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                var regions = new List<(uint address, int length)>
                {
                    (0x100, 2),
                    (0x200, 1),
                };

                PatchMetadataCore.SaveBackup(rom, patchFile, regions);

                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                Assert.True(File.Exists(backupPath));

                var records = PatchMetadataCore.ParseBackupFile(backupPath);
                Assert.NotNull(records);
                Assert.Equal(2, records.Count);

                Assert.Equal(0x100u, records[0].address);
                Assert.Equal(new byte[] { 0xAA, 0xBB }, records[0].data);

                Assert.Equal(0x200u, records[1].address);
                Assert.Equal(new byte[] { 0xCC }, records[1].data);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasBackup_ReturnsFalse_WhenNoFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchHasBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");
                Assert.False(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyPatch_CreatesBackup_Then_UninstallRestores()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchInstallUninstall_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Set up ROM with known data at 0x200
                byte[] romData = new byte[0x1000];
                romData[0x200] = 0x11;
                romData[0x201] = 0x22;
                romData[0x202] = 0x33;
                romData[0x203] = 0x44;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                // Create patch that overwrites 0x200 with different data
                byte[] binData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), binData);

                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=BIN",
                    "BIN:0x200=test.bin",
                    "PATCHED_IF:0x200=0xAA 0xBB 0xCC 0xDD",
                });

                // Install patch
                var installResult = PatchMetadataCore.ApplyPatch(rom, patchFile);
                Assert.True(installResult.Success);
                Assert.Equal(0xAAu, rom.u8(0x200));
                Assert.Equal(0xBBu, rom.u8(0x201));
                Assert.Equal(0xCCu, rom.u8(0x202));
                Assert.Equal(0xDDu, rom.u8(0x203));

                // Verify backup was created
                Assert.True(PatchMetadataCore.HasBackup(patchFile));

                // Uninstall patch
                var uninstallResult = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.True(uninstallResult.Success);
                Assert.Contains("restored", uninstallResult.Message);

                // Verify original bytes restored
                Assert.Equal(0x11u, rom.u8(0x200));
                Assert.Equal(0x22u, rom.u8(0x201));
                Assert.Equal(0x33u, rom.u8(0x202));
                Assert.Equal(0x44u, rom.u8(0x203));

                // Verify backup file was deleted
                Assert.False(PatchMetadataCore.HasBackup(patchFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParseBackupFile_MalformedFile_ReturnsNull()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchBadBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string backupPath = Path.Combine(tempDir, ".backup_PATCH_Test.txt");
                File.WriteAllText(backupPath, "not a valid backup");

                var records = PatchMetadataCore.ParseBackupFile(backupPath);
                Assert.Null(records);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParseBackupFile_NonexistentFile_ReturnsNull()
        {
            var records = PatchMetadataCore.ParseBackupFile("/nonexistent/.backup_test.txt");
            Assert.Null(records);
        }

        [Fact]
        public void UninstallPatch_MalformedBackup_Fails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchUninstallBadBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllText(patchFile, "TYPE=BIN");

                // Create a malformed backup file
                string backupPath = PatchMetadataCore.GetBackupFilePath(patchFile);
                File.WriteAllText(backupPath, "garbage data");

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                var result = PatchMetadataCore.UninstallPatch(rom, patchFile);
                Assert.False(result.Success);
                Assert.Contains("malformed", result.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetBackupFilePath_CorrectFormat()
        {
            string dir = Path.Combine(Path.GetTempPath(), "someDir");
            string patchFile = Path.Combine(dir, "PATCH_MyPatch.txt");
            string expected = Path.Combine(dir, ".backup_PATCH_MyPatch.txt");
            Assert.Equal(expected, PatchMetadataCore.GetBackupFilePath(patchFile));
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

        // ===== Dependency checking tests =====

        [Fact]
        public void GetPatchDependencies_NoDeps_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=NoDeps",
                    "TYPE=BIN",
                    "PATCHED_IF:0x100=0xAB",
                });

                var deps = PatchMetadataCore.GetPatchDependencies(patchFile);
                Assert.Empty(deps);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_WithIfLines_ExtractsConditions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=WithDeps",
                    "IF:0x02BA4=0x00 0xB5 0xC2 0x0F //need Anti-Huffman",
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                    "PATCHED_IF:0x200=0xFF",
                });

                var deps = PatchMetadataCore.GetPatchDependencies(patchFile);
                Assert.Equal(2, deps.Count);
                Assert.Equal("0x02BA4=0x00 0xB5 0xC2 0x0F", deps[0].Condition);
                Assert.Equal("need Anti-Huffman", deps[0].Comment); // from inline comment
                Assert.Equal("0x100=0xAB 0xCD", deps[1].Condition);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_WithIfComment_UsesComment()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=WithComment",
                    "IF:0x100=0xAB 0xCD",
                    "IF_COMMENT=Please install Patch X first.",
                    "IF_COMMENT.en=Please install Patch X first (English).",
                    "TYPE=BIN",
                });

                // With English lang
                var deps = PatchMetadataCore.GetPatchDependencies(patchFile, "en");
                Assert.Single(deps);
                Assert.Equal("Please install Patch X first (English).", deps[0].Comment);

                // With empty lang (Japanese)
                var depsJp = PatchMetadataCore.GetPatchDependencies(patchFile, "");
                Assert.Single(depsJp);
                Assert.Equal("Please install Patch X first.", depsJp[0].Comment);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetPatchDependencies_NonexistentFile_ReturnsEmpty()
        {
            var deps = PatchMetadataCore.GetPatchDependencies("/nonexistent/PATCH_test.txt");
            Assert.Empty(deps);
        }

        [Fact]
        public void EvaluateIfCondition_Satisfied_ReturnsTrue()
        {
            byte[] data = new byte[0x1000];
            data[0x100] = 0xAB;
            data[0x101] = 0xCD;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.True(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB 0xCD", rom));
        }

        [Fact]
        public void EvaluateIfCondition_NotSatisfied_ReturnsFalse()
        {
            byte[] data = new byte[0x1000];
            data[0x100] = 0x00;
            data[0x101] = 0x00;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            Assert.False(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB 0xCD", rom));
        }

        [Fact]
        public void EvaluateIfCondition_GrepCondition_ReturnsTrue()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x100]);

            // GREP conditions are treated as satisfied (can't check simply)
            Assert.True(PatchMetadataCore.EvaluateIfCondition("$GREP4 0xAB=0xAB", rom));
            Assert.True(PatchMetadataCore.EvaluateIfCondition("$FGREP4 test.dmp=0xAB", rom));
        }

        [Fact]
        public void EvaluateIfCondition_NullRom_ReturnsFalse()
        {
            Assert.False(PatchMetadataCore.EvaluateIfCondition("0x100=0xAB", null));
        }

        [Fact]
        public void EvaluateIfCondition_AddrBeyondRom_ReturnsFalse()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x10]);

            Assert.False(PatchMetadataCore.EvaluateIfCondition("0xFF=0xAB", rom));
        }

        [Fact]
        public void CheckDependencies_AllMet_ReturnsEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchCheckDeps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                });

                byte[] data = new byte[0x1000];
                data[0x100] = 0xAB;
                data[0x101] = 0xCD;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var missing = PatchMetadataCore.CheckDependencies(rom, patchFile);
                Assert.Empty(missing);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CheckDependencies_SomeUnmet_ReturnsMissing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchCheckDeps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "IF:0x100=0xAB 0xCD",
                    "IF:0x200=0xEE 0xFF",
                    "TYPE=BIN",
                });

                byte[] data = new byte[0x1000];
                data[0x100] = 0xAB;
                data[0x101] = 0xCD;
                // 0x200 is zeroed = second dep not met
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var missing = PatchMetadataCore.CheckDependencies(rom, patchFile);
                Assert.Single(missing);
                Assert.Equal("0x200=0xEE 0xFF", missing[0].Condition);
                Assert.False(missing[0].IsSatisfied);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_PopulatesDependencyFields()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchDepFields_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=DepPatch",
                    "IF:0x100=0xAB 0xCD",
                    "TYPE=BIN",
                    "PATCHED_IF:0x200=0xFF",
                });

                byte[] data = new byte[0x1000];
                // IF condition NOT met (0x100 is zeroed)
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");
                Assert.Equal(1, info.DependencyCount);
                Assert.Equal(1, info.UnsatisfiedDependencyCount);
                Assert.Single(info.UnsatisfiedDependencies);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ParsePatchFile_NoDeps_ZeroCounts()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PatchNoDepFields_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string patchFile = Path.Combine(tempDir, "PATCH_Test.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "NAME=NoDeps",
                    "TYPE=BIN",
                });

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);

                var info = PatchMetadataCore.ParsePatchFile(patchFile, "TestDir", rom, "en");
                Assert.Equal(0, info.DependencyCount);
                Assert.Equal(0, info.UnsatisfiedDependencyCount);
                Assert.Empty(info.UnsatisfiedDependencies);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
