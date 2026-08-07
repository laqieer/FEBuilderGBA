// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ManagedMd5Tests
    {
        [Theory]
        [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
        [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
        [InlineData("message digest", "f96b697d7cb7938d525a2f31aaf161d0")]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b")]
        [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", "d174ab98d277d9f5a5611c2c9f419d9f")]
        [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57edf4a22be3c955ac49da2e2107b67a")]
        public void ComputeHex_TextKnownVectors_ReturnsStandardLowercaseMd5(string text, string expected)
        {
            byte[] data = Encoding.ASCII.GetBytes(text);

            Assert.Equal(expected, ManagedMd5.ComputeHex(data));
            Assert.Equal(expected, ManagedMd5.ComputeHex(data.AsSpan()));
            Assert.Equal(expected, U.md5(data));
        }

        [Fact]
        public void ComputeHex_BinaryKnownVector_ReturnsStandardLowercaseMd5()
        {
            byte[] data = new byte[256];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

            string hash = ManagedMd5.ComputeHex(data);

            Assert.Equal("e2c865db4162bed963bfaa9ef6ac18f0", hash);
            Assert.Equal(32, hash.Length);
            Assert.Equal(hash.ToLowerInvariant(), hash);
        }

        [Fact]
        public void ComputeHex_StreamHashesFromCurrentPosition()
        {
            byte[] data = new byte[256];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
            using var stream = new MemoryStream(data);
            stream.Position = 1;

            Assert.Equal("2a43f79ceb44831d96f6e456839744e4", ManagedMd5.ComputeHex(stream));
            Assert.Equal(stream.Length, stream.Position);
        }

        [Fact]
        public void ComputeFileHex_HashesFileBytes()
        {
            byte[] data = { 0x00, 0x01, 0x02, 0x03, 0xFF, 0x80, 0x7F, 0x42 };
            string path = Path.Combine(AppContext.BaseDirectory, $"managed-md5-{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(path, data);

                Assert.Equal("608a6a576e392d0d8a9404764e00d848", ManagedMd5.ComputeFileHex(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void BrowserRelevantCallers_DoNotUsePlatformMd5Create()
        {
            string repoRoot = FindRepoRoot();

            string coreU = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Core", "U.cs"));
            string songInstrument = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels", "SongInstrumentViewModel.cs"));

            Assert.DoesNotContain("MD5.Create", coreU);
            Assert.DoesNotContain("MD5.Create", songInstrument);
        }

        static string FindRepoRoot()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root from test output directory.");
        }
    }
}
