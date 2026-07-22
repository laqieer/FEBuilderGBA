// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        public void LoadAll_PreservesFingerprintMalformedEntries_AndKeepsValidOnes()
        {
            var config = new Config
            {
                [FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] =
                    "[" +
                    "{\"FingerprintValue\":\"abc123\",\"TilesetName\":\"\",\"ImagePath\":\"x\",\"GenerationDataPath\":\"y\",\"ExecutablePath\":\"e\",\"ExecutableSha256\":\"h\"}," +
                    "{\"FingerprintValue\":\"def456\",\"TilesetName\":\"Plains\",\"ImagePath\":\"img.png\",\"GenerationDataPath\":\"gen.json\",\"ExecutablePath\":\"exe.path\",\"ExecutableSha256\":\"exehash\"}" +
                    "]",
            };

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("abc123", loaded[0].FingerprintValue);
            Assert.False(loaded[0].IsStructurallyValid);
            Assert.Equal("def456", loaded[1].FingerprintValue);
            Assert.Equal("Plains", loaded[1].TilesetName);
        }

        [Fact]
        public void LoadAll_PreservesEntryMissingExecutableIdentity_SoLookupReportsInvalid()
        {
            // #1978 Slice 2 review fix: an entry recorded before a validated executable existed
            // (or from a foreign/legacy schema) must never be treated as usable.
            var fingerprint = TilesetFingerprint.Compute(
                8,
                new byte[] { 1 },
                new byte[] { 2 },
                new byte[] { 3 });
            var config = new Config
            {
                [FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] =
                    "[{\"FingerprintValue\":\"" + fingerprint.Value + "\",\"TilesetName\":\"Plains\",\"ImagePath\":\"img.png\",\"GenerationDataPath\":\"gen.json\"}]",
            };

            IReadOnlyList<FEMapCreatorTilesetMappingEntry> loaded = FEMapCreatorTilesetMappingStoreCore.LoadAll(config);
            FEMapCreatorTilesetMappingEntry entry = Assert.Single(loaded);
            Assert.False(entry.IsStructurallyValid);

            FEMapCreatorMappingLookupResult lookup =
                FEMapCreatorTilesetMappingStoreCore.Lookup(loaded, fingerprint, null);
            Assert.Equal(FEMapCreatorMappingStatus.Invalid, lookup.Status);
            Assert.Same(entry, lookup.Entry);
        }

        [Fact]
        public void LoadAll_SkipsMalformedEntryWithoutFingerprint()
        {
            var config = new Config
            {
                [FEMapCreatorTilesetMappingStoreCore.MappingsConfigKey] =
                    "[{\"FingerprintValue\":\"\",\"TilesetName\":\"Plains\"}]",
            };

            Assert.Empty(FEMapCreatorTilesetMappingStoreCore.LoadAll(config));
        }

        [Fact]
        public void SaveAll_RoundTripsFingerprintMalformedEntry()
        {
            var malformed = new FEMapCreatorTilesetMappingEntry(
                "fp-malformed", "", "", 0, 0, "",
                "", 0, 0, "",
                "", 0, 0, "",
                "");
            var config = new Config();

            FEMapCreatorTilesetMappingStoreCore.SaveAll(config, new[] { malformed });
            FEMapCreatorTilesetMappingEntry roundTripped =
                Assert.Single(FEMapCreatorTilesetMappingStoreCore.LoadAll(config));

            Assert.Equal("fp-malformed", roundTripped.FingerprintValue);
            Assert.False(roundTripped.IsStructurallyValid);
        }

        [Fact]
        public void SaveAll_LoadAll_RoundTrip_PreservesAllFields()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5, 6, 7 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "assets");

                var config = new Config();
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
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
                Assert.Equal(entry.ExecutableSizeBytes, roundTripped.ExecutableSizeBytes);
                Assert.Equal(entry.ExecutableLastWriteUtcTicks, roundTripped.ExecutableLastWriteUtcTicks);
                Assert.Equal(entry.ExecutableSha256, roundTripped.ExecutableSha256);
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
            string tempRoot = CreateTempDirectory();
            try
            {
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");
                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    TilesetFingerprint.Empty, "Plains", "img.png", "gen.json", profile,
                    out FEMapCreatorTilesetMappingEntry entry, out string error);

                Assert.False(ok);
                Assert.Null(entry);
                Assert.NotEqual("", error);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryCreateEntry_CancellationAwareOverload_ThrowsBeforeHashing()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 2 });
                var fingerprint = TilesetFingerprint.Compute(
                    8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                Assert.Throws<OperationCanceledException>(() =>
                    FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                        fingerprint,
                        "Plains",
                        imagePath,
                        genPath,
                        profile,
                        cts.Token,
                        out _,
                        out _));
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
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
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", missingImage, genPath, profile,
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
        public void TryCreateEntry_ProfileNotConfigured_ReturnsFalseWithError()
        {
            // #1978 Slice 2 review fix: a mapping must never record an executable identity that
            // was never successfully validated.
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 2 });
                var fingerprint = TilesetFingerprint.Compute(7, new byte[] { 9 }, new byte[] { 9 }, new byte[] { 9 });

                FEMapCreatorSetupSnapshot notConfigured = FEMapCreatorProfileCore.Validate("", "");
                Assert.Equal(FEMapCreatorSetupStatus.NotConfigured, notConfigured.Status);

                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, notConfigured,
                    out FEMapCreatorTilesetMappingEntry entry, out string error);

                Assert.False(ok);
                Assert.Null(entry);
                Assert.NotEqual("", error);

                bool okNull = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, null,
                    out FEMapCreatorTilesetMappingEntry entryNull, out string errorNull);
                Assert.False(okNull);
                Assert.Null(entryNull);
                Assert.NotEqual("", errorNull);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryCreateEntry_LiveStatusSnapshot_ReturnsFalseWithoutHashingAssets()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = CreateFile(tempRoot, "FEMapCreator.exe", new byte[] { 1 });
                FEMapCreatorSetupSnapshot liveStatus = FEMapCreatorProfileCore.ValidateForStatus(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, liveStatus.Status);
                Assert.Equal("", liveStatus.ExecutableSha256);

                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    TilesetFingerprint.Compute(7, new byte[] { 9 }, new byte[] { 9 }, new byte[] { 9 }),
                    "Plains",
                    Path.Combine(tempRoot, "missing.png"),
                    Path.Combine(tempRoot, "missing.json"),
                    liveStatus,
                    out FEMapCreatorTilesetMappingEntry entry,
                    out string error);

                Assert.False(ok);
                Assert.Null(entry);
                Assert.Contains("authoritatively", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void TryCreateEntry_ConfiguredSnapshotWithUnlaunchableExecutable_ReturnsFalseBeforeAssetHashing()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string unsupportedPath = CreateFile(
                    tempRoot,
                    "FEMapCreator.txt",
                    new byte[] { 1, 2, 3 });
                var syntheticProfile = new FEMapCreatorSetupSnapshot(
                    FEMapCreatorSetupStatus.Configured,
                    unsupportedPath,
                    "",
                    "",
                    executableSizeBytes: 3,
                    executableLastWriteUtcTicks: File.GetLastWriteTimeUtc(unsupportedPath).Ticks,
                    executableSha256: "synthetic-hash");

                bool ok = FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    TilesetFingerprint.Compute(
                        7,
                        new byte[] { 9 },
                        new byte[] { 9 },
                        new byte[] { 9 }),
                    "Plains",
                    Path.Combine(tempRoot, "missing.png"),
                    Path.Combine(tempRoot, "missing.json"),
                    syntheticProfile,
                    out FEMapCreatorTilesetMappingEntry entry,
                    out string error);

                Assert.False(ok);
                Assert.Null(entry);
                Assert.Contains("not launchable", error, StringComparison.OrdinalIgnoreCase);
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
                Array.Empty<FEMapCreatorTilesetMappingEntry>(), fingerprint, null);

            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, result.Status);
            Assert.Null(result.Entry);
        }

        [Fact]
        public void Lookup_NoMapping_WhenEmptyFingerprint()
        {
            var entry = MakeEntry("fp-a", "TilesetA");
            FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                new[] { entry }, TilesetFingerprint.Empty, null);

            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, result.Status);
        }

        [Fact]
        public void Lookup_Current_WhenMappedFilesAndProfileUnchanged()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "assets");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                // Re-validating the same, unchanged executable/assets root must not itself
                // manufacture staleness.
                FEMapCreatorSetupSnapshot currentProfile = FEMapCreatorProfileCore.Validate(profile.ExecutablePath, profile.AssetsRoot);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, currentProfile);

                Assert.Equal(FEMapCreatorMappingStatus.Current, result.Status);
                Assert.Same(entry, result.Entry);
                Assert.Equal(FEMapCreatorMappingReason.None, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_CancelledToken_ThrowsBeforeAuthoritativeFileHashing()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(
                    8,
                    new byte[] { 1 },
                    new byte[] { 2 },
                    new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "assets");
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                Assert.Throws<OperationCanceledException>(() =>
                    FEMapCreatorTilesetMappingStoreCore.Lookup(
                        new[] { entry }, fingerprint, profile, cts.Token));
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
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                // Simulate the external tileset image being re-exported/edited after mapping.
                File.WriteAllBytes(imagePath, new byte[] { 9, 9, 9, 9, 9, 9 });

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.ImageChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenImageFileDeletedAfterMapping()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                File.Delete(imagePath);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.ImageUnreadable, result.Reason);
                Assert.NotEqual("", result.Detail);
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
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                File.Delete(genPath);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.GenerationDataUnreadable, result.Reason);
                Assert.NotEqual("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenGenerationDataContentChangesAfterMapping()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3, 4 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 5, 6 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                File.WriteAllBytes(genPath, new byte[] { 9, 9, 9, 9 });

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.GenerationDataChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenExecutablePathChangesAfterMapping()
        {
            // #1978 Slice 2 review fix (finding #1): a mapping created against one executable
            // path must go Stale once a *different* executable is configured, even though the
            // mapped image/generation-data files on disk are completely unchanged.
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                FEMapCreatorSetupSnapshot originalProfile = MakeConfiguredProfile(tempRoot, "", exeFileName: "original.exe");
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, originalProfile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                FEMapCreatorSetupSnapshot differentExeProfile = MakeConfiguredProfile(tempRoot, "", exeFileName: "different.exe");

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, differentExeProfile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.ExecutablePathChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenExecutableContentChangesAtSamePath()
        {
            // Same executable path, but its bytes changed (e.g. FEMapCreator was upgraded
            // in-place) — must still be caught via size/mtime/hash, not just the path string.
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                string exePath = CreateFile(tempRoot, "femapcreator.exe", new byte[] { 1, 2, 3, 4, 5 });
                FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, profile.Status);

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                // Rewrite the executable's content in place, at the identical path.
                File.WriteAllBytes(exePath, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
                FEMapCreatorSetupSnapshot rewrittenProfile = FEMapCreatorProfileCore.Validate(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, rewrittenProfile.Status);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, rewrittenProfile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.ExecutableContentChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenAssetsRootChanges()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                string assetsRootOne = CreateSubDirectory(tempRoot, "assets1");
                string assetsRootTwo = CreateSubDirectory(tempRoot, "assets2");
                string exePath = CreateFile(tempRoot, "femapcreator.exe", new byte[] { 1 });
                FEMapCreatorSetupSnapshot profileOne = FEMapCreatorProfileCore.Validate(exePath, assetsRootOne);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, profileOne.Status);

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profileOne,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                FEMapCreatorSetupSnapshot profileTwo = FEMapCreatorProfileCore.Validate(exePath, assetsRootTwo);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, profileTwo.Status);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profileTwo);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.AssetsRootChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenAssetsRootClearedToBlank()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

                string assetsRoot = CreateSubDirectory(tempRoot, "assets1");
                string exePath = CreateFile(tempRoot, "femapcreator.exe", new byte[] { 1 });
                FEMapCreatorSetupSnapshot profileWithAssets = FEMapCreatorProfileCore.Validate(exePath, assetsRoot);
                Assert.Equal(FEMapCreatorSetupStatus.Configured, profileWithAssets.Status);
                Assert.NotEqual("", profileWithAssets.AssetsRoot);

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profileWithAssets,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                // Blank assets root is a valid configuration by itself, but it is a *different*
                // identity from the non-blank root recorded at mapping time.
                FEMapCreatorSetupSnapshot profileCleared = FEMapCreatorProfileCore.Validate(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, profileCleared.Status);
                Assert.Equal("", profileCleared.AssetsRoot);

                FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, profileCleared);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.AssetsRootChanged, result.Reason);
                Assert.Equal("", result.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenCurrentProfileIsNullOrNotConfigured()
        {
            // A mapping must never present as Current when there is currently no way to verify
            // that the recorded executable identity still matches.
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint, "Plains", imagePath, genPath, profile,
                    out FEMapCreatorTilesetMappingEntry entry, out _));

                FEMapCreatorMappingLookupResult resultNull = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, null);
                Assert.Equal(FEMapCreatorMappingStatus.Stale, resultNull.Status);
                Assert.Equal(FEMapCreatorMappingReason.ProfileUnavailable, resultNull.Reason);
                Assert.Equal("", resultNull.Detail);

                FEMapCreatorSetupSnapshot notConfigured = FEMapCreatorProfileCore.Validate("", "");
                FEMapCreatorMappingLookupResult resultNotConfigured = FEMapCreatorTilesetMappingStoreCore.Lookup(
                    new[] { entry }, fingerprint, notConfigured);
                Assert.Equal(FEMapCreatorMappingStatus.Stale, resultNotConfigured.Status);
                Assert.Equal(FEMapCreatorMappingReason.ProfileUnavailable, resultNotConfigured.Reason);
                Assert.Equal("", resultNotConfigured.Detail);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Lookup_Stale_WhenConfiguredSnapshotExecutableIsNoLongerLaunchable()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string imagePath = CreateFile(tempRoot, "tileset.png", new byte[] { 1, 2, 3 });
                string genPath = CreateFile(tempRoot, "tileset.json", new byte[] { 4, 5 });
                var fingerprint = TilesetFingerprint.Compute(
                    8,
                    new byte[] { 1 },
                    new byte[] { 2 },
                    new byte[] { 3 });
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "");
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprint,
                    "Plains",
                    imagePath,
                    genPath,
                    profile,
                    out FEMapCreatorTilesetMappingEntry entry,
                    out _));

                File.Delete(profile.ExecutablePath);

                FEMapCreatorMappingLookupResult result =
                    FEMapCreatorTilesetMappingStoreCore.Lookup(
                        new[] { entry },
                        fingerprint,
                        profile);

                Assert.Equal(FEMapCreatorMappingStatus.Stale, result.Status);
                Assert.Equal(FEMapCreatorMappingReason.ProfileUnavailable, result.Reason);
                Assert.NotEqual("", result.Detail);
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
                fingerprint.Value, "", "img.png", 1, 1, "hash", "gen.json", 1, 1, "hash",
                "exe", 1, 1, "exehash", "assets");

            FEMapCreatorMappingLookupResult result = FEMapCreatorTilesetMappingStoreCore.Lookup(
                new[] { malformed }, fingerprint, null);

            Assert.Equal(FEMapCreatorMappingStatus.Invalid, result.Status);
            Assert.Equal(FEMapCreatorMappingReason.StoredEntryMissingRequiredFields, result.Reason);
            Assert.Equal("", result.Detail);
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
                FEMapCreatorSetupSnapshot profile = MakeConfiguredProfile(tempRoot, "assets");

                var fingerprintA = TilesetFingerprint.Compute(6, new byte[] { 1 }, new byte[] { 1 }, new byte[] { 1 });
                var fingerprintB = TilesetFingerprint.Compute(8, new byte[] { 2 }, new byte[] { 2 }, new byte[] { 2 });
                Assert.NotEqual(fingerprintA, fingerprintB);

                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprintA, "TilesetA", imagePathA, genPathA, profile, out var entryA, out _));
                Assert.True(FEMapCreatorTilesetMappingStoreCore.TryCreateEntry(
                    fingerprintB, "TilesetB", imagePathB, genPathB, profile, out var entryB, out _));

                var mappings = new[] { entryA, entryB };

                FEMapCreatorMappingLookupResult resultA = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprintA, profile);
                FEMapCreatorMappingLookupResult resultB = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprintB, profile);

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
                "exe.path", 30, 300, "exehash",
                "assets.root");

        /// <summary>Create and validate a real, currently-Configured FEMapCreator profile inside <paramref name="tempRoot"/>.</summary>
        static FEMapCreatorSetupSnapshot MakeConfiguredProfile(string tempRoot, string assetsRoot, string exeFileName = "femapcreator.exe")
        {
            string exePath = CreateFile(tempRoot, exeFileName, new byte[] { 0x4D, 0x5A, 1, 2, 3 });
            string normalizedAssetsRoot = "";
            if (!string.IsNullOrWhiteSpace(assetsRoot))
                normalizedAssetsRoot = CreateSubDirectory(tempRoot, assetsRoot);

            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(exePath, normalizedAssetsRoot);
            Assert.Equal(FEMapCreatorSetupStatus.Configured, profile.Status);
            return profile;
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "femapcreator_mapping_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateSubDirectory(string parent, string name)
        {
            string path = Path.Combine(parent, name);
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateFile(string directory, string fileName, byte[] content)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, content);
            if (!OperatingSystem.IsWindows()
                && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                File.SetUnixFileMode(
                    path,
                    File.GetUnixFileMode(path)
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherExecute);
            }
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
