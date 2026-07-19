#nullable enable

using System;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class GenerateRandomMapWorkflow
    {
        internal static async Task<GenerateRandomMapDialogResult?> OpenDialogIfReadyAsync(
            MapEditorViewModel vm,
            Func<string, bool> decompGuard,
            Action<string> showError,
            Func<int, int, Task<GenerateRandomMapDialogResult?>> openDialog)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(decompGuard);
            ArgumentNullException.ThrowIfNull(showError);
            ArgumentNullException.ThrowIfNull(openDialog);

            byte[] cachedMap = vm.GetMapDataSnapshot();
            if (cachedMap == null || cachedMap.Length < 2)
            {
                showError(R._("No map data loaded — select a map first."));
                return null;
            }

            if (decompGuard(R._("map tile layout")))
                return null;

            return await openDialog(vm.MapWidth, vm.MapHeight);
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
            GenerateRandomMapDialogResult result,
            Action refreshMapFromCurrentSelection,
            Action updateTilePalette,
            Action<string> showInfo)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(undo);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(refreshMapFromCurrentSelection);
            ArgumentNullException.ThrowIfNull(updateTilePalette);
            ArgumentNullException.ThrowIfNull(showInfo);

            string? applyError = null;
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                bool applied = TryApplyGeneratedMap(
                    vm,
                    undo,
                    result.Mars,
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
                    reloadFromRom: refreshMapFromCurrentSelection,
                    out string error);
                if (!applied)
                    applyError = error;
            });
            return applyError;
        }
    }
}
