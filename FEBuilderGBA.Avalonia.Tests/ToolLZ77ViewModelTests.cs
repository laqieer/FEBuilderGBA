using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolLZ77ViewModelTests : IDisposable
    {
        readonly string _tempDir;
        readonly ROM? _savedRom;

        public ToolLZ77ViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ToolLZ77VMTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        string TempFile(string name) => Path.Combine(_tempDir, name);

        [Fact]
        public void Decompress_EmptyDestPath_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.DecompressSrcPath = ToolLZ77ViewModel.THIS_ROM;
            vm.DecompressDestPath = "";
            vm.RunDecompress();
            Assert.Contains("destination", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Decompress_NoRomLoadedAndThisRom_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.DecompressSrcPath = ToolLZ77ViewModel.THIS_ROM;
            vm.DecompressDestPath = TempFile("out.bin");
            vm.DecompressAddressText = "0x0";
            vm.RunDecompress();
            Assert.Contains("no rom", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(TempFile("out.bin")));
        }

        [Fact]
        public void Decompress_InvalidAddress_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.DecompressSrcPath = TempFile("dummy.bin");
            vm.DecompressDestPath = TempFile("out.bin");
            vm.DecompressAddressText = "garbage";
            vm.RunDecompress();
            Assert.Contains("valid hex", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Decompress_SourceFileNotFound_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.DecompressSrcPath = TempFile("does_not_exist.gba");
            vm.DecompressDestPath = TempFile("out.bin");
            vm.DecompressAddressText = "0x0";
            vm.RunDecompress();
            Assert.Contains("not found", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Decompress_FromFile_ProducesOutputBytes()
        {
            byte[] original = System.Text.Encoding.ASCII.GetBytes("ABCDEFGH_ABCDEFGH_test_data_test_data_payload");
            byte[] compressed = LZ77.compress(original);
            string srcPath = TempFile("payload.bin");
            string destPath = TempFile("decoded.bin");
            File.WriteAllBytes(srcPath, compressed);

            var vm = new ToolLZ77ViewModel();
            vm.DecompressSrcPath = srcPath;
            vm.DecompressDestPath = destPath;
            vm.DecompressAddressText = "0x0";
            vm.RunDecompress();

            Assert.True(File.Exists(destPath));
            Assert.Equal(original, File.ReadAllBytes(destPath));
        }

        [Theory]
        [InlineData("0x100", true, 0x100u)]
        [InlineData("100", true, 0x100u)]
        [InlineData("0X1234ABCD", true, 0x1234ABCDu)]
        [InlineData("  0xFF  ", true, 0xFFu)]
        [InlineData("", false, 0u)]
        [InlineData("xyz", false, 0u)]
        public void TryParseHex_Cases(string input, bool ok, uint expected)
        {
            bool result = ToolLZ77ViewModel.TryParseHex(input, out uint v);
            Assert.Equal(ok, result);
            if (ok) Assert.Equal(expected, v);
        }

        [Fact]
        public void Compress_EmptySrcPath_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.CompressSrcPath = "";
            vm.CompressDestPath = TempFile("out.bin");
            vm.RunCompress();
            Assert.Contains("source", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compress_FileNotFound_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.CompressSrcPath = TempFile("nope.bin");
            vm.CompressDestPath = TempFile("out.bin");
            vm.RunCompress();
            Assert.Contains("not found", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compress_RealFile_ProducesValidLZ77()
        {
            byte[] original = new byte[256];
            for (int i = 0; i < original.Length; i++) original[i] = (byte)(i & 0xFF);
            string srcPath = TempFile("raw.bin");
            string destPath = TempFile("compressed.bin");
            File.WriteAllBytes(srcPath, original);

            var vm = new ToolLZ77ViewModel();
            vm.CompressSrcPath = srcPath;
            vm.CompressDestPath = destPath;
            vm.RunCompress();

            Assert.True(File.Exists(destPath));
            Assert.Equal(original, LZ77.decompress(File.ReadAllBytes(destPath), 0));
        }

        [Fact]
        public void Base64_TrimAndSpaceReplaced_DecodesCorrectly()
        {
            var vm = new ToolLZ77ViewModel();
            vm.Base64Text = "  YWJjZA==  ";
            string outPath = TempFile("decoded.bin");
            vm.RunBase64TextToFile(outPath);
            Assert.True(File.Exists(outPath));
            Assert.Equal(System.Text.Encoding.ASCII.GetBytes("abcd"), File.ReadAllBytes(outPath));
        }

        [Fact]
        public void Base64_SpaceMapsToPlus()
        {
            byte[] payload = new byte[] { 0x12, 0xAB, 0xCD, 0xEF };
            string b64 = Convert.ToBase64String(payload);
            var vm = new ToolLZ77ViewModel();
            vm.Base64Text = b64.Replace('+', ' ');
            string outPath = TempFile("payload.bin");
            vm.RunBase64TextToFile(outPath);
            Assert.True(File.Exists(outPath));
            Assert.Equal(payload, File.ReadAllBytes(outPath));
        }

        [Fact]
        public void Base64_EmptyText_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.Base64Text = "";
            vm.RunBase64TextToFile(TempFile("noop.bin"));
            Assert.Contains("base64", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Base64_FileToText_PopulatesText()
        {
            byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02 };
            string srcPath = TempFile("src.bin");
            File.WriteAllBytes(srcPath, payload);
            var vm = new ToolLZ77ViewModel();
            vm.RunFileToBase64Text(srcPath);
            Assert.Equal(Convert.ToBase64String(payload), vm.Base64Text);
        }

        [Fact]
        public void Base64Roundtrip_PreservesBytes()
        {
            byte[] original = new byte[] { 0x00, 0xFF, 0x10, 0x20, 0x30, 0x40, 0x50 };
            string srcPath = TempFile("orig.bin");
            File.WriteAllBytes(srcPath, original);
            var vm = new ToolLZ77ViewModel();
            vm.RunFileToBase64Text(srcPath);
            string outPath = TempFile("rt.bin");
            vm.RunBase64TextToFile(outPath);
            Assert.Equal(original, File.ReadAllBytes(outPath));
        }

        [Fact]
        public void ZeroClear_NoRomLoaded_SetsStatus()
        {
            var vm = new ToolLZ77ViewModel();
            vm.ZeroClearFromText = "0x100";
            vm.ZeroClearToText = "0x200";
            vm.RunZeroClear();
            Assert.Contains("no rom", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
    }
}