// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>Typed status of the persisted FEMapCreator executable/assets setup (#1978 Slice 2).</summary>
    public enum FEMapCreatorSetupStatus
    {
        /// <summary>No executable path has been configured yet. This is not an error.</summary>
        NotConfigured,

        /// <summary>The executable path (and optional assets root, when supplied) both resolved to existing, valid local paths.</summary>
        Configured,

        /// <summary>An executable path was supplied but failed validation; see <see cref="FEMapCreatorSetupSnapshot.ErrorMessage"/>.</summary>
        Invalid,
    }

    /// <summary>
    /// Immutable validated snapshot of the persisted FEMapCreator executable path plus optional
    /// assets root (#1978 Slice 2). Produced by <see cref="FEMapCreatorProfileCore.Validate"/>.
    /// When <see cref="Status"/> is <see cref="FEMapCreatorSetupStatus.Configured"/>, the
    /// executable's content identity (<see cref="ExecutableSizeBytes"/>/
    /// <see cref="ExecutableLastWriteUtcTicks"/>/<see cref="ExecutableSha256"/>) is also captured
    /// so <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/> can detect that the configured
    /// executable itself has changed since a mapping was recorded, not just its path string.
    /// </summary>
    public sealed class FEMapCreatorSetupSnapshot
    {
        public FEMapCreatorSetupSnapshot(
            FEMapCreatorSetupStatus status,
            string executablePath,
            string assetsRoot,
            string errorMessage,
            long executableSizeBytes = 0,
            long executableLastWriteUtcTicks = 0,
            string executableSha256 = "")
        {
            Status = status;
            ExecutablePath = executablePath ?? "";
            AssetsRoot = assetsRoot ?? "";
            ErrorMessage = errorMessage ?? "";
            ExecutableSizeBytes = executableSizeBytes;
            ExecutableLastWriteUtcTicks = executableLastWriteUtcTicks;
            ExecutableSha256 = executableSha256 ?? "";
        }

        /// <summary>Overall setup status.</summary>
        public FEMapCreatorSetupStatus Status { get; }

        /// <summary>Normalized absolute executable path, or "" when not configured/invalid.</summary>
        public string ExecutablePath { get; }

        /// <summary>Normalized absolute assets root, or "" when not supplied.</summary>
        public string AssetsRoot { get; }

        /// <summary>Validation failure detail; "" when <see cref="Status"/> is not <see cref="FEMapCreatorSetupStatus.Invalid"/>.</summary>
        public string ErrorMessage { get; }

        /// <summary>Executable file size in bytes at validation time; 0 unless <see cref="Status"/> is <see cref="FEMapCreatorSetupStatus.Configured"/>.</summary>
        public long ExecutableSizeBytes { get; }

        /// <summary>Executable last-write time (UTC ticks) at validation time; 0 unless <see cref="Status"/> is <see cref="FEMapCreatorSetupStatus.Configured"/>.</summary>
        public long ExecutableLastWriteUtcTicks { get; }

        /// <summary>Executable SHA-256 content hash (lowercase hex) at validation time; "" unless <see cref="Status"/> is <see cref="FEMapCreatorSetupStatus.Configured"/>.</summary>
        public string ExecutableSha256 { get; }
    }

    /// <summary>
    /// Validates the persisted FEMapCreator executable path and optional assets root (config keys
    /// <see cref="ExecutablePathConfigKey"/> / <see cref="AssetsRootConfigKey"/>), reusing
    /// <see cref="FEMapCreatorLauncherCore"/>'s existing path-normalization rules so Options/config
    /// validation never diverges from the launch-time checks used by the random-map dialog and
    /// tileset discovery (#1978 Slice 2). Pure and never throws; performs local filesystem
    /// existence checks only — it never launches a process or touches the network, so it is safe
    /// to call from Options load/construction without any explicit user action.
    /// </summary>
    public static class FEMapCreatorProfileCore
    {
        /// <summary>Config key for the FEMapCreator executable path.</summary>
        public const string ExecutablePathConfigKey = "femapcreator_path";

        /// <summary>Config key for the optional FEMapCreator assets root.</summary>
        public const string AssetsRootConfigKey = "femapcreator_assets_root";

        /// <summary>
        /// Validate a raw (possibly user-typed) executable path and optional assets root. A blank
        /// <paramref name="rawExecutablePath"/> yields <see cref="FEMapCreatorSetupStatus.NotConfigured"/>
        /// (not an error) regardless of the assets root; the assets root itself is optional and a
        /// blank value is always valid. A non-blank executable path that fails normalization or does
        /// not exist yields <see cref="FEMapCreatorSetupStatus.Invalid"/>.
        /// </summary>
        public static FEMapCreatorSetupSnapshot Validate(string rawExecutablePath, string rawAssetsRoot)
        {
            if (string.IsNullOrWhiteSpace(rawExecutablePath))
                return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.NotConfigured, "", "", "");

            if (!FEMapCreatorLauncherCore.TryNormalizeAbsoluteLocalFile(
                rawExecutablePath, requireDirectoryComponent: true,
                out string normalizedExecutablePath, out string exeError))
            {
                return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Invalid, "", "", exeError);
            }

            if (!File.Exists(normalizedExecutablePath))
            {
                return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Invalid, "", "",
                    "FEMapCreator executable does not exist: " + normalizedExecutablePath);
            }

            if (!FEMapCreatorLauncherCore.TryNormalizeAssetsDirectory(
                rawAssetsRoot, out string normalizedAssetsRoot, out string assetsError))
            {
                return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Invalid, normalizedExecutablePath, "", assetsError);
            }

            if (!FileContentIdentityCore.TryCompute(
                normalizedExecutablePath, out long exeSize, out long exeTicks, out string exeSha, out string hashError))
            {
                // File.Exists just passed above; only a race (deleted/locked between checks)
                // should ever reach here, but surface it as Invalid rather than silently
                // returning a Configured snapshot with no real executable identity.
                return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Invalid, "", "",
                    "FEMapCreator executable could not be read: " + hashError);
            }

            return new FEMapCreatorSetupSnapshot(
                FEMapCreatorSetupStatus.Configured, normalizedExecutablePath, normalizedAssetsRoot, "",
                exeSize, exeTicks, exeSha);
        }
    }
}
