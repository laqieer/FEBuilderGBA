// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class DotNet10TargetFrameworkTests
    {
        [Fact]
        public void TrackedRepositoryProjectsTargetDotNet10()
        {
            string root = FindRepositoryRoot();
            string[] projects = GetTrackedPaths(
                    root,
                    ":(glob)**/*.csproj")
                .Where(IsRepositoryOwnedProject)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(projects);
            Assert.Contains("FEBuilderGBA.Core/FEBuilderGBA.Core.csproj", projects);
            Assert.Contains("tools/TextToPng/TextToPng.csproj", projects);
            Assert.DoesNotContain(
                projects,
                path => path.StartsWith("tools/ColorzCore/", StringComparison.Ordinal)
                    || path.StartsWith("tools/Event-Assembler/", StringComparison.Ordinal)
                    || path.StartsWith("config/patch2/", StringComparison.Ordinal)
                    || path.StartsWith("resources/", StringComparison.Ordinal));

            foreach (string relativePath in projects)
            {
                string fullPath = Path.Combine(
                    root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                string projectXml = File.ReadAllText(fullPath);
                Assert.DoesNotContain(
                    "net" + "9.0",
                    projectXml,
                    StringComparison.OrdinalIgnoreCase);

                XDocument project = XDocument.Load(fullPath);
                string[] targetFrameworkExpressions = project.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "TargetFramework"
                        || element.Name.LocalName == "TargetFrameworks")
                    .Select(element => element.Value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();

                Assert.NotEmpty(targetFrameworkExpressions);
                Assert.Contains(
                    targetFrameworkExpressions,
                    value => value.Contains(
                        "net10.0",
                        StringComparison.OrdinalIgnoreCase));
                Assert.All(
                    targetFrameworkExpressions
                        .SelectMany(value => Regex.Matches(
                            value,
                            @"net\d+\.\d+",
                            RegexOptions.IgnoreCase)
                            .Select(match => match.Value)),
                    targetFramework => Assert.Equal(
                        "net10.0",
                        targetFramework,
                        ignoreCase: true));
            }
        }

        [Fact]
        public void WindowsWorkflowsUseDotNet10SdkMsBuild()
        {
            string root = FindRepositoryRoot();
            string[] workflowPaths =
            {
                ".github/workflows/check.yml",
                ".github/workflows/e2e-run.yml",
                ".github/workflows/msbuild.yml",
                ".github/workflows/release.yml",
            };
            var standaloneMsBuild = new Regex(
                @"(?m)^\s*(?:run:\s*)?msbuild(?:\s|$)",
                RegexOptions.IgnoreCase);

            foreach (string relativePath in workflowPaths)
            {
                string workflow = File.ReadAllText(Path.Combine(
                    root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));

                Assert.Contains("dotnet-version: '10.0.x'", workflow);
                Assert.Contains("dotnet msbuild", workflow);
                Assert.DoesNotContain(
                    "microsoft/setup-msbuild",
                    workflow,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch(standaloneMsBuild, workflow);
            }
        }

        [Fact]
        public void AndroidManifestTargetsApi36()
        {
            string root = FindRepositoryRoot();
            XDocument manifest = XDocument.Load(Path.Combine(
                root,
                "FEBuilderGBA.Android",
                "Properties",
                "AndroidManifest.xml"));
            XNamespace android =
                "http://schemas.android.com/apk/res/android";
            XElement usesSdk = Assert.Single(
                manifest.Root?.Elements("uses-sdk")
                    ?? Enumerable.Empty<XElement>());

            Assert.Equal("21", usesSdk.Attribute(android + "minSdkVersion")?.Value);
            Assert.Equal("36", usesSdk.Attribute(android + "targetSdkVersion")?.Value);
        }

        [Fact]
        public void ActiveBuildInstructionsUseDotNetSdkMsBuild()
        {
            string root = FindRepositoryRoot();
            var standaloneMsBuild = new Regex(
                @"(?m)^\s*msbuild(?:\.exe)?\s+",
                RegexOptions.IgnoreCase);
            string[] paths = GetTrackedPaths(
                    root,
                    ":(glob)**/*.md",
                    ":(glob)**/*.ps1",
                    ":(glob)**/*.sh",
                    ":(glob)**/*.xml",
                    ":(glob)**/*.yml")
                .Where(path =>
                    !path.StartsWith("docs/archive/", StringComparison.Ordinal)
                    && !path.StartsWith(
                        "docs/superpowers/plans/",
                        StringComparison.Ordinal)
                    && !path.StartsWith(
                        "pr-screenshots/",
                        StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(paths);
            foreach (string relativePath in paths)
            {
                string content = File.ReadAllText(Path.Combine(
                    root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                Assert.DoesNotMatch(standaloneMsBuild, content);
            }
        }

        static bool IsRepositoryOwnedProject(string path)
        {
            if (path.StartsWith("tools/ColorzCore/", StringComparison.Ordinal)
                || path.StartsWith("tools/Event-Assembler/", StringComparison.Ordinal))
            {
                return false;
            }

            return path.StartsWith("FEBuilderGBA", StringComparison.Ordinal)
                || path.StartsWith("tools/", StringComparison.Ordinal);
        }

        static string[] GetTrackedPaths(
            string root,
            params string[] pathspecs)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(root);
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("-z");
            startInfo.ArgumentList.Add("--");
            foreach (string pathspec in pathspecs)
                startInfo.ArgumentList.Add(pathspec);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                "git ls-files failed: " + error);
            return output.Split(
                '\0',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        }

        static string FindRepositoryRoot()
        {
            string directory = AppContext.BaseDirectory;
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
