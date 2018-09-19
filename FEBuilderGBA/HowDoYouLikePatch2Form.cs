﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class HowDoYouLikePatch2Form : Form
    {
        public HowDoYouLikePatch2Form()
        {
            InitializeComponent();
            FormIcon.Image = SystemIcons.Question.ToBitmap();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void HowDoYouLikePatchForm2_Load(object sender, EventArgs e)
        {
            //メッセージ（問い合わせ）を鳴らす
            System.Media.SystemSounds.Question.Play();
        }
        static bool AlwaysFalse()
        {
            return false;
        }
        public enum TYPE
        {
              MagicPatch_By_Menu
            , DrawFont_By_Translate

        };
        public static bool CheckAndShowPopupDialog(TYPE type)
        {
            Func<bool> checkFunc = null;
            string title = "";
            string reason = "";

            string patchName1 = "";
            string patchName2 = "";
            string patchShowName = null;

            string patchName3 = "";
            string patchName4 = "";
            string patchShowName3 = null;
            if (type == TYPE.MagicPatch_By_Menu)
            {
                checkFunc = ()=>{
                    return ImageUtilMagic.SearchMagicSystem() != ImageUtilMagic.magic_system_enum.NO; 
                };
                title = R._("魔法を追加するには、魔法システムパッチが必要です。");
                string version = Program.ROM.VersionToFilename();
                if (version == "FE6")
                {//FE6
                    //patchName1 = "FEditorMagicCSASystem_FE6";///No Translate
                    patchName3 = "CSA_Creator_For_FE6_ver2";///No Translate
                }
                else if (version == "FE7J")
                {//FE7J
                    patchName1 = "Custom Spell Animation Engine";///No Translate
                    //patchName3 = "CSA_Creator_For_FE7U";///No Translate
                    
//                    string no_patch = R._("残念ながら、FE7J用の魔法システムパッチは存在しません。\r\n");
//                    R.ShowStopError(title + "\r\n" + no_patch);
//                    return false;
                }
                else if (version == "FE7U")
                {//FE7U
                    //patchName1 = "FEditorMagicCSASystem_FE7U";///No Translate
                    patchName3 = "CSA_Creator_For_FE7U_ver2";///No Translate
                }
                else if (version == "FE8J")
                {//FE8J
                    patchName1 = "magic patch (FeditorAdvMagicSystem)"; ///No Translate
                    patchName3 = "CSA_Creator_For_FE8J_ver2";///No Translate
                }
                else if (version == "FE8U")
                {//FE8U
                    patchName1 = "FEditorMagicCSASystem_FE8U";///No Translate
                    patchName3 = "CSA_Creator_For_FE8U_ver2";///No Translate
                }

                if (patchName1 != "" && patchName3 != "")
                {
                    reason = R._("魔法システムパッチは2つあるので、どちらかを選んでください。\r\n");
                    reason += R._("FEditorMagicCSASystemは、長い動作実績のある魔法システムです。\r\n");
                    reason += R._("CSA_Creatorは、より解像度をサポートする、新進気鋭の魔法システムです。\r\n");
                }
                else
                {
                    reason = R._("魔法システムパッチを適用してください。");
                }
            }
            else if (type == TYPE.DrawFont_By_Translate)
            {
                checkFunc = () => { return InputFormRef.draw_font_enum.NO != InputFormRef.SearchDrawFontPatch(); };
                title = R._("他の言語を表示するにはDrawFontパッチが必要です。");
                if (Program.ROM.RomInfo.is_multibyte())
                {
                    patchName3 = "DrawSingleByte";///No Translate
                }
                else
                {
                    patchName1 = "DrawMultiByte";///No Translate
                    patchName3 = "DrawUTF8";///No Translate

                    reason += R._("日本語へ翻訳する場合は、DrawMultiByte を選択してください。\r\n");
                    reason += R._("日本語以外へ翻訳する場合は、DrawUTF8 を選択してください\r\n");
                }

            }

            Debug.Assert(checkFunc != null);
            Debug.Assert(title != "");
            if (checkFunc())
            {//すでに適応されている.
                return true;
            }
            if (patchShowName == null)
            {
                patchShowName = patchName1;
            }
            if (patchShowName3 == null)
            {
                patchShowName3 = patchName3;
            }

            HowDoYouLikePatch2Form f = (HowDoYouLikePatch2Form)InputFormRef.JumpFormLow<HowDoYouLikePatch2Form>();
            f.Text = title;
            f.ReasonLabel.Text = R._("{0}\r\n\r\n{1}", title, reason);

            if (patchName1 == "")
            {
                f.EnableButton.Hide();
            }
            else
            {
                f.EnableButton.Text = R._("{0}パッチを有効にする", patchShowName);
                f.EnableButton.Click += (sender, e) =>
                {
                    f.Close();

                    PatchForm patchF = (PatchForm)InputFormRef.JumpForm<PatchForm>();
                    patchF.ApplyPatch(patchName1, patchName2); ///No Translate
                };
            }

            if (patchName3 == "")
            {
                f.EnableButton3.Hide();
            }
            else
            {
                f.EnableButton3.Text = R._("{0}パッチを有効にする", patchShowName3);
                f.EnableButton3.Click += (sender, e) =>
                {
                    f.Close();

                    PatchForm patchF = (PatchForm)InputFormRef.JumpForm<PatchForm>();
                    patchF.ApplyPatch(patchName3, patchName4); ///No Translate
                };
            }
            f.ShowDialog();

            return checkFunc();
        }
    }
}