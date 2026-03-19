using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Resolves paths to external tools (Event Assembler, ColorzCore, etc.).
    /// Checks user-configured path first, then falls back to bundled tools
    /// in the tools/ submodule directories.
    /// </summary>
    public static class ToolPathResolver
    {
        /// <summary>
        /// Resolve the Event Assembler / ColorzCore executable path.
        /// Priority: (1) user-configured path from config, (2) bundled ColorzCore.exe, (3) bundled Core.exe.
        /// Returns null if no valid executable is found.
        /// </summary>
        public static string ResolveEventAssembler()
        {
            // 1. Check user-configured path
            string configured = CoreState.Config?.at("event_assembler", "");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            // 2. Check bundled tools (submodule build output)
            // Search both the app base directory and repo root (for dev builds)
            string[] searchRoots = GetSearchRoots();

            foreach (string root in searchRoots)
            {
                // ColorzCore (preferred) — submodule layout: tools/ColorzCore/ColorzCore/bin/...
                foreach (string config in new[] { "Release", "Debug" })
                {
                    string colorzCore = Path.Combine(root, "tools", "ColorzCore", "ColorzCore",
                        "bin", config, "net6.0", "ColorzCore.exe");
                    if (File.Exists(colorzCore)) return colorzCore;
                }

                // Event Assembler Core.exe — submodule layout: tools/Event-Assembler/Event Assembler/Core/bin/...
                foreach (string config in new[] { "Release", "Debug" })
                {
                    string eaCore = Path.Combine(root, "tools", "Event-Assembler", "Event Assembler", "Core",
                        "bin", config, "net6.0", "Core.exe");
                    if (File.Exists(eaCore)) return eaCore;
                }

                // Pre-built binaries in tools/bin/ (manual placement)
                string preBuild = Path.Combine(root, "tools", "bin", "ColorzCore.exe");
                if (File.Exists(preBuild)) return preBuild;
                preBuild = Path.Combine(root, "tools", "bin", "Core.exe");
                if (File.Exists(preBuild)) return preBuild;
            }

            return null;
        }

        /// <summary>
        /// Get directories to search for bundled tools: app base dir + repo root.
        /// </summary>
        static string[] GetSearchRoots()
        {
            var roots = new System.Collections.Generic.List<string>();

            string baseDir = CoreState.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
                roots.Add(baseDir);

            // Walk up to find repo root (contains .git directory)
            string dir = baseDir ?? System.AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) && dir != baseDir)
                {
                    roots.Add(dir);
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }

            return roots.ToArray();
        }

        /// <summary>
        /// Resolve lyn.exe path from the Event Assembler directory.
        /// Checks Tools/lyn.exe relative to the EA executable, then searches
        /// the EA submodule's Event Assembler directory.
        /// </summary>
        public static string ResolveLynExe(string eaExePath)
        {
            if (string.IsNullOrEmpty(eaExePath)) return null;

            // Standard location: same dir as EA, under Tools/lyn.exe
            string eaDir = Path.GetDirectoryName(eaExePath);
            if (!string.IsNullOrEmpty(eaDir))
            {
                string standard = Path.Combine(eaDir, "Tools", "lyn.exe");
                if (File.Exists(standard)) return standard;

                // Also check parent directories (bundled layout may have deeper nesting)
                string parent = eaDir;
                for (int i = 0; i < 4; i++)
                {
                    parent = Path.GetDirectoryName(parent);
                    if (string.IsNullOrEmpty(parent)) break;
                    string candidate = Path.Combine(parent, "Tools", "lyn.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Search from repo root
            foreach (string root in GetSearchRoots())
            {
                string sub = Path.Combine(root, "tools", "Event-Assembler", "Event Assembler", "Tools", "lyn.exe");
                if (File.Exists(sub)) return sub;
            }

            return null;
        }

        /// <summary>
        /// Check if the resolved EA path points to ColorzCore (vs classic EA Core.exe).
        /// </summary>
        public static bool IsColorzCore(string eaPath)
        {
            if (string.IsNullOrEmpty(eaPath)) return false;
            return Path.GetFileName(eaPath) == "ColorzCore.exe";
        }
    }
}
