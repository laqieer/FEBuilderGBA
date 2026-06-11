// SPDX-License-Identifier: GPL-3.0-or-later
// Init Wizard auto-download/install — cross-platform Core helper (#1031).
//
// Ports the WinForms ToolInitWizardForm.DownloadProgram_Direct /
// DownloadProgram_DirectOneFile / DownloadGitButton flow into a GUI-free Core
// class so the Avalonia Init Wizard can wire its 8 Download buttons.
//
// Design (per the accepted plan + Copilot CLI review findings):
//   - U.HttpDownloadFile (Core) does the raw URL->file fetch (injectable here
//     via the `downloadStep` delegate so tests never hit the network).
//   - Each resource downloads to a per-call STAGING dir under the system temp
//     folder. Archives extract THERE; the expected executable is located +
//     validated in the staging dir, and only THEN copied into the final tool
//     dir. On ANY failure NOTHING is written to the final tool dir, so a
//     pre-existing working install is never clobbered (atomic install).
//   - Explicit failure contract: returns null + a NON-EMPTY `error` on failure;
//     `error` is set to "" ONLY on success.
//   - Git: GetLatestInstallerUrl -> download installer to temp ->
//     RunInstallerSilentlyAsync -> GitUtil.FindGitExecutable. All three steps
//     are injectable so tests exercise the plumbing without a real GitHub call
//     or installer launch.
//
// This class writes ZERO ROM bytes — it only places tool executables on disk
// and (for Git) runs an installer. ROM-write safety guards do not apply here,
// but the install-safety guards (temp staging, validate-before-place, cleanup
// on failure, explicit error contract) do.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform download/extract/locate-by-glob helper backing the Init
    /// Wizard's auto-download buttons. GUI-free; the Avalonia view layers the
    /// confirmation dialog, progress UI, and Browse-mode validation on top.
    /// </summary>
    public static class DownloadInstallCore
    {
        /// <summary>
        /// Stable identifiers for every downloadable tool resource the Init
        /// Wizard offers. Bundled buttons (e.g. ASM = no$gba + arm-as) download
        /// multiple of these; the Avalonia layer treats those as all-or-none.
        /// </summary>
        public enum ResourceId
        {
            VbaM,
            MGba,
            EA,
            Lyn,
            Sappy,
            GbaMusicStudio,
            NoGba,
            ArmAs,
            GbaMusRiper,
            Sox,
            Midfix4agb,
        }

        /// <summary>
        /// Static description of one downloadable resource: source URL, the
        /// sub-directory under <c>{baseDir}/app</c> it installs into, and the
        /// match-glob used to locate its executable after download/extract.
        /// URLs + globs are copied VERBATIM from the WinForms handlers.
        /// </summary>
        public sealed class DownloadSpec
        {
            public ResourceId Id { get; }
            public string Url { get; }
            public string AppSubDir { get; }
            public string MatchGlob { get; }
            /// <summary>
            /// True when the URL points straight at a single .exe (no archive
            /// to extract). Mirrors WF DownloadProgram_DirectOneFile / the
            /// <c>.exe</c> short-circuit inside DownloadProgram_Direct.
            /// </summary>
            public bool IsSingleFile { get; }
            /// <summary>Optional Referer header (Dropbox/some hosts need one).</summary>
            public string Referer { get; }

            public DownloadSpec(ResourceId id, string url, string appSubDir, string matchGlob,
                bool isSingleFile = false, string referer = "")
            {
                Id = id;
                Url = url;
                AppSubDir = appSubDir;
                MatchGlob = matchGlob;
                IsSingleFile = isSingleFile;
                Referer = referer;
            }
        }

        // GitHub releases API source named in the Git confirmation dialog when
        // the exact installer URL can't be resolved up-front.
        public const string GitSourceLabel = "https://github.com/git-for-windows/git/releases (git-scm.com)";

        // ------------------------------------------------------------------
        // Resource catalog — URLs + globs copied verbatim from WF
        // ToolInitWizardForm (SettingStep1..5 + DownloadProgram_*).
        // ------------------------------------------------------------------
        static readonly Dictionary<ResourceId, DownloadSpec> s_specs = new()
        {
            [ResourceId.VbaM] = new DownloadSpec(ResourceId.VbaM,
                "https://github.com/FEBuilderGBA/FEBuilderGBA/releases/download/ver_20200316.21/VisualBoyAdvance-M.7z",
                "VBA-M", "VisualBoyAdvance*.exe"),

            [ResourceId.MGba] = new DownloadSpec(ResourceId.MGba,
                "https://github.com/mgba-emu/mgba/releases/download/0.6.1/mGBA-0.6.1-win32.7z",
                "mVBA", "mGBA.exe"),

            // EA (Dropbox — brittle). Referer left empty; ?dl=1 forces a direct
            // file response when Dropbox cooperates.
            [ResourceId.EA] = new DownloadSpec(ResourceId.EA,
                "https://www.dropbox.com/s/4mql123thxb78kw/Event%20Assembler%20V11.1.3.zip?dl=1",
                "Event Assembler", "Core.exe"),

            [ResourceId.Lyn] = new DownloadSpec(ResourceId.Lyn,
                "https://github.com/StanHash/lyn/releases/download/v2.5.3/lyn.exe",
                Path.Combine("Event Assembler", "Tools"), "lyn.exe", isSingleFile: true),

            // Sappy (Dropbox — brittle).
            [ResourceId.Sappy] = new DownloadSpec(ResourceId.Sappy,
                "https://www.dropbox.com/sh/723s9jdkfkx7pwa/AABrXCMghyx2f74fme6iDoTEa?dl=1",
                "sappy", "sappy.exe"),

            [ResourceId.GbaMusicStudio] = new DownloadSpec(ResourceId.GbaMusicStudio,
                "https://github.com/FEBuilderGBA/DirectPlayS/releases/download/20230504/DirectPlayS.7z",
                "GBAMusicStdio", "VG Music Studio.exe"),

            [ResourceId.NoGba] = new DownloadSpec(ResourceId.NoGba,
                "https://problemkaputt.de/no$gba.zip",
                "no$gba", "NO$GBA.EXE"),

            [ResourceId.ArmAs] = new DownloadSpec(ResourceId.ArmAs,
                "https://github.com/FireEmblemUniverse/SkillSystem_FE8/raw/d6808351425a9098feab27ddbfa9c5c3a46a3f57/Tools/devkitARM/bin/arm-none-eabi-as.exe",
                "asm", "arm-none-eabi-as.exe", isSingleFile: true),

            [ResourceId.GbaMusRiper] = new DownloadSpec(ResourceId.GbaMusRiper,
                "https://github.com/FEBuilderGBA/FEBuilderGBA/releases/download/ver_20200316.21/gba_mus_riper_v24.7z",
                "gba_mus_riper", "song_riper.exe"),

            [ResourceId.Sox] = new DownloadSpec(ResourceId.Sox,
                "https://github.com/FEBuilderGBA/FEBuilderGBA/releases/download/ver_20200316.21/sox-14.4.2.7z",
                "sox", "sox.exe"),

            [ResourceId.Midfix4agb] = new DownloadSpec(ResourceId.Midfix4agb,
                "https://github.com/FEBuilderGBA/FEBuilderGBA/releases/download/ver_20240308.07/midfix4agb.7z",
                "midfix4agb", "midfix4agb.exe"),
        };

        /// <summary>Look up the static spec (URL, target dir, glob) for a resource.</summary>
        public static DownloadSpec GetSpec(ResourceId id) => s_specs[id];

        /// <summary>
        /// Signature for the injectable download step. Matches
        /// <see cref="U.HttpDownloadFile"/> exactly (url, destPath, out error,
        /// referer). Returns true on success; on failure returns false with a
        /// non-empty <paramref name="error"/>.
        /// </summary>
        public delegate bool DownloadStep(string url, string destPath, out string error, string referer);

        /// <summary>
        /// Download (and, if an archive, extract) the resource identified by
        /// <paramref name="id"/>, locate its executable by match-glob, and place
        /// it into <c>{baseDir}/app/{spec.AppSubDir}</c>. Returns the resolved
        /// final exe path on success (with <paramref name="error"/> = ""), or
        /// <c>null</c> with a NON-EMPTY <paramref name="error"/> on any failure.
        ///
        /// Atomicity: everything happens in a per-call staging dir under the
        /// system temp folder. The exe is located + validated THERE; only on
        /// success is it copied into the final tool dir. On failure NOTHING is
        /// written to the final tool dir (a prior install is preserved), and the
        /// staging dir is removed.
        /// </summary>
        public static string Download(ResourceId id, string baseDir, Action<string> progress,
            out string error, DownloadStep downloadStep = null)
        {
            StagedDownload staged = Stage(id, baseDir, progress, out error, downloadStep);
            if (staged == null)
                return null;
            try
            {
                return Commit(staged, ref error);
            }
            finally
            {
                staged.Dispose();
            }
        }

        /// <summary>
        /// One resource downloaded + extracted + located + validated in a per-call
        /// TEMP staging dir, but NOT yet placed into its final tool dir. Created by
        /// <see cref="Stage"/>; the validated exe is <see cref="StagedExe"/>. A
        /// caller commits it via <see cref="Commit"/> (which copies it into
        /// <see cref="FinalDir"/>) and MUST <see cref="Dispose"/> it to remove the
        /// staging dir. Two-phase so a BUNDLE can stage EVERY member first and only
        /// place them once all staged successfully (true all-or-none — Copilot
        /// #1102 finding 2; a later member's failure leaves NOTHING placed).
        /// </summary>
        public sealed class StagedDownload : IDisposable
        {
            internal DownloadSpec Spec { get; init; }
            internal string StagingDir { get; init; }
            /// <summary>The validated exe inside the staging dir.</summary>
            public string StagedExe { get; init; }
            /// <summary>Final tool dir this will be placed into on Commit.</summary>
            public string FinalDir { get; init; }
            /// <summary>For archives, the extracted tree copied wholesale; null for single-file.</summary>
            internal string CopyWholeDir { get; init; }
            internal string PlaceFilename { get; init; }
            public void Dispose()
            {
                try { if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }

        /// <summary>
        /// Phase 1: download + (extract) + locate + validate <paramref name="id"/>'s
        /// exe into a fresh staging dir, WITHOUT touching the final tool dir.
        /// Returns a <see cref="StagedDownload"/> on success (caller commits then
        /// disposes), or <c>null</c> + non-empty <paramref name="error"/> on
        /// failure (staging dir already cleaned up). NO final dir is mutated here.
        /// </summary>
        public static StagedDownload Stage(ResourceId id, string baseDir, Action<string> progress,
            out string error, DownloadStep downloadStep = null)
        {
            error = "";
            DownloadSpec spec;
            if (!s_specs.TryGetValue(id, out spec))
            {
                error = R.Error("Unknown download resource: {0}", id);
                return null;
            }

            if (downloadStep == null) downloadStep = U.HttpDownloadFile;

            string finalDir = Path.Combine(baseDir, "app", spec.AppSubDir);
            string stagingDir = Path.Combine(Path.GetTempPath(),
                "febgba_dl_" + Guid.NewGuid().ToString("N"));
            bool ok = false;
            try
            {
                Directory.CreateDirectory(stagingDir);

                progress?.Invoke(R._("Downloading... {0}", spec.Url));

                string urlFilename = GetUrlFilename(spec.Url);
                string ext = Path.GetExtension(urlFilename);

                // ---- Single-file (.exe) path ----
                if (spec.IsSingleFile || IsExeExtension(ext))
                {
                    string stagedExe = Path.Combine(stagingDir, spec.MatchGlob);
                    if (!downloadStep(spec.Url, stagedExe, out error, spec.Referer))
                    {
                        if (string.IsNullOrEmpty(error))
                            error = R.Error("Download failed.\r\nURL:{0}", spec.Url);
                        return null;
                    }
                    if (!File.Exists(stagedExe))
                    {
                        error = R.Error("Download did not produce the expected file.\r\nURL:{0}\r\nfile:{1}",
                            spec.Url, spec.MatchGlob);
                        return null;
                    }
                    ok = true;
                    return new StagedDownload
                    {
                        Spec = spec, StagingDir = stagingDir, StagedExe = stagedExe,
                        FinalDir = finalDir, CopyWholeDir = null, PlaceFilename = spec.MatchGlob,
                    };
                }

                // ---- Archive path ----
                string archiveExt = string.IsNullOrEmpty(ext) ? ".zip" : ext;
                string stagedArchive = Path.Combine(stagingDir, "download" + archiveExt);
                if (!downloadStep(spec.Url, stagedArchive, out error, spec.Referer))
                {
                    if (string.IsNullOrEmpty(error))
                        error = R.Error("Download failed.\r\nURL:{0}", spec.Url);
                    return null;
                }
                if (!File.Exists(stagedArchive))
                {
                    error = R.Error("Download failed.\r\nURL:{0}", spec.Url);
                    return null;
                }

                progress?.Invoke(R._("Extracting..."));
                string extractDir = Path.Combine(stagingDir, "extract");
                Directory.CreateDirectory(extractDir);
                string extractError = ArchSevenZip.Extract(stagedArchive, extractDir, isHide: true);
                if (!string.IsNullOrEmpty(extractError))
                {
                    error = R.Error("Could not extract the downloaded file.\r\nURL:{0}\r\nfindEXE:{1}\r\n{2}",
                        spec.Url, spec.MatchGlob, extractError);
                    return null;
                }

                progress?.Invoke(R._("Locating {0}...", spec.MatchGlob));
                string locatedExe = GrepFile(extractDir, spec.MatchGlob, ref error);
                if (locatedExe == null)
                {
                    return null; // error already set by GrepFile
                }

                ok = true;
                return new StagedDownload
                {
                    Spec = spec, StagingDir = stagingDir, StagedExe = locatedExe,
                    FinalDir = finalDir, CopyWholeDir = extractDir,
                    PlaceFilename = Path.GetFileName(locatedExe),
                };
            }
            catch (Exception e)
            {
                error = R.Error("Could not open the downloaded file.\r\nURL:{0}\r\nfindEXE:{1}\r\n{2}",
                    spec.Url, spec.MatchGlob, e.Message);
                return null;
            }
            finally
            {
                // On failure, remove the staging dir now (the caller never sees a
                // StagedDownload to dispose). On success the caller owns disposal.
                if (!ok)
                {
                    try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
                    catch { /* best-effort cleanup */ }
                }
            }
        }

        /// <summary>
        /// A reversible commit (Copilot #1102 re-review): the prior install (if
        /// any) was renamed aside to a backup dir, and the new install was swapped
        /// into the final dir. The commit is NOT durable until <see cref="Confirm"/>
        /// deletes the backup; <see cref="Rollback"/> restores the prior install
        /// byte-identical (and removes the just-placed new install). Used by the
        /// BUNDLE path so that if a LATER member's commit fails, EVERY earlier
        /// member's commit is rolled back — true all-or-none at commit time too.
        /// </summary>
        public sealed class CommitToken
        {
            internal string FinalDir { get; init; }
            internal string BackupDir { get; init; }   // null when there was no prior install
            bool _settled;

            /// <summary>Make the commit durable: delete the prior-install backup.</summary>
            public void Confirm()
            {
                if (_settled) return;
                _settled = true;
                if (BackupDir != null)
                    TryDeleteDir(BackupDir);
            }

            /// <summary>
            /// Undo the commit: remove the new install and restore the prior one.
            /// </summary>
            public void Rollback()
            {
                if (_settled) return;
                _settled = true;
                TryDeleteDir(FinalDir);
                if (BackupDir != null && Directory.Exists(BackupDir))
                {
                    try { Directory.Move(BackupDir, FinalDir); }
                    catch { /* best-effort restore */ }
                }
            }
        }

        /// <summary>The outcome of a <see cref="Commit"/> / <see cref="CommitBundle"/> step.</summary>
        public readonly struct CommitResult
        {
            public string Path { get; }
            public string Error { get; }
            internal CommitToken Token { get; }
            public bool Success => Path != null;
            internal CommitResult(string path, string error, CommitToken token)
            { Path = path; Error = error ?? ""; Token = token; }
        }

        /// <summary>
        /// Phase 2: place an already-staged + validated download into its final
        /// tool dir via the exception-safe swap in <see cref="PlaceFile"/>. Returns the
        /// resolved final exe path, or <c>null</c> + non-empty <paramref name="error"/>.
        /// On success the prior install (if any) is auto-confirmed (backup deleted).
        /// Does NOT dispose the staging dir (the caller owns that). For
        /// transactional multi-resource installs use <see cref="CommitBundle"/>.
        /// </summary>
        public static string Commit(StagedDownload staged, ref string error)
        {
            if (staged == null)
            {
                error = R.Error("Nothing to install.");
                return null;
            }
            CommitToken token;
            string placed = PlaceFile(staged.StagedExe, staged.FinalDir, staged.PlaceFilename,
                ref error, out token, copyWholeDir: staged.CopyWholeDir);
            // Single-resource commit is durable immediately.
            token?.Confirm();
            return placed;
        }

        /// <summary>
        /// Transactional bundle commit (Copilot #1102 re-review): commit EVERY
        /// staged member, and only if ALL succeed are the commits confirmed
        /// (backups deleted). If ANY member's commit fails, EVERY already-committed
        /// member is rolled back (its prior install restored byte-identical, its
        /// new install removed), so the bundle leaves NO partial install. Returns
        /// the resolved final exe paths (one per member, in order) on full success,
        /// or <c>null</c> + non-empty <paramref name="error"/> on any failure.
        /// </summary>
        public static string[] CommitBundle(IReadOnlyList<StagedDownload> staged, ref string error)
        {
            if (staged == null || staged.Count == 0)
            {
                error = R.Error("Nothing to install.");
                return null;
            }

            var tokens = new List<CommitToken>(staged.Count);
            var paths = new string[staged.Count];
            try
            {
                for (int i = 0; i < staged.Count; i++)
                {
                    if (staged[i] == null)
                    {
                        error = R.Error("Nothing to install.");
                        RollbackAll(tokens);
                        return null;
                    }
                    string placed = PlaceFile(staged[i].StagedExe, staged[i].FinalDir,
                        staged[i].PlaceFilename, ref error, out CommitToken token,
                        copyWholeDir: staged[i].CopyWholeDir);
                    if (token != null)
                        tokens.Add(token);
                    if (placed == null)
                    {
                        // Later member failed -> undo every prior committed member.
                        RollbackAll(tokens);
                        return null;
                    }
                    paths[i] = placed;
                }

                // All committed OK -> make every commit durable.
                foreach (var t in tokens)
                    t.Confirm();
                return paths;
            }
            catch (Exception e)
            {
                error = R.Error("Could not place the downloaded files.\r\n{0}", e.Message);
                RollbackAll(tokens);
                return null;
            }
        }

        static void RollbackAll(List<CommitToken> tokens)
        {
            // Reverse order so the most-recent swap unwinds first.
            for (int i = tokens.Count - 1; i >= 0; i--)
                tokens[i].Rollback();
        }

        /// <summary>
        /// Result of <see cref="DownloadGitAsync"/>: the resolved git executable
        /// path on success (with <see cref="Error"/> = ""), or <c>null</c>
        /// <see cref="Path"/> + a NON-EMPTY <see cref="Error"/> on any failure.
        /// Returned by value so the outcome travels with the awaited Task and is
        /// NOT subject to thread-hopping after an <c>await</c> (Copilot #1102
        /// finding 3 — replaces the previous <c>[ThreadStatic]</c> error slot).
        /// </summary>
        public readonly struct GitInstallResult
        {
            public string Path { get; }
            public string Error { get; }
            public bool Success => Path != null;
            public GitInstallResult(string path, string error) { Path = path; Error = error ?? ""; }
        }

        /// <summary>
        /// Git auto-download/install. Resolves the latest installer URL, downloads
        /// it to a temp file, runs it silently (UAC-elevating on Windows), then
        /// discovers the installed git executable. Returns a <see cref="GitInstallResult"/>
        /// — git path + "" error on success, or null path + a NON-EMPTY error on any
        /// failure. The temp installer is always deleted. NO config is written here.
        ///
        /// All three external steps are injectable so tests exercise the
        /// plumbing without a real GitHub call or installer launch:
        ///   <paramref name="getInstallerUrl"/> defaults to GitInstaller.GetLatestInstallerUrl,
        ///   <paramref name="downloadStep"/>     defaults to U.HttpDownloadFile,
        ///   <paramref name="runInstaller"/>     defaults to GitInstaller.RunInstallerSilentlyAsync,
        ///   <paramref name="findGit"/>          defaults to GitUtil.FindGitExecutable.
        /// </summary>
        public static async Task<GitInstallResult> DownloadGitAsync(Action<string> progress,
            Func<string> getInstallerUrl = null,
            DownloadStep downloadStep = null,
            Func<string, Task<bool>> runInstaller = null,
            Func<string> findGit = null)
        {
            getInstallerUrl ??= GitInstaller.GetLatestInstallerUrl;
            if (downloadStep == null) downloadStep = U.HttpDownloadFile;
            runInstaller ??= GitInstaller.RunInstallerSilentlyAsync;
            findGit ??= GitUtil.FindGitExecutable;

            progress?.Invoke(R._("Resolving the Git installer download URL..."));
            string installerUrl = getInstallerUrl();
            if (string.IsNullOrEmpty(installerUrl))
            {
                return GitFail(R.Error(
                    "Could not resolve the Git installer download URL.\r\nPlease install manually from https://git-scm.com ."));
            }

            string tempInstaller = Path.Combine(Path.GetTempPath(),
                "febgba_git_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                progress?.Invoke(R._("Downloading the Git installer... {0}", installerUrl));
                string dlError;
                if (!downloadStep(installerUrl, tempInstaller, out dlError, "") || !File.Exists(tempInstaller))
                {
                    return GitFail(R.Error("Failed to download the Git installer.\r\n{0}",
                        string.IsNullOrEmpty(dlError) ? installerUrl : dlError));
                }

                progress?.Invoke(R._("Installing Git... (this can take a while)"));
                bool installed = await runInstaller(tempInstaller);
                if (!installed)
                {
                    return GitFail(R.Error("Git installation failed."));
                }

                progress?.Invoke(R._("Git installation complete."));
                string gitPath = findGit();
                if (string.IsNullOrEmpty(gitPath))
                {
                    return GitFail(R.Error(
                        "Git was installed but its executable could not be found.\r\nPlease set the path manually via Browse."));
                }
                return new GitInstallResult(gitPath, "");
            }
            catch (Exception e)
            {
                return GitFail(R.Error("Git installation failed.\r\n{0}", e.Message));
            }
            finally
            {
                try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); }
                catch { /* best-effort cleanup */ }
            }
        }

        static GitInstallResult GitFail(string error) => new GitInstallResult(null, error);

        // ------------------------------------------------------------------
        // Internal helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// Recursively search <paramref name="dir"/> for the first file matching
        /// <paramref name="glob"/>. Returns the path or null + a non-empty error.
        /// Mirrors WF ToolInitWizardForm.GrepFile.
        /// </summary>
        static string GrepFile(string dir, string glob, ref string error)
        {
            string[] hits = U.Directory_GetFiles_Safe(dir, glob, SearchOption.AllDirectories);
            if (hits.Length <= 0)
            {
                error = R.Error("The downloaded file does not contain the expected file.\r\nPATH:{0}\r\nfindEXE:{1}",
                    dir, glob);
                return null;
            }
            return hits[0];
        }

        /// <summary>
        /// Copy the validated staged executable into the final tool dir. For the
        /// archive path the WHOLE extracted tree is copied (so DLLs / runtime
        /// assets next to the exe travel with it), and the returned path points
        /// at the exe inside the final dir. Single-file copies just the exe.
        ///
        /// Exception-safe swap (Copilot #1102 finding 1): the new install is first
        /// built in a SIBLING staging dir (<c>{finalDir}.new-{guid}</c>). Only
        /// after that copy fully succeeds is the prior <paramref name="finalDir"/>
        /// renamed aside (the <c>.old-{guid}</c> backup), the staging dir renamed
        /// INTO place, and — on Confirm — the backup deleted. If anything before
        /// the swap throws, the prior working install is left byte-identical; if
        /// the rename-in throws, the prior install is restored. Note this guards
        /// against HANDLED failures only — it is NOT atomic against an abrupt
        /// process/power loss BETWEEN the two directory renames, which can leave
        /// <paramref name="finalDir"/> momentarily absent with the backup still on
        /// disk (recoverable, but not point-in-time atomic).
        /// </summary>
        static string PlaceFile(string stagedExe, string finalDir, string finalFilename,
            ref string error, out CommitToken token, string copyWholeDir = null)
        {
            token = null;
            string newDir = finalDir + ".new-" + Guid.NewGuid().ToString("N");
            string backupDir = finalDir + ".old-" + Guid.NewGuid().ToString("N");
            try
            {
                // 1) Build the complete new install in a sibling dir. Nothing
                //    touches finalDir yet, so a failure here leaves the prior
                //    install intact.
                Directory.CreateDirectory(newDir);

                string relExe; // exe path relative to the new dir root
                if (copyWholeDir != null)
                {
                    CopyDirectory(copyWholeDir, newDir);
                    relExe = Path.GetRelativePath(copyWholeDir, stagedExe);
                    string stagedPlaced = Path.Combine(newDir, relExe);
                    if (!File.Exists(stagedPlaced))
                    {
                        // Fallback: re-locate by filename within the new dir.
                        string[] hits = U.Directory_GetFiles_Safe(newDir,
                            Path.GetFileName(stagedExe), SearchOption.AllDirectories);
                        if (hits.Length <= 0)
                        {
                            error = R.Error("The install directory is missing the expected file.\r\nPATH:{0}\r\nfile:{1}",
                                finalDir, Path.GetFileName(stagedExe));
                            TryDeleteDir(newDir);
                            return null;
                        }
                        relExe = Path.GetRelativePath(newDir, hits[0]);
                    }
                }
                else
                {
                    string destDir = Path.GetDirectoryName(finalFilename);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(Path.Combine(newDir, destDir));
                    File.Copy(stagedExe, Path.Combine(newDir, finalFilename), overwrite: true);
                    relExe = finalFilename;
                }

                // 2) Atomically swap newDir into finalDir, preserving the prior
                //    install in the backup dir. The backup is NOT deleted here —
                //    ownership passes to the CommitToken so the caller (a bundle)
                //    can Rollback a later sibling's failure. Confirm() deletes it.
                bool hadPrior = Directory.Exists(finalDir);
                if (hadPrior)
                    Directory.Move(finalDir, backupDir);
                try
                {
                    Directory.Move(newDir, finalDir);
                }
                catch
                {
                    // Swap-in failed: restore the prior install byte-identical.
                    if (hadPrior && Directory.Exists(backupDir))
                        Directory.Move(backupDir, finalDir);
                    throw;
                }

                string placedExe = Path.Combine(finalDir, relExe);
                if (!File.Exists(placedExe))
                {
                    // Roll the swap back so nothing partial is left behind.
                    TryDeleteDir(finalDir);
                    if (hadPrior && Directory.Exists(backupDir))
                        try { Directory.Move(backupDir, finalDir); } catch { /* best-effort */ }
                    error = R.Error("The install directory is missing the expected file.\r\nPATH:{0}\r\nfile:{1}",
                        finalDir, Path.GetFileName(stagedExe));
                    return null;
                }

                token = new CommitToken { FinalDir = finalDir, BackupDir = hadPrior ? backupDir : null };
                return placedExe;
            }
            catch (Exception e)
            {
                error = R.Error("Could not place the downloaded file.\r\nPATH:{0}\r\n{1}", finalDir, e.Message);
                TryDeleteDir(newDir);
                return null;
            }
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (string sub in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
            }
        }

        /// <summary>Filename portion of a URL (handles query strings like ?dl=1).</summary>
        static string GetUrlFilename(string url)
        {
            try
            {
                var uri = new Uri(url);
                string name = Path.GetFileName(uri.AbsolutePath);
                return Uri.UnescapeDataString(name);
            }
            catch
            {
                int q = url.IndexOf('?');
                string trimmed = q >= 0 ? url.Substring(0, q) : url;
                int slash = trimmed.LastIndexOf('/');
                return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            }
        }

        static bool IsExeExtension(string ext)
        {
            return string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
