using System.Reflection;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PatchManagerRefreshTests
{
    [AvaloniaTheory]
    [InlineData("metadata")]
    [InlineData("scanner")]
    public async Task PublicationRequiresBothCapturedLanguagesWithOrdinalEquality(string mismatch)
    {
        using var fixture = new Fixture();
        var service = new PatchManagerRefreshService();
        bool published = false;
        Assert.False(await service.RefreshAsync(g =>
        {
            var request = PatchManagerRefreshService.Capture("", 0, g, true);
            return mismatch == "metadata" ? request with { Language = "EN" } : request with { ScanLanguage = "EN" };
        }, _ => true, _ => published = true));
        Assert.False(published);
        Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true),
            _ => true, _ => published = true));
        Assert.True(published);
    }

    [AvaloniaFact]
    public async Task LanguageChangeDuringOwnedReadRejectsPublicationWithoutReleasingWorkerOwnership()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        byte[] rom = (byte[])fixture.Rom.Data.Clone();
        var undo = CoreState.Undo.UndoBuffer.ToArray();
        byte[] descriptor = File.ReadAllBytes(Path.Combine(fixture.Library, "PATCH_owned.txt"));
        byte[] payload = File.ReadAllBytes(Path.Combine(fixture.Library, "sig.bin"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool exited = false, published = false;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            entered.Set();
            try { release.Wait(); return PatchManagerRefreshService.Read(r, t); }
            finally { exited = true; }
        });
        var stale = service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true),
            _ => true, _ => published = true);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            CoreState.Language = "ja";
            Assert.False(stale.IsCompleted);
            Assert.False(exited);
            Assert.False(ContentRepoGitService.TryEnter());
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
        }
        finally { release.Set(); await stale; }
        Assert.True(exited);
        Assert.False(await stale);
        Assert.False(published);
        Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true),
            _ => true, s =>
            {
                Assert.Equal("", s.Request.Language);
                Assert.Equal("ja", s.Request.ScanLanguage);
            }));
        Assert.Equal(rom, fixture.Rom.Data);
        Assert.Equal(undo, CoreState.Undo.UndoBuffer);
        Assert.Equal(descriptor, File.ReadAllBytes(Path.Combine(fixture.Library, "PATCH_owned.txt")));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(fixture.Library, "sig.bin")));
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    [AvaloniaFact]
    public async Task AttachedLanguageEventRefreshesOnDispatcherAndPreservesFilter()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Library, "PATCH_owned.txt"),
            "NAME=Owned Japanese\nNAME.en=Owned English\nTYPE=BIN");
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref reads);
            return PatchManagerRefreshService.Read(r, t);
        });
        var view = CreateView(service);
        var vm = ViewModel(view);
        vm.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        var host = new Window { Content = view };
        try
        {
            view.FindControl<TextBox>("SearchBox")!.Text = "Owned";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            host.Show();
            Assert.True(await view.RefreshTask);
            var first = view.RefreshTask;
            var list = view.FindControl<ListBox>("PatchListBox")!;
            list.SelectedIndex = 0;
            Assert.Equal("Owned English", view.FindControl<TextBlock>("DetailName")!.Text);
            await Task.Run(() => { CoreState.Language = "ja"; CoreState.RaiseLanguageChanged(); });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.NotSame(first, view.RefreshTask);
            Assert.True(await view.RefreshTask);
            Assert.Equal(2, reads);
            Assert.Equal("Owned", view.FindControl<TextBox>("SearchBox")!.Text);
            Assert.Equal("Owned Japanese", Assert.Single(vm.FilteredPatches).Name);
            Assert.Null(vm.SelectedPatch);
            Assert.Equal("", view.FindControl<TextBlock>("DetailName")!.Text);
            CoreState.RaiseLanguageChanged();
            Assert.Equal(2, reads);

            view.RequestClose();
            await Task.Run(() => { CoreState.Language = "en"; CoreState.RaiseLanguageChanged(); });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(2, reads);
        }
        finally { host.Close(); await service.Completion; await view.RefreshTask; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LanguageChangeClearsPendingSelectionBeforeLocalizedRowsChange(bool duringOperation)
    {
        using var fixture = new Fixture();
        string firstPath = Path.Combine(fixture.Library, "PATCH_first.txt");
        string secondPath = Path.Combine(fixture.Library, "PATCH_second.txt");
        File.WriteAllText(firstPath, "NAME=Other Japanese\nNAME.en=Match English\nTYPE=BIN");
        File.WriteAllText(secondPath, "NAME=Match Japanese\nNAME.en=Other English\nTYPE=BIN");
        var service = new PatchManagerRefreshService();
        var view = CreateView(service);
        var host = new Window { Content = view };
        try
        {
            view.JumpTo("Match", 0);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            host.Show();
            Assert.True(await view.RefreshTask);
            var vm = ViewModel(view);
            var list = view.FindControl<ListBox>("PatchListBox")!;
            Assert.Equal(firstPath, Assert.Single(vm.FilteredPatches).PatchFilePath);
            Assert.Equal(firstPath, vm.SelectedPatch!.PatchFilePath);
            Assert.Equal(0, list.SelectedIndex);
            Assert.Equal(0, ViewField("_pendingSelection").GetValue(view));
            ViewField("_patchActionRunning").SetValue(view, duringOperation);
            CoreState.Language = "ja";
            CoreState.RaiseLanguageChanged();
            if (duringOperation)
            {
                Assert.False(view.IsLoaded);
                Assert.Equal(-1, list.SelectedIndex);
                ViewField("_patchActionRunning").SetValue(view, false);
                InvokeView(view, "UpdateOperationControls");
            }
            Assert.True(await view.RefreshTask);
            Assert.Equal("Match", view.FindControl<TextBox>("SearchBox")!.Text);
            Assert.Equal(secondPath, Assert.Single(vm.FilteredPatches).PatchFilePath);
            Assert.Equal(-1, list.SelectedIndex);
            Assert.Null(list.SelectedItem);
            Assert.Null(vm.SelectedPatch);
            Assert.Equal("", view.FindControl<TextBlock>("DetailName")!.Text);
            Assert.Equal(-1, ViewField("_pendingSelection").GetValue(view));
        }
        finally
        {
            ViewField("_patchActionRunning").SetValue(view, false);
            host.Close();
            await service.Completion;
            await view.RefreshTask;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ModalReattachmentOnlyInvalidatesOperationsForAMissedLanguageChange(bool action, bool change)
    {
        using var fixture = new Fixture();
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            Interlocked.Increment(ref reads);
            return PatchManagerRefreshService.Read(r, t);
        });
        var view = CreateView(service);
        var host = new Window { Content = view };
        using var cancellation = new CancellationTokenSource();
        string running = action ? "_patchActionRunning" : "_importing";
        string dialog = action ? "_actionDialogOpen" : "_importDialogOpen";
        string source = action ? "_actionCancellation" : "_importCancellation";
        try
        {
            host.Show();
            Assert.True(await view.RefreshTask);
            var vm = ViewModel(view);
            view.FindControl<ListBox>("PatchListBox")!.SelectedIndex = 0;
            var selection = vm.SelectedPatch;
            long generation = (long)ViewField("_actionGeneration").GetValue(view)!;
            ViewField(running).SetValue(view, true);
            ViewField(dialog).SetValue(view, true);
            ViewField(source).SetValue(view, cancellation);
            host.Content = null;
            if (change)
            {
                CoreState.Language = "ja";
                CoreState.RaiseLanguageChanged();
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, reads);
            host.Content = view;
            Assert.False(cancellation.IsCancellationRequested);
            if (action) Assert.Equal(generation, ViewField("_actionGeneration").GetValue(view));
            Assert.Equal(!change, vm.IsLoaded);
            if (change) Assert.Null(vm.SelectedPatch);
            else Assert.Same(selection, vm.SelectedPatch);
            Assert.Equal(1, reads);
            ViewField(running).SetValue(view, false);
            InvokeView(view, "UpdateOperationControls");
            Assert.True(await view.RefreshTask);
            Assert.Equal(change ? 2 : 1, reads);
        }
        finally
        {
            ViewField(running).SetValue(view, false);
            ViewField(dialog).SetValue(view, false);
            ViewField(source).SetValue(view, null);
            host.Close();
            await service.Completion;
            await view.RefreshTask;
        }
    }

    [AvaloniaFact]
    public async Task AttachedLanguageChangeWaitsForTheInvalidatedReaderToActuallyExit()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            if (Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(); }
            return PatchManagerRefreshService.Read(r, default);
        });
        var view = CreateView(service);
        var host = new Window { Content = view };
        try
        {
            host.Show();
            var stale = view.RefreshTask;
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            CoreState.Language = "ja";
            CoreState.RaiseLanguageChanged();
            InvokeView(view, "UpdateOperationControls");
            Assert.Equal(1, reads);
            Assert.False(stale.IsCompleted);
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
            release.Set();
            Assert.False(await stale);
            await service.Completion;
            InvokeView(view, "UpdateOperationControls");
            Assert.True(await view.RefreshTask);
            Assert.Equal(2, reads);
            Assert.True(view.IsLoaded);
        }
        finally { release.Set(); host.Close(); await service.Completion; await view.RefreshTask; }
    }

    [AvaloniaFact]
    public async Task CommittedImportRefreshSatisfiesPendingLanguageWithoutCancellingTheImport()
    {
        using var fixture = new Fixture();
        byte[] rom = (byte[])fixture.Rom.Data.Clone();
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("FE8U/PATCH_imported.txt").Open());
            writer.Write("NAME=Imported Japanese\nNAME.en=Imported English\nTYPE=BIN\nPATCHED_IF:0x200=0xAA");
        }
        archive.Position = 0;
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            Interlocked.Increment(ref reads);
            return PatchManagerRefreshService.Read(r, t);
        });
        var view = CreateView(service);
        var host = new Window { Content = view };
        using var cancellation = new CancellationTokenSource();
        try
        {
            host.Show();
            Assert.True(await view.RefreshTask);
            ViewField("_importing").SetValue(view, true);
            ViewField("_importCancellation").SetValue(view, cancellation);
            using (var owner = await PatchDatabaseImportCore.PrepareForTestAsync(archive, fixture.Root, "FE8U",
                cancellation.Token, _ => false))
            {
                Assert.True((await Task.Run(() => owner.Commit(deferCleanup: true))).Success);
                CoreState.Language = "ja";
                CoreState.RaiseLanguageChanged();
                InvokeView(view, "UpdateOperationControls");
                Assert.False(view.IsLoaded);
                Assert.Equal(1, reads);
                Assert.False(cancellation.IsCancellationRequested);
                Assert.True(await (Task<bool>)InvokeView(view, "LoadPatchesAsync", owner)!);
                Assert.Equal("Imported Japanese", Assert.Single(ViewModel(view).FilteredPatches).Name);
                Assert.True(ContentRepoGitService.IsRunning());
            }
            ViewField("_importing").SetValue(view, false);
            InvokeView(view, "UpdateOperationControls");
            Assert.Equal(2, reads);
            Assert.True(view.IsLoaded);
            Assert.Equal(rom, fixture.Rom.Data);
            Assert.Empty(CoreState.Undo.UndoBuffer);
            Assert.True(File.Exists(Path.Combine(fixture.Library, "PATCH_imported.txt")));
            Assert.False(ContentRepoGitService.IsRunning());
        }
        finally
        {
            ViewField("_importing").SetValue(view, false);
            ViewField("_importCancellation").SetValue(view, null);
            host.Close();
            await service.Completion;
            await view.RefreshTask;
        }
    }

    [AvaloniaTheory]
    [InlineData("_importing")]
    [InlineData("_gitRunning")]
    [InlineData("_patchActionRunning")]
    [InlineData("gate")]
    public async Task LanguageRefreshDefersUntilOperationAndReadOwnershipAreIdle(string operation)
    {
        using var fixture = new Fixture();
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            Interlocked.Increment(ref reads);
            return PatchManagerRefreshService.Read(r, t);
        });
        var view = CreateView(service);
        var host = new Window { Content = view };
        bool ownsGate = false;
        using var cancellation = new CancellationTokenSource();
        try
        {
            host.Show();
            Assert.True(await view.RefreshTask);
            ViewField("_importCancellation").SetValue(view, cancellation);
            if (operation != "gate") ViewField(operation).SetValue(view, true);
            Assert.True(ownsGate = ContentRepoGitService.TryEnter());
            CoreState.Language = "ja";
            CoreState.RaiseLanguageChanged();
            InvokeView(view, "UpdateOperationControls");
            Assert.False(ViewModel(view).IsLoaded);
            Assert.Equal(1, reads);
            Assert.False(cancellation.IsCancellationRequested);
            ContentRepoGitService.Exit();
            ownsGate = false;
            if (operation != "gate") ViewField(operation).SetValue(view, false);
            InvokeView(view, "UpdateOperationControls");
            Assert.True(await view.RefreshTask);
            Assert.Equal(2, reads);
            InvokeView(view, "UpdateOperationControls");
            Assert.Equal(2, reads);
        }
        finally
        {
            if (ownsGate) ContentRepoGitService.Exit();
            if (operation != "gate") ViewField(operation).SetValue(view, false);
            ViewField("_importCancellation").SetValue(view, null);
            host.Close();
            await service.Completion;
            await view.RefreshTask;
        }
    }

    [AvaloniaFact]
    public async Task SupersededFailureContinuationCannotClearANewerSuccessfulSnapshot()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            if (Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(); }
            return PatchManagerRefreshService.Read(r, t) with { Message = "latest success" };
        });
        var view = CreateView(service);
        var host = new Window { Content = view };
        var held = new HeldContinuation();
        Task<bool>? stale = null;
        try
        {
            host.Show();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(held);
                stale = LoadView(view);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            var latest = LoadView(view);
            await held.Posted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            Assert.True(await latest);
            Assert.Equal("latest success", view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
            held.Run();
            Assert.False(await stale);
            Assert.True(view.IsLoaded);
            Assert.Equal("latest success", view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
        }
        finally
        {
            release.Set();
            host.Close();
            await service.Completion;
            held.Run();
            if (stale != null) await stale;
            await view.RefreshTask;
        }
    }

    static FieldInfo ViewField(string name) => typeof(PatchManagerView).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    static PatchManagerViewModel ViewModel(PatchManagerView view) => (PatchManagerViewModel)ViewField("_vm").GetValue(view)!;
    static object? InvokeView(PatchManagerView view, string name, params object?[] args) =>
        typeof(PatchManagerView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, args);
    static Task<bool> LoadView(PatchManagerView view) => (Task<bool>)InvokeView(view, "LoadPatchesAsync", new object?[] { null })!;
    static PatchManagerView CreateView(PatchManagerRefreshService service) => new(service);

    sealed class HeldContinuation : SynchronizationContext
    {
        readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> pending = new();
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            pending.Enqueue((callback, state));
            Posted.TrySetResult();
        }
        internal void Run()
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            while (pending.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("detach")]
    [InlineData("generation")]
    [InlineData("selection")]
    [InlineData("in-place")]
    [InlineData("replace")]
    [InlineData("fault")]
    [InlineData("callback")]
    public async Task ManagedActionVerificationStaysOffDispatcherAndOwnsLeaseUntilRealExit(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.MakeManaged();
        var vm = fixture.CreateViewModel("install");
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool exited = false, attached = true, callbackFault = false;
        SetVerifier(vm, (snapshot, scope, token) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            entered.SetResult();
            try
            {
                release.Wait();
                if (change == "fault") throw new IOException("owned verification fault");
                return snapshot.Matches(scope, token);
            }
            finally { exited = true; }
        });
        vm.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        var operation = vm.InstallPatchAsync(true, cancellation.Token,
            () => callbackFault ? throw new InvalidOperationException("owned callback fault") : attached);
        byte[] expected = (byte[])fixture.Rom.Data.Clone();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            if (change == "cancel") cancellation.Cancel();
            if (change == "detach") attached = false;
            if (change == "generation") vm.SetPendingFilter("later");
            if (change == "selection") vm.SelectedPatch = null;
            if (change == "in-place") { fixture.Rom.Data[0x300] = 0x42; expected[0x300] = 0x42; }
            if (change == "replace") CoreState.ROM = new ROM();
            if (change == "callback") callbackFault = true;
            Assert.False(operation.IsCompleted);
            Assert.False(exited);
            await Task.Run(() =>
            {
                using var child = new PatchManagerOperationGuardTests.NativeLeaseProcess(fixture.Root, hold: false);
                Assert.Equal("busy", child.Result);
            });
            Assert.False(exited);
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            release.Set();
            if (change == "callback") await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
            else await operation;
        }
        Assert.True(exited);
        Assert.Equal(expected, fixture.Rom.Data);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    internal static void SetVerifier(PatchManagerViewModel vm,
        Func<PatchDatabaseOperationLeaseCore.ExistingReadSnapshot,
            PatchDatabaseOperationLeaseCore.ExistingLeaseProbe, CancellationToken, bool> verify)
        => typeof(PatchManagerViewModel).GetField("_verifySnapshot",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(vm, verify);

    [AvaloniaFact]
    public async Task ManagedPickerKeepsNativeOwnershipAcrossAwaitAndCancellation()
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.MakeManaged();
        var vm = fixture.CreateViewModel("clean");
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = vm.UninstallPatchAsync(() => { entered.SetResult(); return selected.Task; }, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Task.Run(() =>
            {
                using var child = new PatchManagerOperationGuardTests.NativeLeaseProcess(fixture.Root, hold: false);
                Assert.Equal("busy", child.Result);
            });
            Assert.False(operation.IsCompleted);
        }
        finally { selected.TrySetResult(fixture.CleanRom); await operation; }
        Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData("path")]
    [InlineData("language")]
    public async Task ManagedPickerRejectsChangedCapturedActionInputs(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        fixture.MakeManaged();
        var vm = fixture.CreateViewModel("clean");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = vm.UninstallPatchAsync(() => { entered.SetResult(); return selected.Task; });
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (change == "path") vm.SelectedPatch!.PatchFilePath += ".stale";
            else CoreState.Language = "ja";
            selected.SetResult(fixture.CleanRom);
            Assert.Equal(R._("The loaded ROM or selected patch changed. Uninstall was cancelled."), await operation);
            Assert.Equal(before, fixture.Rom.Data);
            Assert.Empty(CoreState.Undo.UndoBuffer);
        }
        finally { selected.TrySetResult(null); await operation; }
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Theory]
    [InlineData("transition")]
    [InlineData("missing-lock")]
    [InlineData("outside-selection")]
    public void ManagedActionNeverDowngradesOrAuthorizesUnverifiedProvenance(string change)
    {
        using var fixture = new PatchManagerOperationGuardTests.Fixture();
        if (change != "transition") fixture.MakeManaged();
        var vm = fixture.CreateViewModel("install");
        string lockPath = Path.Combine(fixture.Root, ".patch2-import", "lease.lock");
        if (change == "transition") fixture.MakeManaged();
        if (change == "missing-lock") File.Delete(lockPath);
        if (change == "outside-selection") vm.SelectedPatch!.PatchFilePath = fixture.CleanRom;
        var before = fixture.Snapshot();
        vm.InstallPatch(true);
        fixture.AssertSnapshot(before);
        Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        Assert.Empty(CoreState.Undo.UndoBuffer);
        if (change == "missing-lock") Assert.False(File.Exists(lockPath));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaFact]
    public async Task SnapshotPreservesPointerTextDependenciesAndCapturedScannerLanguage()
    {
        using var fixture = new Fixture();
        U.write_u32(fixture.Rom.Data, 0x1100, U.toPointer(0x2200));
        fixture.Rom.Data[0x2200] = 0xAA;
        U.write_u32(fixture.Rom.Data, fixture.Rom.RomInfo.text_pointer, U.toPointer(0x60000));
        U.write_u32(fixture.Rom.Data, 0x6000C, U.toPointer(0x70000));
        fixture.Rom.Data[0x70000] = 0xBB;
        fixture.Rom.Data[0x2000] = 1;
        File.WriteAllText(Path.Combine(fixture.Library, "PATCH_pointer.txt"),
            "NAME=Pointer\nNAME.en=Captured English\nTYPE=BIN\nPATCHED_IF:$P32 0x1100=0xAA\nIF:0x100=0xFF");
        File.WriteAllText(Path.Combine(fixture.Library, "PATCH_text.txt"),
            "NAME=Text\nTYPE=BIN\nPATCHED_IF:$TEXTID 0x3=0xBB");
        File.WriteAllText(Path.Combine(fixture.Library, "PATCH_hardcoded.txt"),
            "NAME=Hardcoded unit\nTYPE=ADDR\nADDRESS=0x2000\nADDRESS_TYPE=UNIT");
        var request = PatchManagerRefreshService.Capture("", 0, 1, true);
        CoreState.Language = "ja";
        fixture.Rom.Data[0x2200] = 0;
        var service = new PatchManagerRefreshService();
        var snapshot = await service.ReadAsync(request, default);
        Assert.Equal("en", request.Language);
        Assert.Equal("en", request.ScanLanguage);
        var pointer = Assert.Single(snapshot.All, p => p.Name == "Captured English");
        Assert.Equal(PatchMetadataCore.PatchStatus.Installed, pointer.Status);
        Assert.Equal(1, pointer.UnsatisfiedDependencyCount);
        Assert.Equal(PatchMetadataCore.PatchStatus.Installed, Assert.Single(snapshot.All, p => p.Name == "Text").Status);
        var filtered = await service.ReadAsync(request with { Filter = "HARDCODING_UNIT=01" }, default);
        Assert.Equal("Hardcoded unit", Assert.Single(filtered.Filtered).Name);
    }

    [AvaloniaFact]
    public async Task LegacyReadOnlyLibraryAndLocklessMissingLibraryNeverCreateStorage()
    {
        using var fixture = new Fixture();
        string descriptor = Path.Combine(fixture.Library, "PATCH_owned.txt");
        var original = File.GetAttributes(descriptor);
        File.SetAttributes(descriptor, original | FileAttributes.ReadOnly);
        var readOnlyAttributes = File.GetAttributes(descriptor);
        string[] inventory = Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories);
        try
        {
            Assert.True((readOnlyAttributes & FileAttributes.ReadOnly) != 0);
            var service = new PatchManagerRefreshService();
            Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, false),
                r => r.Identity.IsCurrent, s => Assert.Single(s.All)));
            Assert.Equal(inventory, Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories));
            Assert.Equal(readOnlyAttributes, File.GetAttributes(descriptor));
        }
        finally { File.SetAttributes(descriptor, original); }
    }

    [AvaloniaFact]
    public async Task CapturedFallbackLocationLocksOnlyItsAdmittedBase()
    {
        using var primary = new Fixture();
        using var selected = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(selected.Root)) { }
        CoreState.BaseDirectory = primary.Root;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(selected.Root));
            Assert.False(Directory.Exists(Path.Combine(primary.Root, ".patch2-import")));
            return PatchManagerRefreshService.Read(r, t);
        });
        Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true) with
        {
            Location = new PatchManagerViewModel.PatchLocation(selected.Root, selected.Library),
        }, r => r.Identity.IsCurrent, _ => { }));
    }

    [AvaloniaFact]
    public async Task ThrowingPartialPublicationReleasesLeaseButDoesNotClaimRefresh()
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        var service = new PatchManagerRefreshService();
        bool partial = false;
        Assert.False(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true),
            r => r.Identity.IsCurrent, _ =>
            {
                partial = true;
                Assert.True(ContentRepoGitService.IsRunning());
                throw new ApplicationException("partial publication");
            }));
        Assert.True(partial);
        Assert.Equal("partial publication", service.Failure!.Detail);
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("reload")]
    [InlineData("in-place")]
    [InlineData("detach")]
    [InlineData("cancel")]
    public async Task StaleSnapshotIsDiscardedAndOwnershipLastsUntilWorkerExits(string change)
    {
        using var fixture = new Fixture();
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root)) { }
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        bool attached = true, exited = false, published = false;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            entered.Set();
            try { release.Wait(); return PatchManagerRefreshService.Read(r, default); }
            finally { exited = true; }
        });
        var task = service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, true),
            r => attached && r.Identity.IsCurrent, _ => published = true, cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            switch (change)
            {
                case "replace": CoreState.ROM = fixture.Rom.Clone(); break;
                case "reload": fixture.Rom.LoadLow("reloaded.gba", new byte[0x1000000], "BE8E01"); break;
                case "in-place": fixture.Rom.Data[100]++; break;
                case "detach": attached = false; service.Invalidate(); break;
                case "cancel": cancellation.Cancel(); break;
            }
            await Dispatcher.UIThread.InvokeAsync(() => Assert.False(exited));
            Assert.True(service.IsBusy);
            Assert.False(ContentRepoGitService.TryEnter());
            Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
            Assert.False(task.IsCompleted);
        }
        finally { release.Set(); await task; }
        Assert.True(exited);
        Assert.False(published);
        Assert.False(await task);
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    [AvaloniaFact]
    public async Task SupersessionClonesOnlyActiveAndLatestIntentAndPublishesOnDispatcher()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0, captures = 0, published = 0, ui = Environment.CurrentManagedThreadId;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            if (Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(); }
            return PatchManagerRefreshService.Read(r, t);
        });
        Task<bool> Queue(string filter) => service.RefreshAsync(g =>
        {
            captures++;
            Assert.Equal(ui, Environment.CurrentManagedThreadId);
            return PatchManagerRefreshService.Capture(filter, 0, g, false);
        }, r => r.Identity.IsCurrent, snapshot =>
        {
            Assert.Equal(ui, Environment.CurrentManagedThreadId);
            Assert.Equal("Owned", snapshot.Request.Filter);
            published++;
        });
        var first = Queue("");
        var rest = new List<Task<bool>>();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            for (int i = 0; i < 40; i++) rest.Add(Queue("stale" + i));
            rest.Add(Queue("Owned"));
            Assert.Equal(1, captures);
            Assert.Equal(1, reads);
        }
        finally { release.Set(); await first; await Task.WhenAll(rest); }
        Assert.Equal(2, captures);
        Assert.Equal(2, reads);
        Assert.Equal(1, published);
        Assert.True(await rest[^1]);
        Assert.All(rest.Take(rest.Count - 1), t => Assert.False(t.Result));
    }

    [AvaloniaFact]
    public async Task LegacyTransitionIsReprobedAndReadAgainUnderExistingLease()
    {
        using var fixture = new Fixture();
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) =>
        {
            if (++reads == 1) { using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root); }
            else Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
            return PatchManagerRefreshService.Read(r, t);
        });
        Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, false),
            r => r.Identity.IsCurrent, _ => Assert.Equal(2, reads)));
        Assert.Equal(2, reads);
    }

    [AvaloniaFact]
    public async Task IncompleteManagedLibraryNeverFallsBackOrReadsUnlocked()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".patch2-import"));
        int reads = 0;
        var service = new PatchManagerRefreshService((r, t) => { reads++; return PatchManagerRefreshService.Read(r, t); });
        Assert.False(await service.RefreshAsync(g => PatchManagerRefreshService.Capture("", 0, g, false),
            _ => true, _ => throw new Exception("Must not publish")));
        Assert.Equal(0, reads);
        Assert.Equal(PatchManagerRefreshService.RefreshFailureTemplate, service.Failure!.Template);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Root, ".patch2-import")));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("HARDCODING_UNIT=01")]
    public async Task RealFgrepAndSpecialFiltersMatchSynchronousSemantics(string filter)
    {
        using var fixture = new Fixture();
        var vm = new PatchManagerViewModel();
        vm.LoadPatchList();
        vm.FilterText = filter;
        var service = new PatchManagerRefreshService();
        Assert.True(await service.RefreshAsync(g => PatchManagerRefreshService.Capture(filter, 0, g, true),
            r => r.Identity.IsCurrent, snapshot =>
            {
                Assert.Equal(vm.FilteredPatches.Select(p => p.Name), snapshot.Filtered.Select(p => p.Name));
                Assert.Equal(1, snapshot.Installed);
            }));
        fixture.Rom.Data[^4] = 0;
        var absent = await service.ReadAsync(PatchManagerRefreshService.Capture("", 0, 2, true), default);
        Assert.Equal(PatchMetadataCore.PatchStatus.NotInstalled, Assert.Single(absent.All).Status);
    }

    [AvaloniaFact]
    public async Task ActualMetadataReadUsesWorkerAndDetachedClone()
    {
        using var fixture = new Fixture();
        var request = PatchManagerRefreshService.Capture("", 0, 1, true);
        int ui = Environment.CurrentManagedThreadId, worker = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = new PatchManagerRefreshService((r, token) =>
        {
            worker = Environment.CurrentManagedThreadId;
            entered.Set();
            if (worker != ui) release.Wait();
            return PatchManagerRefreshService.Read(r, token);
        });
        Task<PatchManagerRefreshService.PatchListSnapshot>? task = null;
        try
        {
            task = service.ReadAsync(request, default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(ui, worker);
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            fixture.Rom.Data[^4] = 0;
        }
        finally { release.Set(); }
        var result = await task!;
        Assert.Single(result.All);
        Assert.Equal(PatchMetadataCore.PatchStatus.Installed, result.All[0].Status);
        Assert.NotSame(CoreState.ROM.Data, request.Rom.Data);
    }

    internal sealed class Fixture : IDisposable
    {
        readonly ROM saved = CoreState.ROM;
        readonly Undo savedUndo = CoreState.Undo;
        readonly string savedBase = CoreState.BaseDirectory, savedLanguage = CoreState.Language;
        internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "refresh-" + Guid.NewGuid().ToString("N"));
        internal ROM Rom { get; } = new();
        internal string Library => Path.Combine(Root, "config", "patch2", "FE8U");
        internal Fixture()
        {
            Directory.CreateDirectory(Library);
            Rom.LoadLow("synthetic.gba", new byte[0x1000000], "BE8E01");
            byte[] signature = { 0xAB, 0xCD, 0xEF, 0x12 };
            signature.CopyTo(Rom.Data, Rom.Data.Length - 4);
            File.WriteAllBytes(Path.Combine(Library, "sig.bin"), signature);
            File.WriteAllText(Path.Combine(Library, "PATCH_owned.txt"),
                "NAME=Owned FGREP\nTYPE=BIN\nPATCHED_IF:$FGREP4 sig.bin=0xAB 0xCD 0xEF 0x12");
            CoreState.ROM = Rom;
            CoreState.Undo = new Undo();
            CoreState.BaseDirectory = Root;
            CoreState.Language = "en";
        }
        public void Dispose()
        {
            CoreState.ROM = saved;
            CoreState.Undo = savedUndo;
            CoreState.BaseDirectory = savedBase;
            CoreState.Language = savedLanguage;
            Directory.Delete(Root, true);
        }
    }
}
