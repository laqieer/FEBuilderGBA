// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 3: MapTilesetMappingDialogViewModel tests — the only place
// FEMapCreatorTilesetDiscoveryCore may run, and only on an explicit DiscoverTilesets() call,
// never from Initialize()/construction. Follows the isolated Config.LoadOrCreate temp-dir
// pattern from FEMapCreatorOptionsConfigPersistenceTests (#1799/#1978 Slice 2).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MapTilesetMappingDialogViewModelTests : IDisposable
    {
        readonly Config? _savedConfig;
        readonly string? _savedBaseDir;
        readonly string _baseDir;

        public MapTilesetMappingDialogViewModelTests()
        {
            _savedConfig = CoreState.Config;
            _savedBaseDir = CoreState.BaseDirectory;

            _baseDir = Path.Combine(Path.GetTempPath(), $"maptileset_vm_{Guid.NewGuid():N}");
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
        public void Initialize_NeverInvokesDiscovery()
        {
            bool discoverCalled = false;
            var vm = new MapTilesetMappingDialogViewModel(
                discoverTilesets: (exe, assets, runner) => { discoverCalled = true; return new FEMapCreatorTilesetDiscoveryResult { Success = true }; },
                getConfig: () => CoreState.Config);

            vm.Initialize(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

            Assert.False(discoverCalled);
            Assert.False(vm.Saved);
            Assert.Equal("", vm.ErrorMessage);
        }

        [Fact]
        public void Initialize_EmptyFingerprint_SetsStatusMessage()
        {
            var vm = new MapTilesetMappingDialogViewModel(getConfig: () => CoreState.Config);
            vm.Initialize(TilesetFingerprint.Empty);
            Assert.NotEqual("", vm.StatusMessage);
        }

        [Fact]
        public void DiscoverTilesets_NoProfileConfigured_SetsErrorAndDoesNotCallDelegate()
        {
            bool discoverCalled = false;
            var vm = new MapTilesetMappingDialogViewModel(
                discoverTilesets: (exe, assets, runner) => { discoverCalled = true; return new FEMapCreatorTilesetDiscoveryResult { Success = true }; },
                getConfig: () => CoreState.Config);

            vm.DiscoverTilesets();

            Assert.False(discoverCalled);
            Assert.NotEqual("", vm.ErrorMessage);
            Assert.Empty(vm.Tilesets);
        }

        [Fact]
        public void DiscoverTilesets_ConfiguredProfile_InvokesDelegateAndPopulatesUsableTilesetsOnly()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;
            CoreState.Config![FEMapCreatorProfileCore.AssetsRootConfigKey] = "";

            bool discoverCalled = false;
            var vm = new MapTilesetMappingDialogViewModel(
                discoverTilesets: (exe, assets, runner) =>
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

            vm.DiscoverTilesets();

            Assert.True(discoverCalled);
            Assert.Equal("", vm.ErrorMessage);
            Assert.Single(vm.Tilesets);
            Assert.Equal("Grassland", vm.Tilesets[0].Name);
        }

        [Fact]
        public void DiscoverTilesets_DelegateFailure_SetsErrorAndClearsTilesets()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;

            var vm = new MapTilesetMappingDialogViewModel(
                discoverTilesets: (exe, assets, runner) => new FEMapCreatorTilesetDiscoveryResult
                {
                    Success = false,
                    ErrorMessage = "boom",
                },
                getConfig: () => CoreState.Config);

            vm.DiscoverTilesets();

            Assert.Equal("boom", vm.ErrorMessage);
            Assert.Empty(vm.Tilesets);
        }

        [Fact]
        public void SaveMapping_EmptyFingerprint_FailsWithoutTouchingConfig()
        {
            var vm = new MapTilesetMappingDialogViewModel(getConfig: () => CoreState.Config);
            vm.Initialize(TilesetFingerprint.Empty);
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland" };

            bool saved = vm.SaveMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.ErrorMessage);
            Assert.False(vm.Saved);
        }

        [Fact]
        public void SaveMapping_NoSelection_Fails()
        {
            var vm = new MapTilesetMappingDialogViewModel(getConfig: () => CoreState.Config);
            vm.Initialize(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

            bool saved = vm.SaveMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.ErrorMessage);
        }

        [Fact]
        public void SaveMapping_NoConfig_Fails()
        {
            var vm = new MapTilesetMappingDialogViewModel(getConfig: () => null);
            vm.Initialize(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland", ImagePath = "g.png", GenerationDataPath = "g.json" };

            bool saved = vm.SaveMapping();

            Assert.False(saved);
            Assert.NotEqual("", vm.ErrorMessage);
        }

        [Fact]
        public void SaveMapping_FullRoundTrip_PersistsRetrievableCurrentMapping()
        {
            string exePath = MakeFile("FEMapCreator.exe", new byte[] { 9, 9, 9 });
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;
            CoreState.Config![FEMapCreatorProfileCore.AssetsRootConfigKey] = "";

            string imagePath = MakeFile("grassland.png", new byte[] { 1, 2, 3, 4 });
            string genDataPath = MakeFile("grassland.json", new byte[] { 5, 6, 7, 8 });

            TilesetFingerprint fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
            var vm = new MapTilesetMappingDialogViewModel(getConfig: () => CoreState.Config);
            vm.Initialize(fingerprint);
            vm.SelectedTileset = new FEMapCreatorTilesetOption { Name = "Grassland", ImagePath = imagePath, GenerationDataPath = genDataPath };

            bool saved = vm.SaveMapping();

            Assert.True(saved);
            Assert.True(vm.Saved);
            Assert.Equal("", vm.ErrorMessage);

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
