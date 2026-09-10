using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Services
{
    public static class PatchDatabaseImportService
    {
        public sealed class RomIdentity
        {
            readonly ROM rom;
            readonly byte[] data;
            readonly ROMFEINFO info;
            readonly byte[] fingerprint;
            public string Version { get; }

            internal RomIdentity(ROM rom)
            {
                this.rom = rom;
                data = rom.Data;
                info = rom.RomInfo;
                fingerprint = SHA256.HashData(data);
                Version = info.VersionToFilename;
            }

            public bool IsCurrent => ReferenceEquals(CoreState.ROM, rom) &&
                ReferenceEquals(rom.Data, data) && ReferenceEquals(rom.RomInfo, info) &&
                rom.RomInfo.VersionToFilename == Version &&
                CryptographicOperations.FixedTimeEquals(fingerprint, SHA256.HashData(data));
        }

        public sealed class Outcome
        {
            public bool Imported { get; internal set; }
            public bool Cancelled { get; internal set; }
            public bool RecoveryRequired { get; internal set; }
            public bool Refreshed { get; internal set; }
            internal PatchDatabaseImportCore.Result? Receipt { get; set; }
            public string Message { get; internal set; } = "";
        }

        public static bool CanImportLoadedRom => CoreState.ROM?.RomInfo != null && CoreState.ROM.Data?.Length > 0 &&
            PatchDatabaseImportCore.IsSupportedVersion(CoreState.ROM.RomInfo.VersionToFilename);

        public static RomIdentity? CaptureLoadedRom() => CanImportLoadedRom ? new RomIdentity(CoreState.ROM) : null;

        public static string AvailabilityMessage =>
            CoreState.ROM?.RomInfo == null || CoreState.ROM.Data?.Length is not > 0
                ? R._("Load a ROM before importing its patch database.")
                : !CanImportLoadedRom
                    ? R._("Patch database import is unavailable for this ROM version.")
                    : "";

        public static string ConfirmationMessage(PatchDatabaseImportCore.PreparedImport prepared)
            => R._("Import the validated patch database for {0}?\r\nTarget: {1}\r\nFiles: {2}; expanded bytes: {3}\r\n{4}\r\nNo patches will be applied to the ROM.",
                prepared.Version, prepared.TargetDirectory, prepared.FileCount, prepared.ExpandedBytes,
                prepared.ReplacesExisting
                    ? R._("The existing database for this version will be replaced after confirmation.")
                    : R._("A new database for this version will be installed."));

        internal static string FormatResult(PatchDatabaseImportCore.Result result)
        {
            string message = result.Kind switch
            {
                PatchDatabaseImportCore.ResultKind.Completed => "",
                PatchDatabaseImportCore.ResultKind.CommittedAfterInterruption =>
                    R._("The database was committed despite an interrupted commit response: {0}", result.Detail),
                PatchDatabaseImportCore.ResultKind.StoppedBeforeCommit =>
                    R._("Import stopped without committing: {0}", result.Detail),
                PatchDatabaseImportCore.ResultKind.RecoveryFailed when !result.RecoveryRequired =>
                    R._("Import stopped without committing: {0}", result.Detail),
                PatchDatabaseImportCore.ResultKind.RecoveryFailed =>
                    R._("Import recovery is required; the owned workspace was retained.\r\n{0}\r\nRecovery: {1}",
                        result.Detail, result.RecoveryDetail),
                _ => R._("Patch database recovery needs attention: {0}", result.Detail),
            };
            if (result.RecoveryRequired && (result.Success || result.CleanupDetail.Length != 0))
                message = Append(message, result.Success
                    ? R._("The new database is installed, but owned backup cleanup is pending: {0}", result.CleanupDetail)
                    : R._("Owned workspace cleanup is pending: {0}", result.CleanupDetail));
            if (result.RetainedPath.Length != 0)
                message = Append(message, R._("Retained workspace: {0}", result.RetainedPath));
            return message;
        }

        internal static string FormatRecoveryException(PatchDatabaseImportCore.RecoveryException exception)
            => R._("Import cleanup requires recovery. Retained workspace: {0}\r\n{1}",
                exception.RetainedPath, exception.InnerException?.Message ?? "");

        static string Append(string message, string next) => message.Length == 0 ? next : message + "\n" + next;

        public static Task<Outcome> ImportAsync(IStorageFile selected, RomIdentity identity, string baseDirectory,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm, CancellationToken cancellationToken = default)
            => ImportCoreAsync(selected, identity, baseDirectory, confirm, cancellationToken,
                PatchDatabaseImportCore.PrepareWithRecoveryNotificationAsync);

        internal static Task<Outcome> ImportForTestAsync(IStorageFile selected, RomIdentity identity, string baseDirectory,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm, CancellationToken cancellationToken,
            Func<Stream, string, string, CancellationToken, Action, Task<PatchDatabaseImportCore.PreparedImport>> prepare,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>>? refresh = null, Action<ImportPhase>? phase = null)
            => ImportCoreAsync(selected, identity, baseDirectory, confirm, cancellationToken, prepare, refresh, phase);

        internal enum ImportPhase { PreCommit, Committed, Mapping, Refresh, Disposing, Disposed, Terminal }

        internal static Task<Outcome> ImportAndRefreshAsync(IStorageFile selected, RomIdentity identity, string baseDirectory,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> refresh, CancellationToken token)
            => ImportCoreAsync(selected, identity, baseDirectory, confirm, token,
                PatchDatabaseImportCore.PrepareWithRecoveryNotificationAsync, refresh);

        static async Task<Outcome> ImportCoreAsync(IStorageFile selected, RomIdentity identity, string baseDirectory,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm, CancellationToken cancellationToken,
            Func<Stream, string, string, CancellationToken, Action, Task<PatchDatabaseImportCore.PreparedImport>> prepare,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>>? refresh = null, Action<ImportPhase>? phase = null)
        {
            var previousNotice = App.CapturePatchDatabaseRecoveryNotice();
            int recoveryCompleted = 0;
            bool committed = false, refreshed = false;
            PatchDatabaseImportCore.PreparedImport? prepared = null;
            PatchDatabaseImportCore.Result? receipt = null;
            Exception? failure = null;
            PatchDatabaseImportCore.RecoveryException? recoveryException = null;
            Outcome? early = null;
            string message = "";
            try
            {
                if (!identity.IsCurrent) early = RomChanged();
                cancellationToken.ThrowIfCancellationRequested();
                if (early == null)
                {
                    await using (Stream source = await selected.OpenReadAsync())
                    {
                        prepared = await Task.Run(() => prepare(source, baseDirectory, identity.Version, cancellationToken,
                            () => Interlocked.Exchange(ref recoveryCompleted, 1)),
                            cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!identity.IsCurrent) early = RomChanged();
                    else if (!await confirm(prepared)) early = Cancelled();
                    if (early == null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Run(() => prepared.ValidateForCommit(cancellationToken), cancellationToken);
                        if (!identity.IsCurrent) early = RomChanged();
                        else
                        {
                            phase?.Invoke(ImportPhase.PreCommit);
                            // No await may interleave the final identity check and short commit.
                            if (!identity.IsCurrent) early = RomChanged();
                            else
                            {
                                receipt = prepared.Commit(cancellationToken, deferCleanup: true);
                                committed = receipt.Success;
                                if (committed) phase?.Invoke(ImportPhase.Committed);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                recoveryException = ex as PatchDatabaseImportCore.RecoveryException;
            }

            // These phases must run independently of cancellation and of faults following the latch.
            if (receipt != null && prepared != null && receipt.RecoveryRequired)
            {
                try
                {
                    receipt = await Task.Run(prepared.CompleteCleanup);
                    if (receipt.Success) committed = true;
                }
                catch (Exception ex) { failure = ex; }
            }
            if (receipt?.RecoveryRequired == true) App.RecordPatchDatabaseRecovery(receipt);
            if (recoveryException != null) App.RecordPatchDatabaseRecovery(recoveryException);
            var ownedNotice = App.CapturePatchDatabaseRecoveryNotice();
            try
            {
                phase?.Invoke(ImportPhase.Mapping);
                if (receipt != null) message = FormatResult(receipt);
            }
            catch (Exception ex) { failure = ex; }
            if (committed && prepared != null && refresh != null)
            {
                try
                {
                    phase?.Invoke(ImportPhase.Refresh);
                    refreshed = await refresh(prepared);
                }
                catch (Exception ex) { failure = ex; }
            }
            if (prepared != null)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        try { phase?.Invoke(ImportPhase.Disposing); }
                        finally { prepared.Dispose(); }
                        phase?.Invoke(ImportPhase.Disposed);
                    });
                }
                catch (Exception ex)
                {
                    failure = ex;
                    if (ex is PatchDatabaseImportCore.RecoveryException recovery)
                    {
                        recoveryException = recovery;
                        if (ReferenceEquals(ownedNotice, App.CapturePatchDatabaseRecoveryNotice()))
                            App.RecordPatchDatabaseRecovery(recovery);
                    }
                }
            }
            if (Volatile.Read(ref recoveryCompleted) != 0)
                App.ClearPatchDatabaseRecoveryNotice(previousNotice);
            if (committed)
            {
                if (failure != null) refreshed = false;
                if (message.Length == 0 && receipt?.RecoveryRequired == true)
                    message = "The database is installed; recovery is required. Retained workspace: " +
                        receipt.RetainedPath + "\n" + receipt.Detail + "\n" + receipt.CleanupDetail;
                if ((!refreshed && refresh != null) || failure != null)
                    message = Append("The database was imported, but the list was not refreshed.", message);
                if (failure != null) message = Append(message, failure.Message);
                return new Outcome { Imported = true, Refreshed = refreshed, Receipt = receipt,
                    RecoveryRequired = receipt?.RecoveryRequired == true ||
                        recoveryException != null, Message = message };
            }
            if (recoveryException != null)
                return new Outcome { RecoveryRequired = true, Message = FormatRecoveryException(recoveryException), Receipt = receipt };
            if (receipt != null)
                return new Outcome { Receipt = receipt, RecoveryRequired = receipt.RecoveryRequired,
                    Message = message.Length != 0 ? message : "Import stopped without committing. " + receipt.Detail };
            if (failure is OperationCanceledException) return Cancelled();
            if (failure != null) return new Outcome { Message = R._("Patch database import failed: {0}", failure.Message) };
            return early ?? new Outcome();
        }

        static Outcome Cancelled() => new Outcome
        {
            Cancelled = true, Message = R._("Import cancelled. The previous database was not replaced."),
        };

        static Outcome RomChanged() => new Outcome
        {
            Message = R._("The loaded ROM changed. Import was cancelled; the previous database was not replaced."),
        };
    }
}
