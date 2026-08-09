// SPDX-License-Identifier: GPL-3.0-or-later
//
// Avalonia / SkiaSharp / HarfBuzz native-version pin guard (#1125, #2067).
//
// This repo has a known landmine: a process loads EXACTLY ONE native
// libSkiaSharp, so the managed SkiaSharp version must match the native that
// Avalonia bundles. Android 15+/16 KB page-size devices additionally reject
// old libSkiaSharp.so binaries, so the accepted Avalonia 11 compatibility stack
// is Avalonia 11.3.18 + SkiaSharp 3.119.4 + HarfBuzzSharp 8.3.1.5.
//
// These three guards (authored cross-platform in FEBuilderGBA.Core.Tests, so
// they also run on the #1126 Android emulator/instrumented CI) catch a version
// skew at three independent layers:
//   (b1) DECLARED — every Avalonia*/SkiaSharp*/HarfBuzzSharp*
//        <PackageReference Version=…> in the repo's csprojs pins the accepted
//        compatibility stack, and the required direct pins exist.
//   (b2) RUNTIME  — the managed SkiaSharp assembly actually loaded into this
//        test process is 3.119.x.
//   (b3) RESTORED — the NuGet restore graph (project.assets.json) resolved only
//        accepted SkiaSharp/HarfBuzzSharp libraries, with no duplicate major
//        family.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class SkiaSharpVersionGuardTests
    {
        // ------------------------------------------------------------------
        // repo-root discovery
        // ------------------------------------------------------------------

        /// <summary>
        /// Walk up from AppContext.BaseDirectory to the directory containing
        /// FEBuilderGBA.sln. When run from inside an isolated git worktree this
        /// resolves to the worktree root — which has its own copy of every
        /// csproj, exactly what we want to assert on.
        ///
        /// Returns <c>null</c> when no FEBuilderGBA.sln is found walking up,
        /// i.e. the source tree / .sln is absent — e.g. an on-device or
        /// instrumented Android test host (#1126) where only the compiled
        /// assemblies + native ship. The two source-tree-dependent guards
        /// (declared + restored-graph) SKIP in that case; the
        /// source-tree-independent runtime-loaded guard still runs.
        /// </summary>
        static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// Enumerate EVERY *.csproj under the repo root (recursively), excluding
        /// build-output (bin/, obj/) and nested git-worktree (.claude/worktrees/)
        /// directories. Scanning all projects — not a hard-coded subset — means a
        /// future NEW project that introduces a SkiaSharp 3.x reference is caught
        /// by the declared-version guard.
        ///
        /// The exclusion test runs against the path RELATIVE to <paramref name="root"/>,
        /// not the absolute path: when this test itself runs from inside an
        /// isolated git worktree the root is ALREADY under .claude/worktrees/, so
        /// an absolute-path filter would exclude every csproj (yielding zero) —
        /// the relative-path filter only skips worktrees nested *under* the root.
        /// </summary>
        static IEnumerable<string> EnumerateRepoCsprojs(string root)
        {
            foreach (string path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                // Wrap in leading/trailing '/' so a segment match is exact (e.g.
                // "bin/" matches the bin directory, not a project named "...bin").
                string probe = "/" + rel + "/";
                if (probe.Contains("/bin/") || probe.Contains("/obj/") || probe.Contains("/.claude/worktrees/"))
                    continue;
                yield return path;
            }
        }

        const string ExpectedAvaloniaVersion = "11.3.18";
        const string ExpectedSkiaSharpVersion = "3.119.4";
        const string ExpectedHarfBuzzSharpVersion = "8.3.1.5";

        static readonly HashSet<string> OldRejectedPackageVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "11.2.3",
            "2.88.9",
            "7.3.0.3",
        };

        static readonly HashSet<string> RequiredDirectPackagePins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Avalonia",
            "Avalonia.Android",
            "Avalonia.Browser",
            "Avalonia.Desktop",
            "Avalonia.Fonts.Inter",
            "Avalonia.Headless",
            "Avalonia.Headless.XUnit",
            "Avalonia.iOS",
            "Avalonia.Themes.Fluent",
            "SkiaSharp",
            "SkiaSharp.HarfBuzz",
            "SkiaSharp.NativeAssets.Android",
            "SkiaSharp.NativeAssets.iOS",
            "SkiaSharp.NativeAssets.Linux",
            "SkiaSharp.NativeAssets.WebAssembly",
            "HarfBuzzSharp",
            "HarfBuzzSharp.NativeAssets.Android",
            "HarfBuzzSharp.NativeAssets.iOS",
            "HarfBuzzSharp.NativeAssets.Linux",
            "HarfBuzzSharp.NativeAssets.WebAssembly",
        };

        // Matches: <PackageReference Include="SkiaSharp[.NativeAssets.*]" Version="X" />
        // (attribute order-independent).
        static readonly Regex PackageRefRegex = new Regex(
            "<PackageReference\\b[^>]*?\\bInclude\\s*=\\s*\"(?<inc>(?:Avalonia|SkiaSharp|HarfBuzzSharp)[^\"]*)\"[^>]*?\\bVersion\\s*=\\s*\"(?<ver>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Also catch the order where Version precedes Include.
        static readonly Regex PackageRefRegexVerFirst = new Regex(
            "<PackageReference\\b[^>]*?\\bVersion\\s*=\\s*\"(?<ver>[^\"]+)\"[^>]*?\\bInclude\\s*=\\s*\"(?<inc>(?:Avalonia|SkiaSharp|HarfBuzzSharp)[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Asserts EVERY direct Avalonia*/SkiaSharp*/HarfBuzzSharp*
        // <PackageReference> across the WHOLE repo (every *.csproj under the
        // root, not a hard-coded subset) pins the accepted #2067 stack. It also
        // requires the explicit pins that keep desktop, Android, iOS, browser,
        // and test heads in lockstep.
        [SkippableFact]
        public void DeclaredGraphicsPackageRefs_Pin_2067_CompatibilityStack()
        {
            string? root = FindRepoRoot();
            Skip.If(root == null, "source tree / FEBuilderGBA.sln not found (e.g. on-device Android instrumented host) — declared/restored-graph guards are source-tree-only; the runtime-loaded guard still validates the native here");
            int found = 0;
            var foundPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in EnumerateRepoCsprojs(root))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                string xml = File.ReadAllText(path);

                foreach (Match m in CollectPackageRefs(xml))
                {
                    string inc = m.Groups["inc"].Value;
                    string ver = m.Groups["ver"].Value;
                    found++;
                    foundPins.Add(inc);

                    Assert.False(OldRejectedPackageVersions.Contains(ver),
                        $"old graphics dependency pin: {rel} pins '{inc}' Version='{ver}' (expected {ExpectedVersionForPackage(inc)} for #2067)");
                    Assert.Equal(ExpectedVersionForPackage(inc), ver);
                }
            }

            Assert.True(found > 0,
                "found ZERO direct Avalonia/SkiaSharp/HarfBuzzSharp PackageReference pins across every csproj in the repo — the parse silently matched nothing");

            foreach (string required in RequiredDirectPackagePins)
            {
                Assert.Contains(required, foundPins);
            }
        }

        static IEnumerable<Match> CollectPackageRefs(string xml)
        {
            var seen = new HashSet<(string inc, string ver, int pos)>();
            var result = new List<Match>();
            foreach (Match m in PackageRefRegex.Matches(xml))
            {
                if (seen.Add((m.Groups["inc"].Value, m.Groups["ver"].Value, m.Index)))
                    result.Add(m);
            }
            foreach (Match m in PackageRefRegexVerFirst.Matches(xml))
            {
                if (seen.Add((m.Groups["inc"].Value, m.Groups["ver"].Value, m.Index)))
                    result.Add(m);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // (b2) runtime-loaded managed guard
        // ------------------------------------------------------------------

        [Fact]
        public void RuntimeLoadedSkiaSharpAssembly_Is_3119()
        {
            // typeof(SKBitmap) forces the managed SkiaSharp assembly that this
            // process actually binds to load; its assembly version tracks the
            // 3.119.x package family.
            Version v = Assert.IsType<Version>(typeof(global::SkiaSharp.SKBitmap).Assembly.GetName().Version);
            Assert.True(v.Major == 3 && v.Minor == 119,
                $"runtime-loaded managed SkiaSharp is {v} — expected 3.119.x for #2067's Avalonia/Skia compatibility stack");
        }

        // ------------------------------------------------------------------
        // (b3) restored-graph guard (the leak-catcher)
        // ------------------------------------------------------------------

        [SkippableFact]
        public void CoreTestsRestoreGraphs_ResolveOnly_2067_GraphicsStack()
        {
            string? root = FindRepoRoot();
            Skip.If(root == null, "source tree / FEBuilderGBA.sln not found (e.g. on-device Android instrumented host) — declared/restored-graph guards are source-tree-only; the runtime-loaded guard still validates the native here");
            AssertCoreTestsRestoreGraphs_ResolveOnly_2067_GraphicsStack(root);
        }

        [Fact]
        public void RestoredAssetsGraph_FailsClosed_WhenCoreTestsRequiredGraphIsMissing()
        {
            string? root = FindRepoRoot();
            Skip.If(root == null, "source tree / FEBuilderGBA.sln not found (e.g. on-device Android instrumented host) — declared/restored-graph guards are source-tree-only; the runtime-loaded guard still validates the native here");

            string scratchRoot = Path.Combine(root, ".scratch", "SkiaSharpVersionGuardTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(scratchRoot, "FEBuilderGBA.SkiaSharp", "obj"));
                Directory.CreateDirectory(Path.Combine(scratchRoot, "FEBuilderGBA.Core.Tests", "obj"));

                File.Copy(
                    Path.Combine(root, "FEBuilderGBA.SkiaSharp", "obj", "project.assets.json"),
                    Path.Combine(scratchRoot, "FEBuilderGBA.SkiaSharp", "obj", "project.assets.json"));
                File.Copy(
                    Path.Combine(root, "FEBuilderGBA.Core.Tests", "obj", "project.assets.json"),
                    Path.Combine(scratchRoot, "FEBuilderGBA.Core.Tests", "obj", "project.assets.json"));

                Assert.ThrowsAny<Exception>(() => AssertCoreTestsRestoreGraphs_ResolveOnly_2067_GraphicsStack(scratchRoot));
            }
            finally
            {
                if (Directory.Exists(scratchRoot))
                    Directory.Delete(scratchRoot, true);
            }
        }

        [Fact]
        public void RestoredAssetsGraph_AcceptsValidContentAfterNoOpRestoreTimestampSkew()
        {
            string? root = FindRepoRoot();
            Skip.If(root == null, "source tree / FEBuilderGBA.sln not found");

            string scratchRoot = Path.Combine(root, ".scratch", "SkiaSharpVersionGuardTests", Guid.NewGuid().ToString("N"));
            string[] projects =
            {
                "FEBuilderGBA.SkiaSharp",
                "FEBuilderGBA.CLI",
                "FEBuilderGBA.Core.Tests",
            };
            try
            {
                foreach (string project in projects)
                {
                    string sourceProjectDir = Path.Combine(root, project);
                    string scratchProjectDir = Path.Combine(scratchRoot, project);
                    string scratchObjDir = Path.Combine(scratchProjectDir, "obj");
                    Directory.CreateDirectory(scratchObjDir);
                    File.Copy(
                        Path.Combine(sourceProjectDir, project + ".csproj"),
                        Path.Combine(scratchProjectDir, project + ".csproj"));
                    string scratchAssets = Path.Combine(scratchObjDir, "project.assets.json");
                    File.Copy(
                        Path.Combine(sourceProjectDir, "obj", "project.assets.json"),
                        scratchAssets);
                    File.SetLastWriteTimeUtc(scratchAssets, DateTime.UtcNow.AddMinutes(-5));
                    File.SetLastWriteTimeUtc(
                        Path.Combine(scratchProjectDir, project + ".csproj"),
                        DateTime.UtcNow);
                }

                AssertCoreTestsRestoreGraphs_ResolveOnly_2067_GraphicsStack(scratchRoot);
            }
            finally
            {
                if (Directory.Exists(scratchRoot))
                    Directory.Delete(scratchRoot, true);
            }
        }

        static void AssertCoreTestsRestoreGraphs_ResolveOnly_2067_GraphicsStack(string root)
        {
            // Core.Tests guarantees these restore graphs: the managed SkiaSharp
            // project, the CLI (built/restored via RestoreFontLibraryCli /
            // BuildFontLibraryCli), and Core.Tests itself. Those are the only
            // graphs this guard validates; Android/iOS/Browser coverage lives in
            // the repo-wide package-declaration guard and the dedicated builds.
            (string Name, string AssetsPath)[] requiredAssets =
            {
                ("FEBuilderGBA.SkiaSharp", Path.Combine(root, "FEBuilderGBA.SkiaSharp", "obj", "project.assets.json")),
                ("FEBuilderGBA.CLI", Path.Combine(root, "FEBuilderGBA.CLI", "obj", "project.assets.json")),
                ("FEBuilderGBA.Core.Tests", Path.Combine(root, "FEBuilderGBA.Core.Tests", "obj", "project.assets.json")),
            };

            int filesScanned = 0;
            int skiaLibsChecked = 0;

            foreach ((string name, string assetsPath) in requiredAssets)
            {
                Assert.True(File.Exists(assetsPath),
                    $"{name} project.assets.json is required for the Core.Tests restore-graph guard (no silent skip for missing required graphs)");
                filesScanned++;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
                JsonElement rootEl = doc.RootElement;

                // family -> set of versions seen (to catch a duplicate major).
                var byFamily = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                CollectGraphicsLibraries(rootEl, "libraries", byFamily, ref skiaLibsChecked, assetsPath);
                CollectGraphicsLibraries(rootEl, "targets", byFamily, ref skiaLibsChecked, assetsPath);

                // No duplicate MAJOR family for the same package, e.g. both
                // the accepted SkiaSharp 3.x stack and an older 2.x stack present.
                foreach (var kv in byFamily)
                {
                    var majors = new HashSet<string>();
                    foreach (string ver in kv.Value)
                        majors.Add(ver.Split('.')[0]);
                    Assert.True(majors.Count <= 1,
                        $"restored graph in {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(assetsPath)))} has DUPLICATE major families for '{kv.Key}': versions [{string.Join(", ", kv.Value)}]");
                }
            }

            Assert.Equal(requiredAssets.Length, filesScanned);
            Assert.True(skiaLibsChecked > 0,
                "scanned required Core.Tests project.assets.json files but matched ZERO SkiaSharp/HarfBuzzSharp libraries — restore graph shape changed unexpectedly");
        }

        /// <summary>
        /// Scan a top-level object (libraries or targets) for keys of the form
        /// "SkiaSharp.../&lt;version&gt;" / "HarfBuzzSharp.../&lt;version&gt;"; assert each
        /// version matches the #2067 compatibility stack and record it per
        /// package family for the duplicate-major check.
        /// </summary>
        static void CollectGraphicsLibraries(
            JsonElement root, string sectionName,
            Dictionary<string, HashSet<string>> byFamily,
            ref int skiaLibsChecked, string assetsPath)
        {
            if (!root.TryGetProperty(sectionName, out JsonElement section)
                || section.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty prop in section.EnumerateObject())
            {
                // "targets" nests one level deeper (TFM -> {lib/version: {...}}).
                if (sectionName == "targets")
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    foreach (JsonProperty inner in prop.Value.EnumerateObject())
                        CheckKey(inner.Name, byFamily, ref skiaLibsChecked, assetsPath);
                }
                else
                {
                    CheckKey(prop.Name, byFamily, ref skiaLibsChecked, assetsPath);
                }
            }
        }

        static void CheckKey(
            string key,
            Dictionary<string, HashSet<string>> byFamily,
            ref int skiaLibsChecked, string assetsPath)
        {
            // key form: "SkiaSharp/3.119.4" or "HarfBuzzSharp/8.3.1.5"
            int slash = key.IndexOf('/');
            if (slash <= 0) return;
            string name = key.Substring(0, slash);
            string ver = key.Substring(slash + 1);

            if (!IsGraphicsRuntimePackage(name))
                return;

            skiaLibsChecked++;

            Assert.False(OldRejectedPackageVersions.Contains(ver),
                $"old graphics dependency in {assetsPath}: restored '{name}' version '{ver}' (expected {ExpectedVersionForPackage(name)})");
            Assert.Equal(ExpectedVersionForPackage(name), ver);

            if (!byFamily.TryGetValue(name, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byFamily[name] = set;
            }
            set.Add(ver);
        }

        static bool IsGraphicsRuntimePackage(string name)
        {
            return name.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("HarfBuzzSharp", StringComparison.OrdinalIgnoreCase);
        }

        static string ExpectedVersionForPackage(string name)
        {
            if (name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
                return ExpectedAvaloniaVersion;
            if (name.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase))
                return ExpectedSkiaSharpVersion;
            if (name.StartsWith("HarfBuzzSharp", StringComparison.OrdinalIgnoreCase))
                return ExpectedHarfBuzzSharpVersion;

            throw new ArgumentOutOfRangeException(nameof(name), name, "Package is not part of the guarded graphics stack");
        }
    }
}
