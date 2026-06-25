#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free port of the WinForms <c>ToolWorkSupportForm</c> download/apply-UPS
    /// half (#1454): the work/ROM-hack update pipeline that the Avalonia "Update"
    /// button must drive instead of the editor's own GitHub release.
    ///
    /// <para>Three stages, each pure orchestration over Core primitives:</para>
    /// <list type="number">
    /// <item><see cref="ResolveDownloadUrl"/> — reads <c>UPDATE_URL</c>/<c>UPDATE_REGEX</c>
    /// from the parsed <c>.updateinfo.txt</c> (with the WF <c>@CHECK_URL</c> /
    /// <c>@DIRECT_URL</c> fallbacks), scraping the listing page via <c>RegexCache</c>.</item>
    /// <item><see cref="DownloadAndStage"/> — downloads the package, copies a raw UPS
    /// into the ROM directory or extracts an archive there, and enumerates the staged
    /// <c>*.ups</c> files.</item>
    /// <item><see cref="ApplyUpsAgainstOriginal"/> — applies each staged UPS to a
    /// user-selected vanilla ROM and writes the patched <c>.gba</c> beside it.</item>
    /// </list>
    ///
    /// <para>Every network / archive / ROM touch is injected as a delegate so the
    /// decision + flow logic is fully testable offline. The Avalonia host wires real
    /// implementations (<c>U.HttpGet</c>, <c>U.HttpDownloadFile</c>,
    /// <c>ArchSevenZip.Extract</c>, <c>ROM.Load</c>/<c>ROM.Save</c>). Mirrors WF
    /// <c>RunDownloadAndExtract</c> + <c>DownloadAndExtract</c>; nothing here throws.</para>
    /// </summary>
    public static class WorkSupportUpdateDownloadCore
    {
        /// <summary>Minimum sane download size (bytes). Mirrors WF's 256-byte floor.</summary>
        public const long MinDownloadSize = 256;

        // ---- ResolveDownloadUrl -------------------------------------------------

        public enum ResolveStatus
        {
            Ok,
            MissingUpdateRegex,
            RegexNoMatch,
            HttpError,
        }

        public sealed class ResolveResult
        {
            public ResolveStatus Status;
            public string Url = "";
            public string Error = "";
            public static ResolveResult Of(ResolveStatus s, string url = "", string err = "")
                => new ResolveResult { Status = s, Url = url, Error = err };
        }

        /// <summary>
        /// Resolve the package download URL from <paramref name="lines"/>. Ports WF
        /// <c>RunDownloadAndExtract</c> lines 420-451:
        /// <list type="bullet">
        /// <item><c>UPDATE_URL</c> empty ⇒ treated as <c>@CHECK_URL</c> (reuse CHECK_URL).</item>
        /// <item><c>UPDATE_REGEX</c> empty ⇒ <see cref="ResolveStatus.MissingUpdateRegex"/>.</item>
        /// <item><c>UPDATE_REGEX == @DIRECT_URL</c> ⇒ use <c>UPDATE_URL</c> verbatim (direct link).</item>
        /// <item><c>UPDATE_URL == @CHECK_URL</c> ⇒ use <c>CHECK_URL</c> verbatim.</item>
        /// <item>otherwise GET <c>UPDATE_URL</c> and scrape group(1) via <c>UPDATE_REGEX</c>.</item>
        /// </list>
        /// </summary>
        public static ResolveResult ResolveDownloadUrl(
            Dictionary<string, string>? lines,
            Func<string, string>? httpGet)
        {
            try
            {
                if (lines == null)
                {
                    return ResolveResult.Of(ResolveStatus.MissingUpdateRegex);
                }

                string url = U.at(lines, "UPDATE_URL");
                if (url == "")
                {
                    url = "@CHECK_URL";
                }

                string regex = U.at(lines, "UPDATE_REGEX");
                if (regex == "")
                {
                    return ResolveResult.Of(ResolveStatus.MissingUpdateRegex);
                }

                if (regex == "@DIRECT_URL")
                {
                    // Direct link: UPDATE_URL itself is the download. If it was the
                    // @CHECK_URL placeholder, fall back to CHECK_URL.
                    if (url == "@CHECK_URL")
                    {
                        url = U.at(lines, "CHECK_URL");
                    }
                    return ResolveResult.Of(ResolveStatus.Ok, url);
                }

                if (url == "@CHECK_URL")
                {
                    // Same listing as the check page.
                    url = U.at(lines, "CHECK_URL");
                    return ResolveResult.Of(ResolveStatus.Ok, url);
                }

                string html;
                try
                {
                    html = httpGet != null ? httpGet(url) : "";
                }
                catch (Exception e)
                {
                    return ResolveResult.Of(ResolveStatus.HttpError, "", e.Message);
                }

                Match m = RegexCache.Match(html, regex);
                if (m.Groups.Count < 2)
                {
                    return ResolveResult.Of(ResolveStatus.RegexNoMatch, "", html);
                }

                string download = m.Groups[1].ToString();
                download = EscapeURLToDecode(download);
                return ResolveResult.Of(ResolveStatus.Ok, download);
            }
            catch (Exception e)
            {
                return ResolveResult.Of(ResolveStatus.HttpError, "", e.Message);
            }
        }

        /// <summary>Ports WF <c>EscapeURLToDecode</c>: un-escape a JSON-escaped <c>:\/\/</c> URL.</summary>
        public static string EscapeURLToDecode(string url)
        {
            if (string.IsNullOrEmpty(url)) return url ?? "";
            if (url.IndexOf(":\\/\\/", StringComparison.Ordinal) >= 0)
            {
                url = url.Replace("\\", "");
            }
            return url;
        }

        // ---- DownloadAndStage ---------------------------------------------------

        public enum StageStatus
        {
            Ok,
            DownloadFailed,
            DownloadMissing,
            DownloadTooSmall,
            ExtractFailed,
            NoUpsFound,
        }

        public sealed class StageResult
        {
            public StageStatus Status;
            public string Error = "";
            /// <summary>Staged <c>*.ups</c> files in <paramref name="romDir"/> (recursive).</summary>
            public List<string> UpsFiles = new List<string>();
            public static StageResult Fail(StageStatus s, string err = "")
                => new StageResult { Status = s, Error = err };
        }

        /// <summary>
        /// Download <paramref name="downloadUrl"/> and stage it into
        /// <paramref name="romDir"/>: a raw UPS is copied in directly; an archive is
        /// extracted (single wrapper directory trimmed). Returns the staged
        /// <c>*.ups</c> files. Ports WF <c>DownloadAndExtract</c> lines 477-563.
        ///
        /// <para>Injected delegates keep this offline-testable:</para>
        /// </summary>
        /// <param name="downloadFile">Download <c>(url, destPath) =&gt; (ok, error)</c>. Writes destPath.</param>
        /// <param name="extract">Extract <c>(archivePath, destDir) =&gt; errorOrEmpty</c> (e.g. <c>ArchSevenZip.Extract</c>).</param>
        /// <param name="recommendUpsName">ROM filename used to derive a fallback <c>.ups</c> name (the loaded ROM path).</param>
        public static StageResult DownloadAndStage(
            string downloadUrl,
            string romDir,
            string recommendUpsName,
            Func<string, string, (bool ok, string error)> downloadFile,
            Func<string, string, string> extract)
        {
            try
            {
                if (string.IsNullOrEmpty(romDir))
                {
                    return StageResult.Fail(StageStatus.DownloadFailed, "ROM directory is empty.");
                }

                string tempfile = Path.GetTempFileName();
                try
                {
                    // ---- download ----
                    (bool ok, string err) = downloadFile != null
                        ? downloadFile(downloadUrl, tempfile)
                        : (false, "no downloader");
                    if (!ok)
                    {
                        return StageResult.Fail(StageStatus.DownloadFailed, err);
                    }
                    if (!File.Exists(tempfile))
                    {
                        return StageResult.Fail(StageStatus.DownloadMissing);
                    }
                    if (U.GetFileSize(tempfile) <= MinDownloadSize)
                    {
                        return StageResult.Fail(StageStatus.DownloadTooSmall);
                    }

                    // ---- raw UPS vs archive ----
                    if (UPSUtilCore.IsUPSFile(tempfile))
                    {
                        string upsName = Path.Combine(romDir, RecommendUPSName(downloadUrl, recommendUpsName));
                        File.Copy(tempfile, upsName, true);
                    }
                    else
                    {
                        string tempDir = MakeTempDir();
                        try
                        {
                            string r = extract != null ? extract(tempfile, tempDir) : "no extractor";
                            if (!string.IsNullOrEmpty(r))
                            {
                                return StageResult.Fail(StageStatus.ExtractFailed, r);
                            }
                            CopyDirectory1Trim(tempDir, romDir);
                        }
                        finally
                        {
                            TryDeleteDir(tempDir);
                        }
                    }
                }
                finally
                {
                    try { if (File.Exists(tempfile)) File.Delete(tempfile); } catch { /* best-effort */ }
                }

                // ---- enumerate staged UPS ----
                string[] upsFiles = U.Directory_GetFiles_Safe(romDir, "*.ups", SearchOption.AllDirectories);
                if (upsFiles.Length <= 0)
                {
                    return StageResult.Fail(StageStatus.NoUpsFound);
                }

                return new StageResult { Status = StageStatus.Ok, UpsFiles = new List<string>(upsFiles) };
            }
            catch (Exception e)
            {
                return StageResult.Fail(StageStatus.DownloadFailed, e.Message);
            }
        }

        /// <summary>Ports WF <c>RecomendUPSName</c>: prefer the URL's filename if it ends in <c>.ups</c>.</summary>
        public static string RecommendUPSName(string url, string romFilename)
        {
            string filename = Path.GetFileName(url ?? "");
            if (!string.IsNullOrEmpty(filename) && filename.IndexOf(".ups", StringComparison.OrdinalIgnoreCase) > 0)
            {
                return filename;
            }
            return Path.GetFileNameWithoutExtension(romFilename ?? "rom") + ".ups";
        }

        // ---- ApplyUpsAgainstOriginal -------------------------------------------

        public enum ApplyStatus
        {
            Ok,
            NoOriginal,
            OriginalUnreadable,
            ApplyFailed,
            SaveFailed,
        }

        public sealed class ApplyResult
        {
            public ApplyStatus Status;
            public string Error = "";
            /// <summary>Patched <c>.gba</c> files written (one per staged UPS).</summary>
            public List<string> SavedRoms = new List<string>();
            /// <summary>
            /// Non-fatal warnings collected during apply — typically <c>UPSUtilCore.ApplyUPS</c>
            /// patch/result CRC mismatches (the patch still produced bytes). Empty on a clean run.
            /// The host can surface these to the user (WF asks whether to continue).
            /// </summary>
            public List<string> Warnings = new List<string>();
            public static ApplyResult Fail(ApplyStatus s, string err = "")
                => new ApplyResult { Status = s, Error = err };
        }

        /// <summary>
        /// Apply every UPS in <paramref name="upsFiles"/> to the vanilla ROM at
        /// <paramref name="originalRomFilename"/> and save the patched <c>.gba</c>
        /// beside each UPS. Ports WF <c>DownloadAndExtract</c> lines 579-598.
        ///
        /// <para><b>Validate-ALL-before-write</b> (Copilot review finding #3): every
        /// UPS is applied in memory first; only if ALL succeed are the <c>.gba</c>
        /// files written, so a late failure can never leave partial outputs. CRC
        /// warnings (patched bytes produced despite a CRC mismatch) are collected in
        /// <see cref="ApplyResult.Warnings"/> rather than silently flattened.</para>
        ///
        /// <para>The ROM load/apply is injected as <paramref name="applyOne"/> so Core
        /// has no dependency on a concrete loader: it receives the original bytes + the
        /// UPS path and returns the patched ROM bytes, a hard error (aborts), and an
        /// optional non-fatal warning.</para>
        /// </summary>
        /// <param name="applyOne">
        /// <c>(originalBytes, upsPath) =&gt; (bytes, error, warning)</c>. A non-empty
        /// <c>error</c> (or null <c>bytes</c>) aborts the whole batch BEFORE any write;
        /// a non-empty <c>warning</c> is collected but does not abort.
        /// </param>
        public static ApplyResult ApplyUpsAgainstOriginal(
            IReadOnlyList<string> upsFiles,
            string originalRomFilename,
            Func<byte[], string, (byte[]? bytes, string error, string warning)> applyOne)
        {
            try
            {
                if (upsFiles == null || upsFiles.Count == 0)
                {
                    return ApplyResult.Fail(ApplyStatus.ApplyFailed, "No UPS files.");
                }
                if (string.IsNullOrEmpty(originalRomFilename) || !File.Exists(originalRomFilename))
                {
                    return ApplyResult.Fail(ApplyStatus.NoOriginal);
                }

                byte[] original;
                try
                {
                    original = File.ReadAllBytes(originalRomFilename);
                }
                catch (Exception e)
                {
                    return ApplyResult.Fail(ApplyStatus.OriginalUnreadable, e.Message);
                }
                if (original.Length == 0)
                {
                    return ApplyResult.Fail(ApplyStatus.OriginalUnreadable);
                }

                // ---- pass 1: apply ALL in memory (abort before any write) ----
                var staged = new List<(string savegba, byte[] bytes)>();
                var warnings = new List<string>();
                foreach (string ups in upsFiles)
                {
                    (byte[]? bytes, string err, string warn) = applyOne != null
                        ? applyOne(original, ups)
                        : (null, "no applier", "");
                    if (bytes == null || !string.IsNullOrEmpty(err))
                    {
                        return ApplyResult.Fail(ApplyStatus.ApplyFailed,
                            string.IsNullOrEmpty(err) ? ("Apply produced no bytes: " + ups) : err);
                    }
                    if (!string.IsNullOrEmpty(warn))
                    {
                        warnings.Add(ups + ": " + warn);
                    }
                    staged.Add((U.ChangeExtFilename(ups, ".gba"), bytes));
                }

                // ---- pass 2: write ALL (only reached when every apply succeeded) ----
                var saved = new List<string>();
                foreach ((string savegba, byte[] bytes) in staged)
                {
                    try
                    {
                        File.WriteAllBytes(savegba, bytes);
                    }
                    catch (Exception e)
                    {
                        return ApplyResult.Fail(ApplyStatus.SaveFailed, e.Message);
                    }
                    saved.Add(savegba);
                }

                return new ApplyResult { Status = ApplyStatus.Ok, SavedRoms = saved, Warnings = warnings };
            }
            catch (Exception e)
            {
                return ApplyResult.Fail(ApplyStatus.ApplyFailed, e.Message);
            }
        }

        // ---- private directory/temp helpers (ported from WF U.cs) ---------------

        static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fe_worksupport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best-effort */ }
        }

        /// <summary>
        /// Ports WF <c>U.CopyDirectory1Trim</c>: if the extracted archive produced a
        /// single wrapper directory (no top-level files), copy that inner directory's
        /// contents instead of the wrapper itself.
        /// </summary>
        internal static void CopyDirectory1Trim(string sourceDirName, string destDirName)
        {
            string[] files = Directory.GetFiles(sourceDirName);
            string[] dirs = Directory.GetDirectories(sourceDirName);
            if (files.Length <= 0 && dirs.Length == 1)
            {
                sourceDirName = dirs[0];
            }
            CopyDirectory(sourceDirName, destDirName);
        }

        /// <summary>Ports WF <c>U.CopyDirectory</c> (recursive, overwrite, skip empty dirs).</summary>
        internal static void CopyDirectory(string sourceDirName, string destDirName)
        {
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            foreach (string file in Directory.GetFiles(sourceDirName))
            {
                string destfilename = Path.Combine(destDirName, Path.GetFileName(file));
                try
                {
                    File.Copy(file, destfilename, true);
                }
                catch (Exception e)
                {
                    Log.Error("CopyDirectory failed Src:" + file + " Dest:" + destfilename + "\r\n" + e.ToString());
                }
            }

            foreach (string dir in Directory.GetDirectories(sourceDirName))
            {
                if (IsEmptyDirectory(dir))
                {
                    continue;
                }
                CopyDirectory(dir, Path.Combine(destDirName, Path.GetFileName(dir)));
            }
        }

        static bool IsEmptyDirectory(string sourceDirName)
        {
            if (!Directory.Exists(sourceDirName)) return false;
            return Directory.GetFiles(sourceDirName).Length <= 0
                && Directory.GetDirectories(sourceDirName).Length <= 0;
        }
    }
}
