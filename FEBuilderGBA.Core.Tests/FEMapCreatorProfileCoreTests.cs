// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class FEMapCreatorProfileCoreTests
    {
        [Fact]
        public void Validate_BlankExecutablePath_IsNotConfigured_RegardlessOfAssetsRoot()
        {
            FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate("", "");
            Assert.Equal(FEMapCreatorSetupStatus.NotConfigured, snapshot.Status);
            Assert.Equal("", snapshot.ExecutablePath);
            Assert.Equal("", snapshot.AssetsRoot);
            Assert.Equal("", snapshot.ErrorMessage);
            Assert.Equal(0, snapshot.ExecutableSizeBytes);
            Assert.Equal(0, snapshot.ExecutableLastWriteUtcTicks);
            Assert.Equal("", snapshot.ExecutableSha256);

            FEMapCreatorSetupSnapshot snapshotWhitespace = FEMapCreatorProfileCore.Validate("   ", null);
            Assert.Equal(FEMapCreatorSetupStatus.NotConfigured, snapshotWhitespace.Status);
        }

        [Fact]
        public void Validate_ExistingExecutable_EmptyAssetsRoot_IsConfigured()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");

                FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate(exePath, "");

                Assert.Equal(FEMapCreatorSetupStatus.Configured, snapshot.Status);
                Assert.Equal(Path.GetFullPath(exePath), snapshot.ExecutablePath);
                Assert.Equal("", snapshot.AssetsRoot);
                Assert.Equal("", snapshot.ErrorMessage);
                Assert.Equal(0, snapshot.ExecutableSizeBytes);
                Assert.NotEqual(0, snapshot.ExecutableLastWriteUtcTicks);
                Assert.NotEqual("", snapshot.ExecutableSha256);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_ExecutableContentIdentity_ChangesWhenBytesChangeAtSamePath()
        {
            // #1978 Slice 2 review fix: the mapping-store staleness check depends on this
            // identity actually changing when the executable is rewritten in place.
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3 });
                FEMapCreatorSetupSnapshot before = FEMapCreatorProfileCore.Validate(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, before.Status);

                File.WriteAllBytes(exePath, new byte[] { 9, 9, 9, 9, 9 });
                FEMapCreatorSetupSnapshot after = FEMapCreatorProfileCore.Validate(exePath, "");
                Assert.Equal(FEMapCreatorSetupStatus.Configured, after.Status);

                Assert.NotEqual(before.ExecutableSizeBytes, after.ExecutableSizeBytes);
                Assert.NotEqual(before.ExecutableSha256, after.ExecutableSha256);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_ExistingExecutable_ExistingAssetsRoot_IsConfigured()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);

                FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate(exePath, assetsRoot);

                Assert.Equal(FEMapCreatorSetupStatus.Configured, snapshot.Status);
                Assert.Equal(Path.GetFullPath(exePath), snapshot.ExecutablePath);
                Assert.Equal(Path.GetFullPath(assetsRoot), snapshot.AssetsRoot);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_NonExistentExecutable_IsInvalid()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string missingExe = Path.Combine(tempRoot, "does-not-exist.exe");

                FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate(missingExe, "");

                Assert.Equal(FEMapCreatorSetupStatus.Invalid, snapshot.Status);
                Assert.Equal("", snapshot.ExecutablePath);
                Assert.Contains("does not exist", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_ExecutablePathIsADirectory_IsInvalid()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                // A path with no directory component fails the "must include an explicit
                // directory component" normalization check before even reaching the
                // directory-vs-file distinction, so use a nested directory here instead.
                string dirAsExe = Path.Combine(tempRoot, "sub");
                Directory.CreateDirectory(dirAsExe);

                FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate(dirAsExe, "");

                Assert.Equal(FEMapCreatorSetupStatus.Invalid, snapshot.Status);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_NonExistentAssetsRoot_IsInvalid_EvenWithValidExecutable()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string exePath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string missingAssetsRoot = Path.Combine(tempRoot, "missing-assets");

                FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate(exePath, missingAssetsRoot);

                Assert.Equal(FEMapCreatorSetupStatus.Invalid, snapshot.Status);
                Assert.Contains("does not exist", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [Fact]
        public void Validate_RelativeExecutablePath_IsInvalid_NoPathSearchAllowed()
        {
            // #1978 Slice 2: setup validation must never fall back to a PATH lookup —
            // only a fully-qualified absolute path is accepted, matching the launcher.
            FEMapCreatorSetupSnapshot snapshot = FEMapCreatorProfileCore.Validate("FEMapCreator.exe", "");

            Assert.Equal(FEMapCreatorSetupStatus.Invalid, snapshot.Status);
        }

        [Fact]
        public void ConfigKeys_AreDistinctAndStable()
        {
            Assert.Equal("femapcreator_path", FEMapCreatorProfileCore.ExecutablePathConfigKey);
            Assert.Equal("femapcreator_assets_root", FEMapCreatorProfileCore.AssetsRootConfigKey);
            Assert.NotEqual(FEMapCreatorProfileCore.ExecutablePathConfigKey, FEMapCreatorProfileCore.AssetsRootConfigKey);
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "femapcreator_profile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateEmptyFile(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, Array.Empty<byte>());
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
