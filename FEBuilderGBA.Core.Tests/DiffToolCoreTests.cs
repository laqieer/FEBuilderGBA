using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for DiffToolCore — the cross-platform ROM diff helper extracted
    /// from WinForms ToolDiffForm. Covers MakeDiff (2-way), MakeDiff3 (3-way),
    /// and DefineFreeSpace.
    /// </summary>
    public class DiffToolCoreTests : IDisposable
    {
        readonly string _tempDir;

        public DiffToolCoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DiffToolCoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
        }

        string TempFile(string name) => Path.Combine(_tempDir, name);

        // ---------- DefineFreeSpace ----------

        [Fact]
        public void DefineFreeSpace_FE8U_NonMultibyte_ReturnsExpectedRange()
        {
            bool ok = DiffToolCore.DefineFreeSpace(version: 8, isMultibyte: false, collectFreeSpace: true,
                out uint begin, out uint end);
            Assert.True(ok);
            Assert.Equal(0xB2A610u, begin);
            Assert.Equal(0xB88560u - 4u, end);
        }

        [Fact]
        public void DefineFreeSpace_FE8J_Multibyte_ReturnsExpectedRange()
        {
            bool ok = DiffToolCore.DefineFreeSpace(version: 8, isMultibyte: true, collectFreeSpace: true,
                out uint begin, out uint end);
            Assert.True(ok);
            Assert.Equal(0xEFB2E0u, begin);
            Assert.Equal(0xF90000u - 4u, end);
        }

        [Fact]
        public void DefineFreeSpace_FE6_ReturnsFalse()
        {
            bool ok = DiffToolCore.DefineFreeSpace(version: 6, isMultibyte: true, collectFreeSpace: true,
                out uint _, out uint _);
            Assert.False(ok);
        }

        [Fact]
        public void DefineFreeSpace_FE7_ReturnsFalse()
        {
            bool ok = DiffToolCore.DefineFreeSpace(version: 7, isMultibyte: false, collectFreeSpace: true,
                out uint _, out uint _);
            Assert.False(ok);
        }

        [Fact]
        public void DefineFreeSpace_CollectFreeSpaceFalse_ReturnsFalse()
        {
            bool ok = DiffToolCore.DefineFreeSpace(version: 8, isMultibyte: false, collectFreeSpace: false,
                out uint _, out uint _);
            Assert.False(ok);
        }

        // ---------- MakeDiff (2-way) ----------

        [Fact]
        public void MakeDiff_IdenticalInputs_ProducesHeaderOnly()
        {
            string outFile = TempFile("PATCH_Identical.txt");
            byte[] current = new byte[256];
            byte[] other = new byte[256];

            DiffToolCore.MakeDiff(outFile, current, other, patchedIfMinSize: 32,
                collectFreeSpace: false, version: 6, isMultibyte: false);

            Assert.True(File.Exists(outFile));
            var lines = File.ReadAllLines(outFile);
            Assert.Contains("TYPE=BIN", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.StartsWith("PATCHED_IF:", StringComparison.Ordinal));
        }

        [Fact]
        public void MakeDiff_OneByteDiff_ProducesBinfLine()
        {
            string outFile = TempFile("PATCH_OneByte.txt");
            byte[] current = new byte[256];
            byte[] other = new byte[256];
            other[100] = 0xAB; // Single byte difference

            DiffToolCore.MakeDiff(outFile, current, other, patchedIfMinSize: 32,
                collectFreeSpace: false, version: 6, isMultibyte: false);

            Assert.True(File.Exists(outFile));
            var lines = File.ReadAllLines(outFile);
            Assert.Contains(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
            // The .bin sidecar should exist
            var sidecars = Directory.GetFiles(_tempDir, "*.bin");
            Assert.NotEmpty(sidecars);
        }

        [Fact]
        public void MakeDiff_LargeDiff_ProducesPatchedIfAndBinf()
        {
            string outFile = TempFile("PATCH_Large.txt");
            byte[] current = new byte[1024];
            byte[] other = new byte[1024];
            // Create a 64-byte block of differences starting at offset 100
            for (int i = 100; i < 164; i++)
                other[i] = 0xAB;

            DiffToolCore.MakeDiff(outFile, current, other, patchedIfMinSize: 32,
                collectFreeSpace: false, version: 6, isMultibyte: false);

            var lines = File.ReadAllLines(outFile);
            Assert.Contains(lines, l => l.StartsWith("PATCHED_IF:", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
        }

        [Fact]
        public void MakeDiff_HeaderNameDerivedFromFilename()
        {
            string outFile = TempFile("PATCH_MyPatch.txt");
            byte[] current = new byte[16];
            byte[] other = new byte[16];

            DiffToolCore.MakeDiff(outFile, current, other, patchedIfMinSize: 32,
                collectFreeSpace: false, version: 6, isMultibyte: false);

            var lines = File.ReadAllLines(outFile);
            Assert.Contains("NAME=MyPatch", lines);
        }

        // ---------- MakeDiff3 (3-way) ----------

        [Fact]
        public void MakeDiff3_AAndBSameDifferentFromRom_EmitsLine()
        {
            string outFile = TempFile("PATCH_Diff3.txt");
            byte[] currentRom = new byte[256];
            byte[] a = new byte[256];
            byte[] b = new byte[256];
            // A and B both have byte 0xAB at offset 100, but current ROM has 0x00
            a[100] = 0xAB;
            b[100] = 0xAB;

            DiffToolCore.MakeDiff3(outFile, currentRom, a, b, patchedIfMinSize: 32);

            Assert.True(File.Exists(outFile));
            var lines = File.ReadAllLines(outFile);
            Assert.Contains(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
        }

        [Fact]
        public void MakeDiff3_RomMatchesAB_NoBinfOutput()
        {
            string outFile = TempFile("PATCH_Diff3Match.txt");
            byte[] currentRom = new byte[256];
            byte[] a = new byte[256];
            byte[] b = new byte[256];
            // All identical → no diff
            currentRom[50] = 0x12;
            a[50] = 0x12;
            b[50] = 0x12;

            DiffToolCore.MakeDiff3(outFile, currentRom, a, b, patchedIfMinSize: 32);

            var lines = File.ReadAllLines(outFile);
            Assert.DoesNotContain(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
        }

        [Fact]
        public void MakeDiff3_ABDisagree_NoBinfOutput()
        {
            string outFile = TempFile("PATCH_Diff3Disagree.txt");
            byte[] currentRom = new byte[256];
            byte[] a = new byte[256];
            byte[] b = new byte[256];
            // A and B disagree → not emitted (algorithm requires A == B)
            a[50] = 0x11;
            b[50] = 0x22;

            DiffToolCore.MakeDiff3(outFile, currentRom, a, b, patchedIfMinSize: 32);

            var lines = File.ReadAllLines(outFile);
            Assert.DoesNotContain(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
        }

        [Fact]
        public void MakeDiff3_LargeChunk_EmitsPatchedIfAndBinf()
        {
            string outFile = TempFile("PATCH_Diff3Large.txt");
            byte[] currentRom = new byte[1024];
            byte[] a = new byte[1024];
            byte[] b = new byte[1024];
            // 64-byte chunk where A == B != currentRom
            for (int i = 100; i < 164; i++)
            {
                a[i] = 0xAB;
                b[i] = 0xAB;
            }

            DiffToolCore.MakeDiff3(outFile, currentRom, a, b, patchedIfMinSize: 32);

            var lines = File.ReadAllLines(outFile);
            Assert.Contains(lines, l => l.StartsWith("PATCHED_IF:", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("BINF:", StringComparison.Ordinal));
        }
    }
}
