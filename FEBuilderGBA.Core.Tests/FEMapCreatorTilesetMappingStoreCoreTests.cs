// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class FEMapCreatorTilesetMappingStoreCoreTests
    {
        [Fact]
        public void LoadAll_NullConfig_ReturnsEmptyList()
        {
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(null);
            Assert.Empty(loaded);
        }

        [Fact]
        public void LoadAll_MissingKey_ReturnsEmptyList()
        {
            var config = new Config();
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);
            Assert.Empty(loaded);
        }

        [Fact]
        public void LoadAll_CorruptJsonBlob_ReturnsEmptyList_DoesNotThrow()
        {
            var config = new Config
            {
                [FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] = "{ this is not valid json ][",
            };

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);

            Assert.Empty(loaded);
        }

        [Fact]
        public void LoadAll_SkipsStructurallyMalformedEntries_KeepsValidOnes()
        {
            var config = new Config
            {
                [FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] =
                    "[" +
                    "{\"FingerprintValue\":\"abc123\",\"TilesetName\":\"\",\"ImagePath\":\"x\",\"GenerationDataPath\":\"y\"}," +
                    "{\"FingerprintValue\":\"def456\",\"TilesetName\":\"Plains\",\"ImagePath\":\"img.png\",\"GenerationDataPath\":\"gen.json\"}" +
                    "]",
            };

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);

            FEMapCreatorTilesetMappingEntry only = Assert.Single(loaded);
            Assert.Equal("def456", only.FingerprintValue);
            Assert.Equal("Plains", only.TilesetName);
        }

        [Fact]
        public void SaveAll_LoadAll_RoundTrip_PreservesAllFields()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5, 6, 7 });

                var config = new Config();
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, "exe.path", "assets",
                    out FEMapCreatorTilesetMappingEntry entry, out string error));
                Assert.Equal("", error);

                var mappings = FEMapCreatorTilesetMappingStoreCore.Upsert(
                    Array.Empty<FEMapCreatorTilesetMappingEntry>(), entry);
                FEMapCreatorTilesetMappingStoreCore.SaveAll(config, mappings);

                IReadOnlyList<FEMapCreatorTilesetMappingEntry> reloaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);

                FEMapCreatorTilesetMappingEntry roundTripped = Assert.Single(reloaded);
                Assert.Equal(entry.FingerprintValue, roundTripped.FingerprintValue);
                Assert.Equal(entry.TilesetName, roundTripped.TilesetName);
                Assert.Equal(entry.ImagePath, roundTripped.ImagePath);
                Assert.Equal(entry.ImageSizeBytes, roundTripped.ImageSizeBytes);
                Assert.Equal(entry.ImageLastWriteUtcTicks, roundTripped.ImageLastWriteUtcTicks);
                Assert.Equal(entry.ImageSha256, roundTripped.ImageSha256);
                Assert.Equal(entry.GenerationDataPath, roundTripped.GenerationDataPath);
                Assert.Equal(entry.GenerationDataSizeBytes, roundTripped.GenerationDataSizeBytes);
                Assert.Equal(entry.GenerationDataSha256, roundTripped.GenerationDataSha256);
                Assert.Equal(entry.ExecutablePath, roundTripped.ExecutablePath);
                Assert.Equal(entry.AssetsRoot, roundTripped.AssetsRoot);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryCreateEntry_EmptyFingerprint_ReturnsFalse()
        {
            bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                TilesetFingerprint.Empty, "Plains", "img.png", "gen.json", "exe", "assets",
                out FEMapCreatorTilesetMappingEntry entry, out string error);

            Assert.False(ok);
            Assert.Null(entry);
            Assert.NotEqual("", error);
        }

        [Fact]
        public void TryCreateEntry_MissingImageFile_ReturnsFalseWithError()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 1 });
                string missingImage = Path.Combine(tempRoot, "missing.png");
                var fingerprint = TilesetFingerprint.Compute(7, new byte[] { 9 }, new byte[] { 9 }, new byte[] { 9 });

                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", missingImage, genPath, "exe", "assets",
                    out FEMapCreatorTilesetMappingEntry entry, out string error);

                Assert.False(ok);
                Assert.Null(entry);
                Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Upsert_ReplacesExistingFingerprint_PreservesPosition()
        {
            var entryA1 = MakeEntry("fp-a", "TilesetA-v1");
            var entryB = MakeEntry("fp-b", "TilesetB");
            var entryA2 = MakeEntry("fp-a", "TilesetA-v2");

            var initial = new List<FEMapCreatorTilesetMappingEntry> { entryA1, entryB };
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> updated = FEMapCreatorTilesetMappingStoreCore.Upsert(initial, entryA2);

            Assert.Equal(2, updated.Count);
            Assert.Equal("TilesetA-v2", updated[0].TilesetName); // replaced in place, not appended
            Assert.Equal("TilesetB", updated[1].TilesetName);

            // Pure: the original list is untouched.
            Assert.Equal("TilesetA-v1", initial[0].TilesetName);
        }

        [Fact]
        public void Upsert_AppendsNewFingerprint()
        {
            var entryA = MakeEntry("fp-a", "TilesetA");
            var entryB = MakeEntry("fp-b", "TilesetB");

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> updated =
                FEMapCreatorTilesetMappingStoreCore.Upsert(new[] { entryA }, entryB);

            Assert.Equal(2, updated.Count);
            Assert.Equal("fp-a", updated[0].FingerprintValue);
            Assert.Equal("fp-b", updated[1].FingerprintValue);
        }

        [Fact]
        public void Remove_RemovesMatchingFingerprint_LeavesOthers()
        {
            var fingerprintA = TilesetFingerprint.Compute(6, new byte[] { 1 }, new byte[] { 1 }, new byte[] { 1 });
            var entryA = MakeEntry(fingerprintA.Value, "TilesetA");
            var entryB = MakeEntry("fp-b", "TilesetB");
            var mappings = new[] { entryA, entryB };

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> result =
                FEMapCreatorTilesetMappingStoreCore.Remove(mappings, fingerprintA);

            FEMapCreatorTilesetMappingEntry remaining = Assert.Single(result);
            Assert.Equal("fp-b", remaining.FingerprintValue);
        }

        [Fact]
        public void Lookup_NoMapping_WhenListEmpty()
        {
            var fingerprint = TilesetFingerprint.Compute(6, new byte[] { 1 }, new byte[] { 1 }, new byte[] { 1 });
            FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                Array.Empty<FEMapCreatorTilesetMappingEntry>(), fingerprint);

            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, result.Status);
            Assert.Null(result.Entry);
        }

        [Fact]
        public void Lookup_NoMapping_WhenEmptyFingerprint()
        {
            var entry = MakeEntry("fp-a", "TilesetA");
            FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                new[] { entry }, TilesetFingerprint.Empty);

            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, result.Status);
        }

        [Fact]
        public void Lookup_Current_WhenMappedFilesUnchanged()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, "exe", "assets",
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint);

                Assert.Equal(FEMapCreatorMappingStatus.Current, result.Status);
                Assert.Same(entry, result.Entry);
                Assert.Equal("", result.Reason);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenImageContentChangesAfterMapping()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, "exe", "assets",
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                // Simulate the external tileset image being re-exported/edited after mapping.
                File.WriteAllBytes(imagePath, new byte[] { 9, 9, 9, 9, 9, 9 });

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Contains("image", result.Reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenGenerationDataFileDeletedAfterMapping()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, "exe", "assets",
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                File.Delete(genPath);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Contains("generation-data", result.Reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Invalid_WhenStoredEntryMissingRequiredFields()
        {
            // Bypass LoadAll's own filtering to exercise Lookup's independent structural check
            // directly, so a future/foreign schema entry that somehow reaches Lookup is still
            // never silently reported as Current.
            var fingerprint = TilesetFingerprint.Compute(6, new byte[] { 1 }, new byte[] { 1 }, new byte[] { 1 });
            var malformed = new FEMapCreatorTilesetMappingEntry(
                fingerprint.Value, "", "img.png", 1, 1, "hash", "gen.json", 1, 1, "hash", "exe", "assets");

            FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                new[] { malformed }, fingerprint);

            Assert.Equal(FEMapCreatorMappingStatus.Invalid, result.Status);
        }

        [Fact]
        public void Lookup_FingerprintIsolation_DifferentFingerprintsDoNotCollide()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePathA = CreateFile(tempRoot, "a.png", new byte[] { 1 });
                string genPathA = CreateFile(tempRoot, "a.json", new byte[] { 2 });
                string imagePathB = CreateFile(tempRoot, "b.png", new byte[] { 3 });
                string genPathB = CreateFile(tempRoot, "b.json", new byte[] { 4 });

                var fingerprintA = TilesetFingerprint.Compute(6, new byte[] { 1 }, new byte[] { 1 }, new byte[] { 1 });
                var fingerprintB = TilesetFingerprint.Compute(8, new byte[] { 2 }, new byte[] { 2 }, new byte[] { 2 });
                Assert.NotEqual(fingerprintA, fingerprintB);

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprintA, "TilesetA", imagePathA, genPathA, "exe", "assets", out var entryA, out _));
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprintB, "TilesetB", imagePathB, genPathB, "exe", "assets", out var entryB, out _));

                var mappings = new[] { entryA, entryB };

                FEMapCreatorMappingLookupResult resultA = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprintA);
                FEMapCreatorMappingLookupResult resultB = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprintB);

                Assert.Equal(FEMapCreatorMappingStatus.Current, resultA.Status);
                Assert.Equal("TilesetA", resultA.Entry.TilesetName);
                Assert.Equal(FEMapCreatorMappingStatus.Current, resultB.Status);
                Assert.Equal("TilesetB", resultB.Entry.TilesetName);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void LoadAll_TwoIndependentConfigInstances_DoNotShareState()
        {
            // #1978 Slice 2: the store must hold no static/in-memory cache, so mappings saved
            // into one Config instance must never leak into an unrelated Config instance.
            var configOne = new Config();
            var entry = MakeEntry("fp-a", "TilesetA");
            FEMapCreatorTilesetMappingStoreCore.SaveAll(configOne, new[] { entry });

            var configTwo = new Config();

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> fromConfigOne = FEMapCreatorTilesetMappingStoreCore.LoadAll(configOne);
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> fromConfigTwo = FEMapCreatorTilesetMappingStoreCore.LoadAll(configTwo);

            Assert.Single(fromConfigOne);
            Assert.Empty(fromConfigTwo);
        }

        static FEMapCreatorTilesetMappingEntry MakeEntry(string fingerprintValue, string tilesetName) =>
            new FEMapCreatorTilesetMappingEntry(
                fingerprintValue, tilesetName,
                "image.png", 10, 100, "imagehash",
                "gen.json", 20, 200, "genhash",
                "exe.path", "assets.root");

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "femapcreator_mapping_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateFile(string directory, string fileName, byte[] content)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, content);
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
