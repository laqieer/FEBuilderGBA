using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Problem-Report tool (#1193, #1235). READ-ONLY w.r.t. the ROM — collects
    /// diagnostics (log + per-ROM etc config + any sibling .ups + emulator save-state
    /// files next to the ROM + an optional clean-ROM .ups delta) plus the user's
    /// problem text into a <c>.report.7z</c> via <see cref="ProblemReportCore.CreateReport"/>.
    /// No ROM mutation, so no undo scope. Mirrors WinForms <c>ToolProblemReportForm</c>.
    /// </summary>
    public class ToolProblemReportViewModel : ViewModelBase
    {
        // Same destinations as WinForms MainFormUtil.GetReport7zURL / GetCommunitiesURL.
        public const string Report7zUrl =
            "https://feuniverse.us/t/fe-builder-gba-if-you-have-any-questions-attach-report7z/2845/4937";
        public const string CommunitiesUrl =
            "https://feuniverse.us/t/feu-discord-server/1480";

        string _problemText = "";
        string _statusMessage = "";
        bool _isLoaded;

        public string ProblemText { get => _problemText; set => SetField(ref _problemText, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>True when a ROM is loaded (report needs ROM diagnostics).</summary>
        public bool HasRom => CoreState.ROM?.RomInfo != null;

        /// <summary>The configured emulator path (WF <c>Program.Config.at("emulator")</c>).</summary>
        public string EmulatorConfigDir => CoreState.Config?.at("emulator") ?? "";

        /// <summary>
        /// Non-mutating probe: would auto-discovery find an emulator save next to the
        /// ROM? When false, the View surfaces the interactive SAV picker (#1235).
        /// </summary>
        public bool HasAutoSaveData()
        {
            string romFilename = CoreState.ROM?.Filename;
            if (string.IsNullOrEmpty(romFilename))
            {
                return false;
            }
            return SaveDataCollectorCore.HasAnySaveData(romFilename, EmulatorConfigDir);
        }

        /// <summary>
        /// Non-mutating probe: is there an already-existing sibling <c>.ups</c> next to
        /// the ROM? When false, the View can offer the backup picker so the user can
        /// supply a clean ROM for a fresh UPS delta (#1235).
        /// </summary>
        public bool HasSiblingUps()
        {
            try
            {
                string romFilename = CoreState.ROM?.Filename;
                if (string.IsNullOrEmpty(romFilename) || !System.IO.File.Exists(romFilename))
                {
                    return false;
                }
                return System.IO.File.Exists(System.IO.Path.ChangeExtension(romFilename, ".ups"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create the report archive at <paramref name="outputPath"/>.
        /// Returns <c>""</c> on success, otherwise a localized error string. Never
        /// throws; sets <see cref="StatusMessage"/> on every path.
        /// </summary>
        /// <param name="cleanRomPath">
        /// Optional clean / old backup ROM (from the interactive backup picker); when
        /// set, a <c>.ups</c> delta from it to the current ROM is added (#1235).
        /// </param>
        /// <param name="savFilePath">
        /// Optional save-state file the user picked in the SAV picker when no save was
        /// auto-discovered next to the ROM (#1235).
        /// </param>
        public string CreateReport(string outputPath, string cleanRomPath = null, string savFilePath = null)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null)
                {
                    string m = R._("No ROM loaded.");
                    StatusMessage = m;
                    return m;
                }
                if (string.IsNullOrWhiteSpace(ProblemText))
                {
                    string m = R._("Please describe the problem.");
                    StatusMessage = m;
                    return m;
                }
                if (string.IsNullOrEmpty(outputPath))
                {
                    string m = R._("Output path is empty.");
                    StatusMessage = m;
                    return m;
                }

                // Emulator save-state files live next to the ROM (+ no$gba BATTERY
                // under the configured emulator dir). Passing these lets the report
                // collect saves + an optional clean-ROM .ups delta (#1235).
                string emulatorConfigDir = CoreState.Config?.at("emulator") ?? "";

                string err = ProblemReportCore.CreateReport(
                    rom, ProblemText, outputPath, emulatorConfigDir, cleanRomPath, savFilePath);
                if (!string.IsNullOrEmpty(err))
                {
                    StatusMessage = err;
                    return err;
                }

                IsLoaded = true;
                StatusMessage = R._("Report created:") + " " + outputPath;
                return "";
            }
            catch (Exception ex)
            {
                string m = R._("Could not create the report.") + "\r\n" + ex;
                StatusMessage = m;
                return m;
            }
        }
    }
}
