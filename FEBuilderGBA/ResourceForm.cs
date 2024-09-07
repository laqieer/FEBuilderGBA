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
        public ResourceForm()
        {
            InitializeComponent();

            TextBoxEx info = new TextBoxEx();
            info.Multiline = true;
            info.Location = new Point(5, 5);
            info.Size = new Size(600, 400);
            info.ReadOnly = true;
            info.ScrollBars = ScrollBars.Both;
            info.Text = Program.ResourceCache.ListAll();
            this.Controls.Add(info);
        }
    }
}
