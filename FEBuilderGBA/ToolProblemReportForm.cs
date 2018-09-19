﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class ToolProblemReportForm : Form
    {
        public ToolProblemReportForm()
        {
            InitializeComponent();
            InputFormRef.TabControlHideTabOption(MainTab);
            OrignalFilename.AllowDropFilename();
            U.AddCancelButton(this);

            this.Problem.Placeholder = R._("例: 主人公が戦闘しようとするとフリーズします。\r\n主人公以外のキャラクタでも発生します。\r\n武器や魔法を変更してもフリーズします。\r\n序章で、エイリークがグラド兵と戦闘するとフリーズを再現できます。\r\n(どういう問題があるか、どうしたら問題を再現できるか、できるだけ詳しい情報を書いてください)");
            this.URLTextBoxEx.Text = MainFormUtil.GetCommunitiesURL();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                this.FindBackup = new FindBackup();
                this.OrignalFilename.Text = FindBackup.OrignalFilename;
            }

            MainTab.SelectedTab = Step1Page;
        }

        private void Step1NextButton_Click(object sender, EventArgs e)
        {
            if (Problem.Text == "")
            {
                Problem.ErrorMessage = R._("問題点を記載してください。");
                return;
            }
            Problem.ErrorMessage = "";
            if (StableDate.Value > EmergenceDate.Value)
            {
                StableDateLabel.ErrorMessage = R._("正しく動作している時期が、問題が発生した時期より後になっています。");
                return;
            }
            StableDateLabel.ErrorMessage = "";
            MainTab.SelectedTab = Step2Page;
        }

        private void Step2PrevButton_Click(object sender, EventArgs e)
        {
            MainTab.SelectedTab = Step1Page;
        }

        private void Step2NextButton_Click(object sender, EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {//2重割り込み禁止
                return;
            }
            string errorMessage = MainFormUtil.CheckOrignalROM(OrignalFilename.Text);
            if (errorMessage != "")
            {
                R.ShowStopError("無改造ROMを指定してください。" + "\r\n" + errorMessage);
                OrignalFilename.ErrorMessage = R._("無改造ROMを指定してください。" + "\r\n" + errorMessage);
                return;
            }

            OrignalFilename.ErrorMessage = "";


            string title = R._("レポートを保存するファイル名を選択してください");
            string filter = R._("report.7z|*.report.7z|All files|*");
            SaveFileDialog save = new SaveFileDialog();
            save.Title = title;
            save.Filter = filter;
            save.AddExtension = true;
            Program.LastSelectedFilename.Load(this, "", save, MakeReportFilename());

            DialogResult dr = save.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return;
            }
            if (save.FileNames.Length <= 0 || !U.CanWriteFileRetry(save.FileNames[0]))
            {
                return;
            }

            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                string error = MakeReport(save.FileNames[0]);
                if (error != "")
                {
                    R.ShowStopError("レポートを作れませんでした。\r\n{0}", error);
                    return;
                }
            }
            MainTab.SelectedTab = EndPage;

            //エクスプローラで選択しよう
            U.SelectFileByExplorer(save.FileNames[0]);
        }

        private void EndButton_Click(object sender, EventArgs e)
        {
            if (this.OpenCommunities.Checked)
            {
                MainFormUtil.GotoCommunities();
            }
            this.Close();
        }

        FindBackup FindBackup;
        private void OrignalSelectButton_Click(object sender, EventArgs e)
        {
            string title = R._("無改造ROMを選択してください");
            string filter = R._("GBA ROMs|*.gba|Binary files|*.bin|All files|*");

            OpenFileDialog open = new OpenFileDialog();
            open.Title = title;
            open.Filter = filter;
            Program.LastSelectedFilename.Load(this, "", open);
            DialogResult dr = open.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return;
            }
            if (!U.CanReadFileRetry(open))
            {
                return;
            }

            Program.LastSelectedFilename.Save(this, "", open);
            OrignalFilename.Text = open.FileNames[0];
            OrignalFilename.ErrorMessage = "";
        }

        string MakeReportFilename()
        {
            string ups = "REPORT." + DateTime.Now.ToString("yyyyMMddHHmmss");
            string upsFullPath = U.MakeFilename(ups, ".report.7z");
            return upsFullPath;
        }

        enum FindOption
        {
            Forward
            ,Prev
            ,PrevPrev
        };

        string FindSrc(DateTime basedate, FindOption option, ref bool ref_hatena)
        {
            string pickup = "";
            if (option == FindOption.Forward)
            {
                for (int i = 0; i < this.FindBackup.Files.Count; i++)
                {
                    string filename = this.FindBackup.Files[i].FilePath;
                    DateTime date = this.FindBackup.Files[i].Date;
                    if (date < basedate)
                    {
                        if (pickup != "")
                        {
                            return pickup;
                        }
                    }
                    pickup = filename;
                }
            }
            else if (option == FindOption.Prev)
            {
                for (int i = 0; i < this.FindBackup.Files.Count; i++)
                {
                    string filename = this.FindBackup.Files[i].FilePath;
                    DateTime date = this.FindBackup.Files[i].Date;
                    if (date < basedate)
                    {
                        return filename;
                    }
                    pickup = filename;
                }
            }
            else if (option == FindOption.PrevPrev)
            {
                for (int i = 0; i < this.FindBackup.Files.Count; i++)
                {
                    string filename = this.FindBackup.Files[i].FilePath;
                    DateTime date = this.FindBackup.Files[i].Date;
                    if (date < basedate)
                    {
                        if (pickup != "")
                        {
                            return filename;
                        }
                        pickup = filename;
                    }
                }
            }

            if (pickup != "")
            {
                ref_hatena = true;
                return pickup;
            }
            return "";
        }

        string FindSrcByFilename(string baseBackupFilename,int oldIndex)
        {
            for (int i = 0; i < this.FindBackup.Files.Count; i++)
            {
                string filename = this.FindBackup.Files[i].FilePath;
                if (filename != baseBackupFilename)
                {
                    continue;
                }
                int searchIndex = i + oldIndex;
                if (searchIndex >= this.FindBackup.Files.Count)
                {//ない.
                    return "";
                }

                return this.FindBackup.Files[searchIndex].FilePath;
            }

            return "";
        }

        string MakeReport(string fullfilename)
        {
            //少し時間がかかるので、しばらくお待ちください表示.
            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            //テンポラリディレクトリを利用する
            using (U.MakeTempDirectory tempdir = new U.MakeTempDirectory())
            {
                string orignalFilename = OrignalFilename.Text;
                byte[] s = File.ReadAllBytes(orignalFilename); 

                //セーブデータの回収
                CollectSaveData(tempdir.Dir);

                //現在のROMのUPSデータの回収
                CollectUPSsCurrentROM(tempdir.Dir, s);
                //保存していないデータが存在する場合 そのデータのupsも作る
                CollectUPSsLastROM(tempdir.Dir, s);

                //動作しないUPSと動作するUPSデータの回収
                bool isEmergenceHatena = false; //確実といえないけど回収できた場合trueになる
                bool isStableHatena = false;    //確実といえないけど回収できた場合trueになる
                CollectUPSs(tempdir.Dir,s, ref isEmergenceHatena, ref isStableHatena);

                //ログとユーザの説明を書き込む
                string log = Path.Combine(tempdir.Dir, "log.txt");
                File.WriteAllText(log, MakeReportLog(isEmergenceHatena, isStableHatena));

                //etcの内容をコピー
                //lintやコメントなどの設定がほしい
                CopyEtcData(tempdir.Dir);

                //7z圧縮
                InputFormRef.DoEvents(this, R._("7z圧縮中"));
                return ArchSevenZip.Compress(fullfilename, tempdir.Dir);
            }
        }

        void CopyEtcData(string tempdir)
        {
            string tempEtcDir = Path.Combine(tempdir, "etc");
            U.mkdir(tempEtcDir);

            string[] etcFiles = new string[] { "lint_", "comment_", "flag_" };
            foreach(var name in etcFiles)
            {
                string src = U.ConfigEtcFilename(name);
                if (! File.Exists(src))
                {
                    continue;
                }

                string dest = Path.Combine(tempEtcDir, Path.GetFileName(src));
                InputFormRef.DoEvents(this,"=>" + Path.GetFileName(dest));
                File.Copy(src, dest);
            }
        }

        void MakeUPS(string tempdir, byte[] s, string targetFilename)
        {
            InputFormRef.DoEvents(this, "=>" + Path.GetFileName(targetFilename));

            string orignalFilename = OrignalFilename.Text;

            byte[] d = MainFormUtil.OpenROMToByte(targetFilename, orignalFilename);
            string ups = Path.Combine(tempdir, Path.GetFileNameWithoutExtension(targetFilename) + ".ups");
            UPSUtil.MakeUPS(s, d, ups);
            U.CopyTimeStamp(targetFilename, ups); //タイムスタンプを元のファイルに合わせる.
        }

        //動作しないUPSと動作するUPSデータの回収
        void CollectUPSs(string tempdir, byte[] s, ref bool ref_isEmergenceHatena, ref bool ref_isStableHatena)
        {
            string lastBackupFilename = "";

            //比較のための動作するコピー
            string emergenceFilename = "";
            ref_isEmergenceHatena = false;
            if (EmergenceDateNone.Checked == false)
            {
                emergenceFilename = FindSrc(EmergenceDate.Value.Date, FindOption.Forward, ref ref_isEmergenceHatena);
            }
            if (emergenceFilename == "")
            {//不明なので、ちょい前
                emergenceFilename = FindSrc(EmergenceDate.Value.Date, FindOption.PrevPrev, ref ref_isEmergenceHatena);
                ref_isEmergenceHatena = true;
            }

            if (emergenceFilename != ""
                && File.Exists(emergenceFilename))
            {
                MakeUPS(tempdir, s, emergenceFilename);
                lastBackupFilename = emergenceFilename;
            }

            string stableFilename = "";
            ref_isStableHatena = false;
            if (StableDateNone.Checked == false)
            {
                stableFilename = FindSrc(StableDate.Value.Date, FindOption.Prev, ref ref_isStableHatena);
            }

            if (stableFilename == "" || stableFilename == emergenceFilename)
            {//不明なので、正しく動作していた日のちょい前
                stableFilename = FindSrc(StableDate.Value.Date, FindOption.PrevPrev, ref ref_isStableHatena);
                ref_isStableHatena = true;
            }

            if (stableFilename != ""
                && File.Exists(stableFilename))
            {
                MakeUPS(tempdir, s, stableFilename);
                lastBackupFilename = emergenceFilename;
            }

            if (lastBackupFilename != "")
            {
                //もっと古いバックアップがあれば取得する
                int[] olderPickup = new int[] { 5, 10, 15, 20, 30, 40, 60, 80, 100, 140, 180, 250, 300, 400 , 600,  800 , 1000 };
                for (int i = 0; i < olderPickup.Length; i++)
                {
                    string moreOlderFilename = FindSrcByFilename(lastBackupFilename, olderPickup[i]);
                    if (moreOlderFilename != ""
                        && File.Exists(moreOlderFilename))
                    {
                        MakeUPS(tempdir, s, moreOlderFilename);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        //現在のROMのUPSデータの回収
        void CollectUPSsCurrentROM(string tempdir, byte[] s)
        {
            string file = Path.GetFileNameWithoutExtension(Program.ROM.Filename);
            string ups = Path.Combine(tempdir, file + ".ups");
            UPSUtil.MakeUPS(s, Program.ROM.Data, ups);
        }

        //保存していないデータが存在する場合 そのデータのupsも作る
        void CollectUPSsLastROM(string tempdir, byte[] s)
        {
            if (Program.ROM.Modified && Program.ROM.IsVirtualROM == false)
            {
                string orignalFilename = OrignalFilename.Text;
                byte[] d = MainFormUtil.OpenROMToByte(Program.ROM.Filename, orignalFilename);

                DateTime date = File.GetLastWriteTime(Program.ROM.Filename);
                string backup = "backup." + date.ToString("yyyyMMddHHmmss");
                string backupFullPath = U.MakeFilename(backup);
                string ups = Path.Combine(tempdir, Path.GetFileNameWithoutExtension(backupFullPath) + ".ups");
                UPSUtil.MakeUPS(s, d, ups);
                U.CopyTimeStamp(Program.ROM.Filename, ups); //タイムスタンプを元のファイルに合わせる.
            }
        }

        //セーブデータの回収
        void CollectSaveData(string tempdir)
        {
            bool r = PickupSaveData(tempdir, ".sav");
            if (r == false)
            {
                //no$gbaの場合は、BATTERYフォルダの下にある.
                CollectNoDollSaveData(tempdir, ".sav");
            }

            PickupSaveData(tempdir, ".emulator.sav");

            for (int i = 1; i < 10; i++)
            {
                PickupSaveData(tempdir, ".emulator" + i + ".sgm");
                PickupSaveData(tempdir, "" + i + ".sgm");
                PickupSaveData(tempdir, ".emulator.ss" + i);
                PickupSaveData(tempdir, ".ss" + i);
            }
        }

        bool PickupSaveData(string tempdir,string needExt)
        {
            string dir  = Path.GetDirectoryName(Program.ROM.Filename);
            string file = Path.GetFileNameWithoutExtension(Program.ROM.Filename);

            string savFilename = Path.Combine(dir, file + needExt);
            if (! File.Exists(savFilename))
            {
                return false;
            }
            string destFilename = Path.Combine(tempdir, file + needExt);
            File.Copy(savFilename, destFilename, true);
            return true;
        }
        //no$gbaの場合は、BATTERYフォルダの下にある.
        void CollectNoDollSaveData(string tempdir, string needExt)
        {
            string emudir = Program.Config.at("emulator");
            if (emudir == "")
            {
                return;
            }
            emudir = Path.GetDirectoryName(emudir);
            string dir = Path.Combine(emudir , "BATTERY");
            if (! Directory.Exists(dir))
            {
                return;
            }

            string file = Path.GetFileNameWithoutExtension(Program.ROM.Filename);

            string savFilename = Path.Combine(dir, file + needExt);
            if (!File.Exists(savFilename))
            {
                return;
            }
            string destFilename = Path.Combine(tempdir, file + needExt);
            InputFormRef.DoEvents(this, "=>" + Path.GetFileName(savFilename));
            File.Copy(savFilename, destFilename, true);
        }

        string MakeReportLog(bool isEmergenceHatena,bool isStableHatena)
        {
            StringBuilder sb = new StringBuilder();
            //User名を消す
            sb.Append(U.TrimPersonalInfomation(Log.LogToString(1024)));
            //情報を書き込む.
            sb.AppendLine("\r\n------\r\n");

            string FEVersion = "";
            if (Program.ROM != null)
            {
                FEVersion = Program.ROM.VersionToFilename();
                FEVersion += " @ROMSize:" + Program.ROM.Data.Length;
            }

            sb.AppendLine(typeof(U).Assembly.GetName().Name + ":" + U.getVersion());
            sb.AppendLine("FEVersion:" + FEVersion);

            //ユーザが書いた問題点
            sb.AppendLine("Problem:");
            sb.AppendLine(Problem.Text);
            if (EmergenceDateNone.Checked)
            {
                sb.AppendLine("EmergenceDate:None");
            }
            else
            {
                sb.AppendLine("EmergenceDate:" 
                    + EmergenceDate.Value.Date.ToLocalTime().ToString()
                    + (isEmergenceHatena ? "(???)" : ""));
            }
            if (StableDateNone.Checked)
            {
                sb.AppendLine("StableDate:None");
            }
            else
            {
                sb.AppendLine("StableDate:" 
                    + StableDate.Value.Date.ToLocalTime().ToString()
                    + (isStableHatena ? "(???)" : ""));
            }
            return sb.ToString();
        }

        private void OrignalFilename_DoubleClick(object sender, EventArgs e)
        {
            OrignalSelectButton.PerformClick();
        }

        private void ToolProblemReportForm_Load(object sender, EventArgs e)
        {

        }

        private void EmergenceDateNone_CheckedChanged(object sender, EventArgs e)
        {
            EmergenceDate.Enabled = !EmergenceDateNone.Checked;
        }

        private void StableDateNone_CheckedChanged(object sender, EventArgs e)
        {
            StableDate.Enabled = !StableDateNone.Checked;
        }

        private void URLTextBoxEx_DoubleClick(object sender, EventArgs e)
        {
            MainFormUtil.GotoCommunities();
        }
    }
}