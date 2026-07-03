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

        // Directories to skip: git-ignored build output, generated data, and git
        // submodules (separate repos, out of scope for this repo's dead-link guard).
        // Note: tools/ and resources/ are NOT blanket-skipped — only their submodule
        // subpaths are — so first-party tools (tools/WinCapture, tools/TextToPng,
        // tools/capture-window.cs, etc.) remain guarded against dead-link reintroduction.
        private static readonly string[] SkipSegments =
        {
            Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
            Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
            Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar,
            Path.DirectorySeparatorChar + "config" + Path.DirectorySeparatorChar,              // generated game data + config/patch2 submodule
            Path.DirectorySeparatorChar + "tools" + Path.DirectorySeparatorChar + "Event-Assembler" + Path.DirectorySeparatorChar, // submodule
            Path.DirectorySeparatorChar + "tools" + Path.DirectorySeparatorChar + "ColorzCore" + Path.DirectorySeparatorChar,       // submodule
            Path.DirectorySeparatorChar + "resources" + Path.DirectorySeparatorChar + "FE-Repo",  // FE-Repo + FE-Repo-Music-No-Preview submodules
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

        private static bool IsSkipped(string path)
        {
            foreach (var seg in SkipSegments)
            {
                if (path.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
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

            var violations = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                bool wanted = false;
                foreach (var e in ScanExtensions)
                {
                    if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
                }
                if (!wanted) continue;
                if (IsSkipped(file)) continue;

                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }

                if (content.IndexOf(DeadLinkPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add(GetRelative(root, file));
                }
            }

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
