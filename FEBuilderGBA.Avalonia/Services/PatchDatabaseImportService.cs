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
            Func<Stream, string, string, CancellationToken, Action, Task<PatchDatabaseImportCore.PreparedImport>> prepare)
            => ImportCoreAsync(selected, identity, baseDirectory, confirm, cancellationToken, prepare);

        static async Task<Outcome> ImportCoreAsync(IStorageFile selected, RomIdentity identity, string baseDirectory,
            Func<PatchDatabaseImportCore.PreparedImport, Task<bool>> confirm, CancellationToken cancellationToken,
            Func<Stream, string, string, CancellationToken, Action, Task<PatchDatabaseImportCore.PreparedImport>> prepare)
        {
            var previousNotice = App.CapturePatchDatabaseRecoveryNotice();
            int recoveryCompleted = 0;
            try
            {
                if (!identity.IsCurrent) return RomChanged();
                cancellationToken.ThrowIfCancellationRequested();
                PatchDatabaseImportCore.PreparedImport? prepared = null;
                try
                {
                    await using (Stream source = await selected.OpenReadAsync())
                    {
                        prepared = await Task.Run(() => prepare(source, baseDirectory, identity.Version, cancellationToken,
                            () => Interlocked.Exchange(ref recoveryCompleted, 1)),
                            cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!identity.IsCurrent) return RomChanged();
                    if (!await confirm(prepared))
                        return Cancelled();
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() => prepared.ValidateForCommit(cancellationToken), cancellationToken);
                    if (!identity.IsCurrent) return RomChanged();
                    // Commit is synchronous on the owning UI context: no ROM-load UI event can
                    // interleave the final identity check with its short rename/journal sequence.
                    var result = prepared.Commit(cancellationToken, deferCleanup: true);
                    if (result.RecoveryRequired)
                        // Leases remain held while full rollback/inventory/cleanup runs off the UI thread.
                        result = await Task.Run(prepared.CompleteCleanup);
                    if (result.RecoveryRequired) App.RecordPatchDatabaseRecovery(result);
                    return new Outcome
                    {
                        Imported = result.Success, RecoveryRequired = result.RecoveryRequired,
                        Message = FormatResult(result),
                    };
                }
                finally
                {
                    if (prepared != null)
                        await Task.Run(prepared.Dispose);
                }
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (PatchDatabaseImportCore.RecoveryException ex)
            {
                App.RecordPatchDatabaseRecovery(ex);
                return new Outcome
                {
                    RecoveryRequired = true,
                    Message = FormatRecoveryException(ex),
                };
            }
            catch (Exception ex)
            {
                return new Outcome { Message = R._("Patch database import failed: {0}", ex.Message) };
            }
            finally
            {
                // Only Core's explicit completion receipt can clear the captured notice.
                // A new cleanup failure or another operation's notice has a different identity.
                if (Volatile.Read(ref recoveryCompleted) != 0)
                    App.ClearPatchDatabaseRecoveryNotice(previousNotice);
            }
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
