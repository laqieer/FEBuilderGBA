using System;
using System.IO;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    /// <summary>
    /// High-level ROM initialization for CLI use.
    /// Sets up CoreState, loads config, text encoder, translation.
    /// </summary>
    public static class RomLoader
    {
        /// <summary>
        /// Initialize the Core environment (config, translation) without loading a ROM.
        /// </summary>
        public static void InitEnvironment()
        {
            string baseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                throw new InvalidOperationException("CoreState.BaseDirectory must be set before calling InitEnvironment.");

            // Load config
            string configPath = Path.Combine(baseDir, "config", "config.xml");
            if (File.Exists(configPath))
            {
                var config = new Config();
                config.Load(configPath);
                CoreState.Config = config;
            }
        }

        /// <summary>
        /// Load a ROM file and set up CoreState.ROM.
        /// </summary>
        public static bool LoadRom(string romPath)
        {
            if (!File.Exists(romPath))
            {
                CoreState.Services.ShowError($"ROM file not found: {romPath}");
                return false;
            }

            ROM rom = new ROM();
            bool ok = rom.Load(romPath, out string version);
            if (!ok)
            {
                CoreState.Services.ShowError($"Failed to load ROM: {romPath} (version: {version})");
                return false;
            }
            CoreState.ROM = rom;
            return true;
        }
    }
}
