﻿using System;
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
        public void Init(string version, string url)
        {
            this.Version = version;
            this.URL = url;

            this.Message.Text = string.Format(this.Message.Text, version, url);
        }


        private void AutoUpdateButton_Click(object sender, EventArgs e)
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
                    string r = ArchSevenZip.Extract(updateArchive, _update, isHide: false);
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
                if (U.GetFileSize(updateNewVersionFilename) < 2 * 1024 * 1024)
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
