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
    }
}
