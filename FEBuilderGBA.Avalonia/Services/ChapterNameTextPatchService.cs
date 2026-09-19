using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services;

internal sealed class ChapterNameTextPatchService
{
    internal sealed record Result(PatchDatabaseImportService.RomIdentity? Identity, string Message = "", bool Applied = false);

    readonly Func<string, ROM, string, CancellationToken, List<PatchMetadataCore.PatchInfo>> discover;
    readonly Func<PatchDatabaseOperationLeaseCore.ExistingReadSnapshot,
        PatchDatabaseOperationLeaseCore.ExistingLeaseProbe, CancellationToken, bool> matches;

    internal ChapterNameTextPatchService(
        Func<string, ROM, string, CancellationToken, List<PatchMetadataCore.PatchInfo>>? discover = null,
        Func<PatchDatabaseOperationLeaseCore.ExistingReadSnapshot,
            PatchDatabaseOperationLeaseCore.ExistingLeaseProbe, CancellationToken, bool>? matches = null)
    {
        this.discover = discover ?? PatchMetadataCore.EnumeratePatches;
        this.matches = matches ?? ((snapshot, scope, token) => snapshot.Matches(scope, token));
    }

    internal async Task<Result> RecommendAsync(ROM rom, PatchDatabaseImportService.RomIdentity identity,
        UndoService undo, Func<Task<bool>> prompt, CancellationToken token = default)
    {
        bool Current() => !token.IsCancellationRequested && ReferenceEquals(CoreState.ROM, rom) && identity.IsCurrent;
        Result Cancelled() => new(null, R._("Cancelled."));
        try
        {
            if (!Current()) return Cancelled();
            bool apply = await prompt();
            if (!Current()) return Cancelled();
            if (!apply) return new(identity);

            using var ownership = new PatchManagerRefreshService.ReadOwnership();
            if (!ownership.TryEnter()) return new(null, PatchManagerViewModel.PatchDatabaseBusyMessage);
            string language = PatchMetadataCore.GetLanguageSuffix();
            var scanRom = rom.Clone();
            // Await the actual worker, not cancellation of the wait: its lease protects every file read.
            var selection = await Task.Run(() =>
            {
                var location = PatchManagerViewModel.ResolvePatchLocation(identity.Version);
                ownership.Acquire(location, identity.Version);
                var before = ownership.CaptureIdentity(token);
                var infos = discover(location.Directory, scanRom, language, token);
                token.ThrowIfCancellationRequested();
                var after = PatchDatabaseOperationLeaseCore.ProbeExisting(location.BaseDirectory,
                    identity.Version, location.Directory);
                if ((!ownership.Managed && after.Managed) ||
                    (ownership.Managed && (!after.Managed || !after.HasLock || before == null ||
                        !matches(before, after, token))))
                    return (Target: (PatchMetadataCore.PatchInfo?)null, Message: PatchManagerViewModel.PatchDatabaseChangedMessage);
                var target = infos.FirstOrDefault(p => p.Name.IndexOf("Convert Chapter Titles to Text",
                    StringComparison.OrdinalIgnoreCase) >= 0);
                return (Target: target, Message: "ChapterNameToText patch not found in " + location.Directory);
            }, token);
            if (!Current()) return Cancelled();
            if (selection.Target == null || string.IsNullOrEmpty(selection.Target.PatchFilePath))
                return new(null, selection.Message);

            // No await separates UI-side admission, undo and mutation.
            undo.Begin("Install ChapterNameToText");
            try
            {
                if (!Current()) { undo.Rollback(); return Cancelled(); }
                var result = PatchMetadataCore.ApplyPatch(rom, selection.Target.PatchFilePath, undo.GetActiveUndoData());
                if (!result.Success)
                {
                    undo.Rollback();
                    return new(null, result.Message);
                }
                undo.Commit();
                if (token.IsCancellationRequested) return Cancelled();
                var continuation = identity.RefreshAfterOwnedMutation();
                return continuation == null ? Cancelled() : new(continuation, result.Message, Applied: true);
            }
            catch
            {
                undo.Rollback();
                throw;
            }
        }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (PatchDatabaseOperationLeaseCore.BusyException)
        {
            return new(null, PatchManagerViewModel.PatchDatabaseBusyMessage);
        }
        catch (Exception ex)
        {
            Log.ErrorF("ChapterNameTextPatchService.Recommend: {0}", ex.Message);
            return new(null, ex.Message);
        }
    }
}
