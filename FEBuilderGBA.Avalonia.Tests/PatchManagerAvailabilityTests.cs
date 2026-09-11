using System;
using System.Collections.Generic;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PatchManagerAvailabilityTests
    {
        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ManagedViewActionHonorsLatestIntentAndReattachmentBeforeRefreshing(bool reattach)
        {
            using var fixture = new PatchManagerOperationGuardTests.Fixture();
            fixture.MakeManaged();
            var view = new PatchManagerView();
            var host = new Window { Content = view };
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                host.Show();
                Assert.True(await view.RefreshTask);
                var vm = (PatchManagerViewModel)typeof(PatchManagerView).GetField("_vm",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                PatchManagerRefreshTests.SetVerifier(vm, (snapshot, scope, token) =>
                {
                    Assert.False(Dispatcher.UIThread.CheckAccess());
                    entered.TrySetResult();
                    release.Wait();
                    return snapshot.Matches(scope, token);
                });
                var list = view.FindControl<ListBox>("PatchListBox")!;
                list.SelectedIndex = 0;
                view.FindControl<Button>("InstallButton")!.RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(view.FindControl<Button>("InstallButton")!.IsEnabled);
                if (reattach) { host.Content = null; host.Content = view; }
                else view.FindControl<TextBox>("SearchBox")!.Text = "latest-no-match";
                await Dispatcher.UIThread.InvokeAsync(() => Assert.False(view.ActionTask.IsCompleted));
                Assert.Throws<PatchDatabaseOperationLeaseCore.BusyException>(() => PatchDatabaseOperationLeaseCore.Acquire(fixture.Root));
                release.Set();
                await view.ActionTask;
                Assert.Equal(0x11u, fixture.Rom.u8(0x200));
                Assert.Empty(CoreState.Undo.UndoBuffer);
                Assert.True(await view.RefreshTask);
                Assert.Equal(reattach ? "" : "latest-no-match", vm.FilterText);

                view.FindControl<TextBox>("SearchBox")!.Text = "";
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await view.RefreshTask;
                list.SelectedIndex = 0;
                Assert.NotNull(vm.SelectedPatch);
                Assert.Same(list.SelectedItem, vm.SelectedPatch);
                Assert.True(view.FindControl<Button>("InstallButton")!.IsEnabled);
                view.FindControl<Button>("InstallButton")!.RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await view.ActionTask;
                Assert.True(fixture.Rom.u8(0x200) == 0xAA,
                    view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
                Assert.True(await view.RefreshTask);
                list.SelectedIndex = 0;
                Assert.True(view.FindControl<Button>("UninstallButton")!.IsEnabled);
            }
            finally
            {
                release.Set();
                host.Close();
                await view.ActionTask;
                await view.RefreshTask;
            }
            Assert.False(ContentRepoGitService.IsRunning());
        }

        [AvaloniaTheory]
        [InlineData("ja", false, false)]
        [InlineData("zh", false, false)]
        [InlineData("ja", true, false)]
        [InlineData("zh", true, false)]
        [InlineData("ja", true, true)]
        [InlineData("zh", true, true)]
        public async Task IncompleteRefreshPublishesLocalizedDiagnosticAndRetainsRecoveryNotice(string language, bool retained, bool newerNotice)
        {
            using var fixture = new PatchManagerRefreshTests.Fixture();
            var translations = typeof(MyTranslateResource).GetField("Resource",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            object? previousTranslations = translations.GetValue(null);
            var previousNotice = App.CapturePatchDatabaseRecoveryNotice();
            var view = new PatchManagerView();
            var host = new Window { Content = view };
            try
            {
                MyTranslateResource.LoadResource(Path.Combine(FindRepoRoot(), "config", "translate", language + ".txt"));
                App.ClearPatchDatabaseRecoveryNotice();
                if (retained) App.RecordPatchDatabaseRecovery(new PatchDatabaseImportCore.RecoveryException(
                    fixture.Root, new IOException("owned retained workspace")));
                var noticeIdentity = App.CapturePatchDatabaseRecoveryNotice();
                string notice = App.PatchDatabaseRecoveryNotice;
                using var locked = new FileStream(Path.Combine(fixture.Library, "PATCH_owned.txt"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.False(PatchMetadataCore.TryEnumeratePatches(fixture.Library, fixture.Rom, "en", out _, out string diagnostic));
                Assert.NotEmpty(diagnostic);
                var refresh = new Services.PatchManagerRefreshService((request, token) =>
                {
                    var result = Services.PatchManagerRefreshService.Read(request with { Strict = true }, token);
                    if (newerNotice) App.RecordPatchDatabaseRecovery(new PatchDatabaseImportCore.RecoveryException(
                        fixture.Root, new IOException("owned retained workspace")));
                    return result;
                });
                typeof(PatchManagerView).GetField("_refresh",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, refresh);
                host.Show();
                Assert.False(await view.RefreshTask);
                string localized = R._("The installed database could not be refreshed: {0}", diagnostic);
                string expected = retained ? notice + "\n" + localized : localized;
                Assert.Equal(expected, view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
                Assert.Contains(diagnostic, expected);
                Assert.Equal(Services.PatchManagerRefreshService.ImportedRefreshFailureTemplate, refresh.Failure!.Template);
                Assert.Equal(diagnostic, refresh.Failure.Detail);
                if (newerNotice) Assert.NotSame(noticeIdentity, App.CapturePatchDatabaseRecoveryNotice());
                else Assert.Same(noticeIdentity, App.CapturePatchDatabaseRecoveryNotice());
                Assert.False(ContentRepoGitService.IsRunning());
            }
            finally
            {
                host.Close();
                await view.RefreshTask;
                translations.SetValue(null, previousTranslations);
                App.ClearPatchDatabaseRecoveryNotice();
                if (previousNotice?.Result != null) App.RecordPatchDatabaseRecovery(previousNotice.Result);
                if (previousNotice?.Exception != null) App.RecordPatchDatabaseRecovery(previousNotice.Exception);
            }
        }

        [Theory]
        [InlineData("", "Available", true)]
        [InlineData("BIN", "Available", true)]
        [InlineData("bin", "Unsupported", false)]
        [InlineData("EA", "Unsupported", false)]
        [InlineData("ea", "Unsupported", false)]
        [InlineData("CUSTOM", "Unsupported", false)]
        public void NotInstalledPatch_StatusAndInstallEligibilityReflectType(
            string type,
            string expectedStatus,
            bool expectedCanInstall)
        {
            var patch = new PatchEntry
            {
                Status = PatchMetadataCore.PatchStatus.NotInstalled,
                Type = type,
                PatchFilePath = "PATCH_Test.txt",
            };
            var vm = new PatchManagerViewModel { SelectedPatch = patch };

            Assert.Equal(expectedStatus, patch.StatusText);
            Assert.Equal(expectedCanInstall, vm.CanInstall);
            Assert.Equal(expectedCanInstall, string.IsNullOrEmpty(patch.ActionRestrictionMessage));
        }

        [Fact]
        public void InstalledUnsupportedPatch_RemainsInstalledButCannotUninstall()
        {
            var patch = new PatchEntry
            {
                Status = PatchMetadataCore.PatchStatus.Installed,
                Type = "EA",
                PatchFilePath = "PATCH_Test.txt",
            };
            var vm = new PatchManagerViewModel { SelectedPatch = patch };

            Assert.Equal("Installed", patch.StatusText);
            Assert.False(vm.CanUninstall);
            Assert.Contains("Event Assembler", patch.ActionRestrictionMessage);
        }

        [Fact]
        public void UnknownUnsupportedType_HasGenericExplanation()
        {
            var patch = new PatchEntry
            {
                Status = PatchMetadataCore.PatchStatus.NotInstalled,
                Type = "CUSTOM",
                PatchFilePath = "PATCH_Test.txt",
            };

            Assert.Contains("CUSTOM", patch.ActionRestrictionMessage);
            Assert.Contains("not currently supported", patch.ActionRestrictionMessage);
        }

        [Theory]
        [InlineData("ja", "未対応")]
        [InlineData("zh", "不支持")]
        public void UnsupportedStatus_LocalizesFromShippedTable(string language, string expected)
        {
            string path = Path.Combine(
                FindRepoRoot(),
                "config",
                "translate",
                language + ".txt");
            try
            {
                MyTranslateResource.LoadResource(path);
                var patch = new PatchEntry
                {
                    Status = PatchMetadataCore.PatchStatus.NotInstalled,
                    Type = "EA",
                };

                Assert.Equal(expected, patch.StatusText);
            }
            finally
            {
                MyTranslateResource.Clear();
            }
        }

        [AvaloniaFact]
        public void SelectingUnsupportedEaPatch_ShowsExplanationAndDisablesActions()
        {
            ROM? savedRom = CoreState.ROM;
            Window? host = null;
            try
            {
                CoreState.ROM = null!;
                var view = new PatchManagerView();
                host = new Window { Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                var patch = new PatchEntry
                {
                    Name = "EA Test",
                    Status = PatchMetadataCore.PatchStatus.NotInstalled,
                    Type = "EA",
                    PatchFilePath = "PATCH_Test.txt",
                    UnsatisfiedDependencyCount = 1,
                    UnsatisfiedDependencies = new List<PatchMetadataCore.PatchDependency>
                    {
                        new() { Comment = "Dependency needed" },
                    },
                };
                ListBox list = view.FindControl<ListBox>("PatchListBox")!;
                list.ItemsSource = new[] { patch };
                list.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Unsupported", view.FindControl<TextBlock>("DetailStatus")!.Text);
                Assert.Contains(
                    "Event Assembler",
                    view.FindControl<TextBlock>("StatusMessageLabel")!.Text);
                Assert.Equal(
                    AutomationLiveSetting.Polite,
                    AutomationProperties.GetLiveSetting(
                        view.FindControl<TextBlock>("StatusMessageLabel")!));
                Assert.False(view.FindControl<Button>("InstallButton")!.IsEnabled);
                Assert.False(view.FindControl<Button>("ForceInstallButton")!.IsEnabled);
                Assert.False(view.FindControl<Button>("ForceInstallButton")!.IsVisible);
                Assert.False(view.FindControl<Button>("UninstallButton")!.IsEnabled);
            }
            finally
            {
                host?.Close();
                CoreState.ROM = savedRom!;
            }
        }

        static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory);
                 dir != null;
                 dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException("Could not locate FEBuilderGBA.sln.");
        }
    }
}
