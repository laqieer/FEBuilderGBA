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

        /// <summary>
        /// Create the report archive at <paramref name="outputPath"/>.
        /// Returns <c>""</c> on success, otherwise a localized error string. Never
        /// throws; sets <see cref="StatusMessage"/> on every path.
        /// </summary>
        /// <param name="cleanRomPath">
        /// Optional clean / old backup ROM (from the interactive backup picker); when
        /// set, a <c>.ups</c> delta from it to the current ROM is added (#1235).
        /// </param>
        public string CreateReport(string outputPath, string cleanRomPath = null)
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
                    rom, ProblemText, outputPath, emulatorConfigDir, cleanRomPath);
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
