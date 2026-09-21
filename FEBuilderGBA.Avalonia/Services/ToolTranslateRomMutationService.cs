using System;
using System.Threading.Tasks;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class ToolTranslateRomMutationService
    {
        internal sealed record Result(bool Applied, int Total);

        internal static async Task<Result> ExecuteAsync(
            ROM rom,
            PatchDatabaseImportService.RomIdentity identity,
            UndoService undoService,
            Func<ROM, Undo.UndoData, int> worker)
        {
            ArgumentNullException.ThrowIfNull(rom);
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentNullException.ThrowIfNull(undoService);
            ArgumentNullException.ThrowIfNull(worker);

            if (!identity.TryCreateWorkingCopy(out ROM? working) || working == null)
                return new Result(false, 0);
            if (CoreState.Undo == null)
                throw new InvalidOperationException("Undo history is unavailable.");

            Undo.UndoData scratch = CoreState.Undo.NewUndoData("Translate ROM staging");
            int total = await Task.Run(() => worker(working, scratch));
            if (!identity.IsCurrent)
                return new Result(false, 0);

            Undo.UndoData active = CoreState.Undo.NewUndoData("Translate ROM");
            try
            {
                if (!identity.IsCurrent)
                    return new Result(false, 0);
                if (!rom.SwapNewROMData(working.Data, "Translate ROM", active, confirmHeaderChange: false))
                    throw new InvalidOperationException("Translate ROM changes were not applied.");
                bool mutated = active.list.Count > 0 || active.filesize != (uint)rom.Data.Length;
                if (mutated && !undoService.CommitExternal(active))
                    throw new InvalidOperationException("Translate ROM undo history was not committed.");
                return new Result(true, total);
            }
            catch
            {
                undoService.RollbackExternal(rom, active);
                throw;
            }
        }
    }
}
