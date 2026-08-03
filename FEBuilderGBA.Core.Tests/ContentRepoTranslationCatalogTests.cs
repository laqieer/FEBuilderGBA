using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>Catalog contract only: this deliberately uses a local parser, not global translation state.</summary>
    public class ContentRepoTranslationCatalogTests
    {
        static readonly string[] HostKeys =
        {
            "Git was not found. Install Git and try again, or set up {0} manually.",
            "{0} operation failed.{1}",
            "{0} initialization failed (git exit {1}).{2}",
            "{0} update failed (git exit {1}).{2}",
            "{0} updated. Restart recommended for all changes to take full effect.",
            "Git: {0} ...",
            // Canonical single-flight message shared by the WinForms hosts and the Avalonia wizard.
            "Another content repository operation is already running.",
        };

        [Fact]
        public void ContentRepoKeys_HaveOneNonemptyJaAndZhTranslation_WithMatchingPlaceholders()
        {
            string root = FindRepoRoot();
            var keys = ContentRepoSetupCore.Repos.Select(r => r.DisplayName).Concat(HostKeys).ToArray();
            foreach (string language in new[] { "ja.txt", "zh.txt" })
            {
                Dictionary<string, List<string>> rows = Parse(Path.Combine(root, "config", "translate", language));
                foreach (string key in keys)
                {
                    Assert.True(rows.TryGetValue(key, out List<string> values), language + " is missing " + key);
                    // Exactly one row per key: a duplicate row silently shadows the other translation.
                    Assert.Single(values);
                    Assert.False(string.IsNullOrWhiteSpace(values[0]));
                    // Exact placeholder multiset AND set: no dropped and no invented placeholders.
                    Assert.Equal(SortedPlaceholders(key), SortedPlaceholders(values[0]));
                    Assert.Equal(DistinctPlaceholders(key), DistinctPlaceholders(values[0]));
                    Assert.Equal(Placeholders(key).Length, Placeholders(values[0]).Length);
                }
            }
        }

        // #2036: the rows added for this feature must exist with exactly these values.
        [Fact]
        public void NewContentRepoRows_HaveTheExactExpectedValues()
        {
            string root = FindRepoRoot();
            Dictionary<string, List<string>> ja = Parse(Path.Combine(root, "config", "translate", "ja.txt"));
            Dictionary<string, List<string>> zh = Parse(Path.Combine(root, "config", "translate", "zh.txt"));

            // Patch database display token (the only descriptor name that is really translated).
            Assert.Equal("パッチデータベース", Assert.Single(ja["Patch database"]));
            Assert.Equal("补丁数据库", Assert.Single(zh["Patch database"]));

            // Git progress banner: the placeholder-only banner stays identical in both catalogs.
            Assert.Equal("Git: {0} ...", Assert.Single(ja["Git: {0} ..."]));
            Assert.Equal("Git: {0} ...", Assert.Single(zh["Git: {0} ..."]));

            // Product names are intentionally not renamed by either catalog.
            Assert.Equal("FE-Repo", Assert.Single(ja["FE-Repo"]));
            Assert.Equal("FE-Repo", Assert.Single(zh["FE-Repo"]));
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(ja["FE-Repo-Music"])));
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(zh["FE-Repo-Music"])));

            Assert.Equal(
                "別のコンテンツリポジトリ操作がすでに実行中です。",
                Assert.Single(ja["Another content repository operation is already running."]));
            Assert.False(string.IsNullOrWhiteSpace(
                Assert.Single(zh["Another content repository operation is already running."])));
        }

        // #2036: a representative render of each parametrized row must survive string.Format with the
        // real argument count — a stray placeholder or a dropped one would throw or lose an argument.
        [Fact]
        public void ParametrizedRows_FormatWithTheRealArgumentCounts()
        {
            string root = FindRepoRoot();
            foreach (string language in new[] { "ja.txt", "zh.txt" })
            {
                Dictionary<string, List<string>> rows = Parse(Path.Combine(root, "config", "translate", language));
                string displayName = rows["Patch database"][0];

                string banner = string.Format(rows["Git: {0} ..."][0], displayName);
                Assert.Contains(displayName, banner);

                string initFailed = string.Format(rows["{0} initialization failed (git exit {1}).{2}"][0],
                    displayName, 128, "\r\nfatal: repository not found");
                Assert.Contains(displayName, initFailed);
                Assert.Contains("128", initFailed);
                Assert.Contains("fatal: repository not found", initFailed);

                string updated = string.Format(
                    rows["{0} updated. Restart recommended for all changes to take full effect."][0], displayName);
                Assert.Contains(displayName, updated);

                string notFound = string.Format(
                    rows["Git was not found. Install Git and try again, or set up {0} manually."][0], displayName);
                Assert.Contains(displayName, notFound);
            }
        }

        static Dictionary<string, List<string>> Parse(string path)
        {
            var rows = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string key = null;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) { key = null; continue; }
                if (key == null)
                {
                    if (line[0] == ':') key = line.Substring(1);
                    continue;
                }
                if (!rows.TryGetValue(key, out List<string> values))
                    rows[key] = values = new List<string>();
                values.Add(line);
                key = null;
            }
            return rows;
        }

        static string[] Placeholders(string value)
            => Regex.Matches(value, @"\{\d+\}").Cast<Match>().Select(m => m.Value).ToArray();

        static string[] SortedPlaceholders(string value)
            => Placeholders(value).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        static string[] DistinctPlaceholders(string value)
            => Placeholders(value).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent ?? "";
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
