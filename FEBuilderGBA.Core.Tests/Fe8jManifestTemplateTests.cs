// SPDX-License-Identifier: GPL-3.0-or-later
// #1778: the shipped FE8J febuilder.project.json template must parse into an
// FE8J, build-enabled project (else it does not actually unblock --build-project).
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class Fe8jManifestTemplateTests
    {
        static string FindRepoRoot()
        {
            var dir = System.AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        [Fact]
        public void Fe8jTemplate_ParsesToBuildEnabledFE8JProject()
        {
            string root = FindRepoRoot();
            if (root == null) return; // packaged CI without the repo checkout

            string templatePath = Path.Combine(root, "docs", "decomp", "febuilder.project.fe8j.json");
            Assert.True(File.Exists(templatePath), $"Missing FE8J manifest template at {templatePath}");

            DecompManifest m = DecompProjectDetector.ParseManifest(templatePath);
            Assert.NotNull(m);

            Assert.Equal(1, m.SchemaVersion);
            Assert.Equal("fireemblem8.gba", m.BuiltRom);
            Assert.Equal("FE8J", m.ForceVersion);
            Assert.Equal("sym_jp.txt", m.SymPath);
            Assert.Equal("make", m.BuildCommand);
            Assert.Equal("make compare", m.CompareTarget);

            // The whole point: a build section flips build-enablement on so
            // --build-project returns Ok instead of NotOptedIn.
            Assert.True(m.BuildEnabled);
            var project = new DecompProject { Manifest = m };
            Assert.True(project.IsBuildEnabled);
        }
    }
}
