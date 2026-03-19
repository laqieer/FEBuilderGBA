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
        }

        static readonly string[] IgnoredDirs = {
            "repo-tools", "Z_Unsorted Content", "ZZ_Backup and Archival ONLY",
            "ZZZ_FE13 (Awakening) Save Files", ".git"
        };

        static readonly string[] ImageExtensions = { ".png", ".bmp", ".gif" };
        static readonly string[] MusicExtensions = { ".s", ".mid", ".midi", ".wav" };

        /// <summary>
        /// Find the FE-Repo root directory by searching from the given base directory.
        /// </summary>
        public static string FindRepoRoot(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;

            // Check resources/FE-Repo relative to baseDir and parent directories
            string dir = baseDir;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "resources", "FE-Repo");
                if (Directory.Exists(candidate))
                    return candidate;
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }

        /// <summary>
        /// Find the FE-Repo-Music root directory by searching from the given base directory.
        /// </summary>
        public static string FindMusicRepoRoot(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;

            string dir = baseDir;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "resources", "FE-Repo-Music-No-Preview");
                if (Directory.Exists(candidate))
                    return candidate;
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }

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
        public static ResourceEntry[] GetResourceFiles(string repoRoot, string category, string subCategory = null, int maxResults = 0)
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
                    if (!ImageExtensions.Contains(ext)) continue;

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

            return entries.OrderBy(e => e.SubCategory).ThenBy(e => e.FileName).ToArray();
        }
    }
}
