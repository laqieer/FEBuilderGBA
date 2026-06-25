#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Single-work Work Support VM (#1454). Parity with WinForms
    /// <c>ToolWorkSupportForm</c>: the "Update" action checks the loaded ROM hack's
    /// own <c>.updateinfo.txt</c> (CHECK_URL/UPDATE_URL), NOT the editor's GitHub
    /// release. Network/archive/ROM touches are delegated into Core
    /// (<see cref="WorkSupportUpdateCheckCore"/> + <see cref="WorkSupportUpdateDownloadCore"/>)
    /// with injectable delegates so the flow is testable offline.
    /// </summary>
    public class ToolWorkSupportViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _name = "";
        string _author = "";
        string _version = "";
        string _communityUrl = "";
        string _infoText = "";
        string _autoFeedbackStatus = "";
        bool _hasUpdateInfo;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public string Author { get => _author; set => SetField(ref _author, value); }
        public string Version { get => _version; set => SetField(ref _version, value); }
        public string CommunityUrl { get => _communityUrl; set => SetField(ref _communityUrl, value); }
        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }
        public string AutoFeedbackStatus { get => _autoFeedbackStatus; set => SetField(ref _autoFeedbackStatus, value); }
        public bool HasUpdateInfo { get => _hasUpdateInfo; set => SetField(ref _hasUpdateInfo, value); }

        /// <summary>Resolved path to the loaded ROM's <c>.updateinfo.txt</c> (or "").</summary>
        public string UpdateInfoPath { get; private set; } = "";

        /// <summary>Absolute filename of the currently-loaded ROM (or "").</summary>
        public string RomFilename { get; private set; } = "";

        /// <summary>Parsed <c>.updateinfo.txt</c> key/value pairs (never null).</summary>
        public Dictionary<string, string> UpdateinfoLines { get; private set; }
            = new Dictionary<string, string>();

        public void Initialize()
        {
            try
            {
                LoadUpdateInfo();
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportViewModel", ex.ToString());
            }
            IsLoaded = true;
        }

        void LoadUpdateInfo()
        {
            Name = "";
            Author = "";
            Version = "";
            CommunityUrl = "";
            UpdateInfoPath = "";
            RomFilename = "";
            HasUpdateInfo = false;
            UpdateinfoLines = new Dictionary<string, string>();

            if (CoreState.ROM == null)
            {
                InfoText = "No ROM loaded.";
                return;
            }

            string romFilename = CoreState.ROM.Filename;
            if (string.IsNullOrEmpty(romFilename))
            {
                InfoText = "ROM filename is empty.";
                return;
            }
            RomFilename = romFilename;

            // Resolve the sidecar exactly like WinForms (exact swap, then trimmed
            // name variants) — NOT a hard-coded ".updateinfo.txt" in the ROM dir.
            string updateInfoPath = WorkSupportScannerCore.GetUpdateInfo(romFilename);
            if (string.IsNullOrEmpty(updateInfoPath) || !File.Exists(updateInfoPath))
            {
                InfoText = "No .updateinfo.txt found for this ROM.";
                HasUpdateInfo = false;
                return;
            }

            HasUpdateInfo = true;
            UpdateInfoPath = updateInfoPath;
            InfoText = updateInfoPath;

            UpdateinfoLines = WorkSupportScannerCore.LoadUpdateInfo(updateInfoPath);
            Name = U.at(UpdateinfoLines, "NAME");
            if (string.IsNullOrEmpty(Name)) Name = Path.GetFileNameWithoutExtension(romFilename);
            Author = U.at(UpdateinfoLines, "AUTHOR");
            CommunityUrl = U.at(UpdateinfoLines, "COMMUNITY_URL");
            Version = GetUpsDateTimeString(romFilename);
        }

        /// <summary>
        /// Local work version string (the newer of the ROM and its sibling <c>.ups</c>
        /// timestamps). Ports WF <c>GetUPSDateTimeString</c>.
        /// </summary>
        public static string GetUpsDateTimeString(string romFilename)
        {
            try
            {
                string ups = Path.ChangeExtension(romFilename, ".ups");
                if (File.Exists(ups))
                {
                    return File.GetLastWriteTime(ups).ToString();
                }
                return File.GetLastWriteTime(romFilename).ToString() + "(ROM)";
            }
            catch
            {
                return "";
            }
        }

        // ---- Update flow (delegates to Core) -----------------------------------

        /// <summary>
        /// Decide whether the loaded work has a newer version. Mirrors WF
        /// <c>CheckUpdate()</c> via <see cref="WorkSupportUpdateCheckCore.Check"/>.
        /// Delegates injected so the call is offline-testable. Read-only.
        /// </summary>
        public WorkSupportUpdateCheckCore.UpdateResult CheckUpdate(
            Func<string, string> httpGet,
            Func<string, string?> httpHeadLastModified,
            Func<string, DateTime> romDateTime)
        {
            return WorkSupportUpdateCheckCore.Check(
                UpdateinfoLines, RomFilename, httpGet, httpHeadLastModified, romDateTime);
        }

        /// <summary>
        /// Resolve the package download URL from UPDATE_URL/UPDATE_REGEX. Mirrors WF
        /// <c>RunDownloadAndExtract</c> URL-resolution head.
        /// </summary>
        public WorkSupportUpdateDownloadCore.ResolveResult ResolveDownloadUrl(Func<string, string> httpGet)
        {
            return WorkSupportUpdateDownloadCore.ResolveDownloadUrl(UpdateinfoLines, httpGet);
        }

        /// <summary>
        /// Download + stage the package into the ROM directory, returning the staged
        /// <c>*.ups</c> files. Mirrors WF <c>DownloadAndExtract</c> (download/extract half).
        /// </summary>
        public WorkSupportUpdateDownloadCore.StageResult DownloadAndStage(
            string downloadUrl,
            Func<string, string, (bool ok, string error)> downloadFile,
            Func<string, string, string> extract)
        {
            string romDir = Path.GetDirectoryName(RomFilename) ?? "";
            return WorkSupportUpdateDownloadCore.DownloadAndStage(
                downloadUrl, romDir, RomFilename, downloadFile, extract);
        }

        /// <summary>
        /// Apply staged UPS files to a user-selected vanilla ROM and write the
        /// patched <c>.gba</c>s. Mirrors WF <c>DownloadAndExtract</c> (UPS-apply half).
        /// </summary>
        public WorkSupportUpdateDownloadCore.ApplyResult ApplyUps(
            IReadOnlyList<string> upsFiles,
            string originalRomFilename,
            Func<byte[], string, (byte[]? bytes, string error, string warning)> applyOne)
        {
            return WorkSupportUpdateDownloadCore.ApplyUpsAgainstOriginal(
                upsFiles, originalRomFilename, applyOne);
        }
    }
}
