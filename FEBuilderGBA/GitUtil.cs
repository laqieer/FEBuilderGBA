using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    public static class GitUtil
    {
        public const string Patch2RemoteUrl      = "https://github.com/laqieer/FEBuilderGBA-patch2.git";
        public const string Patch2RemoteUrlGitee = "https://gitee.com/laqieer/FEBuilderGBA-patch2.git";

        /// <summary>
        /// Returns the appropriate patch2 remote URL based on the user's release_source setting.
        /// Mirrors UseChinaMainlandMirror() logic: uses Gitee when release_source==2,
        /// or when release_source==0 (auto) and the UI language is Chinese.
        /// </summary>
        public static string GetPatch2RemoteUrl()
        {
            int releaseSource = OptionForm.release_source();
            string lang = OptionForm.lang();
            bool useGitee = (releaseSource == 2) || (releaseSource == 0 && lang == "zh");
            return useGitee ? Patch2RemoteUrlGitee : Patch2RemoteUrl;
        }

        /// <summary>
        /// Returns path to git exe, or null if not found.
        /// Priority: configured path → "git" on system PATH → common Windows install locations.
        /// </summary>
        public static string FindGitExecutable()
        {
            // 1. Check configured path (if it's not just the default "git" sentinel)
            string configured = OptionForm.git_path();
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
        /// A .git FILE means a submodule link ("gitdir: ../../.git/modules/…") —
        /// only valid when the referenced gitdir directory actually exists.
        /// Broken submodule links (installed builds, unpacked ZIPs) return false
        /// so the caller falls through to the fresh-clone path.
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
                    string content = File.ReadAllText(dotGit).Trim();
                    if (content.StartsWith("gitdir:"))
                    {
                        string rel = content.Substring("gitdir:".Length).Trim();
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
        /// If remoteUrl is provided the origin remote is updated first, allowing seamless
        /// switching between GitHub and Gitee without re-cloning.
        /// Returns exit code of the final step.
        /// </summary>
        public static int Update(string gitExe, string repoPath,
                                 Action<string> outputCallback = null,
                                 StringBuilder outputLog = null,
                                 string remoteUrl = null)
        {
            // Switch origin to the preferred source (GitHub ↔ Gitee) before fetching
            if (!string.IsNullOrEmpty(remoteUrl))
                RunGit(gitExe, string.Format("remote set-url origin \"{0}\"", remoteUrl), repoPath);

            int code = RunGit(gitExe, "fetch --progress --depth=1 origin", repoPath, outputCallback, outputLog);
            if (code != 0)
                return code;

            return RunGit(gitExe, "reset --hard FETCH_HEAD", repoPath, outputCallback, outputLog);
        }
    }
}
