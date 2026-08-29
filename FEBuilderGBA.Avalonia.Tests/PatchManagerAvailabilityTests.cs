using System;
using System.Collections.Generic;
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
            CoreState.ROM = null!;
            var view = new PatchManagerView();
            var host = new Window { Content = view };
            host.Show();
            try
            {
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
                Assert.False(view.FindControl<Button>("InstallButton")!.IsEnabled);
                Assert.False(view.FindControl<Button>("ForceInstallButton")!.IsEnabled);
                Assert.False(view.FindControl<Button>("ForceInstallButton")!.IsVisible);
                Assert.False(view.FindControl<Button>("UninstallButton")!.IsEnabled);
            }
            finally
            {
                host.Close();
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
