// SPDX-License-Identifier: GPL-3.0-or-later
// #1641 — verify the in-app empty-state for the Android patch2 / FE-Repo
// documented limitation. The platform decision is routed through the injectable
// AndroidResourceNoticeCore.IsAndroidOverride seam so these desktop tests can
// force the Android branch (no Android build required) and assert that:
//   - PatchManagerViewModel surfaces the patch2-unavailable notice when the list
//     resolves empty on "Android";
//   - FERepoResourceBrowserViewModel shows the FE-Repo-unavailable notice when the
//     submodule is absent on "Android", and PRESERVES the desktop "git submodule"
//     hint on desktop.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AndroidResourceEmptyStateTests
    {
        // Clear the VM's private _allPatches list so the empty-state branch is exercised
        // deterministically, independent of whether the desktop test host happens to have a
        // populated config/patch2/{version} dir.
        static void ClearAllPatches(PatchManagerViewModel vm)
        {
            var field = typeof(PatchManagerViewModel)
                .GetField("_allPatches", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            ((List<PatchEntry>)field.GetValue(vm)).Clear();
        }

        [Fact]
        public void PatchManager_OnAndroid_WithNoPatches_ShowsLimitationNotice()
        {
            var savedSeam = AndroidResourceNoticeCore.IsAndroidOverride;
            try
            {
                AndroidResourceNoticeCore.IsAndroidOverride = () => true; // force Android branch

                var vm = new PatchManagerViewModel();
                ClearAllPatches(vm);          // simulate the on-device empty patch list
                vm.ApplyAndroidEmptyStateNotice();

                Assert.Equal(AndroidResourceNoticeCore.PatchLibraryUnavailableMessage, vm.StatusMessage);
            }
            finally
            {
                AndroidResourceNoticeCore.IsAndroidOverride = savedSeam;
            }
        }

        [Fact]
        public void PatchManager_OnDesktop_WithNoPatches_DoesNotShowAndroidNotice()
        {
            var savedSeam = AndroidResourceNoticeCore.IsAndroidOverride;
            try
            {
                AndroidResourceNoticeCore.IsAndroidOverride = () => false; // desktop

                var vm = new PatchManagerViewModel();
                ClearAllPatches(vm);
                vm.ApplyAndroidEmptyStateNotice();

                // Desktop keeps the existing (blank) status — no Android notice even with an empty list.
                Assert.NotEqual(AndroidResourceNoticeCore.PatchLibraryUnavailableMessage, vm.StatusMessage);
            }
            finally
            {
                AndroidResourceNoticeCore.IsAndroidOverride = savedSeam;
            }
        }

        [Fact]
        public void FERepoBrowser_OnAndroid_WithMissingSubmodule_ShowsLimitationNotice()
        {
            var savedSeam = AndroidResourceNoticeCore.IsAndroidOverride;
            string baseDir = Path.Combine(Path.GetTempPath(), "fe_android_repo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(baseDir, "resources", "FE-Repo")); // empty placeholder
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                AndroidResourceNoticeCore.IsAndroidOverride = () => true; // force Android branch

                var vm = new FERepoResourceBrowserViewModel();

                Assert.True(vm.NotFound);
                Assert.Equal(AndroidResourceNoticeCore.FERepoUnavailableMessage, vm.StatusText);
                // The desktop git-submodule command must NOT be shown on Android (it can't work there).
                Assert.DoesNotContain(FERepoResourceBrowserViewModel.SubmoduleInitCommand, vm.StatusText);
            }
            finally
            {
                AndroidResourceNoticeCore.IsAndroidOverride = savedSeam;
                CoreState.BaseDirectory = savedBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void FERepoBrowser_OnDesktop_WithMissingSubmodule_PreservesGitHint()
        {
            var savedSeam = AndroidResourceNoticeCore.IsAndroidOverride;
            string baseDir = Path.Combine(Path.GetTempPath(), "fe_desktop_repo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(baseDir, "resources", "FE-Repo")); // empty placeholder
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                AndroidResourceNoticeCore.IsAndroidOverride = () => false; // desktop

                var vm = new FERepoResourceBrowserViewModel();

                Assert.True(vm.NotFound);
                // Desktop behaviour (#1380) preserved: actionable git submodule init hint.
                Assert.Contains(FERepoResourceBrowserViewModel.SubmoduleInitCommand, vm.StatusText);
                Assert.NotEqual(AndroidResourceNoticeCore.FERepoUnavailableMessage, vm.StatusText);
            }
            finally
            {
                AndroidResourceNoticeCore.IsAndroidOverride = savedSeam;
                CoreState.BaseDirectory = savedBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }
    }
}
