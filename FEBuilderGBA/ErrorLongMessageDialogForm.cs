using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class ErrorLongMessageDialogForm : Form
    {
        public ErrorLongMessageDialogForm()
        {
            InitializeComponent();
        }

        public void SetErrorMessage(string message)
        {
            this.ErrorMessage.Text = message;
            // Automatically copy error message to clipboard
            U.SetClipboardText(message);
        }

        private void ErrorLongMessageDialogForm_Load(object sender, EventArgs e)
        {
            this.MaximizeBox = true;
        }

        private void MyCloseButton_Click(object sender, EventArgs e)
        {
            // Copy to clipboard again when closing, in case user modified the text
            if (!string.IsNullOrEmpty(this.ErrorMessage.Text))
            {
                U.SetClipboardText(this.ErrorMessage.Text);
            }
            this.Close();
        }

        private void ErrorMessage_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            U.OpenURLOrFile(e.LinkText);
        }

        private void ErrorLongMessageDialogForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }
    }
}
