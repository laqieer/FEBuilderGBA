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
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool opened = false;
        Task<string> pending = vm.UninstallPatchAsync(() =>
        {
            opened = true;
            Assert.True(ContentRepoGitService.IsRunning());
            return selection.Task;
        });
        Assert.True(opened);
        Assert.False(pending.IsCompleted);
        Assert.Equal(PatchManagerViewModel.PatchDatabaseBusyMessage,
            fixture.CreateViewModel("force").InstallPatch(true));
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
        string message = await vm.UninstallPatchAsync(() =>
        {
            if (changeRom) CoreState.ROM = new ROM();
            else vm.SelectedPatch = fixture.CreateViewModel("clean").SelectedPatch;
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

    sealed class Fixture : IDisposable
    {
        readonly ROM savedRom = CoreState.ROM;
        readonly Undo savedUndo = CoreState.Undo;
        readonly string savedLanguage = CoreState.Language;
        readonly string savedBase = CoreState.BaseDirectory;
        readonly string root = Path.Combine(AppContext.BaseDirectory, "TestResults", "patch-guard-" + Guid.NewGuid().ToString("N"));
        public ROM Rom { get; } = new();
        public string Descriptor => Path.Combine(root, "PATCH_test.txt");
        public string CleanRom => Path.Combine(root, "clean.gba");

        public Fixture()
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "config", "patch2", "FE8U"));
            var bytes = new byte[0x1000000];
            bytes[0x200] = 0x11;
            Rom.LoadLow("owned-guard.gba", bytes, "BE8E01");
            File.WriteAllBytes(CleanRom, bytes);
            File.WriteAllBytes(Path.Combine(root, "test.bin"), new byte[] { 0xAA });
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
            return new PatchManagerViewModel
            {
                SelectedPatch = new PatchEntry
                {
                    Name = "Owned guard patch", Type = "BIN", PatchFilePath = Descriptor, DirectoryPath = root,
                    Status = installed ? PatchMetadataCore.PatchStatus.Installed : PatchMetadataCore.PatchStatus.NotInstalled,
                },
            };
        }

        public Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
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
