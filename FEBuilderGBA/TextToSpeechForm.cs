﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Speech.Synthesis;

namespace FEBuilderGBA
{
    public partial class TextToSpeechForm : Form
    {
        public TextToSpeechForm()
        {
            InitializeComponent();

            this.IconPictureBox.Image = Properties.Resources.icon_speaker.ToBitmap();
            U.AddCancelButton(this);
        }

        string DefString;
        public void SetDefString(string str)
        {
            DefString = str;
        }
        bool IsEmulatorMode;
        public void SetEmulatorMode(bool isEmulatorMode)
        {
            this.IsEmulatorMode = isEmulatorMode;
        }

        private void TextToSpeechForm_Load(object sender, EventArgs e)
        {
            if (!Init())
            {
                this.Close();
                return;
            }

            Rate.Value = g_VoiceSpeeach.Rate;
            ShortNum.Value = g_ShortLength;

            if (this.IsEmulatorMode)
            {
                ShortNumLabel.Show();
                ShortNum.Show();
            }
            else
            {
                ShortNumLabel.Hide();
                ShortNum.Hide();
            }
            this.VoiceComboBox.BeginUpdate();
            this.VoiceComboBox.Items.Clear();
            foreach (InstalledVoice voiceperson in g_VoiceSpeeach.GetInstalledVoices())
            {
                // Updated to use the VoiceInfo property of InstalledVoice  
                string language = voiceperson.VoiceInfo.Culture.Name;
                string name = voiceperson.VoiceInfo.Name;
                this.VoiceComboBox.Items.Add(name + " Language:" + language);
            }
            this.VoiceComboBox.EndUpdate();
        }

        bool Init()
        {
            if (g_VoiceSpeeach == null)
            {//nullなら初期化する.
                try
                {
                    //合成音声エンジンを初期化する.
                    g_VoiceSpeeach = new SpeechSynthesizer();
                    g_VoiceSpeeach.Rate = 0;

                    if (Program.ROM.RomInfo.is_multibyte)
                    {
                        g_ShortLength = 10;
                    }
                    else
                    {
                        g_ShortLength = 20;
                    }
                }
                catch (Exception ee)
                {
                    R.ShowStopError(ee.ToString());
                    return false;
                }
            }
            return true;
        }
        static SpeechSynthesizer g_VoiceSpeeach = null;

        static string g_CurrentString;
        static int g_ShortLength;
        static string[] ConvertTable = new string[]{
                 "・",""    ///No Translate
                ,"【",""    ///No Translate
                ,"】",""    ///No Translate
                ,"「",""    ///No Translate
                ,"」",""    ///No Translate
                ,"！","。"    ///No Translate
                ,"!","."    ///No Translate
                ,"\r\n","、"    ///No Translate
                ,"。。","。"    ///No Translate
                ,"、、","、"    ///No Translate
                ,",,",","    ///No Translate
                ,"..","."    ///No Translate
                ,"\"",""    ///No Translate
        };
        static string[] ConvertTableEN = new string[]{
                 "・",""    ///No Translate
                ,"【",""    ///No Translate
                ,"】",""    ///No Translate
                ,"「",""    ///No Translate
                ,"」",""    ///No Translate
                ,"！","。"    ///No Translate
                ,"!","."    ///No Translate
                ,"\r\n"," "    ///No Translate
                ,"。。","."    ///No Translate
                ,"、、","、"    ///No Translate
                ,",,",","    ///No Translate
                ,"..","."    ///No Translate
                ,"\"",""    ///No Translate
        };
        public static string TextJoinCopy(string str, bool useSentensLineBreak)
        {
            string text;
            if (Program.ROM.RomInfo.is_multibyte)
            {
                text = U.table_replace(str, ConvertTable);
            }
            else
            {
                text = U.table_replace(str, ConvertTableEN);
            }
            if (useSentensLineBreak)
            {
                if (Program.ROM.RomInfo.is_multibyte)
                {
                    text = text.Replace("。", "。\r\n");   ///No Translate
                    text = text.Replace("\r\n、", "\r\n");   ///No Translate
                }
                else
                {
                    text = text.Replace(".", ".\r\n");   ///No Translate
                    text = text.Replace("\r\n,", "\r\n");   ///No Translate
                }
            }

            return text;
        }
        public static void Speak(string str, bool isForce = false)
        {
            if (g_VoiceSpeeach == null)
            {//未初期化
                return;
            }

            str = TextJoinCopy(str, useSentensLineBreak: false);
            if (str.Length <= 0)
            {
                return;
            }
            if (isForce == false)
            {
                if (g_CurrentString == str)
                {//既に読み上げた文字列は再度読み上げしない
                    return;
                }
            }
            if (str.Length < g_ShortLength)
            {//短すぎ
                return;
            }
            g_CurrentString = str;

            // Check if the synthesizer is currently speaking  
            if (g_VoiceSpeeach.State == SynthesizerState.Speaking)
            {
                g_VoiceSpeeach.SpeakAsyncCancelAll();
            }
            // SpeakAsync is non-blocking, so it will not wait for the speech to finish before continuing
            g_VoiceSpeeach.SpeakAsync(g_CurrentString);
        }
        public static void Stop()
        {
            if (g_VoiceSpeeach == null)
            {
                return;
            }
            g_VoiceSpeeach.SpeakAsyncCancelAll();
            g_VoiceSpeeach = null;
        }

        private void EndButton_Click(object sender, EventArgs e)
        {
            Stop();
            this.Close();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (!Init())
            {
                return;
            }
            Speak(this.DefString , true);
            this.Close();
        }


        private void Rate_ValueChanged(object sender, EventArgs e)
        {
            if (g_VoiceSpeeach == null)
            {
                return;
            }
            g_VoiceSpeeach.Rate = (int)Rate.Value;
        }

        private void ShortNum_ValueChanged(object sender, EventArgs e)
        {
            g_ShortLength = (int)ShortNum.Value;
        }


        public static bool OptionTextToSpeech(string text, bool isEmulatorMode = false)
        {
            TextToSpeechForm f = (TextToSpeechForm)InputFormRef.JumpFormLow<TextToSpeechForm>();
            text = TextForm.StripAllCode(TextForm.ConvertEscapeTextRev(text));
            f.SetDefString(text);
            f.SetEmulatorMode(isEmulatorMode);
            f.ShowDialog();

            return g_VoiceSpeeach != null;
        }

        private void VoiceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selected = this.VoiceComboBox.SelectedIndex;
            if (selected < 0)
            {
                return;
            }

            // Use InstalledVoice collection from GetInstalledVoices instead of GetVoices
            var installedVoices = g_VoiceSpeeach.GetInstalledVoices();
            if (selected >= installedVoices.Count)
            {
                return;
            }

            // Set the selected voice using VoiceInfo.Name
            g_VoiceSpeeach.SelectVoice(installedVoices[selected].VoiceInfo.Name);
        }
    }
}
