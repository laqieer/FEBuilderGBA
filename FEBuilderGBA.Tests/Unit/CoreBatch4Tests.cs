using System;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for batch 4 Core migration: GitInstaller, GitUtil (now using CoreState),
    /// CoreState.GitPath/ReleaseSource properties, and Core U.HttpGet.
    /// </summary>
    public class CoreBatch4Tests : IDisposable
    {
        private readonly string _savedLanguage;
        private readonly int _savedReleaseSource;
        private readonly string _savedGitPath;

        public CoreBatch4Tests()
        {
            _savedLanguage = CoreState.Language;
            _savedReleaseSource = CoreState.ReleaseSource;
            _savedGitPath = CoreState.GitPath;
        }

        public void Dispose()
        {
            CoreState.Language = _savedLanguage;
            CoreState.ReleaseSource = _savedReleaseSource;
            CoreState.GitPath = _savedGitPath;
        }

        // ---- CoreState new properties ----

        [Fact]
        public void CoreState_GitPath_DefaultIsGit()
        {
            // Fresh CoreState should default to "git"
            Assert.Equal("git", CoreState.GitPath);
        }

        [Fact]
        public void CoreState_ReleaseSource_DefaultIsZero()
        {
            Assert.Equal(0, CoreState.ReleaseSource);
        }

        [Fact]
        public void CoreState_GitPath_CanBeSetAndRead()
        {
            CoreState.GitPath = @"C:\Program Files\Git\cmd\git.exe";
            Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", CoreState.GitPath);
        }

        [Fact]
        public void CoreState_ReleaseSource_CanBeSetAndRead()
        {
            CoreState.ReleaseSource = 2;
            Assert.Equal(2, CoreState.ReleaseSource);
        }

        // ---- GitInstaller ----

        [Fact]
        public void GitInstaller_IsPublicStaticClass()
        {
            var type = typeof(GitInstaller);
            Assert.True(type.IsPublic);
            Assert.True(type.IsAbstract && type.IsSealed); // static class
        }

        [Fact]
        public void GitInstaller_GetLatestInstallerUrl_ReturnsNullOrUrl()
        {
            // This test verifies the method exists and doesn't throw.
            // It may return null if network is unavailable (which is fine for CI).
            string url = GitInstaller.GetLatestInstallerUrl();
            if (url != null)
            {
                Assert.StartsWith("https://", url);
                Assert.EndsWith(".exe", url);
            }
        }

        // ---- GitUtil (Core version using CoreState) ----

        [Fact]
        public void GitUtil_IsPublicStaticClass()
        {
            var type = typeof(GitUtil);
            Assert.True(type.IsPublic);
            Assert.True(type.IsAbstract && type.IsSealed); // static class
        }

        [Fact]
        public void GitUtil_GetPatch2RemoteUrl_UsesCoreSateLanguage()
        {
            CoreState.ReleaseSource = 0;
            CoreState.Language = "zh";
            Assert.Equal(GitUtil.Patch2RemoteUrlGitee, GitUtil.GetPatch2RemoteUrl());

            CoreState.Language = "en";
            Assert.Equal(GitUtil.Patch2RemoteUrl, GitUtil.GetPatch2RemoteUrl());
        }

        [Fact]
        public void GitUtil_GetPatch2RemoteUrl_UsesCoreStateReleaseSource()
        {
            CoreState.Language = "ja"; // not Chinese

            CoreState.ReleaseSource = 1; // GitHub explicit
            Assert.Equal(GitUtil.Patch2RemoteUrl, GitUtil.GetPatch2RemoteUrl());

            CoreState.ReleaseSource = 2; // Gitee explicit
            Assert.Equal(GitUtil.Patch2RemoteUrlGitee, GitUtil.GetPatch2RemoteUrl());
        }

        [Fact]
        public void GitUtil_FindGitExecutable_UsesCoreStateGitPath()
        {
            // Set a non-existent custom git path — FindGitExecutable should skip it
            CoreState.GitPath = @"C:\nonexistent\path\git.exe";
            // The method should still try to find git on PATH or common locations
            // It may return null or a valid path — we just verify it doesn't throw
            string result = GitUtil.FindGitExecutable();
            // result is either null (git not found) or a valid path
            if (result != null)
            {
                Assert.True(result == "git" || File.Exists(result),
                    $"FindGitExecutable returned '{result}' which is not 'git' and doesn't exist");
            }
        }

        [Fact]
        public void GitUtil_IsGitRepo_ReturnsTrueForDotGitDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(),
                "FEBuilderGBA_CoreBatch4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // No .git yet
                Assert.False(GitUtil.IsGitRepo(tempDir));

                // Create .git directory
                Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
                Assert.True(GitUtil.IsGitRepo(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void GitUtil_IsGitRepo_HandlesBrokenSubmoduleLink()
        {
            string tempDir = Path.Combine(Path.GetTempPath(),
                "FEBuilderGBA_CoreBatch4_sub_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Write a .git file with a broken gitdir reference
                File.WriteAllText(Path.Combine(tempDir, ".git"),
                    "gitdir: ../../.git/modules/nonexistent");
                Assert.False(GitUtil.IsGitRepo(tempDir));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void GitUtil_ProbeGit_ReturnsFalseForNonexistentExe()
        {
            Assert.False(GitUtil.ProbeGit(@"C:\nonexistent\git.exe"));
        }
    }
}
