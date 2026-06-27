// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Canonical, GUI-free notices for the **Android resource-delivery limitation** (#1641).
    ///
    /// <para>
    /// On desktop the binary-patch library (<c>config/patch2/</c>) and the FE-Repo graphics/music
    /// resource submodules are installed on demand via in-process git (<c>GitUtil</c>). The Android
    /// head cannot do this: it has no in-process git, the submodules are far too large to bundle as
    /// <c>AndroidAsset</c> inside the APK, and app-private SAF/<c>FilesDir</c> storage differs from the
    /// desktop "loose files beside the exe" layout. So patch2 and FE-Repo are **desktop-only for now**;
    /// an on-demand HTTP download into app-private storage is the intended future mechanism, tracked
    /// under epic #1070.
    /// </para>
    ///
    /// <para>
    /// This class is the single source of truth for the user-facing in-app empty-state message and the
    /// <see cref="IsResourceDeliverySupported"/> predicate. It performs NO ROM mutation and NO I/O.
    /// The platform decision is routed through the test-injectable <see cref="IsAndroidOverride"/> seam
    /// so desktop unit tests can force the Android branch without an Android build.
    /// </para>
    /// </summary>
    public static class AndroidResourceNoticeCore
    {
        /// <summary>
        /// Platform predicate seam. Defaults to <see cref="OperatingSystem.IsAndroid"/>. Tests may set
        /// this to force the Android branch on a desktop runner; always restore it in a try/finally so
        /// the override cannot leak between tests.
        /// </summary>
        public static Func<bool> IsAndroidOverride = OperatingSystem.IsAndroid;

        /// <summary>
        /// True when the platform supports the desktop git-backed resource delivery (i.e. NOT Android).
        /// When false, callers should surface <see cref="PatchLibraryUnavailableMessage"/> /
        /// <see cref="FERepoUnavailableMessage"/> instead of the desktop "run git submodule …" hint,
        /// which cannot work on a device.
        /// </summary>
        public static bool IsResourceDeliverySupported => !IsAndroidOverride();

        /// <summary>
        /// In-app empty-state message for the Patch Manager when running on Android (no patch2 on device).
        /// </summary>
        public const string PatchLibraryUnavailableMessage =
            "The binary-patch library (config/patch2) is not available on Android yet. " +
            "It ships on the desktop builds via git and is not bundled in the APK. " +
            "On-device patch delivery is planned (see epic #1070). Use a desktop build to install patches.";

        /// <summary>
        /// In-app empty-state message for the FE-Repo resource browser when running on Android.
        /// </summary>
        public const string FERepoUnavailableMessage =
            "FE-Repo resources are not available on Android yet. " +
            "They ship on the desktop builds via git submodules and are not bundled in the APK. " +
            "On-device resource delivery is planned (see epic #1070). Use a desktop build to browse FE-Repo.";
    }
}
