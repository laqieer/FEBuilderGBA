﻿namespace FEBuilderGBA
{
    partial class ToolCustomBuildForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.TargetFilenameTextBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.UnInstallEngine = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.comboBox2 = new System.Windows.Forms.ComboBox();
            this.button1 = new System.Windows.Forms.Button();
            this.RunButton = new System.Windows.Forms.Button();
            this.textBox4 = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.EAFilenameTextBox = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(12, 12);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            this.textBox1.Size = new System.Drawing.Size(989, 130);
            this.textBox1.TabIndex = 0;
            this.textBox1.Text = "スキル拡張をソースコードからビルドしてインストールします。\r\nこの機能は危険なので、実行前に必ずバックアップを取ってください。\r\nまた、変更後に必ず動作テストをし" +
    "てください。\r\nもし、動作テストに不合格だった場合は、危険なので保全せずにFEBuilderGBAを終了してください。";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 163);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(227, 18);
            this.label1.TabIndex = 1;
            this.label1.Text = "ターゲット:ビルドするスキル拡張";
            // 
            // TargetFilenameTextBox
            // 
            this.TargetFilenameTextBox.Location = new System.Drawing.Point(15, 193);
            this.TargetFilenameTextBox.Name = "TargetFilenameTextBox";
            this.TargetFilenameTextBox.Size = new System.Drawing.Size(947, 25);
            this.TargetFilenameTextBox.TabIndex = 2;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(13, 370);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(207, 18);
            this.label3.TabIndex = 5;
            this.label3.Text = "UnInstallに利用するエンジン";
            // 
            // UnInstallEngine
            // 
            this.UnInstallEngine.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.UnInstallEngine.FormattingEnabled = true;
            this.UnInstallEngine.Location = new System.Drawing.Point(16, 403);
            this.UnInstallEngine.Name = "UnInstallEngine";
            this.UnInstallEngine.Size = new System.Drawing.Size(986, 26);
            this.UnInstallEngine.TabIndex = 6;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(13, 458);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(209, 18);
            this.label4.TabIndex = 7;
            this.label4.Text = "スキル割り当て等を引き継ぎ";
            // 
            // comboBox2
            // 
            this.comboBox2.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox2.FormattingEnabled = true;
            this.comboBox2.Items.AddRange(new object[] {
            "0=引き継がない",
            "1=可能な限り引き継ぐ"});
            this.comboBox2.Location = new System.Drawing.Point(16, 489);
            this.comboBox2.Name = "comboBox2";
            this.comboBox2.Size = new System.Drawing.Size(986, 26);
            this.comboBox2.TabIndex = 8;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(968, 193);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(33, 23);
            this.button1.TabIndex = 9;
            this.button1.Text = "...";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // RunButton
            // 
            this.RunButton.Location = new System.Drawing.Point(16, 545);
            this.RunButton.Name = "RunButton";
            this.RunButton.Size = new System.Drawing.Size(181, 39);
            this.RunButton.TabIndex = 11;
            this.RunButton.Text = "ビルド開始";
            this.RunButton.UseVisualStyleBackColor = true;
            this.RunButton.Click += new System.EventHandler(this.RunButton_Click);
            // 
            // textBox4
            // 
            this.textBox4.Location = new System.Drawing.Point(49, 227);
            this.textBox4.Multiline = true;
            this.textBox4.Name = "textBox4";
            this.textBox4.ReadOnly = true;
            this.textBox4.Size = new System.Drawing.Size(952, 57);
            this.textBox4.TabIndex = 12;
            this.textBox4.Text = "FEBuilderGBAでインストールされるスキル拡張のソースコードが以下のURLで公開されています。";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(13, 299);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(226, 18);
            this.label2.TabIndex = 3;
            this.label2.Text = "ビルドエンジン: EA Coreの設定";
            // 
            // EAFilenameTextBox
            // 
            this.EAFilenameTextBox.Location = new System.Drawing.Point(16, 329);
            this.EAFilenameTextBox.Name = "EAFilenameTextBox";
            this.EAFilenameTextBox.Size = new System.Drawing.Size(947, 25);
            this.EAFilenameTextBox.TabIndex = 4;
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(969, 329);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(33, 23);
            this.button2.TabIndex = 10;
            this.button2.Text = "...";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // ToolCustomBuildForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(144F, 144F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1021, 616);
            this.Controls.Add(this.textBox4);
            this.Controls.Add(this.RunButton);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.comboBox2);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.UnInstallEngine);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.EAFilenameTextBox);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.TargetFilenameTextBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox1);
            this.Name = "ToolCustomBuildForm";
            this.Text = "カスタムビルド";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox TargetFilenameTextBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox UnInstallEngine;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox comboBox2;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button RunButton;
        private System.Windows.Forms.TextBox textBox4;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox EAFilenameTextBox;
        private System.Windows.Forms.Button button2;
    }
}