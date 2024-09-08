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
    }
}
