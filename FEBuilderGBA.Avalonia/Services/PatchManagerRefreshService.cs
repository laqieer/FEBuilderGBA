using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services;

internal sealed class PatchManagerRefreshService
{
    internal const string RefreshFailureTemplate = "The patch database could not be refreshed: {0}";
    internal const string ImportedRefreshFailureTemplate = "The installed database could not be refreshed: {0}";

    internal sealed record RefreshFailure(string Template, string Detail)
    {
        internal string Localize() => R._(Template, Detail);
    }

    internal sealed record Request(PatchDatabaseImportService.RomIdentity Identity, ROM Rom,
        PatchManagerViewModel.PatchLocation Location, string Language, string ScanLanguage,
        string Filter, int Selection, long Generation, bool Strict, bool Android);

    internal sealed record PatchListSnapshot(Request Request, List<PatchEntry> All,
        ObservableCollection<PatchEntry> Filtered, int Installed, string Message, string GitButton,
        bool Complete, bool Transition = false, RefreshFailure? Failure = null);

    internal static Request Capture(string filter, int selection, long generation, bool strict)
    {
        var identity = PatchDatabaseImportService.CaptureLoadedRom()
            ?? throw new InvalidOperationException(PatchDatabaseImportService.AvailabilityMessage);
        return new Request(identity, CoreState.ROM.Clone(),
            PatchManagerViewModel.ResolvePatchLocation(identity.Version),
            PatchMetadataCore.GetLanguageSuffix(), PatchFilterCore.ScanLang(CoreState.Language),
            filter, selection, generation, strict, !AndroidResourceNoticeCore.IsResourceDeliverySupported);
    }

    readonly Func<Request, CancellationToken, PatchListSnapshot> read;
    internal PatchManagerRefreshService(Func<Request, CancellationToken, PatchListSnapshot>? read = null)
        => this.read = read ?? Read;

    internal Task<PatchListSnapshot> ReadAsync(Request request, CancellationToken token)
        => Task.Run(() => read(request, token), token);

    sealed record Intent(Func<long, Request> Capture, Func<Request, bool> Current,
        Action<PatchListSnapshot> Publish, CancellationToken Token,
        PatchDatabaseImportCore.PreparedImport? Owner, TaskCompletionSource<bool> Completion);

    Intent? pending;
    CancellationTokenSource? active;
    long generation;
    bool running;
    internal bool IsBusy => running;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal RefreshFailure? Failure { get; private set; }

    internal void Invalidate()
    {
        generation++;
        active?.Cancel();
        pending?.Completion.TrySetResult(false);
        pending = null;
    }

    internal Task<bool> RefreshAsync(Func<long, Request> capture, Func<Request, bool> current,
        Action<PatchListSnapshot> publish, CancellationToken token = default)
        => Enqueue(capture, current, publish, token, null);

    // Only the committed callback, still owning PreparedImport, may borrow its non-reentrant gate.
    internal Task<bool> RefreshCommittedAsync(PatchDatabaseImportCore.PreparedImport owner,
        Func<long, Request> capture, Func<Request, bool> current, Action<PatchListSnapshot> publish,
        CancellationToken token)
        => Enqueue(capture, current, publish, token, owner);

    Task<bool> Enqueue(Func<long, Request> capture, Func<Request, bool> current,
        Action<PatchListSnapshot> publish, CancellationToken token, PatchDatabaseImportCore.PreparedImport? owner)
    {
        generation++;
        active?.Cancel();
        pending?.Completion.TrySetResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = new Intent(capture, current, publish, token, owner, completion);
        if (!running)
        {
            running = true;
            Completion = DrainAsync();
        }
        return completion.Task;
    }

    async Task DrainAsync()
    {
        try
        {
            while (pending is { } intent)
            {
                pending = null;
                bool published = false;
                Failure = null;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(intent.Token);
                active = cancellation;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var request = intent.Capture(generation);
                    // A legacy-to-managed transition gets one fresh locked read, never an unlocked retry.
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        using var ownership = new ReadOwnership();
                        var snapshot = await Task.Run(() =>
                        {
                            ownership.Acquire(request, intent.Owner);
                            var result = read(request, cancellation.Token);
                            cancellation.Token.ThrowIfCancellationRequested();
                            var after = PatchDatabaseOperationLeaseCore.ProbeExisting(request.Location.BaseDirectory,
                                request.Identity.Version, request.Location.Directory);
                            if (!ownership.Managed && after.Managed) return result with { Transition = true };
                            if (ownership.Managed && (!after.Managed || !after.HasLock))
                                throw new IOException("The managed library changed during refresh.");
                            return result;
                        }, cancellation.Token);
                        if (request.Generation != generation || cancellation.IsCancellationRequested || !intent.Current(request))
                            break;
                        if (snapshot.Transition) continue;
                        if (!snapshot.Complete)
                        {
                            Failure = snapshot.Failure ?? new RefreshFailure(RefreshFailureTemplate, snapshot.Message);
                            break;
                        }
                        intent.Publish(snapshot);
                        published = true;
                        break;
                    }
                }
                catch (Exception ex) { Failure = new RefreshFailure(RefreshFailureTemplate, ex.Message); }
                finally
                {
                    active = null;
                    intent.Completion.TrySetResult(published);
                }
            }
        }
        finally { running = false; }
    }

    sealed class ReadOwnership : IDisposable
    {
        PatchDatabaseOperationLeaseCore.Lease? lease;
        bool gate;
        internal bool Managed { get; private set; }
        internal void Acquire(Request request, PatchDatabaseImportCore.PreparedImport? owner)
        {
            if (owner != null)
            {
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (owner.Version != request.Identity.Version ||
                    !string.Equals(owner.TargetDirectory, request.Location.Directory, comparison))
                    throw new IOException("The committed refresh does not match its prepared owner.");
                Managed = true;
                return;
            }
            if (!ContentRepoGitService.TryEnter())
                throw new IOException("A patch database operation is already running.");
            gate = true;
            var probe = PatchDatabaseOperationLeaseCore.ProbeExisting(request.Location.BaseDirectory,
                request.Identity.Version, request.Location.Directory);
            Managed = probe.Managed;
            if (probe.Managed) lease = PatchDatabaseOperationLeaseCore.AcquireExisting(probe);
        }
        public void Dispose()
        {
            try { lease?.Dispose(); }
            finally { if (gate) ContentRepoGitService.Exit(); }
        }
    }

    internal static PatchListSnapshot Read(Request request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        List<PatchMetadataCore.PatchInfo> infos;
        string error = "";
        bool complete = true;
        if (request.Strict)
            complete = PatchMetadataCore.TryEnumeratePatches(request.Location.Directory, request.Rom,
                request.Language, token, out infos, out error);
        else
            infos = PatchMetadataCore.EnumeratePatches(request.Location.Directory, request.Rom, request.Language, token);
        token.ThrowIfCancellationRequested();
        var all = infos.Select(PatchEntry.FromPatchInfo).ToList();
        string filter = request.Filter.Trim();
        bool installedOnly = PatchFilterCore.IsInstalledOnlyToken(filter);
        bool hardcoding = PatchFilterCore.TryParseHardCodingToken(filter, out string type, out uint value);
        var filtered = new List<PatchEntry>();
        foreach (var entry in all)
        {
            token.ThrowIfCancellationRequested();
            bool match = filter.Length == 0 ||
                (installedOnly ? PatchFilterCore.IsInstalledForFilter(request.Rom, entry.PatchFilePath, request.ScanLanguage) :
                 hardcoding ? PatchFilterCore.IsHardCodingTokenMatch(request.Rom, entry.PatchFilePath, request.ScanLanguage, value, type) :
                 entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                 entry.Author.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                 entry.Tags.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                 entry.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (match) filtered.Add(entry);
        }
        string message = "";
        RefreshFailure? failure = complete ? null : new RefreshFailure(ImportedRefreshFailureTemplate, error);
        if (complete && all.Count == 0)
        {
            if (PatchMetadataCore.IsPatchLibraryEmpty(request.Location.Directory))
                message = request.Android ? AndroidResourceNoticeCore.PatchLibraryUnavailableMessage : PatchMetadataCore.NotInitializedMessage;
            else
            {
                complete = false;
                failure = new RefreshFailure(RefreshFailureTemplate, request.Location.Directory);
            }
        }
        string gitButton = GitUtil.IsGitRepo(Patch2GitService.GetPatch2Dir(request.Location.BaseDirectory))
            ? "Update Patch Database" : "Initialize Patch Database";
        token.ThrowIfCancellationRequested();
        return new PatchListSnapshot(request, all, new ObservableCollection<PatchEntry>(filtered),
            all.Count(p => p.Status == PatchMetadataCore.PatchStatus.Installed), message, gitButton, complete, Failure: failure);
    }
}
