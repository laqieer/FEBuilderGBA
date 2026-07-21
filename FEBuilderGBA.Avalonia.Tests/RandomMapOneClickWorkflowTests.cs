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
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Threading;
using FEBuilderGBA;
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
                Mars = new ushort[] { 0x0002, 0x0003, 0x0004, 0x0005 },
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
                new byte[] { 2, 2, 2, 0, 3, 0, 4, 0, 5, 0 },
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
                        Mars = new ushort[] { 2, 3, 4, 5 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 7,
                    },
                    writeIdentity,
                    TilesetFingerprint.Empty,
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

            ushort[] generatedMars = { 0x0002, 0x0003, 0x0004, 0x0005 };
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
                        Mars = new ushort[] { 2, 3, 4, 5 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 11,
                    },
                    writeIdentity,
                    capturedFingerprint,
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
            int methodEnd = source.IndexOf("\n        void SetRandomMapBusyState(", methodStart, StringComparison.Ordinal);
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
    }
}
