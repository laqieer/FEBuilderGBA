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
            /// <summary>Resolution succeeded but produced no usable URL (e.g. empty CHECK_URL fallback).</summary>
            EmptyUrl,
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
                    // Ok must imply a usable URL (inline review): an empty CHECK_URL
                    // fallback is an error, not a confusing "Ok with empty URL".
                    return string.IsNullOrEmpty(url)
                        ? ResolveResult.Of(ResolveStatus.EmptyUrl)
                        : ResolveResult.Of(ResolveStatus.Ok, url);
                }

                if (url == "@CHECK_URL")
                {
                    // Same listing as the check page.
                    url = U.at(lines, "CHECK_URL");
                    return string.IsNullOrEmpty(url)
                        ? ResolveResult.Of(ResolveStatus.EmptyUrl)
                        : ResolveResult.Of(ResolveStatus.Ok, url);
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
                return string.IsNullOrEmpty(download)
                    ? ResolveResult.Of(ResolveStatus.EmptyUrl)
                    : ResolveResult.Of(ResolveStatus.Ok, download);
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

                // The UPS files staged BY THIS operation, tracked DETERMINISTICALLY by
                // destination path (inline re-review): no reliance on file mtimes (which
                // copy/extract can preserve and filesystems round). A hack folder's
                // pre-existing unrelated *.ups are therefore never applied/written.
                var staged = new List<string>();

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
                        // Destination is known EXPLICITLY — record it as staged.
                        string upsName = Path.Combine(romDir, RecommendUPSName(downloadUrl, recommendUpsName));
                        File.Copy(tempfile, upsName, true);
                        staged.Add(upsName);
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

                            // Enumerate *.ups in the EXTRACTION tree, then map each to its
                            // destination under romDir AFTER the same single-wrapper trim
                            // CopyDirectory1Trim applies — so we return exactly the files
                            // this extraction places, regardless of timestamps.
                            string copyRoot = TrimSingleWrapperDir(tempDir);
                            foreach (string srcUps in U.Directory_GetFiles_Safe(copyRoot, "*.ups", SearchOption.AllDirectories))
                            {
                                string rel = GetRelativePathSafe(copyRoot, srcUps);
                                staged.Add(Path.Combine(romDir, rel));
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

                // Keep only the staged destinations that actually landed on disk, and
                // de-duplicate (a re-download of the same name appears once).
                var present = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in staged)
                {
                    if (File.Exists(f) && seen.Add(f))
                    {
                        present.Add(f);
                    }
                }
                if (present.Count <= 0)
                {
                    return StageResult.Fail(StageStatus.NoUpsFound);
                }

                return new StageResult { Status = StageStatus.Ok, UpsFiles = present };
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

        /// <summary>One UPS applied in memory, not yet written.</summary>
        public sealed class StagedApply
        {
            public string UpsPath = "";
            public string SaveGbaPath = "";
            public byte[] Bytes = Array.Empty<byte>();
        }

        /// <summary>
        /// Result of <see cref="PrepareApply"/> — the in-memory apply pass. NO files
        /// written yet. The host inspects <see cref="Warnings"/> and decides whether to
        /// <see cref="CommitApply"/> (WF prompts before continuing on CRC warnings).
        /// </summary>
        public sealed class PrepareResult
        {
            public ApplyStatus Status;
            public string Error = "";
            public List<StagedApply> Staged = new List<StagedApply>();
            /// <summary>
            /// Non-fatal warnings (typically <c>UPSUtilCore.ApplyUPS</c> patch/result
            /// CRC mismatches — bytes still produced). The host should PROMPT before
            /// committing when this is non-empty.
            /// </summary>
            public List<string> Warnings = new List<string>();
            public static PrepareResult Fail(ApplyStatus s, string err = "")
                => new PrepareResult { Status = s, Error = err };
        }

        public sealed class ApplyResult
        {
            public ApplyStatus Status;
            public string Error = "";
            /// <summary>Patched <c>.gba</c> files written (one per staged UPS).</summary>
            public List<string> SavedRoms = new List<string>();
            /// <summary>Carried over from the prepare pass (informational).</summary>
            public List<string> Warnings = new List<string>();
            public static ApplyResult Fail(ApplyStatus s, string err = "")
                => new ApplyResult { Status = s, Error = err };
        }

        /// <summary>
        /// PHASE 1 (Copilot review #2/#3): apply EVERY UPS to the vanilla ROM
        /// <em>in memory only</em> — NOTHING is written. A hard error aborts the whole
        /// batch; CRC warnings are collected so the host can PROMPT before committing
        /// (WF asks the user whether to continue). Ports WF <c>DownloadAndExtract</c>
        /// lines 579-598 (load+apply half).
        /// </summary>
        /// <param name="applyOne">
        /// <c>(originalBytes, upsPath) =&gt; (bytes, error, warning)</c>. A non-empty
        /// <c>error</c> (or null <c>bytes</c>) aborts; a non-empty <c>warning</c> is collected.
        /// </param>
        public static PrepareResult PrepareApply(
            IReadOnlyList<string> upsFiles,
            string originalRomFilename,
            Func<byte[], string, (byte[]? bytes, string error, string warning)> applyOne)
        {
            try
            {
                if (upsFiles == null || upsFiles.Count == 0)
                {
                    return PrepareResult.Fail(ApplyStatus.ApplyFailed, "No UPS files.");
                }
                if (string.IsNullOrEmpty(originalRomFilename) || !File.Exists(originalRomFilename))
                {
                    return PrepareResult.Fail(ApplyStatus.NoOriginal);
                }

                byte[] original;
                try
                {
                    original = File.ReadAllBytes(originalRomFilename);
                }
                catch (Exception e)
                {
                    return PrepareResult.Fail(ApplyStatus.OriginalUnreadable, e.Message);
                }
                if (original.Length == 0)
                {
                    return PrepareResult.Fail(ApplyStatus.OriginalUnreadable);
                }

                var staged = new List<StagedApply>();
                var warnings = new List<string>();
                foreach (string ups in upsFiles)
                {
                    (byte[]? bytes, string err, string warn) = applyOne != null
                        ? applyOne(original, ups)
                        : (null, "no applier", "");
                    if (bytes == null || !string.IsNullOrEmpty(err))
                    {
                        return PrepareResult.Fail(ApplyStatus.ApplyFailed,
                            string.IsNullOrEmpty(err) ? ("Apply produced no bytes: " + ups) : err);
                    }
                    if (!string.IsNullOrEmpty(warn))
                    {
                        warnings.Add(ups + ": " + warn);
                    }
                    staged.Add(new StagedApply
                    {
                        UpsPath = ups,
                        SaveGbaPath = U.ChangeExtFilename(ups, ".gba"),
                        Bytes = bytes,
                    });
                }

                return new PrepareResult { Status = ApplyStatus.Ok, Staged = staged, Warnings = warnings };
            }
            catch (Exception e)
            {
                return PrepareResult.Fail(ApplyStatus.ApplyFailed, e.Message);
            }
        }

        /// <summary>
        /// PHASE 2 (Copilot review #3): write the pre-staged patched ROMs
        /// <em>atomically</em>. Each <c>.gba</c> is written to a temp file then moved
        /// into place; if ANY write fails, every file already moved is rolled back
        /// (deleted / restored from a backup of a pre-existing target) so the workflow
        /// never leaves a partial set of outputs. Only call after the host has accepted
        /// any <see cref="PrepareResult.Warnings"/>.
        /// </summary>
        public static ApplyResult CommitApply(PrepareResult prepared)
        {
            if (prepared == null || prepared.Status != ApplyStatus.Ok)
            {
                return ApplyResult.Fail(ApplyStatus.ApplyFailed,
                    prepared?.Error ?? "nothing prepared");
            }

            var written = new List<string>();          // targets we moved into place
            var backups = new List<(string target, string backup)>(); // pre-existing targets we displaced
            try
            {
                foreach (StagedApply s in prepared.Staged)
                {
                    string target = s.SaveGbaPath;
                    string tmp = target + ".tmp_" + Guid.NewGuid().ToString("N");
                    bool tmpConsumed = false;
                    try
                    {
                        File.WriteAllBytes(tmp, s.Bytes);

                        // If the target already exists, back it up so we can restore on rollback.
                        if (File.Exists(target))
                        {
                            string backup = target + ".bak_" + Guid.NewGuid().ToString("N");
                            File.Move(target, backup);
                            backups.Add((target, backup));
                        }
                        File.Move(tmp, target);
                        tmpConsumed = true;
                        written.Add(target);
                    }
                    finally
                    {
                        // Never leave a stray temp file behind (inline re-review #1).
                        if (!tmpConsumed)
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        }
                    }
                }

                // Success: drop the backups of displaced originals.
                foreach ((string _, string backup) in backups)
                {
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                }

                return new ApplyResult
                {
                    Status = ApplyStatus.Ok,
                    SavedRoms = written,
                    Warnings = prepared.Warnings,
                };
            }
            catch (Exception e)
            {
                // ---- rollback: delete what we wrote, restore displaced originals ----
                foreach (string t in written)
                {
                    try { if (File.Exists(t)) File.Delete(t); } catch { }
                }
                foreach ((string target, string backup) in backups)
                {
                    try
                    {
                        if (File.Exists(backup))
                        {
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(backup, target);
                        }
                    }
                    catch { }
                }
                return ApplyResult.Fail(ApplyStatus.SaveFailed, e.Message);
            }
        }

        /// <summary>
        /// Convenience one-shot: <see cref="PrepareApply"/> then immediately
        /// <see cref="CommitApply"/> (no warning prompt). Hosts that need to prompt on
        /// CRC warnings should call the two phases directly. Kept for tests / non-
        /// interactive callers.
        /// </summary>
        public static ApplyResult ApplyUpsAgainstOriginal(
            IReadOnlyList<string> upsFiles,
            string originalRomFilename,
            Func<byte[], string, (byte[]? bytes, string error, string warning)> applyOne)
        {
            PrepareResult p = PrepareApply(upsFiles, originalRomFilename, applyOne);
            if (p.Status != ApplyStatus.Ok)
            {
                return ApplyResult.Fail(p.Status, p.Error);
            }
            return CommitApply(p);
        }

        // ---- private directory/temp helpers (ported from WF U.cs) ---------------

        /// <summary>
        /// The directory <see cref="CopyDirectory1Trim"/> actually copies FROM: if the
        /// extraction produced a single wrapper directory (no top-level files), that
        /// inner directory; otherwise <paramref name="sourceDirName"/> itself. Used to
        /// map extracted <c>*.ups</c> to their post-copy destination deterministically.
        /// </summary>
        static string TrimSingleWrapperDir(string sourceDirName)
        {
            try
            {
                string[] files = Directory.GetFiles(sourceDirName);
                string[] dirs = Directory.GetDirectories(sourceDirName);
                if (files.Length <= 0 && dirs.Length == 1)
                {
                    return dirs[0];
                }
            }
            catch { /* fall through to the un-trimmed dir */ }
            return sourceDirName;
        }

        /// <summary>Relative path of <paramref name="full"/> under <paramref name="baseDir"/> (forward/back slashes ok).</summary>
        static string GetRelativePathSafe(string baseDir, string full)
        {
            try
            {
                string rel = Path.GetRelativePath(baseDir, full);
                if (!string.IsNullOrEmpty(rel) && rel != ".") return rel;
            }
            catch { /* fall through */ }
            return Path.GetFileName(full);
        }

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
            CopyDirectory(TrimSingleWrapperDir(sourceDirName), destDirName);
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
