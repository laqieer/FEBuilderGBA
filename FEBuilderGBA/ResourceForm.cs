using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class ResourceForm : Form
    {
        private int sortKey = 0;
        private bool reversed = false;
        private string filter = "";
        public ResourceForm()
        {
            InitializeComponent();

            this.listBoxEx1.SelectedIndex = this.sortKey;
            this.listBoxEx2.SelectedIndex = this.reversed ? 1 : 0;
            this.comboBoxEx1.Items.AddRange(Program.ResourceCache.MakeCategoryList());

            UpdateResources();
        }

        private void UpdateResources()
        {
            this.resources.Text = Program.ResourceCache.ListAll(this.sortKey, this.reversed, this.filter);
        }

        private void listBoxEx1_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.sortKey = this.listBoxEx1.SelectedIndex;
            UpdateResources();
        }

        private void listBoxEx2_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.reversed = this.listBoxEx2.SelectedIndex == 1;
            UpdateResources();
        }

        private void comboBoxEx1_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.filter = this.comboBoxEx1.Text;
            UpdateResources();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Copy text to clipboard
            Clipboard.SetText(this.resources.Text);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Save text to file
            string title = R._("保存するファイル名を選択してください");
            string filter = R._("テキストファイル(*.txt)|*.txt|TSVファイル(*.tsv)|*.tsv|All files|*");

            SaveFileDialog save = new SaveFileDialog();
            save.Title = title;
            save.Filter = filter;
            save.AddExtension = true;
            save.FilterIndex = 1;
            if (save.ShowDialog() == DialogResult.OK)
            {
                U.WriteAllText(save.FileName, this.resources.Text);
                U.OpenURLOrFile(save.FileName);
            }
        }
    }
}
