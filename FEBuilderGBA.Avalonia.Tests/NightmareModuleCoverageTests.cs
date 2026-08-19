using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class NightmareModuleCoverageTests
    {
        const string ExpectedRepository = "laqieer/Fire-Emblem-Nightmare-Modules";
        const string ExpectedCommit = "e854861a7c16dadd3f2ea26be4b8c3d71c0e9c10";
        const string ExpectedPathHash = "a8019f8688da5766c740e4620a662914670fc21a002a0803c28f90cfe0661c6a";

        static JsonDocument LoadManifest()
        {
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia.Tests", "TestData", "nightmare-module-coverage.json");
            return JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        [Fact]
        public void Manifest_HasExpectedPinnedCountsAndHash()
        {
            using var doc = LoadManifest();
            var root = doc.RootElement;
            Assert.Equal(ExpectedRepository, root.GetProperty("sourceRepository").GetString());
            Assert.Equal(ExpectedCommit, root.GetProperty("sourceCommit").GetString());
            Assert.Equal(755, root.GetProperty("totalCount").GetInt32());

            var counts = root.GetProperty("rootCounts");
            Assert.Equal(102, counts.GetProperty("FE6 Nightmare Modules").GetInt32());
            Assert.Equal(373, counts.GetProperty("FE7 Nightmare Modules").GetInt32());
            Assert.Equal(280, counts.GetProperty("FE8 Nightmare modules").GetInt32());

            string[] paths = Entries(root).Select(e => e.GetProperty("path").GetString()!).ToArray();
            string joined = string.Join("\n", paths) + "\n";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
            Assert.Equal(ExpectedPathHash, hash);
            Assert.Equal(hash, root.GetProperty("pathListSha256").GetString());
        }

        [Fact]
        public void Entries_AreSortedUniqueNmmPathsUnderExactlyPinnedRoots()
        {
            using var doc = LoadManifest();
            string[] allowedRoots = { "FE6 Nightmare Modules", "FE7 Nightmare Modules", "FE8 Nightmare modules" };
            string[] paths = Entries(doc.RootElement).Select(e => e.GetProperty("path").GetString()!).ToArray();

            Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal).ToArray(), paths);
            Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
            Assert.All(paths, p =>
            {
                Assert.EndsWith(".nmm", p, StringComparison.OrdinalIgnoreCase);
                string root = p.Split('/')[0];
                Assert.Contains(root, allowedRoots);
            });
        }

        [Fact]
        public void Entries_MapExactlyOnceToDocumentedFamilyAndDisposition()
        {
            using var doc = LoadManifest();
            var root = doc.RootElement;
            var families = root.GetProperty("familyDefinitions").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var dispositions = root.GetProperty("dispositionDefinitions").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var entry in Entries(root))
            {
                string path = entry.GetProperty("path").GetString()!;
                string family = entry.GetProperty("family").GetString()!;
                string disposition = entry.GetProperty("disposition").GetString()!;
                Assert.True(families.Contains(family), $"{path}: undocumented family {family}");
                Assert.True(dispositions.Contains(disposition), $"{path}: undocumented disposition {disposition}");
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("target").GetString()) && disposition != "explicit-exclusion",
                    $"{path}: non-exclusion entries must name a target");
                Assert.NotEqual("missing", disposition);
            }
        }

        [Fact]
        public void SummaryCounts_MatchEntries()
        {
            using var doc = LoadManifest();
            var root = doc.RootElement;
            AssertSummary(root, "familyCounts", e => e.GetProperty("family").GetString()!);
            AssertSummary(root, "dispositionCounts", e => e.GetProperty("disposition").GetString()!);
        }

        [Fact]
        public void TriangleMappings_AreExplicitAndCatalogTargetIsLive()
        {
            using var doc = LoadManifest();
            var root = doc.RootElement;
            var entries = Entries(root).ToDictionary(e => e.GetProperty("path").GetString()!, e => e, StringComparer.Ordinal);

            var fe6 = entries["FE6 Nightmare Modules/Quote editor/Triangle attack quote editor.nmm"];
            Assert.Equal("integrated-surface", fe6.GetProperty("disposition").GetString());
            Assert.Equal("EventBattleTalkFE6", fe6.GetProperty("target").GetString());

            var explicitMap = root.GetProperty("explicitMappings");
            Assert.Equal("0x666528", explicitMap.GetProperty("FE6 Nightmare Modules/Quote editor/Triangle attack quote editor.nmm").GetProperty("directBase").GetString());
            Assert.Equal(6, explicitMap.GetProperty("FE6 Nightmare Modules/Quote editor/Triangle attack quote editor.nmm").GetProperty("count").GetInt32());
            Assert.Equal(16, explicitMap.GetProperty("FE6 Nightmare Modules/Quote editor/Triangle attack quote editor.nmm").GetProperty("stride").GetInt32());

            foreach (string path in new[]
            {
                "FE7 Nightmare Modules/Quote Editors/Triangle Attack Convo Editor.nmm",
                "FE7 Nightmare Modules/Character Editors/Quote Editors/Triangle Attack Convo Editor.nmm",
            })
            {
                Assert.Equal("existing-avalonia-surface", entries[path].GetProperty("disposition").GetString());
                Assert.Equal("EventBattleTalkFE7", entries[path].GetProperty("target").GetString());
                Assert.Equal("0xC9F130", explicitMap.GetProperty(path).GetProperty("directBase").GetString());
            }

            Assert.Contains(EditorCatalog.AllEntries, e => e.Key == "EventBattleTalkFE6");
            Assert.Contains(root.GetProperty("liveCatalogTargets").EnumerateArray(), e => e.GetString() == "EventBattleTalkFE6");
        }

        static JsonElement[] Entries(JsonElement root) => root.GetProperty("entries").EnumerateArray().ToArray();

        static void AssertSummary(JsonElement root, string summaryProperty, Func<JsonElement, string> keySelector)
        {
            var expected = Entries(root)
                .GroupBy(keySelector, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var actual = root.GetProperty(summaryProperty).EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
            Assert.Equal(expected, actual);
        }

        static string FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException($"Could not locate FEBuilderGBA.sln starting from {start}");
        }
    }
}
