// #1380 Part B — seed-navigation tests for the FE-Repo Resource Browser VM.
//
// Marked [Collection("SharedState")] because the VM resolves its repo root via
// CoreState.BaseDirectory, which these tests temporarily point at a fake
// populated FE-Repo tree.
using System;
using System.IO;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class FERepoBrowserSeedTests
    {
        static string MakeFakeRepo(out string root)
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "febuilder-ferepo-seed-" + Path.GetRandomFileName());
            root = Path.Combine(baseDir, "resources", "FE-Repo");
            // Two categories; one with a subcategory containing a PNG.
            string mapSprites = Path.Combine(root, "Map Sprites");
            Directory.CreateDirectory(mapSprites);
            File.WriteAllText(Path.Combine(mapSprites, "hero.png"), "mock");

            string itemIcons = Path.Combine(root, "Item Icons", "Special - Generic Minimugs");
            Directory.CreateDirectory(itemIcons);
            File.WriteAllText(Path.Combine(itemIcons, "minimug.png"), "mock");
            return baseDir;
        }

        [Fact]
        public void Seed_TopLevelCategory_PreSelects()
        {
            string baseDir = MakeFakeRepo(out _);
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(false, "Map Sprites", null);

                Assert.False(vm.NotFound);
                Assert.NotNull(vm.SelectedCategory);
                Assert.Equal("Map Sprites", vm.SelectedCategory.Category);
                // The seeded category's files were loaded.
                Assert.Single(vm.ResourceFiles);
                Assert.Equal("hero.png", vm.ResourceFiles[0].FileName);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void Seed_SubCategory_PreSelectsChild()
        {
            string baseDir = MakeFakeRepo(out _);
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(false, "Item Icons", "Special - Generic Minimugs");

                Assert.NotNull(vm.SelectedCategory);
                Assert.Equal("Item Icons", vm.SelectedCategory.Category);
                Assert.Equal("Special - Generic Minimugs", vm.SelectedCategory.SubCategory);
                Assert.Single(vm.ResourceFiles);
                Assert.Equal("minimug.png", vm.ResourceFiles[0].FileName);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void Seed_NonMatchingCategory_LeavesNoSelection()
        {
            string baseDir = MakeFakeRepo(out _);
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(false, "No Such Category", null);

                Assert.False(vm.NotFound);
                Assert.Null(vm.SelectedCategory);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void EmptyPlaceholder_SetsNotFound()
        {
            // #1380 Part A: an empty submodule placeholder must surface NotFound
            // (which the View binds to the "copy git command" affordance).
            string baseDir = Path.Combine(Path.GetTempPath(), "febuilder-ferepo-empty-" + Path.GetRandomFileName());
            string root = Path.Combine(baseDir, "resources", "FE-Repo");
            Directory.CreateDirectory(root); // empty
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(false, "Map Sprites", null);

                Assert.True(vm.NotFound);
                Assert.Empty(vm.Categories);
                Assert.Contains(FERepoResourceBrowserViewModel.SubmoduleInitCommand, vm.StatusText);
                Assert.Equal(FERepoResourceBrowserViewModel.SubmoduleInitCommand, vm.EffectiveInitCommand);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        static string MakeFakeMusicRepo(out string firstCategory)
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "febuilder-ferepo-music-seed-" + Path.GetRandomFileName());
            firstCategory = "Battle Themes";
            string cat = Path.Combine(baseDir, "resources", "FE-Repo-Music-No-Preview", firstCategory);
            Directory.CreateDirectory(cat);
            // One of each advertised music extension so a parameterized test can
            // assert the music-mode selection works for all of them.
            File.WriteAllText(Path.Combine(cat, "song.s"), "@ mock sondfont source");
            File.WriteAllText(Path.Combine(cat, "tune.mid"), "MThd-mock");
            File.WriteAllText(Path.Combine(cat, "sample.wav"), "RIFF-mock");
            return baseDir;
        }

        [Theory]
        [InlineData("song.s")]
        [InlineData("tune.mid")]
        [InlineData("sample.wav")]
        public void MusicMode_SelectingMusicFile_EnablesInsert_NoBitmap(string fileName)
        {
            // #1383: music files are NOT images. OnSelectedFileChanged must set
            // SelectedFilePath + CanInsert WITHOUT attempting a Bitmap preview,
            // otherwise the Insert button is permanently dead in music mode.
            string baseDir = MakeFakeMusicRepo(out string cat);
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(true, cat, null);
                Assert.False(vm.NotFound);
                Assert.NotEmpty(vm.ResourceFiles);

                var item = System.Linq.Enumerable.First(vm.ResourceFiles, f => f.FileName == fileName);
                vm.SelectedFile = item;

                Assert.True(vm.CanInsert, $"{fileName} should enable Insert in music mode");
                Assert.Equal(item.FullPath, vm.SelectedFilePath);
                Assert.Null(vm.PreviewImage); // no image preview for music files
                Assert.Contains(fileName, vm.StatusText);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void MusicMode_Seed_TopLevelCategory_PreSelects()
        {
            string baseDir = MakeFakeMusicRepo(out string cat);
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(true, cat, null);

                Assert.False(vm.NotFound);
                Assert.NotNull(vm.SelectedCategory);
                Assert.Equal(cat, vm.SelectedCategory.Category);
                // All three mock music files were loaded.
                Assert.Equal(3, vm.ResourceFiles.Count);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void MusicMode_EmptyPlaceholder_ShowsMusicInitCommand()
        {
            // #1380 Copilot review: a missing MUSIC submodule must instruct the
            // user to init the MUSIC submodule, not the graphics one.
            string baseDir = Path.Combine(Path.GetTempPath(), "febuilder-ferepo-music-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(baseDir, "resources", "FE-Repo-Music-No-Preview")); // empty
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                var vm = new FERepoResourceBrowserViewModel(true, null, null);

                Assert.True(vm.NotFound);
                Assert.Equal(FERepoResourceBrowserViewModel.MusicSubmoduleInitCommand, vm.EffectiveInitCommand);
                Assert.Contains("FE-Repo-Music-No-Preview", vm.StatusText);
                Assert.Contains("FE-Repo-Music-No-Preview", vm.CopyTooltip);
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
            }
        }
    }
}
