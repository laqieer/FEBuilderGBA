// SPDX-License-Identifier: GPL-3.0-or-later
//
// SkiaSharp native-version pin guard (#1125).
//
// This repo has a known landmine: a process loads EXACTLY ONE native
// libSkiaSharp, so the managed SkiaSharp version must match the native that
// Avalonia 11.2.3 bundles (2.88.x). A 3.x managed package rejects the 2.88
// native ("88.1") and crashes inside the Avalonia process with a
// TypeInitializationException on non-Windows (Windows can mask it). See
// docs/ANDROID.md §3 and issues #796 / #798.
//
// These three guards (authored cross-platform in FEBuilderGBA.Core.Tests, so
// they also run on the #1126 Android emulator/instrumented CI) catch a 3.x leak
// at three independent layers:
//   (b1) DECLARED — every SkiaSharp* <PackageReference Version=…> in the repo's
//        csprojs pins the 2.88.x family.
//   (b2) RUNTIME  — the managed SkiaSharp assembly actually loaded into this
//        test process is 2.88.x.
//   (b3) RESTORED — the NuGet restore graph (project.assets.json) resolved only
//        2.88.x SkiaSharp libraries, with no duplicate major family.
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
        /// </summary>
        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                $"could not locate FEBuilderGBA.sln walking up from {AppContext.BaseDirectory}");
        }

        // The csprojs that may carry a direct SkiaSharp* PackageReference. The
        // CLI / Avalonia / Tests projects pull SkiaSharp transitively via a
        // ProjectReference (no direct pin), so they will contribute zero refs —
        // that's fine; we only require at least ONE ref across the whole set.
        static readonly string[] CsprojRelPaths =
        {
            Path.Combine("FEBuilderGBA.SkiaSharp", "FEBuilderGBA.SkiaSharp.csproj"),
            Path.Combine("FEBuilderGBA.Android", "FEBuilderGBA.Android.csproj"),
            Path.Combine("FEBuilderGBA.CLI", "FEBuilderGBA.CLI.csproj"),
            Path.Combine("FEBuilderGBA.Avalonia", "FEBuilderGBA.Avalonia.csproj"),
            Path.Combine("FEBuilderGBA.Tests", "FEBuilderGBA.Tests.csproj"),
        };

        // Matches: <PackageReference Include="SkiaSharp[.NativeAssets.*]" Version="X" />
        // (attribute order-independent).
        static readonly Regex PackageRefRegex = new Regex(
            "<PackageReference\\b[^>]*?\\bInclude\\s*=\\s*\"(?<inc>SkiaSharp[^\"]*)\"[^>]*?\\bVersion\\s*=\\s*\"(?<ver>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Also catch the order where Version precedes Include.
        static readonly Regex PackageRefRegexVerFirst = new Regex(
            "<PackageReference\\b[^>]*?\\bVersion\\s*=\\s*\"(?<ver>[^\"]+)\"[^>]*?\\bInclude\\s*=\\s*\"(?<inc>SkiaSharp[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static readonly Regex Pin288 = new Regex("^2\\.88\\.");
        static readonly Regex Reject3x = new Regex("^3\\.");

        [Fact]
        public void DeclaredSkiaSharpPackageRefs_Pin_288_Family()
        {
            string root = FindRepoRoot();
            int found = 0;

            foreach (string rel in CsprojRelPaths)
            {
                string path = Path.Combine(root, rel);
                if (!File.Exists(path))
                    continue; // tolerate a layout where a project is absent

                string xml = File.ReadAllText(path);

                foreach (Match m in CollectPackageRefs(xml))
                {
                    string inc = m.Groups["inc"].Value;
                    string ver = m.Groups["ver"].Value;
                    found++;

                    Assert.False(Reject3x.IsMatch(ver),
                        $"SkiaSharp 3.x leak: {rel} pins '{inc}' Version='{ver}' (must stay on the 2.88.x family — see docs/ANDROID.md §3)");
                    Assert.True(Pin288.IsMatch(ver),
                        $"SkiaSharp version drift: {rel} pins '{inc}' Version='{ver}' (expected 2.88.x — see docs/ANDROID.md §3)");
                }
            }

            Assert.True(found > 0,
                "found ZERO SkiaSharp PackageReference pins across the scanned csprojs — the parse silently matched nothing (expected at least the SkiaSharp + Android pins)");
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
        public void RuntimeLoadedSkiaSharpAssembly_Is_288()
        {
            // typeof(SKBitmap) forces the managed SkiaSharp assembly that this
            // process actually binds to load; its informational/file version
            // tracks the 2.88.x package (managed assembly version is 2.88.0.0).
            Version v = typeof(global::SkiaSharp.SKBitmap).Assembly.GetName().Version;
            Assert.NotNull(v);
            Assert.True(v.Major == 2 && v.Minor == 88,
                $"runtime-loaded managed SkiaSharp is {v} — expected 2.88.x (a 3.x managed assembly crashes against Avalonia's 2.88 native; see docs/ANDROID.md §3)");
        }

        // ------------------------------------------------------------------
        // (b3) restored-graph guard (the leak-catcher)
        // ------------------------------------------------------------------

        [Fact]
        public void RestoredAssetsGraph_Resolves_Only_288_SkiaSharp()
        {
            string root = FindRepoRoot();

            // Only assert on assets files that exist (a clean checkout may not
            // have restored CLI). SkiaSharp's own assets reliably exist because
            // Core.Tests references it, forcing its restore.
            string[] assetsCandidates =
            {
                Path.Combine(root, "FEBuilderGBA.SkiaSharp", "obj", "project.assets.json"),
                Path.Combine(root, "FEBuilderGBA.CLI", "obj", "project.assets.json"),
            };

            int filesScanned = 0;
            int skiaLibsChecked = 0;

            foreach (string assetsPath in assetsCandidates)
            {
                if (!File.Exists(assetsPath))
                    continue;
                filesScanned++;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
                JsonElement rootEl = doc.RootElement;

                // family -> set of versions seen (to catch a duplicate major).
                var byFamily = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                CollectSkiaLibraries(rootEl, "libraries", byFamily, ref skiaLibsChecked, assetsPath);
                CollectSkiaLibraries(rootEl, "targets", byFamily, ref skiaLibsChecked, assetsPath);

                // No duplicate MAJOR family for the same package, e.g. both
                // SkiaSharp/2.88.9 and SkiaSharp/3.x present.
                foreach (var kv in byFamily)
                {
                    var majors = new HashSet<string>();
                    foreach (string ver in kv.Value)
                        majors.Add(ver.Split('.')[0]);
                    Assert.True(majors.Count <= 1,
                        $"restored graph in {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(assetsPath)))} has DUPLICATE major families for '{kv.Key}': versions [{string.Join(", ", kv.Value)}]");
                }
            }

            Skip.If(filesScanned == 0,
                "no project.assets.json found to scan — run `dotnet restore` first (the build that runs these tests normally creates it)");
            Assert.True(skiaLibsChecked > 0,
                "scanned project.assets.json but matched ZERO SkiaSharp libraries — restore graph shape changed unexpectedly");
        }

        /// <summary>
        /// Scan a top-level object (libraries or targets) for keys of the form
        /// "SkiaSharp.../<version>"; assert each version is 2.88.x (not 3.x) and
        /// record it per package family for the duplicate-major check.
        /// </summary>
        static void CollectSkiaLibraries(
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
            // key form: "SkiaSharp/2.88.9" or "SkiaSharp.NativeAssets.Win32/2.88.9"
            int slash = key.IndexOf('/');
            if (slash <= 0) return;
            string name = key.Substring(0, slash);
            string ver = key.Substring(slash + 1);

            if (!name.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase))
                return;

            skiaLibsChecked++;

            Assert.False(Reject3x.IsMatch(ver),
                $"SkiaSharp 3.x leak in {assetsPath}: restored '{name}' version '{ver}' (must be 2.88.x — see docs/ANDROID.md §3)");
            Assert.True(Pin288.IsMatch(ver),
                $"SkiaSharp version drift in {assetsPath}: restored '{name}' version '{ver}' (expected 2.88.x)");

            if (!byFamily.TryGetValue(name, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byFamily[name] = set;
            }
            set.Add(ver);
        }
    }
}
