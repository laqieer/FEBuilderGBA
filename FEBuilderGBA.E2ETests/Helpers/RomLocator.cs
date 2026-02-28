using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FEBuilderGBA.E2ETests.Helpers
{
    /// <summary>
    /// Locates the ROM files used by ROM-based E2E tests.
    ///
    /// Priority:
    ///   1. ROMS_DIR environment variable (set by CI when ROMs are downloaded)
    ///   2. roms/ directory beside FEBuilderGBA.sln (local dev)
    ///
    /// ROM filenames expected: FE6.gba, FE7J.gba, FE7U.gba, FE8J.gba, FE8U.gba
    /// </summary>
    public static class RomLocator
    {
        public static readonly string? RomsDir;
        public static readonly string? FE6;
        public static readonly string? FE7J;
        public static readonly string? FE7U;
        public static readonly string? FE8J;
        public static readonly string? FE8U;

        static RomLocator()
        {
            // Priority 1: ROMS_DIR env var (CI injects this after downloading roms.zip)
            string? envDir = Environment.GetEnvironmentVariable("ROMS_DIR");
            if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
            {
                RomsDir = envDir;
            }
            else
            {
                // Priority 2: roms/ beside FEBuilderGBA.sln — walk up from test assembly
                string thisAssembly = Assembly.GetExecutingAssembly().Location;
                string? dir = Path.GetDirectoryName(thisAssembly);
                for (int i = 0; i < 8 && dir != null; i++)
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

            if (RomsDir != null)
            {
                FE6  = FindRom(RomsDir, "FE6.gba");
                FE7J = FindRom(RomsDir, "FE7J.gba");
                FE7U = FindRom(RomsDir, "FE7U.gba");
                FE8J = FindRom(RomsDir, "FE8J.gba");
                FE8U = FindRom(RomsDir, "FE8U.gba");
            }
        }

        private static string? FindRom(string dir, string filename)
        {
            string path = Path.Combine(dir, filename);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// MemberData source for [Theory] tests — yields one entry per ROM:
        ///   object?[] { romName, romPath? }
        /// romPath is null when the ROM file is not available; tests should skip in that case.
        /// </summary>
        public static IEnumerable<object?[]> AllRoms
        {
            get
            {
                yield return new object?[] { "FE6",  FE6  };
                yield return new object?[] { "FE7J", FE7J };
                yield return new object?[] { "FE7U", FE7U };
                yield return new object?[] { "FE8J", FE8J };
                yield return new object?[] { "FE8U", FE8U };
            }
        }
    }
}
