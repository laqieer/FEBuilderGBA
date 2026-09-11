using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PatchManagerOperationGuardTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TranslationHoldsNativeLeaseThroughUndoCommitAndRollback(bool failCommit)
    {
        using var fixture = new Fixture();
        fixture.MakeManaged();
        fixture.SeedTranslationPatch();
        var view = new ToolTranslateROMView();
        var vm = (ToolTranslateROMViewModel)typeof(ToolTranslateROMView).GetField("_vm",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var undo = new NativeUndoProbe(fixture.Root, failCommit);
        vm.UndoService = undo;
        InstallTranslationPatch(view, fixture.Rom);
        Assert.Equal(failCommit ? 3 : 2, undo.Observations.Count);
        Assert.All(undo.Observations, result => Assert.Equal("busy", result));
        Assert.Equal(failCommit ? 0x11u : 0xAAu, fixture.Rom.u8(0x200));
        Assert.False(ContentRepoGitService.IsRunning());
        using var lease = PatchDatabaseOperationLeaseCore.Acquire(fixture.Root);
    }

    sealed class NativeUndoProbe(string root, bool failCommit) : Services.UndoService
    {
        internal List<string> Observations { get; } = new();
        void Probe()
        {
            using var process = new NativeLeaseProcess(root, false);
            Observations.Add(process.Result);
        }
        public override void Begin(string name) { Probe(); base.Begin(name); }
        public override void Commit()
        {
            Probe();
            if (failCommit) throw new IOException("owned undo commit failure");
            base.Commit();
        }
        public override void Rollback() { Probe(); base.Rollback(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedActionsUsePublishedFallbackScopeAndNeverRepairReadOnlyLocks(bool readOnly)
    {
        using var primary = new Fixture();
        using var selected = new Fixture();
        selected.MakeManaged();
        var vm = selected.CreateViewModel("install");
        CoreState.BaseDirectory = primary.Root;
        string path = Path.Combine(selected.Root, ".patch2-import", "lease.lock");
        var original = File.GetAttributes(path);
        UnixFileMode mode = default;
        try
        {
            if (readOnly)
            {
                if (OperatingSystem.IsWindows()) File.SetAttributes(path, original | FileAttributes.ReadOnly);
                else { mode = File.GetUnixFileMode(path); File.SetUnixFileMode(path, UnixFileMode.UserRead); }
                var attributes = File.GetAttributes(path);
                Assert.NotEqual(PatchManagerViewModel.PatchDatabaseBusyMessage, vm.InstallPatch(true));
                Assert.Equal(attributes, File.GetAttributes(path));
                if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path));
            }
            else
            {
                using var holder = new NativeLeaseProcess(selected.Root, true);
                Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage, vm.InstallPatch(true));
            }
            Assert.False(Directory.Exists(Path.Combine(primary.Root, ".patch2-import")));
            Assert.Equal(0x11u, selected.Rom.u8(0x200));
            Assert.Empty(CoreState.Undo.UndoBuffer);
            Assert.False(File.Exists(PatchMetadataCore.GetBackupFilePath(selected.Descriptor)));
        }
        finally
        {
            if (readOnly)
            {
                if (OperatingSystem.IsWindows()) File.SetAttributes(path, original);
                else File.SetUnixFileMode(path, mode);
            }
        }
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaTheory]
    [InlineData("install")]
    [InlineData("force")]
    [InlineData("uninstall")]
    [InlineData("clean")]
    [InlineData("async")]
    [InlineData("translation")]
    public async Task ManagedForeignWriterBlocksEveryActionBeforeAnyMutation(string action)
    {
        using var fixture = new Fixture();
        fixture.MakeManaged();
        if (action == "translation") fixture.SeedTranslationPatch();
        var vm = fixture.CreateViewModel(action == "async" ? "clean" : action);
        bool backupObserved = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedPatchNeedsCleanRom))
            {
                backupObserved = true;
                _ = vm.SelectedPatchNeedsCleanRom;
            }
        };
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        var files = fixture.Snapshot();
        using (var holder = new NativeLeaseProcess(fixture.Root, hold: true))
        {
            Assert.False(ContentRepoGitService.IsRunning());
            bool dialog = false;
            string message = action switch
            {
                "translation" => InstallTranslationPatch(new ToolTranslateROMView(), fixture.Rom),
                "async" => await vm.UninstallPatchAsync(() =>
                {
                    dialog = true;
                    return Task.FromResult<string?>(fixture.CleanRom);
                }),
                _ => RunAction(vm, action, fixture.CleanRom),
            };
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage, message);
            Assert.False(dialog);
            Assert.False(backupObserved);
            Assert.Equal(before, fixture.Rom.Data);
            Assert.Empty(CoreState.Undo.UndoBuffer);
        }
        fixture.AssertSnapshot(files);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("payload")]
    [InlineData("shared")]
    [InlineData("backup")]
    public void ManagedCompletedReplacementRejectsPublishedSelectionEvenWithoutContention(string changed)
    {
        using var fixture = new Fixture();
        fixture.MakeManaged();
        string shared = Path.Combine(fixture.Root, "config", "patch2", "shared.bin");
        File.WriteAllBytes(shared, new byte[] { 1 });
        var vm = fixture.CreateViewModel(changed == "backup" ? "uninstall" : "install");
        string path = changed switch
        {
            "descriptor" => fixture.Descriptor,
            "payload" => Path.Combine(fixture.Library, "test.bin"),
            "backup" => PatchMetadataCore.GetBackupFilePath(fixture.Descriptor),
            _ => shared,
        };
        var time = File.GetLastWriteTimeUtc(path);
        using (PatchDatabaseOperationLeaseCore.Acquire(fixture.Root))
        {
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 1;
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, time);
        }
        var before = fixture.Snapshot();
        byte[] rom = (byte[])fixture.Rom.Data.Clone();
        string message = changed == "backup" ? vm.UninstallPatch() : vm.InstallPatch(true);
        Assert.Equal(R._("The patch database changed. Refresh the list and select the patch again."), message);
        Assert.Equal(rom, fixture.Rom.Data);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        fixture.AssertSnapshot(before);
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("force")]
    [InlineData("uninstall")]
    [InlineData("clean")]
    [InlineData("async-install")]
    [InlineData("async-force")]
    [InlineData("async-uninstall")]
    [InlineData("async-clean")]
    public async Task UnmanagedPublicationRefusesDifferentSameVersionRomBeforeAction(string action)
    {
        using var fixture = new Fixture();
        string fixtureAction = action.StartsWith("async-", StringComparison.Ordinal) ? action[6..] : action;
        var vm = fixture.CreateViewModel(fixtureAction);
        byte[] publishedRom = (byte[])fixture.Rom.Data.Clone();
        bool publishedModified = fixture.Rom.Modified;
        byte[] replacementBytes = (byte[])publishedRom.Clone();
        replacementBytes[0x210] ^= 1;
        var replacement = new ROM();
        replacement.LoadLow("replacement-owned-guard.gba", replacementBytes, "BE8E01");
        Assert.Equal(fixture.Rom.RomInfo.GetType(), replacement.RomInfo.GetType());
        CoreState.ROM = replacement;
        byte[] before = (byte[])replacement.Data.Clone();
        bool modified = replacement.Modified;
        var files = fixture.Snapshot();
        bool pickerOpened = false;

        string message = action switch
        {
            "async-install" => await vm.InstallPatchAsync(false, CancellationToken.None),
            "async-force" => await vm.InstallPatchAsync(true, CancellationToken.None),
            "async-uninstall" or "async-clean" => await vm.UninstallPatchAsync(() =>
            {
                pickerOpened = true;
                return Task.FromResult<string?>(fixture.CleanRom);
            }),
            _ => RunAction(vm, action, fixture.CleanRom),
        };

        Assert.Equal(R._("The patch database changed. Refresh the list and select the patch again."), message);
        Assert.False(pickerOpened);
        Assert.Same(replacement, CoreState.ROM);
        Assert.Equal(before, replacement.Data);
        Assert.Equal(modified, replacement.Modified);
        Assert.Equal(publishedRom, fixture.Rom.Data);
        Assert.Equal(publishedModified, fixture.Rom.Modified);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        fixture.AssertSnapshot(files);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".patch2-import")));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmanagedPublicationRefusesUnpublishedSelectionOrChangedPath(bool changePath)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("install");
        if (changePath)
        {
            string other = Path.Combine(fixture.Library, "PATCH_changed.txt");
            File.Copy(fixture.Descriptor, other);
            vm.SelectedPatch!.PatchFilePath = other;
        }
        else
        {
            vm.SelectedPatch = fixture.CreateViewModel("install").SelectedPatch;
        }
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        bool modified = fixture.Rom.Modified;
        var files = fixture.Snapshot();

        Assert.Equal(R._("The patch database changed. Refresh the list and select the patch again."),
            vm.InstallPatch());

        Assert.Equal(before, fixture.Rom.Data);
        Assert.Equal(modified, fixture.Rom.Modified);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        fixture.AssertSnapshot(files);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".patch2-import")));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Fact]
    public void ManagedActionExcludesNativeWriterThroughStatusAndInvalidatesOwnBackupSnapshot()
    {
        using var fixture = new Fixture();
        fixture.MakeManaged();
        var vm = fixture.CreateViewModel("install");
        bool observed = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.StatusMessage) || !ContentRepoGitService.IsRunning()) return;
            using var probe = new NativeLeaseProcess(fixture.Root, hold: false);
            Assert.Equal("busy", probe.Result);
            observed = true;
        };
        Assert.Contains("installed", vm.InstallPatch(true));
        Assert.True(observed);
        Assert.False(vm.CanUninstall);
        vm.LoadPatchList();
        vm.SelectedPatch = Assert.Single(vm.FilteredPatches);
        Assert.True(vm.CanUninstall);
        Assert.Contains("restored", vm.UninstallPatch());
    }

    [Fact]
    public void NativeActionLeaseProbe()
    {
        string? root = Environment.GetEnvironmentVariable("FEBUILDER_ACTION_LEASE_ROOT");
        if (root == null) return;
        Assert.StartsWith(Path.Combine(AppContext.BaseDirectory, "TestResults") + Path.DirectorySeparatorChar,
            Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        try
        {
            using var lease = PatchDatabaseOperationLeaseCore.Acquire(root);
            PublishResult("acquired");
            if (Environment.GetEnvironmentVariable("FEBUILDER_ACTION_LEASE_HOLD") == "1")
                Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(root, "native-action-release")), 60_000));
        }
        catch (PatchDatabaseOperationLeaseCore.BusyException)
        {
            PublishResult("busy");
        }
        void PublishResult(string result)
        {
            string path = Path.Combine(root, "native-action-result");
            File.WriteAllText(path + ".pending", result);
            File.Move(path + ".pending", path, true);
        }
    }

    internal sealed class NativeLeaseProcess : IDisposable
    {
        readonly string root;
        readonly System.Diagnostics.Process process;
        readonly Task<string> stdout;
        readonly Task<string> stderr;
        internal string Result
        {
            get
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    try { return File.ReadAllText(Path.Combine(root, "native-action-result")); }
                    // A renamed IPC file can become visible before Windows closes the rename handle.
                    catch (IOException ex) when (PatchDatabaseOperationLeaseCore.IsLeaseContention(ex.HResult) &&
                        elapsed.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        Thread.Sleep(10);
                    }
                }
            }
        }
        internal NativeLeaseProcess(string root, bool hold)
        {
            this.root = root;
            File.Delete(Path.Combine(root, "native-action-result"));
            File.Delete(Path.Combine(root, "native-action-release"));
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(PatchManagerOperationGuardTests).Assembly.Location);
            start.ArgumentList.Add("--Tests:FEBuilderGBA.Avalonia.Tests.PatchManagerOperationGuardTests.NativeActionLeaseProbe");
            start.Environment["FEBUILDER_ACTION_LEASE_ROOT"] = root;
            start.Environment["FEBUILDER_ACTION_LEASE_HOLD"] = hold ? "1" : "0";
            process = System.Diagnostics.Process.Start(start)!;
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            try
            {
                Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(root, "native-action-result")) || process.HasExited, 60_000));
                Assert.True(File.Exists(Path.Combine(root, "native-action-result")), stdout.IsCompleted ? stdout.Result + stderr.Result : "No native result");
                if (hold) Assert.Equal("acquired", Result);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            try
            {
                File.WriteAllText(Path.Combine(root, "native-action-release"), "release");
                if (!process.WaitForExit(60_000))
                {
                    process.Kill(true);
                    process.WaitForExit();
                    throw new TimeoutException("Owned native action probe timed out.");
                }
                Assert.True(process.ExitCode == 0, stdout.Result + stderr.Result);
            }
            finally { process.Dispose(); }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TranslationPatchRefusesBusyGateBeforeDiscoveryAndMutation(bool invalidDiscoveryRoot)
    {
        using var fixture = new Fixture();
        fixture.SeedTranslationPatch();
        var view = new ToolTranslateROMView();
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        var files = fixture.Snapshot();
        if (invalidDiscoveryRoot) CoreState.BaseDirectory = "\0";
        Assert.True(ContentRepoGitService.TryEnter());
        try
        {
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage, InstallTranslationPatch(view, fixture.Rom));
            Assert.Equal(before, fixture.Rom.Data);
            Assert.False(fixture.Rom.Modified);
            Assert.Empty(CoreState.Undo.UndoBuffer);
            fixture.AssertSnapshot(files);
            Assert.True(ContentRepoGitService.IsRunning());
        }
        finally { ContentRepoGitService.Exit(); }
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("invalid-root")]
    public void TranslationPatchReleasesGateAndPreservesExistingSuccessAndUndo(string outcome)
    {
        using var fixture = new Fixture();
        if (outcome == "success") fixture.SeedTranslationPatch();
        if (outcome == "invalid-root") CoreState.BaseDirectory = "\0";
        var view = new ToolTranslateROMView();
        string result = InstallTranslationPatch(view, fixture.Rom);
        Assert.False(ContentRepoGitService.IsRunning());
        if (outcome != "success")
        {
            Assert.Contains(outcome == "missing" ? "not found" : "local absolute", result);
            Assert.Equal(0x11u, fixture.Rom.u8(0x200));
            Assert.Empty(CoreState.Undo.UndoBuffer);
        }
        else
        {
            Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
            Assert.Single(CoreState.Undo.UndoBuffer);
            CoreState.Undo.RunUndo();
            Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        }
    }

    static string InstallTranslationPatch(ToolTranslateROMView view, ROM rom)
        => (string)typeof(ToolTranslateROMView).GetMethod("InstallChapterNameToTextPatch",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { rom })!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncUninstallRefusesInPlaceRomMutationDuringCleanRomPicker(bool rawArrayWrite)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("clean");
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> operation = vm.UninstallPatchAsync(() => selection.Task);
        var data = fixture.Rom.Data;
        if (rawArrayWrite) fixture.Rom.Data[0x300] = 0x42;
        else fixture.Rom.write_u8(0x300, 0x42);
        bool modified = fixture.Rom.Modified;
        byte[] edited = (byte[])fixture.Rom.Data.Clone();
        try
        {
            Assert.False(operation.IsCompleted);
            Assert.True(ContentRepoGitService.IsRunning());
            selection.SetResult(fixture.CleanRom);
            Assert.Equal(R._("The loaded ROM or selected patch changed. Uninstall was cancelled."), await operation);
            Assert.Same(data, fixture.Rom.Data);
            Assert.Equal(edited, fixture.Rom.Data);
            Assert.Equal(modified, fixture.Rom.Modified);
            Assert.Empty(CoreState.Undo.UndoBuffer);
        }
        finally { selection.TrySetResult(null); await operation; }
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("force")]
    [InlineData("uninstall")]
    [InlineData("clean")]
    public void SharedOperationBlocksEveryMutationEntryWithoutChangingRomOrLibrary(string action)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel(action);
        byte[] before = (byte[])fixture.Rom.Data.Clone();
        var files = fixture.Snapshot();
        Assert.True(ContentRepoGitService.TryEnter());
        try
        {
            Assert.False(vm.CanInstall);
            Assert.False(vm.CanUninstall);
            string message = RunAction(vm, action, fixture.CleanRom);
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage, message);
            Assert.True(ContentRepoGitService.IsRunning());
            Assert.Equal(before, fixture.Rom.Data);
            Assert.False(fixture.Rom.Modified);
            Assert.Empty(CoreState.Undo.UndoBuffer);
            fixture.AssertSnapshot(files);
        }
        finally { ContentRepoGitService.Exit(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstallOwnsSharedGateThroughStatusRefreshAndReleasesIt(bool missingDescriptor)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("install");
        if (missingDescriptor) File.Delete(fixture.Descriptor);
        bool observed = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(vm.StatusMessage)) return;
            observed = true;
            Assert.True(ContentRepoGitService.IsRunning());
            Assert.False(ContentRepoGitService.TryEnter());
        };
        vm.InstallPatch(forceIgnoreDependencies: true);
        Assert.True(observed);
        Assert.False(ContentRepoGitService.IsRunning());
        Assert.Equal(missingDescriptor ? 0x11u : 0xAAu, fixture.Rom.u8(0x200));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanRomDialogKeepsSharedGateUntilCancellationOrException(bool throwFromDialog)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("clean");
        var other = fixture.CreateViewModel("force");
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool opened = false;
        Task<string> pending = vm.UninstallPatchAsync(() =>
        {
            opened = true;
            entered.SetResult();
            Assert.True(ContentRepoGitService.IsRunning());
            return selection.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(opened);
            Assert.False(pending.IsCompleted);
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage, other.InstallPatch(true));
            Assert.False(ContentRepoGitService.TryEnter());
            if (throwFromDialog)
            {
                selection.SetException(new IOException("Owned dialog failure"));
                await Assert.ThrowsAsync<IOException>(() => pending);
            }
            else
            {
                selection.SetResult(null);
                Assert.Equal(R._("Uninstall cancelled."), await pending);
            }
        }
        finally
        {
            selection.TrySetResult(null);
            try { await pending; }
            catch (IOException) when (throwFromDialog) { }
        }
        Assert.False(ContentRepoGitService.IsRunning());
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
    }

    [Fact]
    public async Task BusyAsyncUninstallDoesNotOpenTheDialog()
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("clean");
        Assert.True(ContentRepoGitService.TryEnter());
        try
        {
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage,
                await vm.UninstallPatchAsync(() => throw new InvalidOperationException("Must not open")));
        }
        finally { ContentRepoGitService.Exit(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncUninstallPreservesSuccessfulBackupAndCleanRomBehavior(bool backup)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel(backup ? "uninstall" : "clean");
        bool selected = false;
        string message = await vm.UninstallPatchAsync(() =>
        {
            selected = true;
            Assert.True(ContentRepoGitService.IsRunning());
            return Task.FromResult<string?>(fixture.CleanRom);
        });
        Assert.Equal(!backup, selected);
        Assert.Contains("restored", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0x11u, fixture.Rom.u8(0x200));
        Assert.Single(CoreState.Undo.UndoBuffer);
        Assert.False(ContentRepoGitService.IsRunning());
        CoreState.Undo.RunUndo();
        Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncUninstallRefusesChangedRomOrPatchAfterDialog(bool changeRom)
    {
        using var fixture = new Fixture();
        var vm = fixture.CreateViewModel("clean");
        var replacement = fixture.CreateViewModel("clean").SelectedPatch;
        string message = await vm.UninstallPatchAsync(() =>
        {
            if (changeRom) CoreState.ROM = new ROM();
            else vm.SelectedPatch = replacement;
            return Task.FromResult<string?>(fixture.CleanRom);
        });
        Assert.Equal(R._("The loaded ROM or selected patch changed. Uninstall was cancelled."), message);
        Assert.Empty(CoreState.Undo.UndoBuffer);
        Assert.Equal(0xAAu, fixture.Rom.u8(0x200));
        Assert.False(ContentRepoGitService.IsRunning());
    }

    [AvaloniaFact]
    public async Task AttachedViewObservesSharedActivityWithoutAnotherUserAction()
    {
        using var fixture = new Fixture();
        var view = new PatchManagerView();
        var host = new Window { Content = view };
        bool ownsGate = false;
        try
        {
            host.Show();
            await view.RefreshTask;
            var list = view.FindControl<ListBox>("PatchListBox")!;
            list.ItemsSource = new[] { fixture.CreateViewModel("install").SelectedPatch! };
            list.SelectedIndex = 0;
            Assert.True(view.FindControl<Button>("InstallButton")!.IsEnabled);
            ownsGate = ContentRepoGitService.TryEnter();
            Assert.True(ownsGate);
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Button>("InstallButton")!.IsEnabled);
            Assert.False(view.FindControl<Button>("InitUpdatePatch2Button")!.IsEnabled);
            ContentRepoGitService.Exit();
            ownsGate = false;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Button>("InstallButton")!.IsEnabled);
        }
        finally
        {
            if (ownsGate) ContentRepoGitService.Exit();
            host.Close();
            await view.RefreshTask;
        }
    }

    [AvaloniaTheory]
    [InlineData("install", false)]
    [InlineData("force", false)]
    [InlineData("uninstall", false)]
    [InlineData("install", true)]
    [InlineData("force", true)]
    [InlineData("uninstall", true)]
    public void ViewDisablesAndGuardsActionsForLocalOrSharedGit(string action, bool shared)
    {
        using var fixture = new Fixture();
        var view = new PatchManagerView();
        var vm = (PatchManagerViewModel)Field("_vm").GetValue(view)!;
        vm.SelectedPatch = fixture.CreateViewModel(action).SelectedPatch;
        if (action == "force") vm.SelectedPatch!.UnsatisfiedDependencyCount = 1;
        var before = fixture.Snapshot();
        if (shared) Assert.True(ContentRepoGitService.TryEnter());
        else Field("_gitRunning").SetValue(view, true);
        try
        {
            Invoke(view, "UpdateOperationControls");
            Assert.False(view.FindControl<Button>("InstallButton")!.IsEnabled);
            Assert.False(view.FindControl<Button>("ForceInstallButton")!.IsEnabled);
            Assert.False(view.FindControl<Button>("UninstallButton")!.IsEnabled);
            if (action == "uninstall") Invoke(view, "OnUninstallClick", null, new global::Avalonia.Interactivity.RoutedEventArgs());
            else Invoke(view, "DoInstall", action == "force");
            Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage,
                view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
            Assert.Empty(CoreState.Undo.UndoBuffer);
            fixture.AssertSnapshot(before);
        }
        finally
        {
            if (shared) ContentRepoGitService.Exit();
            else Field("_gitRunning").SetValue(view, false);
        }
        Invoke(view, "UpdateOperationControls");
        Assert.True(view.FindControl<Button>(action == "uninstall" ? "UninstallButton" :
            action == "force" ? "ForceInstallButton" : "InstallButton")!.IsEnabled);
    }

    static FieldInfo Field(string name) => typeof(PatchManagerView).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    static void Invoke(PatchManagerView view, string method, params object?[] args)
        => typeof(PatchManagerView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, args);
    static string RunAction(PatchManagerViewModel vm, string action, string cleanRom) => action switch
    {
        "install" => vm.InstallPatch(),
        "force" => vm.InstallPatch(true),
        "uninstall" => vm.UninstallPatch(),
        _ => vm.UninstallPatchWithCleanRom(cleanRom),
    };

    internal sealed class Fixture : IDisposable
    {
        readonly ROM savedRom = CoreState.ROM;
        readonly Undo savedUndo = CoreState.Undo;
        readonly string savedLanguage = CoreState.Language;
        readonly string savedBase = CoreState.BaseDirectory;
        readonly string root = Path.Combine(AppContext.BaseDirectory, "TestResults", "patch-guard-" + Guid.NewGuid().ToString("N"));
        public ROM Rom { get; } = new();
        public string Root => root;
        public string Library => Path.Combine(root, "config", "patch2", "FE8U");
        public string Descriptor => Path.Combine(Library, "PATCH_test.txt");
        public string CleanRom => Path.Combine(root, "clean.gba");

        public Fixture()
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "config", "patch2", "FE8U"));
            var bytes = new byte[0x1000000];
            System.Text.Encoding.ASCII.GetBytes("BE8E01").CopyTo(bytes, 0xAC);
            bytes[0x200] = 0x11;
            Rom.LoadLow("owned-guard.gba", bytes, "BE8E01");
            File.WriteAllBytes(CleanRom, bytes);
            File.WriteAllBytes(Path.Combine(Library, "test.bin"), new byte[] { 0xAA });
            File.WriteAllText(Descriptor, "TYPE=BIN\nBIN:0x200=test.bin\nPATCHED_IF:0x200=0xAA");
            CoreState.ROM = Rom;
            CoreState.Undo = new Undo();
            CoreState.Language = "en";
            CoreState.BaseDirectory = root;
        }

        public PatchManagerViewModel CreateViewModel(string action)
        {
            bool installed = action is "uninstall" or "clean";
            if (installed) Rom.Data[0x200] = 0xAA;
            if (action == "uninstall")
                File.WriteAllText(PatchMetadataCore.GetBackupFilePath(Descriptor), "0x200:1:11");
            var vm = new PatchManagerViewModel();
            vm.LoadPatchList();
            vm.SelectedPatch = vm.FilteredPatches.Single(p => p.PatchFilePath == Descriptor);
            return vm;
        }

        public void MakeManaged() { using var lease = PatchDatabaseOperationLeaseCore.Acquire(root); }
        public void SeedTranslationPatch()
        {
            string library = Path.Combine(root, "config", "patch2", "FE8U");
            File.WriteAllBytes(Path.Combine(library, "test.bin"), new byte[] { 0xAA });
            File.WriteAllText(Path.Combine(library, "PATCH_chapter.txt"),
                "NAME=Convert Chapter Titles to Text\nTYPE=BIN\nBIN:0x200=test.bin\nPATCHED_IF:0x200=0xAA");
        }

        public Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("native-action-", StringComparison.Ordinal))
            .ToDictionary(path => path, File.ReadAllBytes);
        public void AssertSnapshot(Dictionary<string, byte[]> before)
        {
            var after = Snapshot();
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            foreach (var entry in before) Assert.Equal(entry.Value, after[entry.Key]);
        }
        public void Dispose()
        {
            CoreState.ROM = savedRom;
            CoreState.Undo = savedUndo;
            CoreState.Language = savedLanguage;
            CoreState.BaseDirectory = savedBase;
            Directory.Delete(root, true);
        }
    }
}
