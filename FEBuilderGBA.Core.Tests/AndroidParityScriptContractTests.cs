// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class AndroidParityScriptContractTests
    {
        [Fact]
        public void RunnerPackageIdMatchesAndroidTestApplicationId()
        {
            string root = FindRepositoryRoot();
            string script = File.ReadAllText(Path.Combine(
                root, "scripts", "android-parity-run.sh"));
            Match packageMatch = Regex.Match(
                script,
                @"(?m)^TEST_PKG=""(?<id>[^""]+)""$");
            Assert.True(packageMatch.Success, "TEST_PKG is missing from android-parity-run.sh.");

            XDocument project = XDocument.Load(Path.Combine(
                root,
                "FEBuilderGBA.Android.Tests",
                "FEBuilderGBA.Android.Tests.csproj"));
            string applicationId = Assert.Single(
                project.Descendants("ApplicationId")).Value.Trim();

            Assert.Equal(applicationId, packageMatch.Groups["id"].Value);
        }

        [Fact]
        public void WorkflowRunsTransactionHarnessAndProductionRunnerWithBash()
        {
            string root = FindRepositoryRoot();
            string workflow = File.ReadAllText(Path.Combine(
                root,
                ".github",
                "workflows",
                "android-emulator-parity.yml"));

            Assert.Matches(
                new Regex(
                    @"(?m)^\s+- name: Test parity runner transaction\r?\n" +
                    @"\s+run: bash scripts/tests/android-parity-run-test\.sh\s*$"),
                workflow);
            Assert.Matches(
                new Regex(
                    @"(?m)^\s+script: bash scripts/android-parity-run\.sh " +
                    @"\$\{\{ matrix\.arch \}\}\s*$"),
                workflow);
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
