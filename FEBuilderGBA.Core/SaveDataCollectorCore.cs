using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free emulator save / backup collector for the Problem-Report tool
    /// (#1235, ports the WinForms <c>ToolProblemReportForm</c> save-data and
    /// old-backup UPS-delta steps: <c>CollectSaveData</c> / <c>CollectSaveDataInner</c>
    /// / <c>PickupSaveData</c> / <c>PickupOneFile</c> / <c>CollectNoDollSaveData</c>
    /// L350-468 and the <c>MakeUPS</c> delta L242-260).
    ///
    /// READ-ONLY with respect to the ROM and the user's files: it only reads save /
    /// backup files and copies them (or writes a fresh <c>.ups</c> delta) into a
    /// caller-supplied temp dir. It takes the ROM path + emulator config dir as
    /// PARAMETERS — it never touches <c>Program.ROM</c> / <c>Program.Config</c>, so
    /// it is safe for Core / CLI / Avalonia / headless use.
    ///
    /// Every method is best-effort and never throws: a missing save / unreadable
    /// backup simply yields fewer collected files (mirrors the WF graceful behavior
    /// where a missing save does not fail the report).
    /// </summary>
    public static class SaveDataCollectorCore
    {
        /// <summary>
        /// Collect emulator save-state files that sit next to <paramref name="romFullPath"/>
        /// (and, for no$gba, under the emulator's <c>BATTERY/</c> dir) into
        /// <paramref name="tempDir"/>. Faithful port of WF <c>CollectSaveDataInner</c>.
        /// </summary>
        /// <param name="romFullPath">Full path to the currently loaded ROM (used for its dir + base name).</param>
        /// <param name="emulatorConfigDir">
        /// The configured emulator path (WF <c>Program.Config.at("emulator")</c>); may be
        /// <c>null</c>/empty. Its directory's <c>BATTERY/</c> subdir is searched for no$gba saves.
        /// </param>
        /// <param name="tempDir">Destination dir the found saves are copied into.</param>
        /// <returns>The list of file names (not full paths) copied into <paramref name="tempDir"/>.</returns>
        public static List<string> CollectSaveData(string romFullPath, string emulatorConfigDir, string tempDir)
        {
            var collected = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(romFullPath) || string.IsNullOrEmpty(tempDir))
                {
                    return collected;
                }

                // ".sav" — and if not found next to the ROM, try no$gba's BATTERY dir.
                if (!PickupSaveData(romFullPath, tempDir, ".sav", collected))
                {
                    CollectNoDollSaveData(romFullPath, emulatorConfigDir, tempDir, ".sav", collected);
                }

                PickupSaveData(romFullPath, tempDir, ".emulator.sav", collected);

                for (int i = 1; i < 13; i++)
                {
                    string[] exts =
                    {
                        ".emulator" + i + ".sgm",
                        ".emulator" + i + ".sps",
                        "" + i + ".sgm",
                        ".emulator.ss" + i,
                        ".ss" + i,
                        ".emulator.sg" + i,
                        ".sg" + i,
                        ".sa" + i,
                    };
                    foreach (string ext in exts)
                    {
                        PickupSaveData(romFullPath, tempDir, ext, collected);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("SaveDataCollectorCore.CollectSaveData failed: " + e.ToString());
            }
            return collected;
        }

        /// <summary>
        /// Copy <paramref name="explicitFilePath"/> into <paramref name="tempDir"/>
        /// (port of WF <c>PickupOneFile</c> — used for the interactive picker
        /// fallback when auto-discovery finds nothing). Missing file is a no-op.
        /// </summary>
        /// <returns>The copied file name, or <c>null</c> when nothing was copied.</returns>
        public static string PickupOneFile(string tempDir, string explicitFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(tempDir) ||
                    string.IsNullOrEmpty(explicitFilePath) ||
                    !File.Exists(explicitFilePath))
                {
                    return null;
                }
                string name = Path.GetFileName(explicitFilePath);
                string dest = Path.Combine(tempDir, name);
                File.Copy(explicitFilePath, dest, true);
                return name;
            }
            catch (Exception e)
            {
                Log.Error("SaveDataCollectorCore.PickupOneFile failed: " + e.ToString());
                return null;
            }
        }

        /// <summary>
        /// Look for <c>&lt;romdir&gt;/&lt;romBaseName&gt;&lt;needExt&gt;</c> next to the ROM
        /// (with a space-&gt;underscore base-name fallback some emulators use) and copy
        /// it into <paramref name="tempDir"/>. Port of WF <c>PickupSaveData</c>.
        /// </summary>
        static bool PickupSaveData(string romFullPath, string tempDir, string needExt, List<string> collected)
        {
            string dir = Path.GetDirectoryName(romFullPath);
            string file = Path.GetFileNameWithoutExtension(romFullPath);
            if (dir == null || file == null)
            {
                return false;
            }

            string savFilename = Path.Combine(dir, file + needExt);
            if (!File.Exists(savFilename))
            {
                // Some emulators replace spaces with underscores in the save name.
                file = file.Replace(" ", "_");
                savFilename = Path.Combine(dir, file + needExt);
                if (!File.Exists(savFilename))
                {
                    return false;
                }
            }

            string destName = file + needExt;
            string destFilename = Path.Combine(tempDir, destName);
            File.Copy(savFilename, destFilename, true);
            collected.Add(destName);
            return true;
        }

        /// <summary>
        /// no$gba keeps its battery saves under <c>&lt;emulatorDir&gt;/BATTERY/</c>.
        /// Port of WF <c>CollectNoDollSaveData</c>.
        /// </summary>
        static bool CollectNoDollSaveData(string romFullPath, string emulatorConfigDir,
            string tempDir, string needExt, List<string> collected)
        {
            if (string.IsNullOrEmpty(emulatorConfigDir))
            {
                return false;
            }
            string emudir = Path.GetDirectoryName(emulatorConfigDir);
            if (string.IsNullOrEmpty(emudir))
            {
                return false;
            }
            string dir = Path.Combine(emudir, "BATTERY");
            if (!Directory.Exists(dir))
            {
                return false;
            }

            string file = Path.GetFileNameWithoutExtension(romFullPath);
            if (file == null)
            {
                return false;
            }
            string savFilename = Path.Combine(dir, file + needExt);
            if (!File.Exists(savFilename))
            {
                return false;
            }

            string destName = file + needExt;
            string destFilename = Path.Combine(tempDir, destName);
            File.Copy(savFilename, destFilename, true);
            collected.Add(destName);
            return true;
        }

        /// <summary>
        /// Generate a <c>.ups</c> delta from a user-picked clean / old backup ROM to
        /// the current ROM bytes and write it into <paramref name="tempDir"/>
        /// (Core port of WF <c>MakeUPS</c> / <c>CollectOldUPSs</c>: the clean ROM is
        /// the UPS source, the current ROM is the destination). Read-only on disk
        /// except for the single <c>.ups</c> output.
        /// </summary>
        /// <param name="cleanRomPath">Full path to the clean / old backup ROM (UPS source).</param>
        /// <param name="currentRomBytes">The current (modified) ROM bytes (UPS destination).</param>
        /// <param name="tempDir">Destination dir the <c>.ups</c> delta is written into.</param>
        /// <returns>The written <c>.ups</c> file name, or <c>null</c> on any failure.</returns>
        public static string MakeBackupUps(string cleanRomPath, byte[] currentRomBytes, string tempDir)
        {
            try
            {
                if (string.IsNullOrEmpty(cleanRomPath) ||
                    !File.Exists(cleanRomPath) ||
                    currentRomBytes == null ||
                    currentRomBytes.Length == 0 ||
                    string.IsNullOrEmpty(tempDir))
                {
                    return null;
                }

                byte[] src = File.ReadAllBytes(cleanRomPath);
                string upsName = Path.GetFileNameWithoutExtension(cleanRomPath) + ".ups";
                string ups = Path.Combine(tempDir, upsName);
                UPSUtilCore.MakeUPS(src, currentRomBytes, ups);
                return upsName;
            }
            catch (Exception e)
            {
                Log.Error("SaveDataCollectorCore.MakeBackupUps failed: " + e.ToString());
                return null;
            }
        }
    }
}
