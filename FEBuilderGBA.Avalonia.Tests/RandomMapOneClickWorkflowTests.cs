// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 3: one-click Map Editor integration. Replaces the retired
// GenerateRandomMapDialogWorkflowTests.cs (dialog-first flow) with coverage for the no-dialog
// GenerateRandomMapWorkflow helpers, RandomMapOneClickService backend orchestration, and the
// explicit per-fingerprint mapping action (relocated to the Options FEMapCreator section per
// review finding #5; see OptionsViewModelTilesetMappingTests).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class RandomMapOneClickWorkflowTests : IDisposable
    {
        readonly ROM? _savedRom = CoreState.ROM;
        readonly Undo? _savedUndo = CoreState.Undo;
        readonly DecompProject? _savedDecompProject = CoreState.DecompProject;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
            CoreState.DecompProject = _savedDecompProject;
        }

        [Fact]
        public void GenerationOutcome_Mars_SourceAndGetterMutationCannotAlterStoredOutcome()
        {
            ushort[] source = { 1, 2, 3 };
            var outcome = new RandomMapGenerationOutcome { Mars = source };

            source[0] = 99;
            ushort[] publicCopy = outcome.Mars;
            publicCopy[1] = 99;

            Assert.Equal(new ushort[] { 1, 2, 3 }, outcome.Mars);
        }

        // ------------------------------------------------------------------
        // TryPrepareForGeneration — no-dialog pre-flight guard
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public void TryPrepareForGeneration_NoMapLoaded_ShowsErrorAndReturnsFalse()
        {
            var vm = new MapEditorViewModel();
            string? error = null;

            bool ready = GenerateRandomMapWorkflow.TryPrepareForGeneration(vm, _ => false, message => error = message);

            Assert.False(ready);
            Assert.Equal("No map data loaded — select a map first.", error);
        }

        [AvaloniaFact]
        public void TryPrepareForGeneration_DecompMode_BlocksBeforeGeneration()
        {
            var vm = new MapEditorViewModel { MapWidth = 15, MapHeight = 10 };
            RandomMapOneClickTestSupport.SeedMapData(vm, RandomMapOneClickTestSupport.BuildMap(15, 10, 0x0001));
            CoreState.DecompProject = new DecompProject { ProjectRoot = "." };

            bool guardCalled = false;
            bool ready = GenerateRandomMapWorkflow.TryPrepareForGeneration(
                vm,
                assetName =>
                {
                    guardCalled = true;
                    return DecompMapAssetGuard.BlockIfDecomp(assetName);
                },
                _ => { });

            Assert.False(ready);
            Assert.True(guardCalled);
        }

        [AvaloniaFact]
        public void TryPrepareForGeneration_MapLoadedNotDecomp_ReturnsTrue()
        {
            var vm = new MapEditorViewModel { MapWidth = 15, MapHeight = 10 };
            RandomMapOneClickTestSupport.SeedMapData(vm, RandomMapOneClickTestSupport.BuildMap(15, 10, 0x0001));

            bool ready = GenerateRandomMapWorkflow.TryPrepareForGeneration(
                vm,
                _ => false,
                _ => throw new InvalidOperationException("must not show error"));

            Assert.True(ready);
        }

        // ------------------------------------------------------------------
        // ApplyGeneratedMapOnUiThreadAsync — apply/rollback/identity + fingerprint revalidation
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_Success_CommitsOnceRefreshesAndShowsSeed()
        {
            CoreState.ROM = RandomMapOneClickTestSupport.CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = RandomMapOneClickTestSupport.BuildMap(2, 2, 0x0001);
            byte[] originalCompressed = LZ77.compress(originalMap);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, originalCompressed);

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            RandomMapOneClickTestSupport.ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            var sequence = new List<string>();
            var undo = new RandomMapOneClickTestSupport.RecordingUndoService(sequence);
            int refreshCalls = 0;
            int paletteCalls = 0;
            bool callbacksOnUiThread = true;
            string infoMessage = "";
            var outcome = new RandomMapGenerationOutcome
            {
                Mars = new ushort[] { 0, 4, 8, 12 },
                Width = 2,
                Height = 2,
                EffectiveSeed = 4242,
            };

            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    outcome,
                    writeIdentity,
                    TilesetFingerprint.Empty,
                    CancellationToken.None,
                    refreshMapFromCurrentSelection: () =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        refreshCalls++;
                        sequence.Add("Refresh");
                    },
                    updateTilePalette: () =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        paletteCalls++;
                        sequence.Add("Palette");
                    },
                    reloadFromRom: () => sequence.Add("Reload"),
                    showInfo: message =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        infoMessage = message;
                        sequence.Add("Info");
                    }));

            Assert.Null(error);
            Assert.True(callbacksOnUiThread);
            Assert.True(undo.BeginOnUiThread);
            Assert.True(undo.CommitOnUiThread);
            Assert.Equal(1, undo.BeginCalls);
            Assert.Equal(1, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal("MapEditor.GenerateRandomMap", undo.LastBeginName);
            Assert.Equal(new[] { "Begin", "Refresh", "Palette", "Info", "Commit" }, sequence);
            Assert.Equal(1, refreshCalls);
            Assert.Equal(1, paletteCalls);
            Assert.Contains("2x2", infoMessage);
            Assert.Contains("seed=4242", infoMessage);
            Assert.Equal(
                new byte[] { 2, 2, 0, 0, 4, 0, 8, 0, 12, 0 },
                vm.GetMapDataSnapshot());
        }

        [AvaloniaTheory]
        [InlineData("rom")]
        [InlineData("selection")]
        [InlineData("plist")]
        [InlineData("table")]
        [InlineData("pointer")]
        [InlineData("payload")]
        public async Task ApplyGeneratedMapOnUiThreadAsync_ContextChanged_AbortsBeforeUndo(
            string changeKind)
        {
            CoreState.ROM = RandomMapOneClickTestSupport.CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = RandomMapOneClickTestSupport.BuildMap(2, 2, 0x0001);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, LZ77.compress(originalMap));

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            RandomMapOneClickTestSupport.ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            switch (changeKind)
            {
                case "rom":
                    CoreState.ROM = RandomMapOneClickTestSupport.CreateRom();
                    break;
                case "selection":
                    vm.MapId = 2;
                    break;
                case "plist":
                    CoreState.ROM!.write_u8(vm.CurrentAddr + 8, 2);
                    break;
                case "table":
                    CoreState.ROM!.write_p32(0x220, 0x260);
                    break;
                case "pointer":
                    const uint newMapAddr = 0x1200;
                    CoreState.ROM!.write_range(newMapAddr, LZ77.compress(originalMap));
                    CoreState.ROM.write_p32(pointerEntryAddr, newMapAddr);
                    break;
                case "payload":
                    CoreState.ROM!.write_range(oldAddr, LZ77.compress(RandomMapOneClickTestSupport.BuildMap(2, 2, 0x0009)));
                    break;
                default:
                    throw new InvalidOperationException("Unknown change kind: " + changeKind);
            }

            var undo = new RandomMapOneClickTestSupport.RecordingUndoService(new List<string>());
            int reloadCalls = 0;
            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new RandomMapGenerationOutcome
                    {
                        Mars = new ushort[] { 0, 4, 8, 12 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 7,
                    },
                    writeIdentity,
                    TilesetFingerprint.Empty,
                    CancellationToken.None,
                    refreshMapFromCurrentSelection: () => throw new InvalidOperationException("must not refresh success"),
                    updateTilePalette: () => throw new InvalidOperationException("must not update palette"),
                    reloadFromRom: () => reloadCalls++,
                    showInfo: _ => throw new InvalidOperationException("must not show success")));

            Assert.NotNull(error);
            Assert.Contains("changed", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, undo.BeginCalls);
            Assert.Equal(0, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal(changeKind == "rom" ? 0 : 1, reloadCalls);
        }

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_PostWriteFailure_RollsBackAndReloadsCache()
        {
            CoreState.ROM = RandomMapOneClickTestSupport.CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = RandomMapOneClickTestSupport.BuildMap(2, 2, 0x0001);
            byte[] originalCompressed = LZ77.compress(originalMap);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, originalCompressed);

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            RandomMapOneClickTestSupport.ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            ushort[] generatedMars = { 0, 4, 8, 12 };
            bool reloadCalled = false;
            var undo = new UndoService();

            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new RandomMapGenerationOutcome
                    {
                        Mars = generatedMars,
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 9,
                    },
                    writeIdentity,
                    TilesetFingerprint.Empty,
                    CancellationToken.None,
                    refreshMapFromCurrentSelection: () => { },
                    updateTilePalette: () => throw new InvalidOperationException("post-write failure"),
                    reloadFromRom: () =>
                    {
                        reloadCalled = true;
                        RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
                    },
                    showInfo: _ => { }));

            Assert.NotNull(error);
            Assert.True(reloadCalled);
            Assert.Contains("post-write failure", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalCompressed, CoreState.ROM.Data.Skip((int)oldAddr).Take(originalCompressed.Length).ToArray());
            Assert.Equal(originalMap, vm.GetMapDataSnapshot());
        }

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_FingerprintMismatch_RejectsWithoutMutation()
        {
            CoreState.ROM = RandomMapOneClickTestSupport.CreateResolvableTilesetRom(
                2, 2, 0x0001,
                out uint mapSettingAddr, out uint pointerEntryAddr, out byte[] originalMap);
            CoreState.Undo = new Undo();

            Assert.True(
                BuiltInRandomMapTilesetCore.TryResolveMapTileset(CoreState.ROM, mapSettingAddr, out MapTilesetSnapshot originalSnapshot, out string tilesetError),
                tilesetError);
            TilesetFingerprint capturedFingerprint = originalSnapshot.Fingerprint;

            var vm = new MapEditorViewModel
            {
                CurrentAddr = mapSettingAddr,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            // Simulate an OBJ/PAL/CFG tileset edit that happened while generation was running:
            // the map's write identity (pointer/bytes) is untouched, but the tileset content
            // backing it changed, so its fingerprint must now differ.
            RandomMapOneClickTestSupport.MutateObjTileset(CoreState.ROM);
            Assert.True(
                BuiltInRandomMapTilesetCore.TryResolveMapTileset(CoreState.ROM, mapSettingAddr, out MapTilesetSnapshot mutatedSnapshot, out string mutatedError),
                mutatedError);
            Assert.NotEqual(capturedFingerprint, mutatedSnapshot.Fingerprint);

            var undo = new RandomMapOneClickTestSupport.RecordingUndoService(new List<string>());

            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new RandomMapGenerationOutcome
                    {
                        Mars = new ushort[] { 0, 4, 8, 12 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 11,
                    },
                    writeIdentity,
                    capturedFingerprint,
                    CancellationToken.None,
                    refreshMapFromCurrentSelection: () => throw new InvalidOperationException("must not refresh"),
                    updateTilePalette: () => throw new InvalidOperationException("must not update palette"),
                    reloadFromRom: () => throw new InvalidOperationException("must not reload"),
                    showInfo: _ => throw new InvalidOperationException("must not show success")));

            Assert.NotNull(error);
            Assert.Contains("tileset changed", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, undo.BeginCalls);
            Assert.Equal(0, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal(originalMap, vm.GetMapDataSnapshot());
        }

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_CancelledBeforeDispatcherMutation_DoesNotBeginUndo()
        {
            CoreState.ROM = RandomMapOneClickTestSupport.CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = RandomMapOneClickTestSupport.BuildMap(2, 2, 0x0001);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, LZ77.compress(originalMap));

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            RandomMapOneClickTestSupport.ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            RandomMapOneClickTestSupport.SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(vm.TryCaptureMapWriteIdentity(
                out MapEditorViewModel.MapWriteIdentity writeIdentity,
                out string identityError), identityError);

            var undo = new RandomMapOneClickTestSupport.RecordingUndoService(new List<string>());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new RandomMapGenerationOutcome
                    {
                        Mars = new ushort[] { 0, 4, 8, 12 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 13,
                    },
                    writeIdentity,
                    TilesetFingerprint.Empty,
                    cts.Token,
                    refreshMapFromCurrentSelection: () => throw new InvalidOperationException("must not refresh"),
                    updateTilePalette: () => throw new InvalidOperationException("must not update palette"),
                    reloadFromRom: () => throw new InvalidOperationException("must not reload"),
                    showInfo: _ => throw new InvalidOperationException("must not show success"))));

            Assert.Equal(0, undo.BeginCalls);
            Assert.Equal(0, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal(originalMap, vm.GetMapDataSnapshot());
        }

        [AvaloniaFact]
        public void MapEditorView_RandomMapBusyState_EnablesOnlyInlineCancelAction()
        {
            var view = new MapEditorView();
            var generate = view.FindControl<Button>("GenerateRandomMapButton");
            var cancel = view.FindControl<Button>("CancelRandomMapButton");
            var randomize = view.FindControl<Button>("RandomizeSeedButton");
            var tileset = view.FindControl<Button>("MapTilesetButton");
            var seed = view.FindControl<TextBox>("RandomMapSeedTextBox");

            view.SetRandomMapBusyState(true);

            Assert.False(generate!.IsEnabled);
            Assert.True(cancel!.IsEnabled);
            Assert.False(randomize!.IsEnabled);
            Assert.False(tileset!.IsEnabled);
            Assert.False(seed!.IsEnabled);

            view.SetRandomMapBusyState(false);
            Assert.True(generate.IsEnabled);
            Assert.False(cancel.IsEnabled);
        }

        [AvaloniaFact]
        public void MapEditorView_SeedTextBox_IsAssociatedWithLocalizedLabel()
        {
            var view = new MapEditorView();
            Label label = view.FindControl<Label>("RandomMapSeedLabel")!;
            TextBox seed = view.FindControl<TextBox>("RandomMapSeedTextBox")!;

            Assert.Same(seed, label.Target);
            Assert.Equal("Seed:", label.Content);
        }

        [Fact]
        public void RandomMapSeedRecoveryAndMappingStrings_HaveJapaneseAndChineseTranslations()
        {
            string repoRoot = RandomMapOneClickTestSupport.FindRepoRoot();
            L10nFinding seedFinding = Assert.Single(
                L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                    .Where(f =>
                        f.AxamlPath.EndsWith("FEBuilderGBA.Avalonia/Views/MapEditorView.axaml", StringComparison.Ordinal) &&
                        f.Literal == "Seed:"));
            Assert.Equal(L10nVerdict.Translated, seedFinding.Verdict);

            IReadOnlyList<L10nScanner.CodeLiteralFinding> codeFindings =
                L10nScanner.ScanCodeLiterals(repoRoot, new[] { "ja", "zh" });
            foreach (string literal in new[]
            {
                "Draw or import a representative map for this tileset, or configure FEMapCreator in Options.",
                "Try a different seed, or configure FEMapCreator in Options.",
                "Random map generation returned the current map unchanged. Try a different seed.",
                "The saved mapping entry is missing required fields.",
                "The mapped image file is no longer readable.",
                "The mapped image file has changed since the mapping was saved.",
                "The mapped generation-data file is no longer readable.",
                "The mapped generation-data file has changed since the mapping was saved.",
                "FEMapCreator is no longer configured or valid.",
                "The configured FEMapCreator executable path has changed.",
                "The configured FEMapCreator executable content has changed.",
                "The configured FEMapCreator assets root has changed.",
            })
            {
                Assert.True(
                    codeFindings.Any(f =>
                        (f.SourcePath.EndsWith(
                             "FEBuilderGBA.Avalonia/Services/RandomMapOneClickService.cs",
                             StringComparison.Ordinal)
                         || f.SourcePath.EndsWith(
                             "FEBuilderGBA.Avalonia/Views/MapEditorView.axaml.cs",
                             StringComparison.Ordinal))
                        && f.Literal == literal
                        && f.Verdict == L10nVerdict.Translated),
                    $"Missing Japanese/Chinese translation coverage for: {literal}");
            }
        }

        [AvaloniaFact]
        public void MapEditorView_BlankSeed_IsMaterializedImmediatelyForReplay()
        {
            var view = new MapEditorView();
            TextBox seedBox = view.FindControl<TextBox>("RandomMapSeedTextBox")!;
            seedBox.Text = "";

            Assert.True(view.TryGetSeed(out int seed, out string error), error);
            Assert.Equal(
                seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                seedBox.Text);
        }

        [AvaloniaFact]
        public void MapEditorView_PreflightFailure_SetsTerminalFailedStatus()
        {
            var view = new MapEditorView();
            Button generate = view.FindControl<Button>("GenerateRandomMapButton")!;
            TextBlock status = view.FindControl<TextBlock>("RandomMapStatusLabel")!;

            generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Failed.", status.Text);
        }

        [AvaloniaFact]
        public void MapEditorView_RandomMapStatus_IsPoliteLiveRegion()
        {
            var view = new MapEditorView();
            TextBlock status = view.FindControl<TextBlock>("RandomMapStatusLabel")!;

            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        }

        [AvaloniaTheory]
        [InlineData(FEMapCreatorMappingStatus.Invalid, FEMapCreatorMappingReason.StoredEntryMissingRequiredFields, "missing required fields", "invalid")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.ImageUnreadable, "image file is no longer readable", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.ImageChanged, "image file has changed", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.GenerationDataUnreadable, "generation-data file is no longer readable", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.GenerationDataChanged, "generation-data file has changed", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.ProfileUnavailable, "no longer configured or valid", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.ExecutablePathChanged, "executable path has changed", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.ExecutableContentChanged, "executable content has changed", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.AssetsRootChanged, "assets root has changed", "stale")]
        [InlineData(FEMapCreatorMappingStatus.Stale, FEMapCreatorMappingReason.None, "no longer valid", "stale")]
        public void FormatMappingNotice_LocalizesTypedFallbackAtUiBoundary(
            FEMapCreatorMappingStatus status,
            FEMapCreatorMappingReason reason,
            string expectedReasonText,
            string expectedStatusWord)
        {
            string notice = MapEditorView.FormatMappingNotice(new RandomMapOneClickResult
            {
                MappingStatus = status,
                MappingReason = reason,
                MappingDetail = "raw technical detail must not be displayed",
            });

            Assert.Contains(expectedStatusWord, notice, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedReasonText, notice, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("built-in", notice, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw technical detail", notice, StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaTheory]
        [InlineData((int)RandomMapBackendUsed.BuiltIn, "Built-in Experimental")]
        [InlineData((int)RandomMapBackendUsed.External, "FEMapCreator Experimental")]
        public void FormatBackendStatus_UsesPlanV4ExperimentalNames(
            int backendValue,
            string expectedName)
        {
            var backend = (RandomMapBackendUsed)backendValue;
            Assert.Equal(
                string.Format(R._("Backend: {0}"), R._(expectedName)),
                MapEditorView.FormatBackendStatus(backend));
        }

        // ------------------------------------------------------------------
        // Structural: no dialog, no ellipsis, adapters wired correctly
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public void MapEditorView_StrictRandomMapAdaptersThrowAndAreWired()
        {
            var view = new MapEditorView();

            Assert.Throws<InvalidOperationException>(view.RefreshMapImageFromCurrentSelectionStrict);
            Assert.Throws<InvalidOperationException>(view.UpdateTilePaletteStrict);

            string source = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));
            int callStart = source.IndexOf("ApplyGeneratedMapOnUiThreadAsync(", StringComparison.Ordinal);
            Assert.True(callStart >= 0);
            string call = source.Substring(callStart, Math.Min(1200, source.Length - callStart));

            Assert.Contains("RefreshMapImageFromCurrentSelectionStrict", call);
            Assert.Contains("UpdateTilePaletteStrict", call);
            Assert.Contains("RefreshMapFromCurrentSelectionStrict", call);
        }

        [AvaloniaFact]
        public void MapEditorView_GenerateButton_HasNoEllipsisAndNeverReferencesTheRetiredDialog()
        {
            string source = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));
            string markup = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml"));

            Assert.DoesNotContain("GenerateRandomMapDialog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerateRandomMapDialog", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Generate Random Map…", markup, StringComparison.Ordinal);
            Assert.Contains("Content=\"Generate Random Map\"", markup, StringComparison.Ordinal);

            Assert.False(
                File.Exists(Path.Combine(
                    RandomMapOneClickTestSupport.FindRepoRoot(),
                    "FEBuilderGBA.Avalonia",
                    "Dialogs",
                    "GenerateRandomMapDialog.axaml")),
                "The dialog-first GenerateRandomMapDialog must be fully retired.");
            Assert.False(
                File.Exists(Path.Combine(
                    RandomMapOneClickTestSupport.FindRepoRoot(),
                    "FEBuilderGBA.Avalonia",
                    "ViewModels",
                    "GenerateRandomMapDialogViewModel.cs")),
                "The dialog-first GenerateRandomMapDialogViewModel must be fully retired.");
        }

        // ------------------------------------------------------------------
        // Structural: _randomMapCts race-safe cancel/dispose ownership (#1978 Slice 3 re-review
        // finding #1). GenerateRandomMap_Click is a private async-void UI event handler that
        // needs a fully resolvable ROM tileset to drive end-to-end (see
        // RandomMapOneClickServiceTests/RandomMapOneClickWorkflowTests above for the behavioral
        // coverage of everything downstream of tileset resolution); the CTS lifecycle itself —
        // "never dispose while an in-flight run still depends on it; the owning run disposes in
        // its own finally; detach only cancels" — is a structural/source-level contract, so it is
        // verified the same way this file already verifies other MapEditorView.axaml.cs wiring
        // contracts (see the two tests immediately above) rather than by re-deriving a full
        // tileset-resolvable ROM fixture solely for this check.
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public void MapEditorView_RandomMapCts_NeverDisposedWhileStillOwnedByAnInFlightRun()
        {
            string source = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));

            int methodStart = source.IndexOf("async void GenerateRandomMap_Click(", StringComparison.Ordinal);
            Assert.True(methodStart >= 0);
            int methodEnd = source.IndexOf("\n        internal void SetRandomMapBusyState(", methodStart, StringComparison.Ordinal);
            Assert.True(methodEnd > methodStart);
            string method = source.Substring(methodStart, methodEnd - methodStart);

            // The old "dispose the previous CTS synchronously at the top of the handler" pattern
            // must be gone: it disposed a source that a still-in-flight prior run could still be
            // depending on (only reachable in practice via the _generatingRandomMap guard, but
            // the unsafe call shape itself must not remain in the source).
            Assert.DoesNotContain("_randomMapCts?.Dispose();\r\n            var cts = new CancellationTokenSource();", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_randomMapCts?.Dispose();\n            var cts = new CancellationTokenSource();", method, StringComparison.Ordinal);

            // The owning run's own finally must: (1) clear the shared field only if it still
            // refers to this run's own source, (2) then dispose that same local source — race-safe
            // regardless of a concurrent DetachedFromVisualTree cancel-only call.
            Assert.Contains("ReferenceEquals(_randomMapCts, cts)", method, StringComparison.Ordinal);
            int finallyIdx = method.IndexOf("finally", StringComparison.Ordinal);
            Assert.True(finallyIdx >= 0);
            string finallyBlock = method.Substring(finallyIdx);
            int referenceEqualsIdx = finallyBlock.IndexOf("ReferenceEquals(_randomMapCts, cts)", StringComparison.Ordinal);
            int disposeIdx = finallyBlock.IndexOf("cts.Dispose();", StringComparison.Ordinal);
            Assert.True(referenceEqualsIdx >= 0 && disposeIdx > referenceEqualsIdx,
                "finally must clear the field before disposing the local cts.");

            // DetachedFromVisualTree must remain cancel-only — it must never dispose a source an
            // in-flight run still owns.
            int detachIdx = source.IndexOf("DetachedFromVisualTree += (_, _) => _randomMapCts?.Cancel();", StringComparison.Ordinal);
            Assert.True(detachIdx >= 0, "DetachedFromVisualTree must remain a cancel-only handler for _randomMapCts.");
        }

        [Fact]
        public void MapEditorView_CurrentGridPreflight_FailsClosedBeforeBackendInvocation()
        {
            byte[] valid = { 2, 1, 4, 0, 8, 0 };
            Assert.True(MapEditorView.TryBuildCurrentGrid(valid, 2, 1, out ushort[] grid));
            Assert.Equal(new ushort[] { 4, 8 }, grid);

            Assert.False(MapEditorView.TryBuildCurrentGrid(null!, 2, 1, out _));
            Assert.False(MapEditorView.TryBuildCurrentGrid(new byte[] { 1, 1, 4, 0 }, 2, 1, out _));
            Assert.False(MapEditorView.TryBuildCurrentGrid(new byte[] { 2, 1, 4, 0, 8 }, 2, 1, out _));

            string source = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));
            int methodStart = source.IndexOf("async void GenerateRandomMap_Click(", StringComparison.Ordinal);
            int methodEnd = source.IndexOf("\n        internal void SetRandomMapBusyState(", methodStart, StringComparison.Ordinal);
            Assert.True(methodStart >= 0 && methodEnd > methodStart);
            string method = source.Substring(methodStart, methodEnd - methodStart);

            int preflight = method.IndexOf("if (!TryBuildCurrentGrid(", StringComparison.Ordinal);
            int backend = method.IndexOf("_randomMapService.GenerateAsync(", StringComparison.Ordinal);
            Assert.True(preflight >= 0 && backend > preflight);
            string guard = method.Substring(preflight, backend - preflight);
            Assert.Contains("Fail(", guard, StringComparison.Ordinal);
            Assert.Contains("return;", guard, StringComparison.Ordinal);
            Assert.DoesNotContain("ushort[]? currentGrid", method, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "BuiltInRandomMapTilesetCore.TryResolveMapTileset",
                method,
                StringComparison.Ordinal);

            int fingerprint = method.IndexOf(
                "TilesetFingerprint expectedFingerprint = result.SourceTilesetFingerprint;",
                StringComparison.Ordinal);
            int apply = method.IndexOf(
                "GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(",
                StringComparison.Ordinal);
            Assert.True(fingerprint > backend && apply > fingerprint);
        }

        [Fact]
        public void MapEditorView_BackendFailure_ReturnsBeforeApplyOrUndoMutation()
        {
            string source = File.ReadAllText(Path.Combine(
                RandomMapOneClickTestSupport.FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));
            int methodStart = source.IndexOf("async void GenerateRandomMap_Click(", StringComparison.Ordinal);
            int methodEnd = source.IndexOf("\n        internal void SetRandomMapBusyState(", methodStart, StringComparison.Ordinal);
            Assert.True(methodStart >= 0 && methodEnd > methodStart);
            string method = source.Substring(methodStart, methodEnd - methodStart);

            int failureGuard = method.IndexOf(
                "if (!result.Success || result.Outcome == null)",
                StringComparison.Ordinal);
            int apply = method.IndexOf(
                "GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(",
                StringComparison.Ordinal);
            Assert.True(failureGuard >= 0 && apply > failureGuard);

            string failureBlock = method.Substring(failureGuard, apply - failureGuard);
            Assert.Contains("Fail(", failureBlock, StringComparison.Ordinal);
            Assert.Contains("return;", failureBlock, StringComparison.Ordinal);
        }
    }
}
