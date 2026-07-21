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
using System.Reflection;
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
            vm.FEMapCreatorPath = exePath;

            await vm.DiscoverTilesetsAsync();

            Assert.True(discoverCalled);
            Assert.Equal("", vm.TilesetMappingErrorMessage);
            Assert.Single(vm.Tilesets);
            Assert.Equal("Grassland", vm.Tilesets[0].Name);
            Assert.False(vm.IsDiscoveringTilesets);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_UsesCurrentUnsavedProfileInsteadOfPersistedConfig()
        {
            string persistedExePath = MakeFile("PersistedFEMapCreator.exe");
            string liveExePath = MakeFile("LiveFEMapCreator.exe");
            string persistedAssetsRoot = Path.Combine(_baseDir, "PersistedAssets");
            string liveAssetsRoot = Path.Combine(_baseDir, "LiveAssets");
            Directory.CreateDirectory(persistedAssetsRoot);
            Directory.CreateDirectory(liveAssetsRoot);
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = persistedExePath;
            CoreState.Config![FEMapCreatorProfileCore.AssetsRootConfigKey] = persistedAssetsRoot;

            bool discoverCalled = false;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    discoverCalled = true;
                    Assert.Equal(liveExePath, exe);
                    Assert.Equal(liveAssetsRoot, assets);
                    return new FEMapCreatorTilesetDiscoveryResult { Success = true };
                },
                getConfig: () => CoreState.Config)
            {
                FEMapCreatorPath = liveExePath,
                FEMapCreatorAssetsRoot = liveAssetsRoot,
            };

            await vm.DiscoverTilesetsAsync();

            Assert.True(discoverCalled);
            Assert.Equal(persistedExePath, CoreState.Config.at(FEMapCreatorProfileCore.ExecutablePathConfigKey));
            Assert.Equal(persistedAssetsRoot, CoreState.Config.at(FEMapCreatorProfileCore.AssetsRootConfigKey));
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_ClearedCurrentPathDoesNotFallBackToPersistedProfile()
        {
            string persistedExePath = MakeFile("PersistedFEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = persistedExePath;

            bool discoverCalled = false;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    discoverCalled = true;
                    return new FEMapCreatorTilesetDiscoveryResult { Success = true };
                },
                getConfig: () => CoreState.Config);

            await vm.DiscoverTilesetsAsync();

            Assert.False(discoverCalled);
            Assert.NotEqual("", vm.TilesetMappingErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ChangingCurrentProfileAfterDiscovery_ClearsStaleTilesetsAndSelection(bool changeAssetsRoot)
        {
            string firstExePath = MakeFile("FirstFEMapCreator.exe");
            string secondExePath = MakeFile("SecondFEMapCreator.exe");
            string firstAssetsRoot = Path.Combine(_baseDir, "FirstAssets");
            string secondAssetsRoot = Path.Combine(_baseDir, "SecondAssets");
            Directory.CreateDirectory(firstAssetsRoot);
            Directory.CreateDirectory(secondAssetsRoot);

            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
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
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config)
            {
                FEMapCreatorPath = firstExePath,
                FEMapCreatorAssetsRoot = firstAssetsRoot,
            };

            await vm.DiscoverTilesetsAsync();
            Assert.Single(vm.Tilesets);
            vm.SelectedTileset = vm.Tilesets[0];

            if (changeAssetsRoot)
                vm.FEMapCreatorAssetsRoot = secondAssetsRoot;
            else
                vm.FEMapCreatorPath = secondExePath;

            Assert.Empty(vm.Tilesets);
            Assert.Null(vm.SelectedTileset);
            Assert.False(vm.TilesetMappingSaved);
        }

        [Fact]
        public async Task ChangingCurrentProfileDuringDiscovery_CancelsAndDiscardsLateResults()
        {
            string firstExePath = MakeFile("FirstFEMapCreator.exe");
            string secondExePath = MakeFile("SecondFEMapCreator.exe");
            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);

            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    started.Set();
                    while (!token.IsCancellationRequested && !release.IsSet)
                        Thread.Sleep(5);
                    var discoveryResult = new FEMapCreatorTilesetDiscoveryResult { Success = true };
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "LateResult",
                        HasImage = true,
                        HasGenerationData = true,
                        ResolvedImagePath = "late.png",
                        ResolvedGenerationDataPath = "late.json",
                        IsCompatible = true,
                    });
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config)
            {
                FEMapCreatorPath = firstExePath,
            };

            Task discoveryTask = vm.DiscoverTilesetsAsync();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

            vm.FEMapCreatorPath = secondExePath;
            release.Set();
            await discoveryTask;

            Assert.Empty(vm.Tilesets);
            Assert.Null(vm.SelectedTileset);
            Assert.Contains("cancel", vm.TilesetMappingStatusMessage, StringComparison.OrdinalIgnoreCase);
            AssertNoStaleDiscoveryCts(vm);
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
            vm.FEMapCreatorPath = exePath;

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
            vm.FEMapCreatorPath = exePath;

            Task discoverTask = vm.DiscoverTilesetsAsync();
            started.Wait(TimeSpan.FromSeconds(5));
            vm.CancelTilesetDiscovery();
            await discoverTask;

            Assert.Empty(vm.Tilesets);
            Assert.False(vm.IsDiscoveringTilesets);
            Assert.Contains("cancel", vm.TilesetMappingStatusMessage, StringComparison.OrdinalIgnoreCase);
            AssertNoStaleDiscoveryCts(vm);
        }

        static CancellationTokenSource? GetTilesetDiscoveryCts(OptionsViewModel vm)
        {
            FieldInfo? field = typeof(OptionsViewModel).GetField(
                "_tilesetDiscoveryCts", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (CancellationTokenSource?)field!.GetValue(vm);
        }

        // #1978 Slice 3 re-review finding #1: after DiscoverTilesetsAsync fully completes
        // (success, failure, or cancellation), the shared _tilesetDiscoveryCts field must be
        // cleared back to null (its owning run disposed its own source in `finally`) rather than
        // left pointing at a disposed-but-still-referenced instance.
        static void AssertNoStaleDiscoveryCts(OptionsViewModel vm) => Assert.Null(GetTilesetDiscoveryCts(vm));

        [Fact]
        public async Task DiscoverTilesetsAsync_SuccessfulCompletion_ClearsCancellationTokenSourceField()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;

            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) => new FEMapCreatorTilesetDiscoveryResult { Success = true },
                getConfig: () => CoreState.Config);
            vm.FEMapCreatorPath = exePath;

            Assert.Null(GetTilesetDiscoveryCts(vm));
            await vm.DiscoverTilesetsAsync();

            AssertNoStaleDiscoveryCts(vm);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_DelegateFailure_ClearsCancellationTokenSourceField()
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
            vm.FEMapCreatorPath = exePath;

            await vm.DiscoverTilesetsAsync();

            AssertNoStaleDiscoveryCts(vm);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_NoProfileConfigured_ClearsCancellationTokenSourceField()
        {
            // The early "not configured" return happens before the discovery Task.Run — proving
            // the CTS created for this attempt is still disposed/cleared even on that early path
            // (this path previously leaked a never-disposed CancellationTokenSource).
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config);

            await vm.DiscoverTilesetsAsync();

            AssertNoStaleDiscoveryCts(vm);
        }

        [Fact]
        public async Task DiscoverTilesetsAsync_ConcurrentCall_IsIgnoredWithoutCancellingOrReplacingFirstRun()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = exePath;

            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            int invocationCount = 0;
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    Interlocked.Increment(ref invocationCount);
                    started.Set();
                    // Simulate a slow external discovery process; must not observe cancellation
                    // from a concurrent second DiscoverTilesetsAsync() call (finding #2 — a
                    // duplicate concurrent call must be ignored, not cancel the first run).
                    while (!token.IsCancellationRequested && !release.IsSet)
                        Thread.Sleep(5);
                    Assert.False(token.IsCancellationRequested);
                    var discoveryResult = new FEMapCreatorTilesetDiscoveryResult { Success = true };
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "Grassland",
                        HasImage = true,
                        HasGenerationData = true,
                        IsCompatible = true,
                        ResolvedImagePath = "g.png",
                        ResolvedGenerationDataPath = "g.json",
                    });
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config);
            vm.FEMapCreatorPath = exePath;

            Task firstCall = vm.DiscoverTilesetsAsync();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

            // A second concurrent call while the first is in flight must be ignored: it must
            // return promptly without invoking the delegate again and without disturbing the
            // first run's busy/cancellation state.
            Task secondCall = vm.DiscoverTilesetsAsync();
            await secondCall;

            Assert.Equal(1, invocationCount);
            Assert.True(vm.IsDiscoveringTilesets); // first run still in flight, untouched

            release.Set();
            await firstCall;

            Assert.Equal(1, invocationCount);
            Assert.False(vm.IsDiscoveringTilesets);
            Assert.Single(vm.Tilesets);
            Assert.Equal("Grassland", vm.Tilesets[0].Name);
            AssertNoStaleDiscoveryCts(vm);
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
        public async Task SaveTilesetMapping_UsesCurrentProfileAndPersistsRetrievableMapping()
        {
            string persistedExePath = MakeFile("PersistedFEMapCreator.exe", new byte[] { 1, 1, 1 });
            string liveExePath = MakeFile("LiveFEMapCreator.exe", new byte[] { 9, 9, 9 });
            CoreState.Config![FEMapCreatorProfileCore.ExecutablePathConfigKey] = persistedExePath;

            string imagePath = MakeFile("grassland.png", new byte[] { 1, 2, 3, 4 });
            string genDataPath = MakeFile("grassland.json", new byte[] { 5, 6, 7, 8 });

            TilesetFingerprint fingerprint = TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
            var vm = new OptionsViewModel(
                discoverTilesets: (exe, assets, token) =>
                {
                    var discoveryResult = new FEMapCreatorTilesetDiscoveryResult { Success = true };
                    discoveryResult.Tilesets.Add(new FEMapCreatorTilesetInfo
                    {
                        Name = "Grassland",
                        HasImage = true,
                        HasGenerationData = true,
                        ResolvedImagePath = imagePath,
                        ResolvedGenerationDataPath = genDataPath,
                        IsCompatible = true,
                    });
                    return discoveryResult;
                },
                getConfig: () => CoreState.Config);
            vm.FEMapCreatorPath = liveExePath;
            vm.FEMapCreatorAssetsRoot = "";
            vm.SetTilesetContext(fingerprint);
            await vm.DiscoverTilesetsAsync();
            Assert.Single(vm.Tilesets);
            vm.SelectedTileset = vm.Tilesets[0];

            bool saved = vm.SaveTilesetMapping();

            Assert.True(saved);
            Assert.True(vm.TilesetMappingSaved);
            Assert.Equal("", vm.TilesetMappingErrorMessage);
            Assert.Equal(liveExePath, CoreState.Config.at(FEMapCreatorProfileCore.ExecutablePathConfigKey));
            Assert.Equal("", CoreState.Config.at(FEMapCreatorProfileCore.AssetsRootConfigKey));

            var mappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(CoreState.Config!);
            Assert.Single(mappings);
            FEMapCreatorSetupSnapshot profile = FEMapCreatorProfileCore.Validate(liveExePath, "");
            FEMapCreatorMappingLookupResult lookup = FEMapCreatorTilesetMappingStoreCore.Lookup(mappings, fingerprint, profile);
            Assert.Equal(FEMapCreatorMappingStatus.Current, lookup.Status);
            Assert.Equal("Grassland", lookup.Entry.TilesetName);

            // Reload config from disk to prove the mapping was actually persisted, not just held in memory.
            Config reloaded = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
            var reloadedMappings = FEMapCreatorTilesetMappingStoreCore.LoadAll(reloaded);
            Assert.Single(reloadedMappings);
        }

        [Fact]
        public void SaveTilesetMapping_RejectsSelectionNotProducedByCurrentDiscovery()
        {
            string exePath = MakeFile("FEMapCreator.exe");
            string imagePath = MakeFile("grassland.png");
            string genDataPath = MakeFile("grassland.json");
            var vm = new OptionsViewModel(discoverTilesets: null, getConfig: () => CoreState.Config)
            {
                FEMapCreatorPath = exePath,
            };
            vm.SetTilesetContext(TilesetFingerprint.Compute(8, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));
            vm.SelectedTileset = new FEMapCreatorTilesetOption
            {
                Name = "Grassland",
                ImagePath = imagePath,
                GenerationDataPath = genDataPath,
            };

            bool saved = vm.SaveTilesetMapping();

            Assert.False(saved);
            Assert.Equal(R._("Discover and choose a compatible tileset first."), vm.TilesetMappingErrorMessage);
            Assert.Empty(FEMapCreatorTilesetMappingStoreCore.LoadAll(CoreState.Config!));
        }
    }
}
