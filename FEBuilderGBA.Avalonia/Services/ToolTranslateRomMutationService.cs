using System;
using System.Threading.Tasks;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class ToolTranslateRomMutationService
    {
        internal sealed record Result(bool Applied, int Total);
        internal sealed record MutationResult<T>(bool Applied, bool Mutated, T Value);

        internal static async Task<Result> ExecuteAsync(
            ROM rom,
            PatchDatabaseImportService.RomIdentity identity,
            UndoService undoService,
            Func<ROM, Undo.UndoData, int> worker)
        {
            MutationResult<int> result = await ExecuteAsync(
                rom, identity, undoService, worker, _ => true, "Translate ROM");
            return new Result(result.Applied, result.Value);
        }

        internal static async Task<MutationResult<T>> ExecuteAsync<T>(
            ROM rom,
            PatchDatabaseImportService.RomIdentity identity,
            UndoService undoService,
            Func<ROM, Undo.UndoData, T> worker,
            Predicate<T> shouldApply,
            string undoName)
        {
            ArgumentNullException.ThrowIfNull(rom);
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentNullException.ThrowIfNull(undoService);
            ArgumentNullException.ThrowIfNull(worker);
            ArgumentNullException.ThrowIfNull(shouldApply);
            ArgumentException.ThrowIfNullOrWhiteSpace(undoName);

            if (!identity.TryCreateWorkingCopy(out ROM? working) || working == null)
                return new MutationResult<T>(false, false, default!);
            Undo? expectedUndo = CoreState.Undo;
            if (expectedUndo == null)
                throw new InvalidOperationException("Undo history is unavailable.");

            Undo.UndoData scratch = expectedUndo.NewUndoData(undoName + " staging");
            T value = await Task.Run(() => worker(working, scratch));
            if (!shouldApply(value))
                return new MutationResult<T>(false, false, value);
            if (!identity.IsCurrent || !ReferenceEquals(CoreState.Undo, expectedUndo))
                return new MutationResult<T>(false, false, value);
            if (working.Data.AsSpan().SequenceEqual(rom.Data))
                return new MutationResult<T>(true, false, value);

            Undo.UndoData active = expectedUndo.NewUndoData(undoName);
            bool modifiedBefore = rom.Modified;
            EtcCacheSnapshot? commentCacheBefore = null;
            CoreState.CommentCache?.TryCaptureAll(out commentCacheBefore);
            PatchDatabaseImportService.RomIdentity? appliedIdentity = null;
            try
            {
                if (!identity.IsCurrent || !ReferenceEquals(CoreState.Undo, expectedUndo))
                    return new MutationResult<T>(false, false, value);
                if (!rom.SwapNewROMData(working.Data, undoName, active, confirmHeaderChange: false))
                    throw new InvalidOperationException(undoName + " changes were not applied.");
                appliedIdentity = identity.RefreshAfterOwnedMutation();
                bool mutated = active.list.Count > 0 || active.filesize != (uint)rom.Data.Length;
                if (appliedIdentity == null || !appliedIdentity.IsCurrent ||
                    (mutated && !undoService.CommitExternal(rom, expectedUndo, active)))
                    throw new InvalidOperationException(undoName + " undo history was not committed.");
                return new MutationResult<T>(true, mutated, value);
            }
            catch
            {
                if (appliedIdentity?.IsSourceCurrent == true ||
                    (appliedIdentity == null && identity.CanRestoreOwnedMutation))
                    undoService.RestoreExternal(
                        rom, active, modifiedBefore, commentCacheBefore);
                throw;
            }
        }
    }
}
