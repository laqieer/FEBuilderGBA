using CliProgram = FEBuilderGBA.CLI.Program;

namespace FEBuilderGBA.Tests.Unit
{
    public sealed class CliBattleAnimeFormatDetectorTests : IDisposable
    {
        private readonly string _workDir;

        public CliBattleAnimeFormatDetectorTests()
        {
            _workDir = Path.Combine(AppContext.BaseDirectory, "cli-format-detector-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workDir, recursive: true); } catch { }
        }

        [Fact]
        public void IsFEditorBattleAnimationBin_EmptyBinFile_ReturnsFalse()
        {
            string path = WriteBytes("empty.bin", Array.Empty<byte>());

            Assert.False(CliProgram.IsFEditorBattleAnimationBin(path));
        }

        [Fact]
        public void IsFEditorBattleAnimationBin_ShortFiles_PreserveFirstTwoByteSemantics()
        {
            for (int length = 1; length <= 7; length++)
            {
                byte[] nonFEditor = Enumerable.Repeat((byte)0x41, length).ToArray();
                Assert.False(CliProgram.IsFEditorBattleAnimationBin(WriteBytes($"plain-{length}.bin", nonFEditor)));

                if (length >= 2)
                {
                    byte[] fEditor = Enumerable.Repeat((byte)0x00, length).ToArray();
                    fEditor[0] = 0x5C;
                    fEditor[1] = 0x78;
                    Assert.True(CliProgram.IsFEditorBattleAnimationBin(WriteBytes($"feditor-{length}.bin", fEditor)));
                }
            }
        }

        [Fact]
        public void IsFEditorBattleAnimationBin_ValidFEditorHeaders_ReturnTrue()
        {
            byte[][] headers =
            {
                new byte[] { 0x5C, 0x78, 0x78, 0x75, 0x72, 0x00, 0x00, 0x00 },
                new byte[] { 0x5C, 0x78, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00 },
            };

            for (int i = 0; i < headers.Length; i++)
            {
                Assert.True(CliProgram.IsFEditorBattleAnimationBin(WriteBytes($"valid-{i}.bin", headers[i])));
            }
        }

        [Fact]
        public void IsFEditorBattleAnimationBin_NonFEditorBin_ReturnsFalse()
        {
            string path = WriteBytes("not-feditor.bin", new byte[] { 0x42, 0x49, 0x4E, 0x00, 0x5C, 0x78, 0x00, 0x00 });

            Assert.False(CliProgram.IsFEditorBattleAnimationBin(path));
        }

        private string WriteBytes(string fileName, byte[] bytes)
        {
            string path = Path.Combine(_workDir, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
