// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 3 review finding #5: the explicit "map this tileset" logic — discovery, the
// discovered-tileset list, and per-fingerprint mapping save — moved from the retired standalone
// Map Editor dialog (MapTilesetMappingDialogViewModel, deleted) into OptionsViewModel so all
// FEMapCreator setup (executable/assets AND discovered mapping) lives in Options per Plan v4
// §4/§7. This file replaces MapTilesetMappingDialogViewModelTests.cs with equivalent coverage
// against the new OptionsViewModel API, plus new async-discovery/cancellation coverage (review
// finding #3). Follows the isolated temp-dir Config convention from
// FEMapCreatorOptionsConfigPersistenceTests (#1978 Slice 2).
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class OptionsViewModelTilesetMappingTests : IDisposable
    {
        readonly Config? _savedConfig;
        readonly string? _savedBaseDir;
        readonly string _baseDir;

        public OptionsViewModelTilesetMappingTests()
        {
            _savedConfig = CoreState.Config;
            _savedBaseDir = CoreState.BaseDirectory;

            _baseDir = Path.Combine(Path.GetTempPath(), $"options_tileset_map_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_baseDir, "config"));
            CoreState.BaseDirectory = _baseDir;
            CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
        }

        public void Dispose()
        {
            CoreState.Config = _savedConfig;
            CoreState.BaseDirectory = _savedBaseDir;
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { /* best effort */ }
        }

        string MakeFile(string name, byte[]? content = null)
        {
            string path = Path.Combine(_baseDir, name);
            File.WriteAllBytes(path, content ?? new byte[] { 1, 2, 3 });
            return path;
        }

        [Fact]
        public void SetTilesetContext_NeverInvokesDiscovery()
        {
            bool discoverCalled = false;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) => { discoverCalled = true; return new FEMapCreatorTilesetDiscoveryResult { Success = true }; },
                getConfig: () => CoreState.Config);

            vm.SetTilesetContext(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

            Assert.False(discoverCalled);
            Assert.False(vm.TilesetMappingSaved);
            Assert.Equal("", vm.TilesetMappingErrorMessage);
            Assert.True(vm.HasTilesetContext);
        }

        [Fact]
        public void SetTilesetContext_EmptyFingerprint_SetsStatusMessageAndNoContext()
        {
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config);
            vm.SetTilesetContext(TilesetFingerprint.Empty);
            Assert.NotEqual("", vm.TilesetMappingStatusMessage);
            Assert.False(vm.HasTilesetContext);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_NoProfileConfigured_SetsErrorAndDoesNotCallDelegate()
        {
            bool discoverCalled = false;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) => { discoverCalled = true; return new FEMapCreatorTilesetDiscoveryResult { Success = true }; },
                getConfig: () => CoreState.Config);

            await vm.DiscoverTilesetsAsync();

            Assert.False(discoverCalled);
            Assert.NotEqual("", vm.TilesetMappingErrorMessage);
            Assert.Empty(vm.Tilesets);
            Assert.False(vm.IsDiscoveringTilesets);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_ConfiguredProfile_InvokesDelegateAndPopulatesUsableTilesetsOnly()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;
            CoreState.Config![FEMapCreatorProfileCore.AssetsRootConfigKey] = "";

            bool discoverCalled = false;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    discoverCalled = true;
                    Assert.Equal(exePath, exe);
                    Assert.Null(assets);
                    var discoveryResult = new FEMapCreatorTilesetDiscoveryResult { Success = true };
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "Grassland",
                        HasImage = true,
                        HasGenerationData = true,
                        ResolvedImagePath = "g.png",
                        ResolvedGenerationDataPath = "g.json",
                        IsCompatible = true,
                    });
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "Broken",
                        HasImage = false,
                        HasGenerationData = false,
                    });
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config);

            await vm.DiscoverTilesetsAsync();

            Assert.True(discoverCalled);
            Assert.Equal("", vm.TilesetMappingErrorMessage);
            Assert.Single(vm.Tilesets);
            Assert.Equal("Grassland", vm.Tilesets[0].Name);
            Assert.False(vm.IsDiscoveringTilesets);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_DelegateFailure_SetsErrorAndClearsTilesets()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;

            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) => new FEMapCreatorTilesetDiscoveryResult
                {
                    Success = false,
                    ErrorMessage = "boom",
                },
                getConfig: () => CoreState.Config);

            await vm.DiscoverTilesetsAsync();

            Assert.Equal("boom", vm.TilesetMappingErrorMessage);
            Assert.Empty(vm.Tilesets);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_Cancelled_LeavesTilesetsEmptyAndReportsCancelledStatus()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;

            var started = new ManualResetEventSlim(false);
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    started.Set();
                    // Simulate a slow external discovery process honoring the token.
                    while (!token.IsCancellationRequested)
                        Thread.Sleep(5);
                    var discoveryResult = new FEMapCreatorTilesetDiscoveryResult { Success = true };
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "LateResult",
                        HasImage = true,
                        HasGenerationData = true,
                        IsCompatible = true,
                    });
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config);

            Task discoverTask = vm.DiscoverTilesetsAsync();
            started.Wait(TimeSpan.FromSeconds(5));
            vm.CancelTilesetDiscovery();
            await discoverTask;

            Assert.Empty(vm.Tilesets);
            Assert.False(vm.IsDiscoveringTilesets);
            Assert.Contains("cancel", vm.TilesetMappingStatusMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SaveTilesetMapping_EmptyFingerprint_FailsWithoutTouchingConfig()
        {
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config);
            vm.SetTilesetContext(TilesetFingerprint.Empty);
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland" };

            bool saved = vm.SaveTilesetMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.TilesetMappingErrorMessage);
            Assert.False(vm.TilesetMappingSaved);
        }

        [Fact]
        public void SaveTilesetMapping_NoSelection_Fails()
        {
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config);
            vm.SetTilesetContext(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

            bool saved = vm.SaveTilesetMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.TilesetMappingErrorMessage);
        }

        [Fact]
        public void SaveTilesetMapping_NoConfig_Fails()
        {
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => null);
            vm.SetTilesetContext(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland", ImagePath = "g.png", GenerationDataPath = "g.json" };

            bool saved = vm.SaveTilesetMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.TilesetMappingErrorMessage);
        }

        [Fact]
        public void SaveTilesetMapping_FullRoundTrip_PersistsRetrievableCurrentMapping()
        {
            string exePath = MakeFile("FEMapCreator.exe", new byte[] { 9, 9, 9 });
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;
            CoreState.Config![FEMapCreatorProfileCore.AssetsRootConfigKey] = "";

            string imagePath = MakeFile("grassland.png", new byte[] { 1, 2, 3, 4 });
            string genDataPath = MakeFile("grassland.json", new byte[] { 5, 6, 7, 8 });

            TilesetFingerprint fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config);
            vm.SetTilesetContext(fingerprint);
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland", ImagePath = imagePath, GenerationDataPath = genDataPath };

            bool saved = vm.SaveTilesetMapping();

            Assert.True(saved);
            Assert.True(vm.TilesetMappingSaved);
            Assert.Equal("", vm.TilesetMappingErrorMessage);

            var mappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(CoreState.Config!);
            Assert.Single(mappings);
            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(exePath, "");
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprint, profile);
            Assert.Equal(FEMapCreatorMappingStatus.Current, lookup.Status);
            Assert.Equal("Grassland", lookup.Entry.TilesetName);

            // Reload config from disk to prove the mapping was actually persisted, not just held in memory.
            Config reloaded = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
            var reloadedMappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(reloaded);
            Assert.Single(reloadedMappings);
        }
    }
}
