using System.IO;

namespace FEBuilderGBA.Tests
{
    /// <summary>
    /// Tests for ArchSevenZip (SharpCompress fallback path).
    /// </summary>
    public class ArchSevenZipTests
    {
        [Fact]
        public void Compress_SingleFile_CreatesArchive()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ArchTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string srcFile = Path.Combine(tempDir, "test.bin");
                byte[] data = new byte[2048];
                new Random(42).NextBytes(data);
                File.WriteAllBytes(srcFile, data);

                string archive = Path.Combine(tempDir, "output.zip");
                string result = ArchSevenZip.Compress(archive, srcFile, 100);

                Assert.Equal("", result);
                Assert.True(File.Exists(archive));
                Assert.True(new FileInfo(archive).Length >= 100);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Compress_Directory_CreatesArchive()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ArchTest_" + Guid.NewGuid().ToString("N"));
            string subDir = Path.Combine(tempDir, "content");
            Directory.CreateDirectory(subDir);
            try
            {
                File.WriteAllText(Path.Combine(subDir, "a.txt"), new string('A', 2048));
                File.WriteAllText(Path.Combine(subDir, "b.txt"), new string('B', 2048));

                string archive = Path.Combine(tempDir, "output.zip");
                string result = ArchSevenZip.Compress(archive, subDir, 100);

                Assert.Equal("", result);
                Assert.True(File.Exists(archive));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Extract_ValidArchive_ExtractsFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ArchTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create source file and compress
                string srcFile = Path.Combine(tempDir, "hello.txt");
                File.WriteAllText(srcFile, "Hello World from ArchSevenZip test!");

                string archive = Path.Combine(tempDir, "test.zip");
                string compressResult = ArchSevenZip.Compress(archive, srcFile, 10);
                Assert.Equal("", compressResult);

                // Extract to new directory
                string extractDir = Path.Combine(tempDir, "extracted");
                string extractResult = ArchSevenZip.Extract(archive, extractDir, true);
                Assert.Equal("", extractResult);

                // Verify extracted content
                string extractedFile = Path.Combine(extractDir, "hello.txt");
                Assert.True(File.Exists(extractedFile));
                Assert.Equal("Hello World from ArchSevenZip test!", File.ReadAllText(extractedFile));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Extract_NonexistentArchive_ReturnsError()
        {
            string fakeArchive = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".zip");
            string extractDir = Path.Combine(Path.GetTempPath(), "extract_" + Guid.NewGuid().ToString("N"));

            string result = ArchSevenZip.Extract(fakeArchive, extractDir, true);
            Assert.NotEqual("", result); // Should return error message
        }

        [Fact]
        public void Compress_NonexistentTarget_ReturnsError()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ArchTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string archive = Path.Combine(tempDir, "output.zip");
                string result = ArchSevenZip.Compress(archive, Path.Combine(tempDir, "no_such_file.bin"), 10);
                Assert.NotEqual("", result); // Should return error message
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Compress_TooSmall_ReturnsError()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ArchTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create a tiny file
                string srcFile = Path.Combine(tempDir, "tiny.txt");
                File.WriteAllText(srcFile, "x");

                string archive = Path.Combine(tempDir, "output.zip");
                // Require very large checksize — the resulting archive should be smaller
                string result = ArchSevenZip.Compress(archive, srcFile, 999999);
                Assert.Equal("file size too short", result);
                Assert.False(File.Exists(archive)); // Should have been deleted
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
