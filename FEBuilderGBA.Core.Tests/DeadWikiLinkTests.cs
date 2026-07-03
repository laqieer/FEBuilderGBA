using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Regression guard for issue #1780: the legacy DokuWiki host
    /// <c>https://dw.ngmansion.xyz/</c> was shut down, and all in-app help links /
    /// documentation references were migrated to the static mirror
    /// <c>https://laqieer.github.io/dw.ngmansion.xyz/</c>. This test fails if any
    /// source or documentation file re-introduces a dead <c>doku.php</c> link, so
    /// the dead links cannot silently come back.
    /// </summary>
    public class DeadWikiLinkTests
    {
        // The dead DokuWiki dispatcher pattern = legacy host + the "/doku.php" dispatcher.
        // Built from two parts so this guard file does not match itself, and so it does
        // NOT match the valid static mirror (which contains the host as a path segment:
        // laqieer.github.io/dw.ngmansion.xyz/wiki/...). Any occurrence of the combined
        // pattern means a help link / doc reference was not migrated to the mirror.
        private const string DeadHost = "dw.ngmansion.xyz";
        private const string DeadDispatcher = "/doku.php";
        private static readonly string DeadLinkPattern = DeadHost + DeadDispatcher;

        // Build output and git trees pruned by directory NAME during the walk.
        private static readonly string[] SkipDirNames = { "bin", "obj", ".git" };

        // Repo-root-relative directories to prune (forward-slash). config/ is generated
        // game data + the config/patch2 submodule; the rest are git submodules (separate
        // repos, out of scope). tools/ and resources/ are NOT blanket-pruned — only their
        // submodule subpaths are — so first-party tools (tools/WinCapture, tools/TextToPng,
        // tools/capture-window.cs, etc.) remain guarded against dead-link reintroduction.
        private static readonly string[] SkipRelDirs =
        {
            "config",                 // generated game data + config/patch2 submodule
            "tools/Event-Assembler",  // submodule
            "tools/ColorzCore",       // submodule
            "resources/FE-Repo",      // FE-Repo + FE-Repo-Music-No-Preview submodules
        };

        private static readonly string[] ScanExtensions =
        {
            ".cs", ".md", ".axaml",
        };

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        // Recursive walk that PRUNES skipped directories before descending, so the large
        // build-output, .git, generated-data, and submodule trees are never enumerated.
        // Yields only first-party source/doc files with a scanned extension.
        private static IEnumerable<string> EnumerateSourceFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f);
                    foreach (var e in ScanExtensions)
                    {
                        if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) { yield return f; break; }
                    }
                }

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { subdirs = Array.Empty<string>(); }
                foreach (var sub in subdirs)
                {
                    var name = Path.GetFileName(sub);
                    bool prune = false;
                    foreach (var d in SkipDirNames)
                    {
                        if (string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) { prune = true; break; }
                    }
                    if (!prune)
                    {
                        var rel = Path.GetRelativePath(root, sub).Replace(Path.DirectorySeparatorChar, '/');
                        foreach (var s in SkipRelDirs)
                        {
                            if (rel.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                                rel.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase)) { prune = true; break; }
                        }
                    }
                    if (!prune) stack.Push(sub);
                }
            }
        }

        [Fact]
        public void NoSourceFile_ReferencesDeadDokuWikiHost()
        {
            var root = FindRepoRoot();
            if (root == null)
            {
                // Running without the full repo checkout (e.g. packaged CI) — nothing to scan.
                return;
            }

            int scanned = 0;
            var violations = new List<string>();
            foreach (var file in EnumerateSourceFiles(root))
            {
                scanned++;

                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }

                if (content.IndexOf(DeadLinkPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add(GetRelative(root, file));
                }
            }

            // When the repo root resolves (the normal dev/CI case), a green pass MUST mean the
            // tree was actually walked — not a silent no-op. The repo has far more than 200
            // scanned source/doc files, so this floor proves real coverage occurred.
            Assert.True(scanned > 200,
                $"Expected to scan the first-party source tree, but only saw {scanned} files " +
                "(directory-prune/walk bug?).");

            Assert.True(violations.Count == 0,
                "The dead DokuWiki host '" + DeadLinkPattern + "' must not be referenced " +
                "(migrate to https://laqieer.github.io/dw.ngmansion.xyz/wiki/<id-with-slashes>.html). " +
                "Offending files:\n  " + string.Join("\n  ", violations));
        }

        private static string GetRelative(string root, string file)
        {
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
            return file;
        }
    }
}
