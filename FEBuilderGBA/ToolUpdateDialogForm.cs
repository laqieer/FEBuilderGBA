using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class ToolUpdateDialogForm : Form
    {
        public ToolUpdateDialogForm()
        {
            InitializeComponent();
        }

        private void UpdateDialog_Load(object sender, EventArgs e)
        {
            FormIcon.Image = SystemIcons.Question.ToBitmap();
        }

        string Version;
        string URL;
        UpdateInfo UpdateInfoData;
        UpdateInfo.PackageType PackageType;
        private string _gitExe = null;  // cached in InitSplitPackage

        public void Init(string version, string url)
        {
            this.Version = version;
            this.URL = url;

            this.Message.Text = string.Format(this.Message.Text, version, url);

            // Legacy mode: hide split-package buttons, keep the single auto-update button
            this.UpdateCoreButton.Visible = false;
            this.UpdatePatch2GitButton.Visible = false;
            this.AutoUpdateButton.Text = "全自動でアップデートします";
        }

        /// <summary>
        /// Initialize with split package support — shows individual Core / Patch2 / Full buttons.
        /// When git is available, the Git Patch2 button replaces the zip Patch2 button and
        /// the Full button is hidden (Core + Git Patch2 covers it).
        /// Buttons are stacked dynamically so there are no blank gaps.
        /// </summary>
        public void InitSplitPackage(UpdateInfo updateInfo)
        {
            this.UpdateInfoData = updateInfo;

            // Detect git once and cache it
            _gitExe = GitUtil.FindGitExecutable();
            bool useGit = (_gitExe != null);

            // Always hide the legacy Full button in split-package mode
            this.AutoUpdateButton.Visible = false;
            this.AutoUpdateButton.Enabled = false;

            // --- stack update buttons from y=182 downward ---
            int y = 182;
            const int btnH   = 34;
            const int btnGap = 6;

            // Core-only button — always shown
            bool hasCore = !string.IsNullOrEmpty(updateInfo.URL_CORE);
            this.UpdateCoreButton.Visible  = hasCore;
            this.UpdateCoreButton.Enabled  = hasCore;
            this.UpdateCoreButton.Location = new System.Drawing.Point(17, y);
            if (hasCore) y += btnH + btnGap;

            // Git Patch2 button — shown when git is found
            this.UpdatePatch2GitButton.Visible  = useGit;
            this.UpdatePatch2GitButton.Enabled  = useGit;
            this.UpdatePatch2GitButton.Location = new System.Drawing.Point(17, y);
            if (useGit) y += btnH + btnGap;

            // Push OpenBrowser and Ignore below all visible update buttons
            // (keep at least the original gap from y=182 so layout isn't cramped)
            int openY   = Math.Max(234, y);
            int ignoreY = openY + btnH + btnGap;
            this.OpenBrowserButton.Location = new System.Drawing.Point(17, openY);
            this.IgnoreButton.Location      = new System.Drawing.Point(17, ignoreY);

            // Resize panel and form to fit without extra whitespace
            int panelH  = ignoreY + btnH + 22;   // 22 px bottom padding
            this.panel1.Size    = new System.Drawing.Size(879, panelH);
            this.ClientSize     = new System.Drawing.Size(904, panelH + 26); // 13 top + 13 bottom

            // Set URL for OpenBrowser to the most relevant package
            UpdateInfo.PackageType pt;
            this.URL         = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out pt);
            this.PackageType = pt;

            this.Message.Text = BuildUpdateMessage(updateInfo);
        }

        private string BuildUpdateMessage(UpdateInfo updateInfo)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("アップデートが利用可能です:");
            sb.AppendLine();

            string remoteCore = UpdateCheckSplitPackage.ExtractVersionFromUrl(updateInfo.URL_CORE);
            sb.AppendLine($"プログラム本体: {updateInfo.VERSION_CORE} → {remoteCore}");

            return sb.ToString();
        }

        private void UpdateCoreButton_Click(object sender, EventArgs e)
        {
            this.URL = this.UpdateInfoData.URL_CORE;
            this.PackageType = UpdateInfo.PackageType.CoreOnly;
            AutoUpdateStandard(e);
        }

        private void AutoUpdateButton_Click(object sender, EventArgs e)
        {
            // Legacy single-package mode (Init(), not InitSplitPackage())
            AutoUpdateStandard(e);
        }

        private void UpdatePatch2GitButton_Click(object sender, EventArgs e)
            => AutoUpdatePatch2Git(e);

        /// <summary>
        /// Update patch2 data via git clone (first time) or git fetch+reset (subsequent).
        /// Git runs on a background Task; the main thread polls every 80 ms so the
        /// please-wait label shows real-time git progress output.
        /// No application restart is needed — the data is reloaded on next launch.
        /// </summary>
        private void AutoUpdatePatch2Git(EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {
                return;
            }

            // Resolve git executable — auto-install if not found
            string gitExe = GitUtil.FindGitExecutable();
            if (gitExe == null)
            {
                gitExe = TryAutoInstallGit();
                if (gitExe == null)
                    return;
            }

            // Unsaved ROM check
            if (Program.ROM != null && Program.ROM.Modified)
            {
                DialogResult dr = R.ShowQ("未保存の変更があるようです。\r\n保存してもよろしいですか？");
                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    MainFormUtil.SaveForce(Program.MainForm());
                }
                else if (dr == System.Windows.Forms.DialogResult.Cancel)
                {
                    return;
                }
            }

            string patchPath = Path.Combine(Program.BaseDirectory, "config", "patch2");

            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                var gitLog = new System.Text.StringBuilder();

                // lastLine[0] is written by the git output callback (background thread)
                // and consumed by the polling loop below (main thread) via Interlocked.
                var lastLine = new string[1];
                Action<string> progress = line =>
                {
                    if (!string.IsNullOrEmpty(line))
                        System.Threading.Interlocked.Exchange(ref lastLine[0], line);
                };

                string patch2RemoteUrl = GitUtil.GetPatch2RemoteUrl();

                if (GitUtil.IsGitRepo(patchPath))
                {
                    // UPDATE PATH: run git on background task, poll for progress on main thread
                    pleaseWait.DoEvents("Git: fetch --progress --depth=1 origin ...");
                    var task = System.Threading.Tasks.Task.Run(
                        () => GitUtil.Update(gitExe, patchPath, progress, gitLog, patch2RemoteUrl));
                    PollGitProgress(task, pleaseWait, lastLine);

                    if (task.Result != 0)
                    {
                        R.ShowStopError("Gitによる更新に失敗しました。\r\n終了コード: {0}\r\n\r\n{1}",
                            task.Result, gitLog.ToString().Trim());
                        this.Close();
                        return;
                    }
                }
                else
                {
                    // FIRST-TIME PATH: back up existing dir, then clone
                    string backupPath = null;
                    if (Directory.Exists(patchPath))
                    {
                        backupPath = Path.Combine(Program.BaseDirectory, "config",
                            "_patch2_backup_" + DateTime.Now.Ticks.ToString());
                        pleaseWait.DoEvents("Backing up existing patch2 data...");
                        Directory.Move(patchPath, backupPath);
                    }

                    pleaseWait.DoEvents("Git: clone --progress --depth=1 ...");
                    var task = System.Threading.Tasks.Task.Run(
                        () => GitUtil.Clone(gitExe, patch2RemoteUrl, patchPath, progress, gitLog));
                    PollGitProgress(task, pleaseWait, lastLine);

                    if (task.Result != 0)
                    {
                        // Restore backup on failure
                        if (backupPath != null)
                        {
                            if (Directory.Exists(patchPath))
                                Directory.Delete(patchPath, true);
                            Directory.Move(backupPath, patchPath);
                        }
                        R.ShowStopError("Gitによるクローンに失敗しました。\r\n終了コード: {0}\r\n\r\n{1}",
                            task.Result, gitLog.ToString().Trim());
                        this.Close();
                        return;
                    }

                    // Success — remove backup
                    if (backupPath != null)
                    {
                        try { Directory.Delete(backupPath, true); }
                        catch { }
                    }
                }
            }

            R.ShowOK("パッチデータの更新が完了しました。\r\n変更を反映するには再起動してください。");
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// Pumps the UI message loop while a background git Task is running,
        /// showing each new output line in the please-wait label as it arrives.
        /// lastLine[0] is written atomically by the git output callback and consumed here.
        /// </summary>
        private static void PollGitProgress(System.Threading.Tasks.Task task,
                                            InputFormRef.AutoPleaseWait pleaseWait,
                                            string[] lastLine)
        {
            while (!task.IsCompleted)
            {
                string line = System.Threading.Interlocked.Exchange(ref lastLine[0], null);
                if (!string.IsNullOrEmpty(line))
                    pleaseWait.DoEvents(line);
                else
                    System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(80);
            }
            // Flush any final line that arrived just before the task completed
            string last = System.Threading.Interlocked.Exchange(ref lastLine[0], null);
            if (!string.IsNullOrEmpty(last))
                pleaseWait.DoEvents(last);
        }

        /// <summary>
        /// Prompts the user to auto-install Git, downloads the installer, and runs it silently.
        /// Returns the path to git.exe on success, or null if the user declines / install fails.
        /// On failure, offers to open https://git-scm.com for manual installation.
        /// </summary>
        private string TryAutoInstallGit()
        {
            // Three-way choice: Yes = auto-install, No = open browser, Cancel = do nothing
            DialogResult choice = MessageBox.Show(
                "Gitが見つかりません。\r\n" +
                "パッチデータの更新にはGitが必要です。\r\n\r\n" +
                "自動でGitをダウンロード・インストールしますか？\r\n" +
                "(「いいえ」を選択するとブラウザでダウンロードページを開きます)",
                "Gitが必要です",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
                return null;

            if (choice == DialogResult.No)
            {
                U.OpenURLOrFile("https://git-scm.com");
                return null;
            }

            // Auto-install chosen
            string gitExe = TryAutoInstallGitInternal();
            if (gitExe != null)
            {
                R.ShowOK("Gitのインストールが完了しました。\r\n引き続きパッチデータの更新を行います。");
                return gitExe;
            }

            // Auto-install failed — guide to manual install
            DialogResult dr = R.ShowQ(
                "Gitの自動インストールに失敗しました。\r\n" +
                "https://git-scm.com から手動でインストールしてください。\r\n\r\n" +
                "ダウンロードページをブラウザで開きますか？");
            if (dr == DialogResult.Yes)
                U.OpenURLOrFile("https://git-scm.com");
            return null;
        }

        /// <summary>
        /// Core logic for auto-installing Git:
        ///   1. Fetch the latest installer URL from the GitHub releases API.
        ///   2. Download the installer (with progress in the please-wait label).
        ///   3. Run the installer silently (UAC prompt may appear).
        ///   4. Locate the newly installed git.exe.
        /// Returns the git.exe path on success, or null on any failure.
        /// </summary>
        private string TryAutoInstallGitInternal()
        {
            string installerPath = null;
            try
            {
                // Step 1 + 2: fetch URL and download installer
                string installerUrl = null;
                using (InputFormRef.AutoPleaseWait pw = new InputFormRef.AutoPleaseWait(this))
                {
                    pw.DoEvents("Gitインストーラーの最新バージョンを確認中...");
                    installerUrl = GitInstaller.GetLatestInstallerUrl();
                    if (string.IsNullOrEmpty(installerUrl))
                        return null;

                    installerPath = Path.Combine(Path.GetTempPath(),
                        "git_installer_" + DateTime.Now.Ticks.ToString() + ".exe");

                    pw.DoEvents("Gitインストーラーをダウンロード中...");
                    U.DownloadFile(installerPath, installerUrl, pw);
                }

                if (!File.Exists(installerPath))
                    return null;

                // Step 3: run installer (outside of please-wait — UAC prompt will appear)
                using (InputFormRef.AutoPleaseWait pw = new InputFormRef.AutoPleaseWait(this))
                {
                    pw.DoEvents("Gitをインストール中... しばらくお待ちください");
                    var task = GitInstaller.RunInstallerSilentlyAsync(installerPath);
                    while (!task.IsCompleted)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        System.Threading.Thread.Sleep(200);
                    }
                    if (!task.Result)
                        return null;
                }

                // Step 4: locate the newly installed git.exe
                return GitUtil.FindGitExecutable();
            }
            catch
            {
                return null;
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(installerPath)) File.Delete(installerPath); }
                catch { }
            }
        }

        /// <summary>
        /// Standard update flow (FULL/CORE packages or legacy)
        /// </summary>
        private void AutoUpdateStandard(EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {//2重割り込み禁止
                return;
            }

            if (Program.ROM != null && Program.ROM.Modified)
            {
                DialogResult dr = R.ShowQ("未保存の変更があるようです。\r\n保存してもよろしいですか？");
                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    MainFormUtil.SaveForce(Program.MainForm());
                }
                else if (dr == System.Windows.Forms.DialogResult.Cancel)
                {
                    return;
                }
            }

            //少し時間がかかるので、しばらくお待ちください表示.
            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                //実行中のファイルは上書きできないので、アップデーターに処理を引き継がなくてはいけない。

                string updater_org_txt = System.IO.Path.Combine(Program.BaseDirectory, "config", "data", "updater.bat.txt");
                string updater = Path.Combine(Program.BaseDirectory, "updater.bat");
                if (!File.Exists(updater_org_txt))
                {
                    BrokenDownload(R._("アップデーターのバッチファイルがありません。\r\n{0}",updater_org_txt));
                    this.Close();
                    return;
                }

                try
                {
                    File.Copy(updater_org_txt, updater, true);
                }
                catch (Exception ee)
                {
                    BrokenDownload(R._("アップデーターのバッチファイルをコピーできませんでした。\r\n{0}", ee.ToString()));
                    this.Close();
                    return;
                }

                if (!File.Exists(updater))
                {
                    BrokenDownload(R._("アップデーターのバッチファイルをコピーできませんでした。\r\n{0}", updater));
                    this.Close();
                    return;
                }

                string ext = ".7z";
                int func_update_source = OptionForm.update_source();
                if (func_update_source == 1)
                    ext = ".zip";
                string updateArchive = Path.Combine(Program.BaseDirectory, "dltemp_" + DateTime.Now.Ticks.ToString() + ext);

                //ダウンロード
                try
                {
                    U.DownloadFile(updateArchive, this.URL, pleaseWait);
                }
                catch (Exception ee)
                {
                    BrokenDownload(ee);
                    this.Close();
                    return;
                }
                if (! File.Exists(updateArchive))
                {
                    BrokenDownload(R._("ダウンロードしたはずのファイルがありません。"));
                    this.Close();
                    return;
                }
                if (U.GetFileSize(updateArchive) < 2 * 1024 * 1024)
                {
                    BrokenDownload(R._("ダウンロードしたファイルが小さすぎます。"));
                    this.Close();
                    return;
                }

                pleaseWait.DoEvents("Extract...");

                //解凍
                try
                {
                    string _update = Path.Combine(Program.BaseDirectory, "_update");
                    U.mkdir(_update);
                    string r = ArchSevenZip.Extract(updateArchive, _update, isHide: false,
                        (current, total, file, elapsed, remaining) =>
                        {
                            string progress;
                            if (total > 0)
                            {
                                progress = string.Format("Extract... ({0}/{1}) {2:0.0}% - {3}\r\nElapsed: {4:mm\\:ss} / Remaining: {5:mm\\:ss}",
                                    current, total,
                                    (double)current / total * 100,
                                    file,
                                    elapsed,
                                    remaining);
                            }
                            else
                            {
                                // Fast mode: no total count, just show current file
                                progress = string.Format("Extract... ({0} files) - {1}\r\nElapsed: {2:mm\\:ss}",
                                    current,
                                    file,
                                    elapsed);
                            }
                            pleaseWait.DoEvents(progress);
                        });
                    if (r != "")
                    {
                        BrokenDownload(R._("ダウンロードしたファイルを解凍できませんでした。") + "\r\n" + r);
                        this.Close();
                        return;
                    }
                }
                catch (Exception ee)
                {
                    BrokenDownload(ee);
                    this.Close();
                    return;
                }

                pleaseWait.DoEvents("Check...");
                string updateNewVersionFilename = Path.Combine(Program.BaseDirectory, "_update", "FEBuilderGBA.exe");
                if (!File.Exists(updateNewVersionFilename))
                {
                    BrokenDownload(R._("ダウンロードしたファイルを解凍した中に、実行ファイルがありませんでした。"));
                    this.Close();
                    return;
                }
                // With .NET SDK-style projects, FEBuilderGBA.exe is a small AppHost stub (~172KB).
                // The actual application code is in FEBuilderGBA.dll. Check that for the size guard.
                string updateNewVersionDllname = Path.Combine(Program.BaseDirectory, "_update", "FEBuilderGBA.dll");
                if (!File.Exists(updateNewVersionDllname) || U.GetFileSize(updateNewVersionDllname) < 2 * 1024 * 1024)
                {
                    BrokenDownload(R._("ダウンロードしたファイルを解凍した中にあった、実行ファイルが小さすぎます。"));
                    this.Close();
                    return;
                }

                pleaseWait.DoEvents("GO!");

                int pid = Process.GetCurrentProcess().Id;
                string args = pid.ToString();
                try
                {
                    Process p = new Process();
                    p.StartInfo.FileName = updater;
                    p.StartInfo.Arguments = args;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.WorkingDirectory = Program.BaseDirectory;
                    p.Start();

                    pleaseWait.DoEvents("Executed!");
                }
                catch (Exception ee)
                {
                    BrokenDownload(ee);
                    return;
                }
            }

            this.DialogResult = System.Windows.Forms.DialogResult.Abort;
            Application.Exit();
            this.Close();
        }
        static public bool CheckUpdateGarbage()
        {
            string updater = Path.Combine(Program.BaseDirectory, "updater.bat");
            string newexe = Path.Combine(Program.BaseDirectory, ".\\_update\\FEBuilderGBA.exe");

            if (File.Exists(updater) && File.Exists(newexe))
            {//アップデータがうまく実行されていない形跡がある.
                int pid = Process.GetCurrentProcess().Id;
                string args = pid.ToString();
                try
                {
                    Process p = new Process();
                    p.StartInfo.FileName = updater;
                    p.StartInfo.Arguments = args;
                    p.StartInfo.UseShellExecute = false;
                    p.Start();

                    System.Threading.Thread.Sleep(500);
                }
                catch (Exception)
                {
                    return false;
                }

                return true;
            }
            return false;
        }

        void BrokenDownload(Exception e)
        {
            BrokenDownload(e.ToString());
        }
        void BrokenDownload(string errormessage)
        {
            R.ShowStopError("エラーにより自動アップデートできませんでした。\r\n代わりにURLをブラウザで表示します。\r\n手動でダウンロードしてください。\r\n{0}", errormessage);
            OpenBrower();
        }

        private void OpenBrowserButton_Click(object sender, EventArgs e)
        {
            OpenBrower();
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void IgnoreButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }


        void OpenBrower()
        {
            U.OpenURLOrFile(this.URL);
        }
    }
}
