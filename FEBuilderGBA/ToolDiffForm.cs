using System;
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
    public partial class ToolDiffForm : Form
    {
        public ToolDiffForm()
        {
            InitializeComponent();
            OtherFilename.AllowDropFilename();
            AFilename.AllowDropFilename();
            BFilename.AllowDropFilename();
        }

        string OpenFile()
        {
            string title = R._("開くファイル名を選択してください");
            string filter = R._("GBA ROMs|*.gba|Binary files|*.bin|All files|*");

            OpenFileDialog open = new OpenFileDialog();
            open.Title = title;
            open.Filter = filter;
            Program.LastSelectedFilename.Load(this, "", open);
            DialogResult dr = open.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return "";
            }
            if (!U.CanReadFileRetry(open))
            {
                return "";
            }

            Program.LastSelectedFilename.Save(this, "", open);
            return open.FileNames[0];
        }


        private void OtherSelectButton_Click(object sender, EventArgs e)
        {
            string filename = OpenFile();
            if (filename.Length > 0)
            {
                OtherFilename.Text = filename;
            }
        }

        private void OtherFilename_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            OtherSelectButton.PerformClick();
        }

        private void MakeBinPatchButton_Click(object sender, EventArgs e)
        {
            if (this.OtherFilename.Text.Length <= 0)
            {
                return;
            }

            string title = R._("保存するファイル名を選択してください");
            string filter = R._("TEXT|*.txt|All files|*");

            SaveFileDialog save = new SaveFileDialog();
            save.Title = title;
            save.Filter = filter;
            save.AddExtension = true;
            Program.LastSelectedFilename.Load(this, "", save, "PATCH_(NAME)");

            DialogResult dr = save.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return;
            }
            if (save.FileNames.Length <= 0 || !U.CanWriteFileRetry(save.FileNames[0]))
            {
                return;
            }
            Program.LastSelectedFilename.Save(this, "", save);

            uint PATCHED_IF_MinSize = (uint)RecoverMissMatchNumericUpDown.Value;
            string bin_patchfilename = save.FileName;

            bool isCollectFreeSpace = this.IsCollectFreeSpace.Checked;
            byte[] otherBIN = File.ReadAllBytes(OtherFilename.Text);
            MakeDiff(bin_patchfilename, Program.ROM.Data, otherBIN, PATCHED_IF_MinSize, isCollectFreeSpace);

            //エクスプローラで選択しよう
            U.SelectFileByExplorer(bin_patchfilename);
        }
        public static void MakeDiff(string bin_patchfilename,byte[] currentBIN, byte[] otherBIN, uint PATCHED_IF_MinSize, bool isCollectFreeSpace)
        {
            // Delegates to the cross-platform Core helper so the Avalonia ToolDiffViewModel
            // and WinForms ToolDiffForm produce identical patch output.
            DiffToolCore.MakeDiff(bin_patchfilename, currentBIN, otherBIN, PATCHED_IF_MinSize,
                isCollectFreeSpace,
                version: Program.ROM.RomInfo.version,
                isMultibyte: Program.ROM.RomInfo.is_multibyte);
        }

        private void AFileSelectButton_Click(object sender, EventArgs e)
        {
            string filename = OpenFile();
            if (filename.Length > 0)
            {
                AFilename.Text = filename;
            }
        }

        private void BFileSelectButton_Click(object sender, EventArgs e)
        {
            string filename = OpenFile();
            if (filename.Length > 0)
            {
                BFilename.Text = filename;
            }
        }

        private void AFilename_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            AFileSelectButton.PerformClick();
        }

        private void BFilename_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            BFileSelectButton.PerformClick();
        }

        private void MakeBinPatch3Button_Click(object sender, EventArgs e)
        {
            if (this.AFilename.Text.Length <= 0)
            {
                return;
            }
            if (this.BFilename.Text.Length <= 0)
            {
                return;
            }

            string title = R._("保存するファイル名を選択してください");
            string filter = R._("TEXT|*.txt|All files|*");

            SaveFileDialog save = new SaveFileDialog();
            save.Title = title;
            save.Filter = filter;
            DialogResult dr = save.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return;
            }
            if (save.FileNames.Length <= 0 || !U.CanWriteFileRetry(save.FileNames[0]))
            {
                return;
            }
            Program.LastSelectedFilename.Save(this, "", save);


            uint PATCHED_IF_MinSize = (uint)RecoverMissMatchDiff3NumericUpDown.Value;
            string bin_patchfilename = save.FileName;

            byte[] a = File.ReadAllBytes(AFilename.Text);
            byte[] b = File.ReadAllBytes(BFilename.Text);

            if (Diff3Method.SelectedIndex == 0)
            {//AとBにあって、自分にだけないもの — delegates to Core helper (shared with Avalonia)
                DiffToolCore.MakeDiff3(bin_patchfilename, Program.ROM.Data, a, b, PATCHED_IF_MinSize);
            }
            else
            {
                // Unknown Diff3 method — preserve original behavior of producing the header-only file.
                File.WriteAllLines(bin_patchfilename, new[] { "TYPE=BIN", "" });
            }

            //エクスプローラで選択しよう
            U.SelectFileByExplorer(bin_patchfilename);
        }

        private void DiffToolForm_Load(object sender, EventArgs e)
        {

        }

        private void OtherFilename_TextChanged(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

    }
}
