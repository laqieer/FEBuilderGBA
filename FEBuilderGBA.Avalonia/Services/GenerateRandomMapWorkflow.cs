#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class GenerateRandomMapWorkflow
    {
        /// <summary>
        /// Pre-flight readiness check shared by the one-click generate button (#1978 Slice 3):
        /// a map must be loaded and the current tile-layout asset must not be blocked by the
        /// decomp guard. Performs no generation itself and never opens a dialog.
        /// </summary>
        internal static bool TryPrepareForGeneration(
            MapEditorViewModel vm,
            Func<string, bool> decompGuard,
            Action<string> showError)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(decompGuard);
            ArgumentNullException.ThrowIfNull(showError);

            byte[] cachedMap = vm.GetMapDataSnapshot();
            if (cachedMap == null || cachedMap.Length < 2)
            {
                showError(R._("No map data loaded — select a map first."));
                return false;
            }

            if (decompGuard(R._("map tile layout")))
                return false;

            return true;
        }

        internal static bool TryApplyGeneratedMap(
            MapEditorViewModel vm,
            UndoService undo,
            ushort[] mars,
            int width,
            int height,
            Action postApplySuccess,
            Action reloadFromRom,
            out string error)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(undo);
            ArgumentNullException.ThrowIfNull(postApplySuccess);
            ArgumentNullException.ThrowIfNull(reloadFromRom);

            error = "";

            undo.Begin("MapEditor.GenerateRandomMap");

            bool ok;
            string applyError;
            uint writeAddr;
            try
            {
                ok = vm.ApplyMapGrid(mars, width, height, out applyError, out writeAddr);
            }
            catch (Exception ex)
            {
                undo.Rollback();
                error = ex.Message;
                return false;
            }

            if (!ok)
            {
                undo.Rollback();
                error = applyError ?? "";
                return false;
            }

            try
            {
                // Commit intentionally stays LAST. Once ApplyMapGrid succeeds, the ROM bytes
                // and VM cache both point at the generated layout; keeping the undo scope open
                // through refresh + notification lets us truly roll back the ROM if any
                // post-write UI work faults, then re-load the cache from the reverted bytes.
                postApplySuccess();
                undo.Commit();
                _ = writeAddr;
                return true;
            }
            catch (Exception ex)
            {
                undo.Rollback();
                try
                {
                    reloadFromRom();
                }
                catch (Exception reloadEx)
                {
                    Log.Error("GenerateRandomMapWorkflow.TryApplyGeneratedMap reload failed: " + reloadEx);
                    error = string.Format(
                        R._("Generated map was rolled back, but refreshing the editor failed: {0}"),
                        reloadEx.Message);
                    return false;
                }

                error = ex.Message;
                return false;
            }
        }

        internal static async Task<string?> ApplyGeneratedMapOnUiThreadAsync(
            MapEditorViewModel vm,
            UndoService undo,
            RandomMapGenerationOutcome result,
            MapEditorViewModel.MapWriteIdentity expectedIdentity,
            TilesetFingerprint expectedFingerprint,
            CancellationToken cancellationToken,
            Action refreshMapFromCurrentSelection,
            Action updateTilePalette,
            Action reloadFromRom,
            Action<string> showInfo)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(undo);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(expectedIdentity);
            ArgumentNullException.ThrowIfNull(refreshMapFromCurrentSelection);
            ArgumentNullException.ThrowIfNull(updateTilePalette);
            ArgumentNullException.ThrowIfNull(reloadFromRom);
            ArgumentNullException.ThrowIfNull(showInfo);

            string? applyError = null;
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!vm.IsMapWriteIdentityCurrent(
                    expectedIdentity,
                    out string identityError))
                {
                    if (ReferenceEquals(CoreState.ROM, expectedIdentity.Rom))
                    {
                        try
                        {
                            reloadFromRom();
                        }
                        catch (Exception reloadEx)
                        {
                            Log.Error(
                                "GenerateRandomMapWorkflow identity reload failed: "
                                + reloadEx.ToString());
                        }
                    }
                    applyError = identityError;
                    return;
                }

                // #1978 Slice 3: the tileset fingerprint captured at click time must still match
                // right before the generated grid is written — a tileset edit (OBJ/PAL/CFG) that
                // happened during generation invalidates the result even when the map's write
                // identity (pointer/size) is unchanged. Reject without mutation on any mismatch.
                if (!expectedFingerprint.IsEmpty)
                {
                    bool fingerprintCurrent =
                        BuiltInRandomMapTilesetCore.TryResolveMapTileset(CoreState.ROM, vm.CurrentAddr, out MapTilesetSnapshot snapshot, out string _)
                        && snapshot.Fingerprint == expectedFingerprint;
                    if (!fingerprintCurrent)
                    {
                        applyError = R._("The map's tileset changed since generation started; discarding the result to avoid writing a mismatched layout.");
                        return;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                bool applied = TryApplyGeneratedMap(
                    vm,
                    undo,
                    result.MarsBuffer,
                    result.Width,
                    result.Height,
                    postApplySuccess: () =>
                    {
                        refreshMapFromCurrentSelection();
                        updateTilePalette();
                        showInfo(string.Format(
                            R._("Generated random map: {0}x{1}, seed={2}."),
                            result.Width,
                            result.Height,
                            result.EffectiveSeed));
                    },
                    reloadFromRom,
                    out string error);
                if (!applied)
                    applyError = error;
            });
            return applyError;
        }
    }
}
