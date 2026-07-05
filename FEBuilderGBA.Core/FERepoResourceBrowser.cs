using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FEBuilderGBA
{
    /// <summary>
    /// Discovers and catalogs resources from the FE-Repo submodule.
    /// Returns empty collections when the submodule is not initialized.
    /// </summary>
    public static class FERepoResourceBrowser
    {
        // -----------------------------------------------------------------
        // #1644 — on-demand resource delivery for RELEASED (non-git) builds.
        //
        // The FE-Repo / FE-Repo-Music submodules are intentionally NOT bundled
        // into shipped artifacts (their payload is too large to attach to every
        // release). A source clone fetches them with `git submodule update`, but
        // a user of a released .zip has no git repo and no `scripts/` folder, so
        // the empty-state must offer a self-contained instruction that needs
        // neither: a shallow `git clone` of the public repo straight into the
        // expected `resources/` folder next to the executable.
        // -----------------------------------------------------------------

        /// <summary>
        /// Public upstream URL of the FE-Repo graphics repository. Aliased to the
        /// single source of truth in <see cref="GitUtil.FERepoDefaultUrl"/> so the
        /// remote URL is defined once (#1669 review).
        /// </summary>
        public const string GraphicsRepoUrl = GitUtil.FERepoDefaultUrl;

        /// <summary>
        /// Public upstream URL of the FE-Repo-Music (no-preview) repository.
        /// Aliased to <see cref="GitUtil.FERepoMusicDefaultUrl"/> (single source
        /// of truth, #1669 review).
        /// </summary>
        public const string MusicRepoUrl = GitUtil.FERepoMusicDefaultUrl;

        /// <summary>
        /// Self-contained fetch command for a RELEASED build (no git repo / no
        /// submodule / no <c>scripts/</c> folder required): shallow-clone the
        /// public graphics repo directly into the <c>resources/FE-Repo</c> folder
        /// the Resource Browser searches. Works from any extracted release zip.
        /// </summary>
        public const string GraphicsCloneCommand =
            "git clone --depth 1 " + GraphicsRepoUrl + " resources/FE-Repo";

        /// <summary>
        /// Self-contained fetch command for a RELEASED build — music repo
        /// counterpart of <see cref="GraphicsCloneCommand"/>.
        /// </summary>
        public const string MusicCloneCommand =
            "git clone --depth 1 " + MusicRepoUrl + " resources/FE-Repo-Music-No-Preview";

        /// <summary>
        /// A single resource file entry from FE-Repo.
        /// </summary>
        public class ResourceEntry
        {
            public string FullPath { get; set; }
            public string FileName { get; set; }
            public string Category { get; set; }
            public string SubCategory { get; set; }
            public long FileSize { get; set; }

            // #1807 — path of the file relative to the searched category/
            // subcategory folder (e.g. "FF9 Beatrix/1. Sword/Sword.gif"). For
            // flat single-image editors this equals <see cref="FileName"/>; for
            // the deeply-nested Battle Animations folder it lets the browser
            // label otherwise-identical "Sword.gif" entries by their animation.
            public string RelativePath { get; set; }
        }

        static readonly string[] IgnoredDirs = {
            "repo-tools", "Z_Unsorted Content", "ZZ_Backup and Archival ONLY",
            "ZZZ_FE13 (Awakening) Save Files", ".git"
        };

        static readonly string[] ImageExtensions = { ".png", ".bmp", ".gif" };
        static readonly string[] MusicExtensions = { ".s", ".mid", ".midi", ".wav" };

        /// <summary>
        /// Returns true only when <paramref name="dir"/> exists AND contains at
        /// least one child directory. An uninitialized git submodule leaves an
        /// empty-but-existing placeholder directory on disk; a bare
        /// <see cref="Directory.Exists"/> check would treat that placeholder as a
        /// valid repo root and silently show a blank browser (#1380 Part A).
        /// Requiring >=1 child directory makes the placeholder behave as
        /// "not found" so the actionable "run git submodule update" message
        /// surfaces. Never throws.
        /// </summary>
        static bool HasAnyChildDirectory(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return false;
                using var e = Directory.EnumerateDirectories(dir).GetEnumerator();
                return e.MoveNext();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Find the FE-Repo root directory by searching from the given base directory.
        /// An empty placeholder directory (uninitialized submodule) is treated as
        /// not-found (#1380 Part A).
        /// </summary>
        public static string FindRepoRoot(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;

            // Check resources/FE-Repo relative to baseDir and parent directories
            string dir = baseDir;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "resources", "FE-Repo");
                if (HasAnyChildDirectory(candidate))
                    return candidate;
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }

        /// <summary>
        /// Find the FE-Repo-Music root directory by searching from the given base directory.
        /// An empty placeholder directory (uninitialized submodule) is treated as
        /// not-found (#1380 Part A).
        /// </summary>
        public static string FindMusicRepoRoot(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;

            string dir = baseDir;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "resources", "FE-Repo-Music-No-Preview");
                if (HasAnyChildDirectory(candidate))
                    return candidate;
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }

        /// <summary>
        /// True when the FE-Repo-Music submodule is checked out (resolves to a
        /// real, populated root from <paramref name="baseDir"/>). Retained as a
        /// public, test-covered helper: as of #1815 the Song-editor
        /// "FE-Repo-Music" button is ALWAYS shown (like the graphics FE-Repo
        /// button) and its browser surfaces the missing-repo empty-state, so this
        /// no longer gates the button — but it remains available for callers that
        /// need to probe music-repo presence. Reuses the #1380
        /// empty-placeholder-as-not-found semantics of
        /// <see cref="FindMusicRepoRoot"/>. Never throws.
        /// </summary>
        public static bool IsMusicRepoAvailable(string baseDir)
            => FindMusicRepoRoot(baseDir) != null;

        /// <summary>
        /// Get music resource files within a category and optional subcategory.
        /// Filters for .s, .mid, .midi, .wav files.
        /// </summary>
        public static ResourceEntry[] GetMusicFiles(string repoRoot, string category, string subCategory = null, int maxResults = 0)
        {
            if (string.IsNullOrEmpty(repoRoot) || string.IsNullOrEmpty(category))
                return Array.Empty<ResourceEntry>();

            string searchDir = string.IsNullOrEmpty(subCategory)
                ? Path.Combine(repoRoot, category)
                : Path.Combine(repoRoot, category, subCategory);

            if (!Directory.Exists(searchDir))
                return Array.Empty<ResourceEntry>();

            var entries = new List<ResourceEntry>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!MusicExtensions.Contains(ext)) continue;

                    var info = new FileInfo(file);
                    entries.Add(new ResourceEntry
                    {
                        FullPath = file,
                        FileName = Path.GetFileName(file),
                        Category = category,
                        SubCategory = subCategory ?? "",
                        FileSize = info.Length
                    });

                    if (maxResults > 0 && entries.Count >= maxResults)
                        break;
                }
            }
            catch (Exception) { }

            return entries.OrderBy(e => e.FileName).ToArray();
        }

        /// <summary>
        /// Get top-level resource categories from FE-Repo.
        /// </summary>
        public static string[] GetCategories(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
                return Array.Empty<string>();

            return Directory.GetDirectories(repoRoot)
                .Select(Path.GetFileName)
                .Where(name => !IgnoredDirs.Contains(name) && !name.StartsWith("ZZ"))
                .OrderBy(name => name)
                .ToArray();
        }

        /// <summary>
        /// Get subcategories within a category.
        /// </summary>
        public static string[] GetSubCategories(string repoRoot, string category)
        {
            if (string.IsNullOrEmpty(repoRoot) || string.IsNullOrEmpty(category))
                return Array.Empty<string>();

            string catDir = Path.Combine(repoRoot, category);
            if (!Directory.Exists(catDir))
                return Array.Empty<string>();

            return Directory.GetDirectories(catDir)
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();
        }

        /// <summary>
        /// Get image resource files within a category and optional subcategory.
        /// Recursively searches all subdirectories.
        /// </summary>
        /// <param name="extensionFilter">
        /// #1807 — optional set of lower-case extensions (e.g. <c>{".gif"}</c>)
        /// to list instead of the default image extensions. Used by the Battle
        /// Animations editor to surface exactly one preview <c>.gif</c> per
        /// weapon-animation folder (frames are <c>.png</c>). <c>null</c>/empty
        /// keeps the default <c>.png/.bmp/.gif</c> behaviour so the existing
        /// single-image editors are unaffected.
        /// </param>
        public static ResourceEntry[] GetResourceFiles(string repoRoot, string category, string subCategory = null, int maxResults = 0, string[] extensionFilter = null)
        {
            if (string.IsNullOrEmpty(repoRoot) || string.IsNullOrEmpty(category))
                return Array.Empty<ResourceEntry>();

            bool useFilter = extensionFilter != null && extensionFilter.Length > 0;
            string[] allowedExts = useFilter
                ? extensionFilter.Select(x => (x ?? "").ToLowerInvariant()).ToArray()
                : ImageExtensions;

            string searchDir = string.IsNullOrEmpty(subCategory)
                ? Path.Combine(repoRoot, category)
                : Path.Combine(repoRoot, category, subCategory);

            if (!Directory.Exists(searchDir))
                return Array.Empty<ResourceEntry>();

            var entries = new List<ResourceEntry>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!allowedExts.Contains(ext)) continue;

                    var info = new FileInfo(file);
                    string relativePath = file.Substring(searchDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string subCat = subCategory;
                    if (string.IsNullOrEmpty(subCat))
                    {
                        int sep = relativePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                        subCat = sep >= 0 ? relativePath.Substring(0, sep) : "";
                    }

                    entries.Add(new ResourceEntry
                    {
                        FullPath = file,
                        FileName = Path.GetFileName(file),
                        Category = category,
                        SubCategory = subCat,
                        RelativePath = relativePath,
                        FileSize = info.Length
                    });

                    if (maxResults > 0 && entries.Count >= maxResults)
                        break;
                }
            }
            catch (Exception)
            {
                // Directory access errors — return what we have
            }

            // #1807 — when a filter is active (nested Battle Animations), order
            // by the relative path so entries group by animation folder; the
            // default single-image editors keep the historical FileName order.
            return useFilter
                ? entries.OrderBy(e => e.SubCategory).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()
                : entries.OrderBy(e => e.SubCategory).ThenBy(e => e.FileName).ToArray();
        }

        /// <summary>
        /// #1807 — given a selected FE-Repo battle-animation preview
        /// (<c>Sword.gif</c>), resolve the sibling importable FEditor file next
        /// to it: prefer the <c>.txt</c> script, else the <c>.bin</c> serialize.
        /// Returns <c>null</c> when neither exists so the caller can show a clear
        /// error instead of passing the <c>.gif</c> (a stray non-animation
        /// preview) through to the extension-dispatching importer. Pure; never
        /// throws.
        /// </summary>
        public static string ResolveBattleAnimeImportFile(string previewFilePath)
        {
            if (string.IsNullOrEmpty(previewFilePath))
                return null;
            try
            {
                string txt = Path.ChangeExtension(previewFilePath, ".txt");
                if (File.Exists(txt)) return txt;
                string bin = Path.ChangeExtension(previewFilePath, ".bin");
                if (File.Exists(bin)) return bin;
            }
            catch (Exception)
            {
                // treat any path/IO error as "not resolvable"
            }
            return null;
        }

        // -----------------------------------------------------------------
        // #1380 Part B — editor-kind -> FE-Repo folder resolver
        //
        // Single Core source of truth mapping each graphics editor to the
        // FE-Repo top-level category (+ optional subcategory) it should seed.
        // Folder strings are taken VERBATIM from the FE-Repo structure at the
        // pinned commit 3eb758268b09be70230e0ff94f05fa3b3d3bc787.
        //
        // Editors whose mapping is uncertain (issue "(verify)") or which have
        // no real source folder return Supported=false so a caller can hide
        // the FE-Repo button rather than seed to a wrong/empty path.
        // -----------------------------------------------------------------

        /// <summary>
        /// Identifies a graphics editor for FE-Repo folder resolution.
        /// One value per editor kind that may carry an FE-Repo button.
        /// </summary>
        public enum FERepoEditorKind
        {
            // --- wired in this PR (representative set) ---
            UnitWaitIcon,
            UnitMoveIcon,
            ItemIcon,
            GenericEnemyPortrait,
            BackgroundImage,

            // --- known good folders, deferred wiring (follow-up issue) ---
            Portrait,
            ClassCard,
            BattleAnimation,
            SkillIcon,
            SpellAnimation,
            BattleBackground,
            CGImage,
            Tileset,
        }

        /// <summary>
        /// Result of <see cref="GetFERepoFolderForEditor"/>: whether a real
        /// FE-Repo source folder exists for the editor, plus the seed
        /// category/subcategory to pre-navigate the browser to.
        /// </summary>
        public readonly struct FERepoFolderResult
        {
            public bool Supported { get; }
            public string Category { get; }
            public string SubCategory { get; }

            public FERepoFolderResult(bool supported, string category, string subCategory)
            {
                Supported = supported;
                Category = category;
                SubCategory = subCategory;
            }

            public static FERepoFolderResult Unsupported => new FERepoFolderResult(false, null, null);
        }

        /// <summary>
        /// Resolve the FE-Repo seed folder for a graphics editor. Returns
        /// <see cref="FERepoFolderResult.Unsupported"/> for editors with no
        /// real / verified source folder (caller should hide the button).
        /// Pure; never throws.
        /// </summary>
        public static FERepoFolderResult GetFERepoFolderForEditor(FERepoEditorKind kind)
        {
            switch (kind)
            {
                // GBAFE map sprites bundle standing + walking frames; both the
                // Wait (idle) and Move (walking) icon editors derive from
                // "Map Sprites/". This is a best-effort source: the editor's
                // own import path validates dimensions and reports a clear
                // error for an unsuitable asset (#1380 review blocking #2).
                case FERepoEditorKind.UnitWaitIcon:
                case FERepoEditorKind.UnitMoveIcon:
                    return new FERepoFolderResult(true, "Map Sprites", null);

                case FERepoEditorKind.ItemIcon:
                    return new FERepoFolderResult(true, "Item Icons", null);

                case FERepoEditorKind.GenericEnemyPortrait:
                    return new FERepoFolderResult(true, "Item Icons", "Special - Generic Minimugs");

                // #1393: at the pinned FE-Repo commit "CG Images" holds only a
                // README.md (its referenced PNGs are not in the tree), so seeding
                // it shows a blank browser. "Background CGs" is the populated
                // full-screen CG/background folder (256x160 assets); the
                // Background Image and CG editors both seed there.
                case FERepoEditorKind.BackgroundImage:
                    return new FERepoFolderResult(true, "BGs, Interface Elements", "Background CGs");

                case FERepoEditorKind.Portrait:
                    return new FERepoFolderResult(true, "Portrait Repository", null);

                case FERepoEditorKind.ClassCard:
                    return new FERepoFolderResult(true, "Class Cards", null);

                case FERepoEditorKind.BattleAnimation:
                    return new FERepoFolderResult(true, "Battle Animations", null);

                case FERepoEditorKind.SkillIcon:
                    return new FERepoFolderResult(true, "Item Icons", "Special - Skill Icons");

                case FERepoEditorKind.SpellAnimation:
                    return new FERepoFolderResult(true, "Spells n Skills", "7. Spells");

                case FERepoEditorKind.BattleBackground:
                    return new FERepoFolderResult(true, "BGs, Interface Elements", "Battle Frames & Backgrounds");

                // #1393: see BackgroundImage above — "Background CGs" is the
                // populated 256x160 full-screen CG folder; "CG Images" is empty
                // at the pinned commit.
                case FERepoEditorKind.CGImage:
                    return new FERepoFolderResult(true, "BGs, Interface Elements", "Background CGs");

                case FERepoEditorKind.Tileset:
                    return new FERepoFolderResult(true, "Tilesets", null);

                default:
                    return FERepoFolderResult.Unsupported;
            }
        }
    }
}
