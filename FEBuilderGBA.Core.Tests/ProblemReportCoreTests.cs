using System;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="ProblemReportCore.CreateReport"/> (#1193 — the Avalonia
    /// Problem-Report tool's GUI-free Core packager). Asserts:
    ///  - a non-empty archive is written and extracts back to a <c>log.txt</c> that
    ///    contains the user's problem text + the ROM version + CRC32 + header title;
    ///  - the archive contains NO raw <c>.gba</c> (the WF tool only reads a clean ROM
    ///    to make UPS deltas — archiving the whole ROM would be a parity regression
    ///    and a copyright/privacy risk; verified explicitly here);
    ///  - the never-throws contract on null ROM and bad output path.
    ///
    /// Validation extracts through <see cref="ArchSevenZip.Extract"/> because the
    /// SharpCompress fallback writes Zip content even under a <c>.report.7z</c> name.
    /// </summary>
    public class ProblemReportCoreTests
    {
        /// <summary>
        /// Build a minimal FE8U ROM with a recognizable GBA cartridge title at 0xA0
        /// so the log's Title: line is deterministic.
        /// </summary>
        static ROM MakeRom(string headerTitle = "REPORTTEST")
        {
            var rom = new ROM();
            var data = new byte[0x1000000];
            rom.LoadLow("problemreport-fe8u.gba", data, "BE8E01");
            Assert.NotNull(rom.RomInfo);

            byte[] title = System.Text.Encoding.ASCII.GetBytes(headerTitle);
            int n = Math.Min(title.Length, 12);
            Array.Copy(title, 0, rom.Data, 0xA0, n);
            return rom;
        }

        // A few KB of problem text so the compressed archive comfortably exceeds the
        // ArchSevenZip.Compress default 1024-byte size floor on a synthetic ROM
        // (whose Log/etc inputs are otherwise empty).
        const string BigProblem =
            "Freeze on chapter 1 when attacking with the lord. Steps to reproduce: load the suspend " +
            "data and attack. The game freezes when the enemy tries to counter. ";

        static string MakeProblemText()
        {
            return string.Concat(Enumerable.Repeat(BigProblem, 40)) + "UNIQUE_MARKER_42";
        }

        [Fact]
        public void CreateReport_WritesArchive_WithLogAndNoRawRom()
        {
            ROM rom = MakeRom();
            string problem = MakeProblemText();

            string outDir = Path.Combine(Path.GetTempPath(), "febuilder_report_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "test.report.7z");
            string extractDir = Path.Combine(outDir, "extract");

            try
            {
                string err = ProblemReportCore.CreateReport(rom, problem, outPath);
                Assert.Equal("", err);

                Assert.True(File.Exists(outPath), "report archive should exist");
                Assert.True(new FileInfo(outPath).Length > 0, "report archive should be non-empty");

                // Extract and inspect contents.
                Directory.CreateDirectory(extractDir);
                string exErr = ArchSevenZip.Extract(outPath, extractDir, isHide: true);
                Assert.Equal("", exErr);

                string[] files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);

                // log.txt is present.
                string log = files.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), "log.txt", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(log);

                string logText = File.ReadAllText(log);
                Assert.Contains("UNIQUE_MARKER_42", logText);     // user problem text
                Assert.Contains("CRC32:", logText);               // diagnostics
                Assert.Contains("FEVersion:", logText);
                Assert.Contains("REPORTTEST", logText);           // GBA header title

                // No raw ROM in the archive (parity + copyright/privacy).
                Assert.DoesNotContain(files, f =>
                    Path.GetExtension(f).Equals(".gba", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                TryDeleteDir(outDir);
            }
        }

        [Fact]
        public void CreateReport_ExplicitPickedSave_LandsInArchive()
        {
            // The synthetic ROM has no on-disk sibling save, so auto-discovery finds
            // none and the explicit picked save (WF CollectSaveData picker fallback)
            // must be copied into the report (#1235).
            ROM rom = MakeRom();
            string problem = MakeProblemText();

            string outDir = Path.Combine(Path.GetTempPath(), "febuilder_report_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "picked.report.7z");
            string extractDir = Path.Combine(outDir, "extract");

            // A fake picked save outside the ROM dir.
            string pickedSave = Path.Combine(outDir, "PickedHack.sav");
            File.WriteAllBytes(pickedSave, new byte[] { 1, 2, 3, 4, 5 });

            try
            {
                string err = ProblemReportCore.CreateReport(
                    rom, problem, outPath,
                    emulatorConfigDir: null, cleanRomPath: null, savFilePath: pickedSave);
                Assert.Equal("", err);

                Directory.CreateDirectory(extractDir);
                string exErr = ArchSevenZip.Extract(outPath, extractDir, isHide: true);
                Assert.Equal("", exErr);

                string[] files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                Assert.Contains(files, f =>
                    string.Equals(Path.GetFileName(f), "PickedHack.sav", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                TryDeleteDir(outDir);
            }
        }

        [Fact]
        public void CreateReport_NullRom_ReturnsError_NoThrow()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "febuilder_report_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "null.report.7z");
            try
            {
                string err = ProblemReportCore.CreateReport(null, "anything", outPath);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.False(File.Exists(outPath), "no archive should be written for a null ROM");
            }
            finally
            {
                TryDeleteDir(outDir);
            }
        }

        [Fact]
        public void CreateReport_EmptyOutputPath_ReturnsError_NoThrow()
        {
            ROM rom = MakeRom();
            string err = ProblemReportCore.CreateReport(rom, "problem", "");
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void CreateReport_BadOutputDir_ReturnsError_NoThrow()
        {
            ROM rom = MakeRom();
            // A path inside a non-existent, unwritable directory chain. Compress
            // must fail gracefully (error string) without throwing.
            string bad = Path.Combine(Path.GetTempPath(),
                "febuilder_report_missing_" + Guid.NewGuid().ToString("N"),
                "deep", "nope", "x.report.7z");
            // Do NOT create the parent dirs — but note ArchSevenZip creates output
            // dirs, so make it genuinely invalid with an illegal char.
            string invalid = bad + "\0illegal";

            string err = ProblemReportCore.CreateReport(rom, MakeProblemText(), invalid);
            Assert.False(string.IsNullOrEmpty(err));
        }

        static void TryDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
