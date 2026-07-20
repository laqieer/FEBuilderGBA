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
    /// </summary>
    public sealed class FEMapCreatorSetupSnapshot
    {
        public FEMapCreatorSetupSnapshot(
            FEMapCreatorSetupStatus status,
            string executablePath,
            string assetsRoot,
            string errorMessage)
        {
            Status = status;
            ExecutablePath = executablePath ?? "";
            AssetsRoot = assetsRoot ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        /// <summary>Overall setup status.</summary>
        public FEMapCreatorSetupStatus Status { get; }

        /// <summary>Normalized absolute executable path, or "" when not configured/invalid.</summary>
        public string ExecutablePath { get; }

        /// <summary>Normalized absolute assets root, or "" when not supplied.</summary>
        public string AssetsRoot { get; }

        /// <summary>Validation failure detail; "" when <see cref="Status"/> is not <see cref="FEMapCreatorSetupStatus.Invalid"/>.</summary>
        public string ErrorMessage { get; }
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

            return new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Configured, normalizedExecutablePath, normalizedAssetsRoot, "");
        }
    }
}
