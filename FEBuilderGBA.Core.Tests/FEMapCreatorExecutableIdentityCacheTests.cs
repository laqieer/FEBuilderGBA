// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Covers the per-session executable-identity hash-reuse cache added in the #1978 Slice 2
    /// review follow-up: repeated live-status validation (one call per keystroke in the Options
    /// FEMapCreator textboxes) must not re-hash an unchanged executable, while any real path,
    /// size, or last-write-time change must still be detected and rehashed correctly.
    /// </summary>
    public class FEMapCreatorExecutableIdentityCacheTests
    {
        [Fact]
        public void TryGetOrCompute_RepeatedCallsWithUnchangedFile_ReuseCachedHash()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string path = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });

                var cache = new FEMapCreatorExecutableIdentityCache();

                Assert.True(cache.TryGetOrCompute(path, out long size1, out long ticks1, out string sha1, out string error1));
                Assert.Equal("", error1);
                Assert.Equal(1, cache.HashComputeCount);

                for (int i = 0; i < 20; i++)
                {
                    Assert.True(cache.TryGetOrCompute(path, out long size, out long ticks, out string sha, out string error));
                    Assert.Equal(size1, size);
                    Assert.Equal(ticks1, ticks);
                    Assert.Equal(sha1, sha);
                    Assert.Equal("", error);
                }

                // 20 repeated calls with a byte-for-byte unchanged file must never re-hash.
                Assert.Equal(1, cache.HashComputeCount);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryGetOrCompute_ContentRewrittenAtSamePath_Rehashes()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string path = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

                var cache = new FEMapCreatorExecutableIdentityCache();
                Assert.True(cache.TryGetOrCompute(path, out _, out _, out string shaBefore, out _));
                Assert.Equal(1, cache.HashComputeCount);

                // Force both size and last-write-time to change deterministically (avoids relying
                // on filesystem timestamp-resolution timing between two immediately-adjacent writes).
                File.WriteAllBytes(path, new byte[] { 9, 9, 9, 9, 9, 9 });
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

                Assert.True(cache.TryGetOrCompute(path, out long sizeAfter, out long ticksAfter, out string shaAfter, out string errorAfter));
                Assert.Equal("", errorAfter);
                Assert.Equal(6, sizeAfter);
                Assert.NotEqual(shaBefore, shaAfter);

                // The content actually changed, so this must be a real second hash computation,
                // not a stale cache hit.
                Assert.Equal(2, cache.HashComputeCount);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryGetOrCompute_DifferentPath_RehashesEvenWithIdenticalContent()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                byte[] bytes = { 7, 7, 7 };
                string pathA = Path.Combine(tempRoot, "A.exe");
                string pathB = Path.Combine(tempRoot, "B.exe");
                File.WriteAllBytes(pathA, bytes);
                File.WriteAllBytes(pathB, bytes);

                var cache = new FEMapCreatorExecutableIdentityCache();
                Assert.True(cache.TryGetOrCompute(pathA, out _, out _, out string shaA, out _));
                Assert.Equal(1, cache.HashComputeCount);

                Assert.True(cache.TryGetOrCompute(pathB, out _, out _, out string shaB, out _));
                Assert.Equal(2, cache.HashComputeCount);

                // Same bytes, so the resulting hash values are equal, but switching paths must
                // still have triggered an actual recompute rather than returning A's cached value.
                Assert.Equal(shaA, shaB);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryGetOrCompute_MissingFile_ReturnsFalse_AndDoesNotServeStaleCacheOnRecovery()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string path = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

                var cache = new FEMapCreatorExecutableIdentityCache();
                Assert.True(cache.TryGetOrCompute(path, out _, out _, out string shaBefore, out _));
                Assert.Equal(1, cache.HashComputeCount);

                File.Delete(path);
                Assert.False(cache.TryGetOrCompute(path, out _, out _, out string shaMissing, out string error));
                Assert.Equal("", shaMissing);
                Assert.NotEqual("", error);

                // Recreate with different content at the same path; the cache must not resurrect
                // the stale pre-deletion hash.
                File.WriteAllBytes(path, new byte[] { 4, 4, 4, 4 });
                Assert.True(cache.TryGetOrCompute(path, out _, out _, out string shaAfter, out _));
                Assert.NotEqual(shaBefore, shaAfter);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_WithSharedCache_AssetsRootEditsDoNotRehashUnchangedExecutable()
        {
            // Reproduces the Options-dialog scenario driving this cache: typing into the
            // assets-root field alone must never re-hash the (unchanged) executable.
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3 });
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);

                var cache = new FEMapCreatorExecutableIdentityCache();

                FEMapCreatorSetupSnapshot first = FEMapCreatorProfileCore.Validate(exePath, "", cache);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, first.Status);
                Assert.Equal(1, cache.HashComputeCount);

                // Simulate 10 incremental keystrokes building up the assets-root path.
                string partial = "";
                foreach (char c in Path.GetFileName(assetsRoot))
                {
                    partial += c;
                    string candidateRoot = Path.Combine(tempRoot, partial);
                    // Only the fully-typed path exists on disk; intermediate substrings are
                    // expected to be transiently Invalid, exactly like real typing — the
                    // assertion under test is the hash count, not each intermediate status.
                    FEMapCreatorProfileCore.Validate(exePath, candidateRoot, cache);
                }

                FEMapCreatorSetupSnapshot final = FEMapCreatorProfileCore.Validate(exePath, assetsRoot, cache);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, final.Status);
                Assert.Equal(first.ExecutableSha256, final.ExecutableSha256);

                // The executable itself never changed across all of these calls.
                Assert.Equal(1, cache.HashComputeCount);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_WithSharedCache_StillDetectsExecutableContentChange()
        {
            // The cache must never trade correctness for speed: an actual executable edit
            // (e.g. the user re-installed/rebuilt FEMapCreator at the same path) must still be
            // reflected in the next status/snapshot.
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3 });

                var cache = new FEMapCreatorExecutableIdentityCache();
                FEMapCreatorSetupSnapshot before = FEMapCreatorProfileCore.Validate(exePath, "", cache);
                Assert.Equal(1, cache.HashComputeCount);

                File.WriteAllBytes(exePath, new byte[] { 8, 8, 8, 8, 8, 8, 8 });
                File.SetLastWriteTimeUtc(exePath, DateTime.UtcNow.AddSeconds(5));

                FEMapCreatorSetupSnapshot after = FEMapCreatorProfileCore.Validate(exePath, "", cache);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, after.Status);
                Assert.Equal(2, cache.HashComputeCount);
                Assert.NotEqual(before.ExecutableSha256, after.ExecutableSha256);
                Assert.NotEqual(before.ExecutableSizeBytes, after.ExecutableSizeBytes);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_WithoutCache_BehavesIdenticallyToPublicTwoArgOverload()
        {
            // Regression guard: the public Validate(string, string) overload must remain a pure
            // pass-through to Validate(string, string, null) with unchanged, always-uncached
            // behavior for every existing caller (including FEMapCreatorTilesetMappingStoreCore.Lookup).
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 5, 5, 5 });

                FEMapCreatorSetupSnapshot viaPublic = FEMapCreatorProfileCore.Validate(exePath, "");
                FEMapCreatorSetupSnapshot viaNullCache = FEMapCreatorProfileCore.Validate(exePath, "", null);

                Assert.Equal(viaPublic.Status, viaNullCache.Status);
                Assert.Equal(viaPublic.ExecutablePath, viaNullCache.ExecutablePath);
                Assert.Equal(viaPublic.ExecutableSha256, viaNullCache.ExecutableSha256);
                Assert.Equal(viaPublic.ExecutableSizeBytes, viaNullCache.ExecutableSizeBytes);
                Assert.Equal(viaPublic.ExecutableLastWriteUtcTicks, viaNullCache.ExecutableLastWriteUtcTicks);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "femapcreator_identitycache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static void DeleteDirectoryIfPresent(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // best effort cleanup
            }
            catch (UnauthorizedAccessException)
            {
                // best effort cleanup
            }
        }
    }
}
