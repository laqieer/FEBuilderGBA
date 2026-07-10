using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class AtomicFileSetWriterCoreTests : IDisposable
    {
        readonly string _tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "febuilder_atomic_" + Guid.NewGuid().ToString("N"));

        public AtomicFileSetWriterCoreTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Fact]
        public void WriteAll_DistinctOutputs_WritesEveryArtifact()
        {
            string first = System.IO.Path.Combine(_tempDirectory, "first.bin");
            string second = System.IO.Path.Combine(_tempDirectory, "second.bin");

            AtomicFileSetWriterCore.WriteAll(new[]
            {
                new AtomicFileSetWriterCore.FileOutput(first, new byte[] { 1, 2 }),
                new AtomicFileSetWriterCore.FileOutput(second, new byte[] { 3, 4 }),
            });

            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(first));
            Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(second));
        }

        [Fact]
        public void WriteAll_CaseAliases_FollowActualFilesystemSemantics()
        {
            string lower = System.IO.Path.Combine(_tempDirectory, "case.bin");
            string upper = System.IO.Path.Combine(_tempDirectory, "CASE.BIN");
            File.WriteAllBytes(lower, new byte[] { 0 });
            bool caseInsensitive = File.Exists(upper);
            File.Delete(lower);

            var outputs = new[]
            {
                new AtomicFileSetWriterCore.FileOutput(lower, new byte[] { 1 }),
                new AtomicFileSetWriterCore.FileOutput(upper, new byte[] { 2 }),
            };

            if (caseInsensitive)
            {
                Assert.Throws<IOException>(() => AtomicFileSetWriterCore.WriteAll(outputs));
                Assert.False(File.Exists(lower));
            }
            else
            {
                AtomicFileSetWriterCore.WriteAll(outputs);
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(lower));
                Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(upper));
            }
        }

        [Fact]
        public void WriteAll_ExistingHardLinks_EndAsDistinctArtifacts()
        {
            string first = System.IO.Path.Combine(_tempDirectory, "first-link.bin");
            string second = System.IO.Path.Combine(_tempDirectory, "second-link.bin");
            File.WriteAllBytes(first, new byte[] { 0 });
            Assert.True(CreateHardLink(second, first),
                "Failed to create a hard link for the transactional output test.");

            AtomicFileSetWriterCore.WriteAll(new[]
            {
                new AtomicFileSetWriterCore.FileOutput(first, new byte[] { 1 }),
                new AtomicFileSetWriterCore.FileOutput(second, new byte[] { 2 }),
            });

            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first));
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second));
        }

        [Fact]
        public void WriteAll_SymlinkedParentAlias_RestoresExistingOutput()
        {
            string realDirectory = System.IO.Path.Combine(_tempDirectory, "real");
            string aliasDirectory = System.IO.Path.Combine(_tempDirectory, "alias");
            Directory.CreateDirectory(realDirectory);
            try
            {
                Directory.CreateSymbolicLink(aliasDirectory, realDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            string first = System.IO.Path.Combine(realDirectory, "shared.bin");
            string second = System.IO.Path.Combine(aliasDirectory, "shared.bin");
            File.WriteAllBytes(first, new byte[] { 9 });
            Assert.Throws<IOException>(() => AtomicFileSetWriterCore.WriteAll(new[]
            {
                new AtomicFileSetWriterCore.FileOutput(first, new byte[] { 1 }),
                new AtomicFileSetWriterCore.FileOutput(second, new byte[] { 2 }),
            }));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(first));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(second));
        }

        static bool CreateHardLink(string linkPath, string existingPath)
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero);
            return CreateHardLinkUnix(existingPath, linkPath) == 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateHardLinkW")]
        static extern bool CreateHardLinkWindows(string fileName, string existingFileName,
            IntPtr securityAttributes);

        [DllImport("libc", SetLastError = true, EntryPoint = "link")]
        static extern int CreateHardLinkUnix(string existingPath, string linkPath);
    }
}
