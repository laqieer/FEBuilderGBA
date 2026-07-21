// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 3: backend orchestration tests for RandomMapOneClickService — the pure decision
// logic (external vs. built-in, per RandomMapBackendSelectorCore) lives in
// RandomMapBackendSelectorCoreTests (FEBuilderGBA.Core.Tests); this file exercises the async
// orchestration itself with fully injected delegates so no real FEMapCreator process, ROM disk
// I/O, or config file is ever touched.
using System;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class RandomMapOneClickServiceTests
    {
        [AvaloniaFact]
        public async Task GenerateAsync_NoMapping_UsesBuiltInWithTypedNoMappingState()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);

            bool builtInInvoked = false;
            bool externalInvoked = false;
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) =>
                {
                    externalInvoked = true;
                    throw new InvalidOperationException("must not run external backend");
                },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    builtInInvoked = true;
                    result = MakeBuiltInSuccess(width, height, seed);
                    error = "";
                    return true;
                },
                resolveMapping: fingerprint => (
                    new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.NotConfigured, "", "", ""),
                    FEMapCreatorMappingLookupResult.NoMapping()));

            RandomMapOneClickResult result = await service.GenerateAsync(
                rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 111, CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(builtInInvoked);
            Assert.False(externalInvoked);
            Assert.Equal(RandomMapBackendUsed.BuiltIn, result.BackendUsed);
            Assert.Equal(FEMapCreatorMappingStatus.NoMapping, result.MappingStatus);
            Assert.Equal("", result.MappingReason);
            Assert.NotNull(result.Outcome);
            Assert.Equal(111, result.Outcome!.EffectiveSeed);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_CurrentMapping_UsesExternalOnlyAndNeverInvokesBuiltIn()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);

            bool builtInInvoked = false;
            var entry = MakeMappingEntry("Grassland");
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) =>
                {
                    Assert.Equal("Grassland", request.TilesetName);
                    Assert.Equal(2, request.Width);
                    Assert.Equal(2, request.Height);
                    return new RandomMapGenerationResult
                    {
                        Success = true,
                        Mars = new ushort[] { 10, 11, 12, 13 },
                    };
                },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    builtInInvoked = true;
                    result = null;
                    error = "";
                    return false;
                },
                resolveMapping: fingerprint => (
                    new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Configured, @"C:\trusted\FEMapCreator.exe", "", ""),
                    FEMapCreatorMappingLookupResult.Current(entry)));

            RandomMapOneClickResult result = await service.GenerateAsync(
                rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 55, CancellationToken.None);

            Assert.True(result.Success);
            Assert.False(builtInInvoked);
            Assert.Equal(RandomMapBackendUsed.External, result.BackendUsed);
            Assert.Equal(FEMapCreatorMappingStatus.Current, result.MappingStatus);
            Assert.Equal("", result.MappingReason);
            Assert.Equal(new ushort[] { 10, 11, 12, 13 }, result.Outcome!.Mars);
            Assert.Equal(55, result.Outcome.EffectiveSeed);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_StaleMapping_UsesBuiltInAndPreservesTypedReason()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);

            bool externalInvoked = false;
            var entry = MakeMappingEntry("Grassland");
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) =>
                {
                    externalInvoked = true;
                    throw new InvalidOperationException("must not run external backend");
                },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    result = MakeBuiltInSuccess(width, height, seed);
                    error = "";
                    return true;
                },
                resolveMapping: fingerprint => (
                    new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Configured, @"C:\trusted\FEMapCreator.exe", "", ""),
                    FEMapCreatorMappingLookupResult.Stale(entry, "the mapped image file changed size")));

            RandomMapOneClickResult result = await service.GenerateAsync(
                rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 7, CancellationToken.None);

            Assert.True(result.Success);
            Assert.False(externalInvoked);
            Assert.Equal(RandomMapBackendUsed.BuiltIn, result.BackendUsed);
            Assert.Equal(FEMapCreatorMappingStatus.Stale, result.MappingStatus);
            Assert.Contains("changed size", result.MappingReason, StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_StartedExternalFailure_DoesNotFallBackToBuiltIn()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);

            bool builtInInvoked = false;
            var entry = MakeMappingEntry("Grassland");
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) => new RandomMapGenerationResult
                {
                    Success = false,
                    ErrorCategory = RandomMapGeneratorErrorCategory.NonZeroExit,
                    ErrorMessage = "FEMapCreator exited with code 9.",
                },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    builtInInvoked = true;
                    result = null;
                    error = "";
                    return false;
                },
                resolveMapping: fingerprint => (
                    new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.Configured, @"C:\trusted\FEMapCreator.exe", "", ""),
                    FEMapCreatorMappingLookupResult.Current(entry)));

            RandomMapOneClickResult result = await service.GenerateAsync(
                rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(builtInInvoked);
            Assert.Contains("code 9", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_UnresolvableTileset_FailsWithoutInvokingEitherBackend()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateRom();
            bool builtInInvoked = false;
            bool externalInvoked = false;
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) => { externalInvoked = true; return new RandomMapGenerationResult { Success = true }; },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    builtInInvoked = true;
                    result = null;
                    error = "";
                    return true;
                },
                resolveMapping: fingerprint => throw new InvalidOperationException("must not resolve mapping before tileset is known"));

            RandomMapOneClickResult result = await service.GenerateAsync(
                rom, mapSettingAddr: 0x300, 2, 2, currentGrid: null, seed: 1, CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(builtInInvoked);
            Assert.False(externalInvoked);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_TypedBuiltInErrors_MapToLocalizedActionableMessages()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);

            async Task<string> RunWithError(BuiltInRandomMapErrorCategory category)
            {
                var service = new RandomMapOneClickService(
                    runner: null,
                    generateExternal: (request, runner) => throw new InvalidOperationException(),
                    generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                        out BuiltInRandomMapGenerationResult? result, out string error) =>
                    {
                        result = MakeBuiltInFailure(category, "detail-" + category);
                        error = "";
                        return true;
                    },
                    resolveMapping: fingerprint => (
                        new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.NotConfigured, "", "", ""),
                        FEMapCreatorMappingLookupResult.NoMapping()));

                RandomMapOneClickResult result = await service.GenerateAsync(
                    rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 1, CancellationToken.None);
                Assert.False(result.Success);
                return result.ErrorMessage;
            }

            Assert.Contains("invalid", await RunWithError(BuiltInRandomMapErrorCategory.InvalidInput), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("enough source", await RunWithError(BuiltInRandomMapErrorCategory.InsufficientSourceData), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cancelled", await RunWithError(BuiltInRandomMapErrorCategory.Cancelled), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("search budget", await RunWithError(BuiltInRandomMapErrorCategory.SearchExhausted), StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaFact]
        public async Task GenerateAsync_Cancelled_ThrowsOperationCanceledAndInvokesNeitherBackend()
        {
            ROM rom = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001, out uint mapSettingAddr, out _, out _);
            bool builtInInvoked = false;
            bool externalInvoked = false;
            var service = new RandomMapOneClickService(
                runner: null,
                generateExternal: (request, runner) => { externalInvoked = true; return new RandomMapGenerationResult { Success = true }; },
                generateBuiltIn: (ROM r, uint addr, int width, int height, ushort[]? currentGrid, int seed, CancellationToken ct,
                    out BuiltInRandomMapGenerationResult? result, out string error) =>
                {
                    builtInInvoked = true;
                    result = null;
                    error = "";
                    return true;
                },
                resolveMapping: fingerprint => (
                    new FEMapCreatorSetupSnapshot(FEMapCreatorSetupStatus.NotConfigured, "", "", ""),
                    FEMapCreatorMappingLookupResult.NoMapping()));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.GenerateAsync(rom, mapSettingAddr, 2, 2, currentGrid: null, seed: 1, cts.Token));

            Assert.False(builtInInvoked);
            Assert.False(externalInvoked);
        }

        static BuiltInRandomMapGenerationResult MakeBuiltInSuccess(int width, int height, int seed)
        {
            var mars = new ushort[width * height];
            return new BuiltInRandomMapGenerationResult(
                success: true,
                errorCategory: BuiltInRandomMapErrorCategory.None,
                errorMessage: "",
                mars: mars,
                effectiveSeed: seed,
                adjacencyModel: BuiltInRandomMapAdjacencyModel.Strict,
                restartsUsed: 1,
                distinctChipsetsUsed: 1);
        }

        static BuiltInRandomMapGenerationResult MakeBuiltInFailure(BuiltInRandomMapErrorCategory category, string detail) =>
            new BuiltInRandomMapGenerationResult(
                success: false,
                errorCategory: category,
                errorMessage: detail,
                mars: Array.Empty<ushort>(),
                effectiveSeed: 0,
                adjacencyModel: BuiltInRandomMapAdjacencyModel.Strict,
                restartsUsed: 0,
                distinctChipsetsUsed: 0);

        static FEMapCreatorTilesetMappingEntry MakeMappingEntry(string tilesetName) =>
            new FEMapCreatorTilesetMappingEntry(
                fingerprintValue: "fingerprint-value",
                tilesetName: tilesetName,
                imagePath: @"C:\assets\grassland.png", imageSizeBytes: 100, imageLastWriteUtcTicks: 1, imageSha256: "img-hash",
                generationDataPath: @"C:\assets\grassland.json", generationDataSizeBytes: 10, generationDataLastWriteUtcTicks: 1, generationDataSha256: "gen-hash",
                executablePath: @"C:\trusted\FEMapCreator.exe", executableSizeBytes: 1000, executableLastWriteUtcTicks: 1, executableSha256: "exe-hash",
                assetsRoot: "");
    }
}
