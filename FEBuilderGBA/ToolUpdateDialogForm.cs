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

        public void Init(string version, string url)
        {
            this.Version = version;
            this.URL = url;

            this.Message.Text = string.Format(this.Message.Text, version, url);

            // Legacy mode: hide split-package buttons, keep the single auto-update button
            this.UpdateCoreButton.Visible = false;
            this.UpdatePatch2Button.Visible = false;
            this.AutoUpdateButton.Text = "全自動でアップデートします";
        }

        /// <summary>
        /// Initialize with split package support — shows individual Core / Patch2 / Full buttons.
        /// Buttons are stacked dynamically so there are no blank gaps.
        /// </summary>
        public void InitSplitPackage(UpdateInfo updateInfo)
        {
            this.UpdateInfoData = updateInfo;

            // --- stack update buttons from y=182 downward ---
            int y = 182;
            const int btnH   = 34;
            const int btnGap = 6;

            // Full / legacy button
            bool hasFull = !string.IsNullOrEmpty(updateInfo.URL_FULL);
            this.AutoUpdateButton.Visible  = hasFull;
            this.AutoUpdateButton.Enabled  = hasFull;
            this.AutoUpdateButton.Text     = "全部を更新します (Core + Patch2)";
            this.AutoUpdateButton.Location = new System.Drawing.Point(17, y);
            if (hasFull) y += btnH + btnGap;

            // Core-only button
            bool hasCore = !string.IsNullOrEmpty(updateInfo.URL_CORE);
            this.UpdateCoreButton.Visible  = hasCore;
            this.UpdateCoreButton.Enabled  = hasCore;
            this.UpdateCoreButton.Location = new System.Drawing.Point(17, y);
            if (hasCore) y += btnH + btnGap;

            // Patch2-only button
            bool hasPatch2 = !string.IsNullOrEmpty(updateInfo.URL_PATCH2);
            this.UpdatePatch2Button.Visible  = hasPatch2;
            this.UpdatePatch2Button.Enabled  = hasPatch2;
            this.UpdatePatch2Button.Location = new System.Drawing.Point(17, y);
            if (hasPatch2) y += btnH + btnGap;

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

            string remoteCore   = UpdateCheckSplitPackage.ExtractVersionFromUrl(updateInfo.URL_CORE   ?? updateInfo.URL_FULL, 0);
            string remotePatch2 = UpdateCheckSplitPackage.ExtractVersionFromUrl(updateInfo.URL_PATCH2 ?? updateInfo.URL_FULL, 1);

            sb.AppendLine($"プログラム本体: {updateInfo.VERSION_CORE} → {remoteCore}");
            sb.AppendLine($"パッチデータ:   {updateInfo.VERSION_PATCH2} → {remotePatch2}");

            return sb.ToString();
        }

        private void UpdateCoreButton_Click(object sender, EventArgs e)
        {
            this.URL = this.UpdateInfoData.URL_CORE;
            this.PackageType = UpdateInfo.PackageType.CoreOnly;
            AutoUpdateStandard(e);
        }

        private void UpdatePatch2Button_Click(object sender, EventArgs e)
        {
            this.URL = this.UpdateInfoData.URL_PATCH2;
            this.PackageType = UpdateInfo.PackageType.Patch2Only;
            AutoUpdatePatch2Only(e);
        }

        private void AutoUpdateButton_Click(object sender, EventArgs e)
        {
            if (this.UpdateInfoData != null)
            {
                // Split package mode: this button downloads the full package
                this.URL = this.UpdateInfoData.URL_FULL;
                this.PackageType = UpdateInfo.PackageType.Full;
                AutoUpdateStandard(e);
                return;
            }

            // Legacy single-package mode
            AutoUpdateStandard(e);
        }

        /// <summary>
        /// Handle PATCH2-only updates (no application restart needed)
        /// </summary>
        private void AutoUpdatePatch2Only(EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {
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

            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                string ext = OptionForm.update_source() == 1 ? ".zip" : ".7z";
                string updateArchive = Path.Combine(Program.BaseDirectory, "dltemp_patch2_" + DateTime.Now.Ticks.ToString() + ext);

                // Download
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

                if (!File.Exists(updateArchive))
                {
                    BrokenDownload(R._("ダウンロードしたファイルがありません。"));
                    this.Close();
                    return;
                }

                pleaseWait.DoEvents("Extract...");

                // Extract PATCH2 package to temporary directory first
                string tempExtractPath = Path.Combine(Program.BaseDirectory, "_temp_patch2_extract");
                U.mkdir(tempExtractPath);

                try
                {
                    string r = ArchSevenZip.Extract(updateArchive, tempExtractPath, isHide: false,
                        (current, total, file, elapsed, remaining) =>
                        {
                            string progress = string.Format("Extract... ({0}/{1}) - {2}",
                                current, total, file);
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

                pleaseWait.DoEvents("Installing patch data...");

                // Copy extracted files to config/patch2/
                try
                {
                    // upload-artifact@v4 may preserve full workspace-relative paths
                    // (config/patch2/FE6/...) or strip the directory prefix (FE6/...
                    // directly at extract root). Search all likely locations.
                    string sourcePatch2 = FindPatch2Source(tempExtractPath);
                    string targetPatch2 = Path.Combine(Program.BaseDirectory, "config", "patch2");

                    if (sourcePatch2 == null)
                    {
                        BrokenDownload(R._("パッチデータが見つかりませんでした。"));
                        this.Close();
                        return;
                    }

                    // Backup current patch2 directory
                    string backupPath = Path.Combine(Program.BaseDirectory, "config", "_patch2_backup_" + DateTime.Now.Ticks);
                    if (Directory.Exists(targetPatch2))
                    {
                        Directory.Move(targetPatch2, backupPath);
                    }

                    // Copy new patch2 data
                    U.DirectoryCopy(sourcePatch2, targetPatch2, true);

                    // Clean up backup after successful copy
                    if (Directory.Exists(backupPath))
                    {
                        Directory.Delete(backupPath, true);
                    }

                    // Clean up temp directory
                    Directory.Delete(tempExtractPath, true);

                    // Delete download archive
                    File.Delete(updateArchive);

                    R.ShowOK("パッチデータの更新が完了しました。\r\n変更を反映するには、アプリケーションを再起動してください。");
                }
                catch (Exception ee)
                {
                    BrokenDownload(R._("パッチデータのインストール中にエラーが発生しました。\r\n{0}", ee.ToString()));
                    this.Close();
                    return;
                }
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// Locates the patch2 data root inside an extraction directory.
        /// Handles three possible upload-artifact@v4 path structures:
        ///   1. Full workspace-relative path preserved: extractRoot/config/patch2/FE6/...
        ///   2. Recursive any-depth match: extractRoot/.../patch2/FE6/...
        ///   3. Prefix stripped: extractRoot/FE6/... (config/patch2/ stripped)
        /// Returns null if patch2 data cannot be found.
        /// </summary>
        private static string FindPatch2Source(string extractRoot)
        {
            // Case 1: upload-artifact preserved full workspace path
            string direct = Path.Combine(extractRoot, "config", "patch2");
            if (Directory.Exists(direct) && Directory.GetFileSystemEntries(direct).Length > 0)
                return direct;

            // Case 2: search recursively for any directory named "patch2"
            try
            {
                string[] candidates = Directory.GetDirectories(extractRoot, "patch2", SearchOption.AllDirectories);
                if (candidates.Length > 0 && Directory.GetFileSystemEntries(candidates[0]).Length > 0)
                    return candidates[0];
            }
            catch { }

            // Case 3: upload-artifact stripped config/patch2/ prefix;
            // ROM-version subdirs (FE6, FE7J, …) land directly at extractRoot
            string[] romVersions = { "FE6", "FE7J", "FE7U", "FE8J", "FE8U" };
            bool looksLikePatch2Root = Array.Exists(romVersions,
                v => Directory.Exists(Path.Combine(extractRoot, v)));
            if (looksLikePatch2Root)
                return extractRoot;

            return null;
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
