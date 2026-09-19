using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ChapterNameTextPatchSafetyTests
{
    [AvaloniaTheory]
    [InlineData(false, "replace")]
    [InlineData(true, "replace")]
    [InlineData(false, "reload")]
    [InlineData(true, "reload")]
    [InlineData(false, "edit")]
    [InlineData(true, "edit")]
    [InlineData(false, "info")]
    [InlineData(true, "info")]
    public async Task PromptRomChangeCancelsBothSkipAndApply(bool apply, string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        fixture.MakeManaged();
        var view = new ToolTranslateROMView();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        var prompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = view.ShowChapterNameTextRecommendation(fixture.Rom, identity, () => prompt.Task);
        var files = fixture.Snapshot();
        switch (change)
        {
            case "replace":
                var replacement = new ROM();
                replacement.LoadLow("replacement-synthetic.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = replacement;
                break;
            case "reload": fixture.Rom.LoadLow("reload-synthetic.gba", new byte[0x1000000], "BE8E01"); break;
            case "edit": fixture.Rom.Data[0x300] ^= 1; break;
            case "info":
                typeof(ROM).GetProperty(nameof(ROM.RomInfo))!.SetValue(fixture.Rom, new ROMFE8U(fixture.Rom));
                break;
        }
        byte[] old = (byte[])fixture.Rom.Data.Clone();
        byte[] current = (byte[])CoreState.ROM.Data.Clone();
        prompt.SetResult(apply);
        var continuation = await pending;
        Assert.Equal(old, fixture.Rom.Data);
        Assert.Equal(current, CoreState.ROM.Data);
        Assert.Null(continuation);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        fixture.AssertSnapshot(files);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData("payload")]
    [InlineData("descriptor")]
    [InlineData("other-version")]
    [InlineData("backup")]
    public async Task ManagedLibraryChangeDuringDiscoveryNeverStartsUndo(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        fixture.MakeManaged();
        string other = Path.Combine(fixture.Root, "config", "patch2", "FE7U", "other.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllBytes(other, [0x11]);
        string backup = PatchMetadataCore.GetBackupFilePath(fixture.Descriptor);
        File.WriteAllText(backup, "unchanged");
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        byte[] bytes = (byte[])fixture.Rom.Data.Clone();
        var view = new ToolTranslateROMView();
        var result = await view.ShowChapterNameTextRecommendation(fixture.Rom, identity,
            () => Task.FromResult(true), (path, rom, language, token) =>
            {
                var infos = PatchMetadataCore.EnumeratePatches(path, rom, language, token);
                string changed = change switch
                {
                    "payload" => Path.Combine(fixture.Library, "test.bin"),
                    "descriptor" => Path.Combine(fixture.Library, "PATCH_chapter.txt"),
                    "backup" => backup,
                    _ => other,
                };
                var timestamp = File.GetLastWriteTimeUtc(changed);
                byte[] content = File.ReadAllBytes(changed);
                content[^1] ^= 1;
                File.WriteAllBytes(changed, content);
                File.SetLastWriteTimeUtc(changed, timestamp);
                return infos;
            });
        Assert.Equal(bytes, fixture.Rom.Data);
        Assert.Null(result);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipAndOwnedApplyKeepIndependentContinuation(bool apply)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        fixture.MakeManaged();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        var view = new ToolTranslateROMView();
        var continuation = await view.ShowChapterNameTextRecommendation(fixture.Rom, identity,
            () => Task.FromResult(apply));
        Assert.NotNull(continuation);
        Assert.True(continuation.IsCurrent);
        Assert.Equal(!apply, identity.IsCurrent);
        Assert.Equal(apply ? 0xAAu : 0x11u, fixture.Rom.u8(0x200));
        Assert.Equal(apply ? 1 : 0, CoreState.Undo.UndoBuffer.Count);
        var translation = new UndoService();
        translation.Begin("Translate ROM");
        fixture.Rom.write_u8(0x300, 0xBB);
        translation.Commit();
        Assert.Equal(apply ? 2 : 1, CoreState.Undo.UndoBuffer.Count);
        CoreState.Undo.RunUndo();
        Assert.Equal(0u, fixture.Rom.u8(0x300));
        Assert.Equal(apply ? 0xAAu : 0x11u, fixture.Rom.u8(0x200));
        if (apply) CoreState.Undo.RunUndo();
        Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        fixture.Rom.Data[0x300] = 1;
        Assert.False(continuation.IsCurrent);
    }

    [AvaloniaTheory]
    [InlineData("discovery", "cancel")]
    [InlineData("discovery", "reload")]
    [InlineData("discovery", "edit")]
    [InlineData("match", "cancel")]
    [InlineData("match", "reload")]
    [InlineData("match", "edit")]
    public async Task WorkerAndSnapshotStayOffDispatcherAndOwnLeaseThroughRealExit(string stage, string interruption)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        fixture.MakeManaged();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        int reads = 0, verifications = 0;
        bool exited = false;
        void Block()
        {
            entered.Set();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(20))); }
            finally { exited = true; }
        }
        var service = new ChapterNameTextPatchService((path, scanRom, language, token) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Assert.NotSame(fixture.Rom, scanRom);
            Interlocked.Increment(ref reads);
            if (stage == "discovery") Block();
            return PatchMetadataCore.EnumeratePatches(path, scanRom, language, token);
        }, (snapshot, scope, token) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref verifications);
            if (stage == "match") Block();
            return snapshot.Matches(scope, token);
        });
        var undo = new UiUndo(fixture.Root);
        var pending = service.RecommendAsync(fixture.Rom, PatchDatabaseImportService.CaptureLoadedRom()!,
            undo, () => Task.FromResult(true), cancellation.Token);
        byte[] expected;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            bool dispatcherResponsive = false;
            await Dispatcher.UIThread.InvokeAsync(() => dispatcherResponsive = true, DispatcherPriority.Background);
            Assert.True(dispatcherResponsive);
            if (interruption == "cancel") cancellation.Cancel();
            else if (interruption == "reload") fixture.Rom.LoadLow("reload-synthetic.gba", new byte[0x1000000], "BE8E01");
            else fixture.Rom.Data[0x300] = 0x42;
            expected = (byte[])fixture.Rom.Data.Clone();
            Assert.False(pending.IsCompleted);
            Assert.False(exited);
            Assert.False(ContentRepoGitService.TryEnter());
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() =>
                PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
        }
        finally { release.Set(); await pending; }
        Assert.True(exited);
        Assert.Null((await pending).Identity);
        Assert.Equal(1, reads);
        if (stage == "match") Assert.Equal(1, verifications);
        Assert.Equal(expected, fixture.Rom.Data);
        Assert.Empty(undo.Calls);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    [AvaloniaTheory]
    [InlineData("legacy-transition")]
    [InlineData("missing-lock")]
    [InlineData("busy-lock")]
    public async Task ReadAdmissionNeverDowngradesOrRetriesUnlocked(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        if (change != "legacy-transition") fixture.MakeManaged();
        if (change == "missing-lock") File.Delete(Path.Combine(fixture.Root, ".patch2-import", "lease.lock"));
        using var held = change == "busy-lock" ? PatchDatabaseOperationLeaseCore.Acquire(fixture.Root) : null;
        int reads = 0;
        var service = new ChapterNameTextPatchService((path, rom, language, token) =>
        {
            reads++;
            var result = PatchMetadataCore.EnumeratePatches(path, rom, language, token);
            if (change == "legacy-transition") fixture.MakeManaged();
            return result;
        });
        var undo = new UiUndo(fixture.Root);
        var result = await service.RecommendAsync(fixture.Rom, PatchDatabaseImportService.CaptureLoadedRom()!,
            undo, () => Task.FromResult(true));
        Assert.Null(result.Identity);
        Assert.Equal(change == "legacy-transition" ? 1 : 0, reads);
        Assert.Empty(undo.Calls);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData("none")]
    [InlineData("replace")]
    [InlineData("reload")]
    [InlineData("info")]
    [InlineData("commit-failure")]
    public async Task OwnedMutationRefreshRequiresSameLoadedReferencesAndKeepsUndoOnDispatcher(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.SeedTranslationPatch();
        fixture.MakeManaged();
        var identity = PatchDatabaseImportService.CaptureLoadedRom()!;
        var undo = new UiUndo(fixture.Root, () =>
        {
            if (change == "replace") CoreState.ROM = new ROM();
            if (change == "reload") fixture.Rom.LoadLow("reload-synthetic.gba", new byte[0x1000000], "BE8E01");
            if (change == "info")
                typeof(ROM).GetProperty(nameof(ROM.RomInfo))!.SetValue(fixture.Rom, new ROMFE8U(fixture.Rom));
        }, change == "commit-failure");
        var result = await new ChapterNameTextPatchService().RecommendAsync(fixture.Rom, identity, undo,
            () => Task.FromResult(true));
        Assert.Equal(change == "commit-failure" ? ["Begin", "Commit", "Rollback"] : new[] { "Begin", "Commit" }, undo.Calls);
        if (change == "none")
        {
            Assert.NotNull(result.Identity);
            Assert.True(result.Identity.IsCurrent);
            Assert.False(identity.IsCurrent);
            Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
            CoreState.Undo.RunUndo();
            Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        }
        else Assert.Null(result.Identity);
        if (change == "commit-failure") Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        Assert.False(undo.HasPendingUndo);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    sealed class UiUndo(string root, Action? afterCommit = null, bool failCommit = false) : UndoService
    {
        public List<string> Calls { get; } = new();
        void Observe(string call)
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Assert.True(ContentRepoGitService.IsRunning());
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() =>
                PatchDatabaseOperationLeaseCore.Acquire(root));
            Calls.Add(call);
        }
        public override void Begin(string name) { Observe("Begin"); base.Begin(name); }
        public override void Commit()
        {
            Observe("Commit");
            if (failCommit) throw new IOException("Injected undo commit failure");
            base.Commit();
            afterCommit?.Invoke();
        }
        public override void Rollback() { Observe("Rollback"); base.Rollback(); }
    }
}
