// SPDX-License-Identifier: GPL-3.0-or-later
// #1978 Slice 2 review fix (finding #3): the FEMapCreator Options status line must refresh as
// soon as the user types directly into either textbox — not only after Browse/Clear/initial
// Load — while never launching a process, performing discovery, or touching the network.
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Views;
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class FEMapCreatorOptionsManualEditStatusTests
    {
        [AvaloniaFact]
        public void TypingNonExistentExecutablePath_ImmediatelyShowsInvalidStatus_WithoutBrowseOrLoad()
        {
            var view = new OptionsView();
            var pathBox = view.FindControl<TextBox>("FEMapCreatorPathTextBox");
            var statusText = view.FindControl<TextBlock>("FEMapCreatorStatusText");
            Assert.NotNull(pathBox);
            Assert.NotNull(statusText);

            pathBox!.Text = Path.Combine(Path.GetTempPath(), "definitely-not-a-real-" + Guid.NewGuid().ToString("N") + ".exe");
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(statusText!.Text);
            Assert.Contains("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaFact]
        public void FEMapCreatorInputs_AreAssociatedWithVisibleLabels()
        {
            var view = new OptionsView();
            Label pathLabel = view.FindControl<Label>("FEMapCreatorPathLabel")!;
            TextBox pathBox = view.FindControl<TextBox>("FEMapCreatorPathTextBox")!;
            Label assetsLabel = view.FindControl<Label>("FEMapCreatorAssetsRootLabel")!;
            TextBox assetsBox = view.FindControl<TextBox>("FEMapCreatorAssetsRootTextBox")!;
            Label tilesetsLabel = view.FindControl<Label>("TilesetOptionsLabel")!;
            ListBox tilesets = view.FindControl<ListBox>("TilesetOptionsListBox")!;

            Assert.Same(pathBox, pathLabel.Target);
            Assert.Equal("Executable Path", pathLabel.Content);
            Assert.Same(assetsBox, assetsLabel.Target);
            Assert.Equal("Assets Root (optional)", assetsLabel.Content);
            Assert.Same(tilesets, tilesetsLabel.Target);
            Assert.Equal("Discovered Tilesets", tilesetsLabel.Content);
        }

        [AvaloniaFact]
        public void FEMapCreatorAndTilesetMappingStatuses_ArePoliteLiveRegions()
        {
            var view = new OptionsView();
            TextBlock profileStatus = view.FindControl<TextBlock>("FEMapCreatorStatusText")!;
            TextBlock status = view.FindControl<TextBlock>("TilesetMappingStatusText")!;
            TextBlock error = view.FindControl<TextBlock>("TilesetMappingErrorText")!;

            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(profileStatus));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
        }

        [AvaloniaFact]
        public void TypingExistingExecutablePath_ImmediatelyShowsConfiguredStatus()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "femc_opt_manual_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3 });
                MakeExecutableOnUnix(exePath);

                var view = new OptionsView();
                var pathBox = view.FindControl<TextBox>("FEMapCreatorPathTextBox");
                var statusText = view.FindControl<TextBlock>("FEMapCreatorStatusText");
                Assert.NotNull(pathBox);
                Assert.NotNull(statusText);

                pathBox!.Text = exePath;
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(statusText!.Text);
                Assert.DoesNotContain("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Not configured", statusText.Text, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
            }
        }

        [AvaloniaFact]
        public void TypingNonExistentAssetsRoot_ImmediatelyShowsInvalidStatus_EvenWithValidExecutable()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "femc_opt_manual_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3 });
                MakeExecutableOnUnix(exePath);

                var view = new OptionsView();
                var pathBox = view.FindControl<TextBox>("FEMapCreatorPathTextBox");
                var assetsBox = view.FindControl<TextBox>("FEMapCreatorAssetsRootTextBox");
                var statusText = view.FindControl<TextBlock>("FEMapCreatorStatusText");
                Assert.NotNull(pathBox);
                Assert.NotNull(assetsBox);
                Assert.NotNull(statusText);

                pathBox!.Text = exePath;
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(statusText!.Text);
                Assert.DoesNotContain("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);

                // Typing a non-existent assets root must immediately invalidate the status, even
                // though the executable itself is still perfectly valid.
                assetsBox!.Text = Path.Combine(tempRoot, "missing-assets-dir");
                Dispatcher.UIThread.RunJobs();

                Assert.Contains("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);

                // Clearing the assets-root text back to blank (an explicitly supported, valid
                // configuration) must immediately restore a non-Invalid status without any
                // Browse/Clear button click.
                assetsBox.Text = "";
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
            }
        }

        [AvaloniaFact]
        public void RapidAssetsRootEdits_RemainMetadataOnly_WhileStatusStillConverges()
        {
            // Live status must not hash executable content on any textbox keystroke, while the
            // displayed status must still converge when the complete assets path becomes valid.
            string tempRoot = Path.Combine(Path.GetTempPath(), "femc_opt_manual_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string exePath = Path.Combine(tempRoot, "FEMapCreator.exe");
                File.WriteAllBytes(exePath, new byte[] { 1, 2, 3, 4, 5 });
                MakeExecutableOnUnix(exePath);
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);

                var view = new OptionsView();
                var pathBox = view.FindControl<TextBox>("FEMapCreatorPathTextBox");
                var assetsBox = view.FindControl<TextBox>("FEMapCreatorAssetsRootTextBox");
                var statusText = view.FindControl<TextBlock>("FEMapCreatorStatusText");
                Assert.NotNull(pathBox);
                Assert.NotNull(assetsBox);
                Assert.NotNull(statusText);

                pathBox!.Text = exePath;
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("Invalid", statusText!.Text, StringComparison.OrdinalIgnoreCase);

                FEMapCreatorSetupSnapshot initial = view.ViewModelForTests.GetFEMapCreatorStatusSnapshot();
                Assert.Equal(FEMapCreatorSetupStatus.Configured, initial.Status);
                Assert.Equal("", initial.ExecutableSha256);

                // Simulate rapid manual typing of the assets-root path, one character at a time,
                // each triggering a TextChanged-driven status refresh (matching finding #3's
                // already-shipped manual-edit-status behavior).
                string partial = "";
                foreach (char c in assetsRoot)
                {
                    partial += c;
                    assetsBox!.Text = partial;
                    Dispatcher.UIThread.RunJobs();
                }

                FEMapCreatorSetupSnapshot afterTyping = view.ViewModelForTests.GetFEMapCreatorStatusSnapshot();
                Assert.Equal(FEMapCreatorSetupStatus.Configured, afterTyping.Status);
                Assert.Equal("", afterTyping.ExecutableSha256);
                Assert.Equal(initial.ExecutableSizeBytes, afterTyping.ExecutableSizeBytes);
                Assert.Equal(initial.ExecutableLastWriteUtcTicks, afterTyping.ExecutableLastWriteUtcTicks);

                // ...and the status still converges to the correct, non-Invalid result once the
                // fully-typed assets root matches a real, existing directory.
                Assert.DoesNotContain("Invalid", statusText.Text, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
            }
        }

        static void MakeExecutableOnUnix(string path)
        {
            if (OperatingSystem.IsWindows())
                return;

            File.SetUnixFileMode(
                path,
                File.GetUnixFileMode(path)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
        }
    }
}
