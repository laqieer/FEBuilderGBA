#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class AvaloniaAsyncHttpContractTests
    {
        [Fact]
        public void AvaloniaUpdateViewsDoNotUseSyncHttpCalls()
        {
            string root = FindRepositoryRoot();
            string avaloniaRoot = Path.Combine(root, "FEBuilderGBA.Avalonia");
            string[] files =
            {
                Path.Combine(avaloniaRoot, "Views", "MainWindow.axaml.cs"),
                Path.Combine(avaloniaRoot, "Views", "ToolInitWizardView.axaml.cs"),
                Path.Combine(avaloniaRoot, "Views", "ToolWorkSupportView.axaml.cs"),
                Path.Combine(avaloniaRoot, "Views", "ToolAllWorkSupportView.axaml.cs"),
            };
            string[] forbidden =
            {
                "U.HttpGet(",
                "U.HttpDownloadFile(",
                "DownloadInstallCore.Download(",
                "DownloadInstallCore.Stage(",
                "GitInstaller.GetLatestInstallerUrl()",
                ".Send(",
                "Task.Run(() => UpdateCheckCore.CheckLatest",
                "Task.Run(() => U.HttpGet(",
                "Task.Run(() => U.HttpDownloadFile(",
                "Task.Run(() => DownloadInstallCore.Download(",
                "Task.Run(() => DownloadInstallCore.Stage(",
                "Task.Run(() => GitInstaller.GetLatestInstallerUrl()",
            };

            var failures = new List<string>();
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string token in forbidden)
                {
                    if (text.Contains(token, StringComparison.Ordinal))
                    {
                        failures.Add(Path.GetRelativePath(root, file) + " contains " + token);
                    }
                }
            }

            Assert.Empty(failures);
        }

        [Fact]
        public void ToolWorkSupportApplySaveRunsOffUiThreadAfterAsyncDownload()
        {
            string root = FindRepositoryRoot();
            string viewPath = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views", "ToolWorkSupportView.axaml.cs");
            string text = File.ReadAllText(viewPath);

            Assert.Contains("await _vm.DownloadAndStageAsync(", text, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => _vm.PrepareUps(", text, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => _vm.CommitUps(", text, StringComparison.Ordinal);
        }

        [Fact]
        public void InitWizardDownloadHandlersTreatUserCancellationAsCancel()
        {
            string root = FindRepositoryRoot();
            string viewPath = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views", "ToolInitWizardView.axaml.cs");
            string text = File.ReadAllText(viewPath);

            Assert.Contains("catch (OperationCanceledException)", text, StringComparison.Ordinal);
            Assert.Contains("Download cancelled.", text, StringComparison.Ordinal);
        }

        static string FindRepositoryRoot()
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory, "FEBuilderGBA.sln")))
                    return directory;
                directory = Path.GetDirectoryName(directory);
            }
            throw new InvalidOperationException("Cannot find repository root.");
        }
    }
}
