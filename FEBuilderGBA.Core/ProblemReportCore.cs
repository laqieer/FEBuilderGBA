using System;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free bug-report packager (#1193, ports the WinForms
    /// <c>ToolProblemReportForm.MakeReport</c> diagnostic-collection step).
    ///
    /// READ-ONLY with respect to the ROM: it reads <see cref="ROM.Data"/> only to
    /// compute the log (version / size / CRC32 / header title) and never mutates
    /// the ROM, opens an undo scope, or saves it. It writes ONE output file (the
    /// <c>.report.7z</c> / <c>.report.zip</c> archive).
    ///
    /// Parity note: unlike the WinForms tool this does NOT archive the raw
    /// <c>.gba</c> ROM bytes (the WinForms tool only reads a clean ROM to generate
    /// UPS deltas; archiving the whole ROM would be a parity regression and a
    /// copyright/privacy risk). The archive therefore contains only
    /// <c>log.txt</c>, the per-ROM <c>etc/</c> config, and any already-existing
    /// sibling <c>.ups</c> delta next to the loaded ROM. Generating fresh UPS
    /// deltas (needs a clean-ROM input + WinForms <c>MainFormUtil.OpenROMToByte</c>)
    /// and the emulator save/backup search are out of scope for this slice.
    ///
    /// Never throws: every failure path returns a (localized) error string.
    /// </summary>
    public static class ProblemReportCore
    {
        /// <summary>The per-ROM etc config files copied into the report (port of WF <c>CopyEtcData</c>).</summary>
        static readonly string[] EtcFileTypes = { "lint_", "comment_", "flag_" };

        /// <summary>
        /// Collect diagnostics for <paramref name="rom"/> plus the user's
        /// <paramref name="problemText"/> into a compressed report archive at
        /// <paramref name="outputPath"/>.
        /// </summary>
        /// <returns>
        /// <c>""</c> on success, otherwise a localized error message. Never throws.
        /// </returns>
        public static string CreateReport(ROM rom, string problemText, string outputPath)
        {
            string tempDir = null;
            try
            {
                if (rom == null || rom.RomInfo == null || rom.Data == null)
                {
                    return R.Error("No ROM loaded.");
                }
                if (string.IsNullOrEmpty(outputPath))
                {
                    return R.Error("Output path is empty.");
                }

                // A private temp working dir. Core's U has no MakeTempDirectory
                // (WinForms-only), so manage it locally and clean it up in finally.
                tempDir = Path.Combine(Path.GetTempPath(), "febuilder_report_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                // 1) log.txt — diagnostics + the user's problem text.
                string log = Path.Combine(tempDir, "log.txt");
                File.WriteAllText(log, MakeReportLog(rom, problemText), new UTF8Encoding(false));

                // 2) Any already-existing sibling .ups delta (NOT the raw ROM).
                CollectSiblingUps(rom, tempDir);

                // 3) Per-ROM etc config (lint / comment / flag).
                CopyEtcData(rom, tempDir);

                // 4) Compress (native 7-zip32.dll -> real .7z, else SharpCompress -> .zip).
                // checksize:1 (not the default 1024) — a minimal report (log + a tiny
                // etc dir, highly compressible) can legitimately fall under the 1KB
                // heuristic floor the WF tool uses to detect a failed 7z run; we have
                // already validated the inputs, so only treat an empty file as failure.
                string err = ArchSevenZip.Compress(outputPath, tempDir, checksize: 1);
                if (!string.IsNullOrEmpty(err))
                {
                    return R.Error("Could not create the report.") + "\r\n" + err;
                }
                return "";
            }
            catch (Exception e)
            {
                return R.Error("Could not create the report.") + "\r\n" + e.ToString();
            }
            finally
            {
                // Best-effort cleanup — must never throw after a success/error return.
                if (tempDir != null)
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("ProblemReportCore: temp cleanup failed: " + e.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Build the report log (Core-safe subset of WF <c>MakeReportLog</c>): the
        /// recent application log, app version, ROM version / size / CRC32 / header
        /// title, then the user's problem text.
        /// </summary>
        public static string MakeReportLog(ROM rom, string problemText)
        {
            var sb = new StringBuilder();

            // Recent application log (helps reproduce the issue).
            sb.Append(Log.LogToString(1024));
            sb.Append("\r\n------\r\n");

            sb.AppendLine(typeof(U).Assembly.GetName().Name + ":" + U.getVersion());

            if (rom != null && rom.RomInfo != null && rom.Data != null)
            {
                sb.AppendLine("FEVersion:" + rom.RomInfo.VersionToFilename);
                sb.AppendLine("ROMSize:" + rom.Data.Length);

                uint crc = new U.CRC32().Calc(rom.Data);
                sb.AppendLine("CRC32:" + U.ToHexString8(crc));

                sb.AppendLine("Title:" + ReadHeaderTitle(rom.Data));
            }

            sb.AppendLine("Problem:");
            sb.AppendLine(problemText ?? "");
            return sb.ToString();
        }

        /// <summary>Read the 12-byte GBA cartridge title at 0xA0 (ASCII, NUL-trimmed).</summary>
        static string ReadHeaderTitle(byte[] data)
        {
            if (data == null || data.Length < 0xAC)
            {
                return "";
            }
            return Encoding.ASCII.GetString(data, 0xA0, 12).TrimEnd('\0');
        }

        /// <summary>
        /// Copy an already-existing sibling <c>.ups</c> next to the loaded ROM into
        /// the report (a small delta the user already chose to keep — never the raw
        /// ROM). Missing / no-filename is simply skipped.
        /// </summary>
        static void CollectSiblingUps(ROM rom, string tempDir)
        {
            try
            {
                string romFilename = rom.Filename;
                if (string.IsNullOrEmpty(romFilename) || !File.Exists(romFilename))
                {
                    return;
                }
                string ups = Path.ChangeExtension(romFilename, ".ups");
                if (File.Exists(ups))
                {
                    string dest = Path.Combine(tempDir, Path.GetFileName(ups));
                    File.Copy(ups, dest, true);
                }
            }
            catch (Exception e)
            {
                Log.Error("ProblemReportCore: sibling .ups copy failed: " + e.ToString());
            }
        }

        /// <summary>
        /// Copy the per-ROM etc config (<c>lint_</c> / <c>comment_</c> / <c>flag_</c>)
        /// into an <c>etc/</c> subdir (port of WF <c>CopyEtcData</c>). Missing files
        /// are skipped. Uses the explicit-rom etc lookup, never the ambient ROM.
        /// </summary>
        static void CopyEtcData(ROM rom, string tempDir)
        {
            try
            {
                string tempEtcDir = Path.Combine(tempDir, "etc");
                Directory.CreateDirectory(tempEtcDir);

                foreach (string name in EtcFileTypes)
                {
                    string src = U.ConfigEtcFilename(name, rom);
                    if (string.IsNullOrEmpty(src) || !File.Exists(src))
                    {
                        continue;
                    }
                    string dest = Path.Combine(tempEtcDir, Path.GetFileName(src));
                    File.Copy(src, dest, true);
                }
            }
            catch (Exception e)
            {
                Log.Error("ProblemReportCore: etc copy failed: " + e.ToString());
            }
        }
    }
}
