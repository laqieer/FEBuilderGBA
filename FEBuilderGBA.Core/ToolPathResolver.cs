using System;
using System.IO;
using System.Runtime.InteropServices;

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
        /// Get executable filenames to search for, accounting for platform.
        /// On Windows: "Name.exe". On Linux/macOS: both "Name" and "Name.exe".
        /// </summary>
        static string[] GetExeNames(string baseName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new[] { baseName + ".exe" };
            return new[] { baseName, baseName + ".exe" };
        }

        /// <summary>
        /// Resolve the Event Assembler / ColorzCore executable path.
        /// Priority: (1) user-configured path from config, (2) bundled ColorzCore, (3) bundled Core.
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
            string[] colorzNames = GetExeNames("ColorzCore");
            string[] coreNames = GetExeNames("Core");

            foreach (string root in searchRoots)
            {
                // ColorzCore (preferred) — submodule layout: tools/ColorzCore/ColorzCore/bin/...
                // ColorzCore.csproj sets BaseOutputPath=bin/Core, so output lands in bin/Core/{config}/net6.0/
                foreach (string config in new[] { "Release", "Debug" })
                {
                    foreach (string name in colorzNames)
                    {
                        string colorzCore = Path.Combine(root, "tools", "ColorzCore", "ColorzCore",
                            "bin", "Core", config, "net6.0", name);
                        if (File.Exists(colorzCore)) return colorzCore;

                        // Also check standard output path (bin/{config}/net6.0/) in case BaseOutputPath is removed
                        colorzCore = Path.Combine(root, "tools", "ColorzCore", "ColorzCore",
                            "bin", config, "net6.0", name);
                        if (File.Exists(colorzCore)) return colorzCore;
                    }
                }

                // Event Assembler Core — submodule layout: tools/Event-Assembler/Event Assembler/Core/bin/...
                foreach (string config in new[] { "Release", "Debug" })
                {
                    foreach (string name in coreNames)
                    {
                        string eaCore = Path.Combine(root, "tools", "Event-Assembler", "Event Assembler", "Core",
                            "bin", config, "net6.0", name);
                        if (File.Exists(eaCore)) return eaCore;
                    }
                }

                // Pre-built binaries in tools/bin/ (manual placement or CI-shipped)
                foreach (string name in colorzNames)
                {
                    string preBuild = Path.Combine(root, "tools", "bin", name);
                    if (File.Exists(preBuild)) return preBuild;
                }
                foreach (string name in coreNames)
                {
                    string preBuild = Path.Combine(root, "tools", "bin", name);
                    if (File.Exists(preBuild)) return preBuild;
                }
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

            // Always include AppDomain base directory as a fallback search root
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(appBaseDir) && appBaseDir != baseDir)
                roots.Add(appBaseDir);

            // Walk up to find repo root (contains .git directory)
            string dir = baseDir ?? appBaseDir;
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
        /// Resolve the devkitARM tool directory from the <c>devkitpro_eabi</c> config
        /// marker (the as/gcc tree root is the marker's directory). Returns null when
        /// the marker is unset or missing. Symmetric with
        /// <see cref="ResolveEventAssembler"/> / <see cref="ResolveLynExe"/>.
        /// </summary>
        public static string ResolveDevkitArmDir()
        {
            string eabi = CoreState.Config?.at("devkitpro_eabi", "");
            if (string.IsNullOrEmpty(eabi) || !File.Exists(eabi))
                return null;
            return Path.GetDirectoryName(eabi);
        }

        /// <summary>
        /// Resolve a devkitARM tool (e.g. <c>*gcc.exe</c>, <c>*g++.exe</c>,
        /// <c>*as.exe</c>) by globbing the configured devkitARM tree recursively.
        /// Returns null when the tree or the tool is missing.
        /// </summary>
        public static string ResolveDevkitArmTool(string glob)
        {
            string dir = ResolveDevkitArmDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;
            string[] files = U.Directory_GetFiles_Safe(dir, glob, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        /// <summary>Resolve the devkitARM C compiler (<c>*gcc.exe</c>), or null.</summary>
        public static string ResolveDevkitArmGcc() => ResolveDevkitArmTool("*gcc.exe");

        /// <summary>Resolve the devkitARM C++ compiler (<c>*g++.exe</c>), or null.</summary>
        public static string ResolveDevkitArmGpp() => ResolveDevkitArmTool("*g++.exe");

        /// <summary>Resolve the devkitARM assembler (<c>*as.exe</c>), or null.</summary>
        public static string ResolveDevkitArmAs() => ResolveDevkitArmTool("*as.exe");

        /// <summary>
        /// Check if the resolved EA path points to ColorzCore (vs classic EA Core.exe).
        /// </summary>
        public static bool IsColorzCore(string eaPath)
        {
            if (string.IsNullOrEmpty(eaPath)) return false;
            string fileName = Path.GetFileName(eaPath);
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return fileName.Equals("ColorzCore.exe", comparison)
                || fileName.Equals("ColorzCore", comparison);
        }
    }
}
