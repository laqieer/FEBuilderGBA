using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    public static class GitUtil
    {
        public const string Patch2RemoteUrl = "https://github.com/laqieer/FEBuilderGBA-patch2.git";

        /// <summary>
        /// Returns the patch2 remote URL: a user-configured custom URL when set,
        /// otherwise the default GitHub remote.
        /// </summary>
        public static string GetPatch2RemoteUrl()
        {
            // Check user-configured custom URL first
            string custom = CoreState.Config?.at("submodule_patch2_url", "");
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            return Patch2RemoteUrl;
        }

        /// <summary>Returns the FE-Repo remote URL: a user-configured custom URL when set, else the default (#1813).</summary>
        public static string GetFERepoRemoteUrl()
        {
            string custom = CoreState.Config?.at("submodule_fe_repo_url", "");
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;
            return FERepoDefaultUrl;
        }

        /// <summary>Returns the FE-Repo-Music (music) remote URL: a user-configured custom URL when set, else the default (#1813).</summary>
        public static string GetFERepoMusicRemoteUrl()
        {
            string custom = CoreState.Config?.at("submodule_fe_repo_music_url", "");
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;
            return FERepoMusicDefaultUrl;
        }

        /// <summary>Repo-root FE-Repo resource directory: <c>&lt;baseDir&gt;/resources/FE-Repo</c> (#1813).</summary>
        public static string GetFERepoDir(string baseDir)
            => Path.Combine(baseDir ?? "", "resources", "FE-Repo");

        /// <summary>Repo-root FE-Repo-Music resource directory: <c>&lt;baseDir&gt;/resources/FE-Repo-Music-No-Preview</c> (#1813).</summary>
        public static string GetFERepoMusicDir(string baseDir)
            => Path.Combine(baseDir ?? "", "resources", "FE-Repo-Music-No-Preview");

        /// <summary>
        /// Returns path to git exe, or null if not found.
        /// Priority: configured path → "git" on system PATH → common Windows install locations.
        /// </summary>
        public static string FindGitExecutable()
        {
            // 1. Check configured path (if it's not just the default "git" sentinel)
            string configured = CoreState.GitPath;
            if (!string.IsNullOrEmpty(configured) && configured != "git")
            {
                if (File.Exists(configured) && ProbeGit(configured))
                    return configured;
            }

            // 2. Try "git" on system PATH
            if (ProbeGit("git"))
                return "git";

            // 3. Common Windows install locations
            string[] candidates = {
                @"C:\Program Files\Git\cmd\git.exe",
                @"C:\Program Files (x86)\Git\cmd\git.exe",
                @"C:\Program Files\Git\bin\git.exe",
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate) && ProbeGit(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Returns true if "git --version" exits 0.
        /// </summary>
        public static bool ProbeGit(string gitExe)
        {
            try
            {
                int code = RunGit(gitExe, "--version", null);
                return code == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if path contains a usable git repository.
        /// A .git DIRECTORY means a proper standalone clone — always valid.
        /// A .git FILE means a submodule or linked-worktree pointer
        /// ("gitdir: ../../.git/modules/…" or an absolute worktree path) — only valid when
        /// its first line contains a non-empty <c>gitdir:</c> value and the referenced
        /// directory actually exists. Broken links and ordinary files named .git return false.
        /// </summary>
        public static bool IsGitRepo(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            string dotGit = Path.Combine(path, ".git");

            // Proper standalone repo created by "git clone"
            if (Directory.Exists(dotGit))
                return true;

            // Submodule link file — verify the referenced gitdir actually exists
            if (File.Exists(dotGit))
            {
                try
                {
                    string firstLine;
                    using (var reader = new StreamReader(dotGit))
                        firstLine = reader.ReadLine();
                    if (firstLine != null && firstLine.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        string rel = firstLine.Substring("gitdir:".Length).Trim();
                        if (rel.Length == 0)
                            return false;
                        string abs = Path.GetFullPath(Path.Combine(path, rel));
                        return Directory.Exists(abs);
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Runs git synchronously, captures stdout+stderr, returns exit code.
        /// outputCallback (nullable) is called for each output line on a background thread.
        /// outputLog (nullable) accumulates all output lines for error reporting.
        /// Pass null callbacks and use fixed DoEvents checkpoint messages for UI updates.
        /// </summary>
        public static int RunGit(string gitExe, string args, string workingDir,
                                 Action<string> outputCallback = null,
                                 StringBuilder outputLog = null)
        {
            Process p = new Process();
            p.StartInfo.FileName = gitExe;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;

            if (!string.IsNullOrEmpty(workingDir))
                p.StartInfo.WorkingDirectory = workingDir;

            // Prevent git from hanging on credential prompts (patch2 repo is public)
            p.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            p.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputCallback?.Invoke(e.Data);
                    if (outputLog != null) lock (outputLog) outputLog.AppendLine(e.Data);
                }
            };
            p.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputCallback?.Invoke(e.Data);
                    if (outputLog != null) lock (outputLog) outputLog.AppendLine(e.Data);
                }
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            int exitCode = p.ExitCode;
            p.Close();

            return exitCode;
        }

        /// <summary>
        /// git clone --progress --depth=1 &lt;url&gt; &lt;targetPath&gt;  (targetPath must not exist yet)
        /// --progress forces git to emit progress lines even when stderr is redirected.
        /// </summary>
        public static int Clone(string gitExe, string url, string targetPath,
                                Action<string> outputCallback = null,
                                StringBuilder outputLog = null)
        {
            string args = string.Format("clone --progress --depth=1 \"{0}\" \"{1}\"", url, targetPath);
            return RunGit(gitExe, args, null, outputCallback, outputLog);
        }

        /// <summary>
        /// git fetch --progress --depth=1 origin  +  git reset --hard FETCH_HEAD
        /// --progress forces git to emit progress lines even when stderr is redirected.
        /// If remoteUrl is provided the origin remote is updated first, allowing the
        /// custom-URL override to take effect without re-cloning.
        /// Returns exit code of the final step.
        /// </summary>
        public static int Update(string gitExe, string repoPath,
                                 Action<string> outputCallback = null,
                                 StringBuilder outputLog = null,
                                 string remoteUrl = null)
        {
            // Switch origin to the configured remote URL before fetching
            if (!string.IsNullOrEmpty(remoteUrl))
                RunGit(gitExe, string.Format("remote set-url origin \"{0}\"", remoteUrl), repoPath);

            int code = RunGit(gitExe, "fetch --progress --depth=1 origin", repoPath, outputCallback, outputLog);
            if (code != 0)
                return code;

            return RunGit(gitExe, "reset --hard FETCH_HEAD", repoPath, outputCallback, outputLog);
        }

        /// <summary>
        /// Default remote URLs for managed submodules.
        /// </summary>
        public const string FERepoDefaultUrl = "https://github.com/Klokinator/FE-Repo";
        public const string FERepoMusicDefaultUrl = "https://github.com/laqieer/FE-Repo-Music-No-Preview";

        /// <summary>
        /// Set the origin remote URL for a submodule directory.
        /// Returns true if successful.
        /// </summary>
        public static bool SetSubmoduleRemote(string submodulePath, string newUrl)
        {
            if (string.IsNullOrEmpty(submodulePath) || !IsGitRepo(submodulePath))
                return false;
            if (string.IsNullOrEmpty(newUrl))
                return false;

            string gitExe = FindGitExecutable();
            if (gitExe == null) return false;

            int code = RunGit(gitExe, $"remote set-url origin \"{newUrl}\"", submodulePath);
            return code == 0;
        }
    }
}
