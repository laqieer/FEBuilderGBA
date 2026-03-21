using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Locates ROM files for Avalonia headless tests.
    ///
    /// Priority:
    ///   1. ROMS_DIR environment variable (set by CI when ROMs are downloaded)
    ///   2. roms/ directory beside FEBuilderGBA.sln (local dev)
    ///
    /// ROM filenames expected: FE6.gba, FE7J.gba, FE7U.gba, FE8J.gba, FE8U.gba
    /// Version is detected by reading the 6-byte header signature at offset 0xAC.
    /// </summary>
    public static class TestRomLocator
    {
        /// <summary>
        /// Map from header signature (6 ASCII bytes at offset 0xAC) to friendly version name.
        /// </summary>
        private static readonly Dictionary<string, string> SignatureToVersion = new()
        {
            { "AFEJ01", "FE6" },
            { "AE7J01", "FE7J" },
            { "AE7E01", "FE7U" },
            { "BE8J01", "FE8J" },
            { "BE8E01", "FE8U" },
        };

        public static readonly string? RomsDir;

        static TestRomLocator()
        {
            // Priority 1: ROMS_DIR env var (CI injects this after downloading roms.zip).
            // If the variable is present at all (even empty / non-existent path) we treat
            // it as an explicit override and skip the walk-up fallback entirely.
            string? envDir = Environment.GetEnvironmentVariable("ROMS_DIR");
            bool envExplicitlySet = envDir != null;

            if (envExplicitlySet)
            {
                if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
                    RomsDir = envDir;
            }
            else
            {
                // Priority 2: roms/ beside FEBuilderGBA.sln -- walk up from test assembly
                string thisAssembly = Assembly.GetExecutingAssembly().Location;
                string? dir = Path.GetDirectoryName(thisAssembly);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    {
                        string candidate = Path.Combine(dir, "roms");
                        if (Directory.Exists(candidate))
                            RomsDir = candidate;
                        break;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
        }

        /// <summary>
        /// Find a ROM file for the given version ("FE6", "FE7J", "FE7U", "FE8J", "FE8U").
        /// Returns the full path if found and the header signature matches, null otherwise.
        /// </summary>
        public static string? FindRom(string version)
        {
            if (RomsDir == null)
                return null;

            string path = Path.Combine(RomsDir, version + ".gba");
            if (!File.Exists(path))
                return null;

            // Verify version by reading header signature
            string? detected = DetectVersion(path);
            if (detected != null && detected == version)
                return path;

            // If detection failed but file exists with expected name, still return it
            // (ROM may be modified but filename is correct)
            if (detected == null)
                return path;

            return null;
        }

        /// <summary>
        /// Detect ROM version from the 6-byte header signature at offset 0xAC.
        /// Returns "FE6", "FE7J", "FE7U", "FE8J", "FE8U", or null if unrecognized.
        /// </summary>
        public static string? DetectVersion(string romPath)
        {
            try
            {
                // Read just the header bytes we need (offset 0xAC, 6 bytes)
                using var fs = new FileStream(romPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 0xAC + 6)
                    return null;

                fs.Seek(0xAC, SeekOrigin.Begin);
                byte[] sigBytes = new byte[6];
                int read = fs.Read(sigBytes, 0, 6);
                if (read < 6)
                    return null;

                string sig = Encoding.ASCII.GetString(sigBytes);
                return SignatureToVersion.TryGetValue(sig, out string? ver) ? ver : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// MemberData source for [Theory] tests -- yields one entry per ROM version:
        ///   object?[] { versionName, romPath? }
        /// romPath is null when the ROM file is not available; tests should skip in that case.
        /// </summary>
        public static IEnumerable<object?[]> AllRoms
        {
            get
            {
                yield return new object?[] { "FE6", FindRom("FE6") };
                yield return new object?[] { "FE7J", FindRom("FE7J") };
                yield return new object?[] { "FE7U", FindRom("FE7U") };
                yield return new object?[] { "FE8J", FindRom("FE8J") };
                yield return new object?[] { "FE8U", FindRom("FE8U") };
            }
        }
    }
}
