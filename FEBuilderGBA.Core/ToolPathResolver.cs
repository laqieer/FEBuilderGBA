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
            string baseDir = CoreState.BaseDirectory ?? "";
            if (string.IsNullOrEmpty(baseDir))
                return null;

            // ColorzCore (preferred)
            string colorzCorePath = Path.Combine(baseDir, "tools", "ColorzCore", "bin", "Release", "net8.0", "ColorzCore.exe");
            if (File.Exists(colorzCorePath))
                return colorzCorePath;

            // ColorzCore published output
            string colorzCorePublish = Path.Combine(baseDir, "tools", "ColorzCore", "bin", "Release", "net8.0", "publish", "ColorzCore.exe");
            if (File.Exists(colorzCorePublish))
                return colorzCorePublish;

            // Event Assembler Core.exe
            string eaCorePath = Path.Combine(baseDir, "tools", "Event-Assembler", "bin", "Release", "Core.exe");
            if (File.Exists(eaCorePath))
                return eaCorePath;

            // Event Assembler in tools/bin (if pre-built)
            string eaBinPath = Path.Combine(baseDir, "tools", "bin", "Core.exe");
            if (File.Exists(eaBinPath))
                return eaBinPath;

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
