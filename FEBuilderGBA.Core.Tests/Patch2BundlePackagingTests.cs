using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Packaging regression tests for #1630 — the published CLI + Avalonia desktop
    /// artifacts must bundle the <c>config/patch2/**</c> binary-patch library so the
    /// Patch Manager is populated and <c>--list-patches</c> works on
    /// Linux/macOS/Windows-portable. These tests parse the two .csproj files and assert
    /// the config Content include ships patch2 (and excludes only the submodule .git
    /// plumbing), and that the Android APK is still kept patch2-free.
    /// </summary>
    public class Patch2BundlePackagingTests
    {
        static string RepoRoot()
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate repo root (FEBuilderGBA.sln) from " + thisAssembly);
        }

        static XDocument LoadCsproj(string relativePath)
        {
            string full = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), "csproj not found: " + full);
            return XDocument.Load(full);
        }

        /// <summary>
        /// Returns the config-tree Content include element ("..\config\**\*.*") from a csproj,
        /// or null if absent. Namespace-agnostic (SDK-style projects have no default namespace,
        /// but be defensive).
        /// </summary>
        static XElement FindConfigContentInclude(XDocument doc)
        {
            return doc.Descendants()
                .Where(e => e.Name.LocalName == "Content")
                .FirstOrDefault(e =>
                {
                    string inc = (string)e.Attribute("Include");
                    return inc != null
                        && inc.Replace('/', '\\').Contains(@"config\**");
                });
        }

        /// <summary>
        /// Asserts that the config Content Exclude metadata strips BOTH the submodule
        /// <c>.git</c> pointer file AND the recursive <c>.git\**</c> tree — so that even
        /// when an initialized submodule materializes a real <c>.git</c> directory (instead
        /// of the usual gitdir pointer file), none of it ships in the artifact. A weaker
        /// "only the pointer file" exclusion would silently package a stale <c>.git</c>
        /// directory; this guards against that regression.
        /// </summary>
        static void AssertExcludesGitPlumbing(string excludeNormalizedToBackslash)
        {
            // The .git pointer file itself.
            Assert.Contains(@"config\patch2\.git", excludeNormalizedToBackslash);
            // The recursive .git directory tree (a separate glob token, not just a prefix
            // of the pointer-file token).
            Assert.Contains(@"config\patch2\.git\**", excludeNormalizedToBackslash);
        }

        [Fact]
        public void Cli_Csproj_Bundles_Patch2_And_Excludes_Only_Git()
        {
            XDocument doc = LoadCsproj("FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj");
            XElement content = FindConfigContentInclude(doc);
            Assert.NotNull(content);

            string exclude = ((string)content.Attribute("Exclude") ?? string.Empty).Replace('/', '\\');

            // #1630: the patch2 directory MUST NOT be excluded wholesale any more.
            Assert.DoesNotContain(@"config\patch2\**", exclude);

            // Copilot finding 1 / WinForms parity: BOTH the submodule .git pointer file
            // AND the recursive .git tree are excluded, so no git plumbing ships even if
            // an initialized submodule materializes a real .git directory.
            AssertExcludesGitPlumbing(exclude);

            // Sanity: content is actually copied to output.
            Assert.Equal("PreserveNewest", (string)content.Attribute("CopyToOutputDirectory"));
        }

        [Fact]
        public void Avalonia_Csproj_Bundles_Patch2_On_Desktop_Excludes_Only_Git()
        {
            XDocument doc = LoadCsproj("FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj");
            XElement content = FindConfigContentInclude(doc);
            Assert.NotNull(content);

            string exclude = ((string)content.Attribute("Exclude") ?? string.Empty).Replace('/', '\\');

            // #1630: patch2 ships on the desktop TFM.
            Assert.DoesNotContain(@"config\patch2\**", exclude);

            // Copilot finding 1 / WinForms parity: BOTH the submodule .git pointer file
            // AND the recursive .git tree are excluded.
            AssertExcludesGitPlumbing(exclude);

            Assert.Equal("PreserveNewest", (string)content.Attribute("CopyToOutputDirectory"));
        }

        [Fact]
        public void Avalonia_ConfigContent_Is_Excluded_From_Android_APK()
        {
            // The Android APK must stay patch2-free (#1123): config/ ships as <AndroidAsset>.
            // The config Content ItemGroup is guarded by a Condition that turns it off on
            // the net9.0-android TFM, so the whole config tree (incl. patch2) is never a
            // loose Content item in the APK.
            XDocument doc = LoadCsproj("FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj");
            XElement content = FindConfigContentInclude(doc);
            Assert.NotNull(content);

            XElement itemGroup = content.Parent;
            Assert.NotNull(itemGroup);
            Assert.Equal("ItemGroup", itemGroup.Name.LocalName);

            string condition = ((string)itemGroup.Attribute("Condition") ?? string.Empty)
                .Replace(" ", string.Empty);
            Assert.Contains("'$(TargetFramework)'!='net9.0-android'", condition);
        }
    }
}
