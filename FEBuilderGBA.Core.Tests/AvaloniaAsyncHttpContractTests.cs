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
                "Task.Run(() =>",
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
