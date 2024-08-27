using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class SkillConfigCSkillSystem09xForm : Form
    {
        const uint gpSkillInfos = 0xB2A614;
        const uint gpSkillInfos_Desc = 0xB2A760;
        const uint gpEfxSkillAnims = 0xB2A630;
        const uint SkillPalettePointer = 0x22370; //オリジナルROMからあるパレット.

        public SkillConfigCSkillSystem09xForm()
        {
            InitializeComponent();

            this.AddressList.OwnerDraw(DrawSkillAndText, DrawMode.OwnerDrawFixed);
            InputFormRef = Init(this, 0);
            this.InputFormRef.MakeGeneralAddressListContextMenu(false);
            this.InputFormRef.CheckProtectionPaddingALIGN4 = false;

            ShowZoomComboBox.SelectedIndex = 0;
            U.SetIcon(AnimationInportButton, Properties.Resources.icon_upload);
            U.SetIcon(AnimationExportButton, Properties.Resources.icon_arrow);

            U.AllowDropFilename(this, ImageFormRef.IMAGE_FILE_FILTER, (string filename) =>
            {
                using (ImageFormRef.AutoDrag ad = new ImageFormRef.AutoDrag(filename))
                {
                    ImportButton_Click(null, null);
                }
            });

            U.AllowDropFilename(this, new string[] { ".TXT" }, (string filename) =>
            {
                using (ImageFormRef.AutoDrag ad = new ImageFormRef.AutoDrag(filename))
                {
                    AnimationImportButton_Click(null, null);
                }
            });
        }

        public InputFormRef InputFormRef;
        static InputFormRef Init(Form self, uint textPointer)
        {
            InputFormRef ifr = new InputFormRef(self
                , ""
                , gpSkillInfos
                , 8
                , (int i, uint addr) =>
                {//読込最大値検索
                    return i < 0x400;
                }
                , (int i, uint addr) =>
                {
                    return U.ToHexString(i) + " " + GetSkillName((uint)i);
                }
            );
            return ifr;
        }

        private void SkillConfigCSkillSystem09xForm_Load(object sender, EventArgs e)
        {

        }

        static uint GetSkillAnimInfo(uint index)
        {
            return Program.ROM.p32(gpEfxSkillAnims) + 4 * index;
        }

        static uint GetSkillInfo(uint index)
        {
            return Program.ROM.p32(gpSkillInfos) + 8 * index;
        }

        public static uint GetSkillIcon(uint index)
        {
            return Program.ROM.p32(GetSkillInfo(index) + 0);
        }

        public static Bitmap DrawSkillIcon(uint index)
        {
            uint pr_icon = GetSkillIcon(index);
            if  (!U.isSafetyOffset(pr_icon))
                return ImageUtil.Blank(16,16);

            return ImageUtil.ByteToImage16Tile(
                16, 16,
                Program.ROM.Data,
                (int)pr_icon,
                Program.ROM.Data,
                (int)Program.ROM.p32(SkillPalettePointer));
        }

        static uint GetSkillDescMsg(uint index)
        {
            return Program.ROM.u16(GetSkillInfo(index) + 6);
        }

        static uint GetSkillNameMsg(uint index)
        {
            return Program.ROM.u16(GetSkillInfo(index) + 4);
        }

        static string SkillTextToName(string name)
        {
            int i = name.IndexOf(':');
            if (i < 0)
            {
                return "";
            }
            return name.Substring(0, i).Trim();
        }

        public static string GetSkillDesc(uint index)
        {
            return TextForm.Direct(GetSkillDescMsg(index)); ;
        }

        public static string GetSkillName(uint index)
        {
            uint name_msg = GetSkillNameMsg(index);
            if (name_msg != 0)
                return TextForm.Direct(name_msg);

            return SkillTextToName(GetSkillDesc(index));
        }

        Size DrawSkillAndText(ListBox lb, int index, Graphics g, Rectangle listbounds, bool isWithDraw)
        {
            if (index < 0 || index >= lb.Items.Count)
            {
                return new Size(listbounds.X, listbounds.Y);
            }
            string text = lb.Items[index].ToString();

            SolidBrush brush = new SolidBrush(lb.ForeColor);
            Font normalFont = lb.Font;
            Rectangle bounds = listbounds;

            int textmargineY = (ListBoxEx.OWNER_DRAW_ICON_SIZE - (int)lb.Font.Height) / 2;

            Bitmap bitmap = DrawSkillIcon((uint)index);
            U.MakeTransparent(bitmap);

            //アイコンを描く.
            Rectangle b = bounds;
            b.Width = ListBoxEx.OWNER_DRAW_ICON_SIZE;
            b.Height = ListBoxEx.OWNER_DRAW_ICON_SIZE;
            bounds.X += U.DrawPicture(bitmap, g, isWithDraw, b);
            bitmap.Dispose();

            //テキストを描く.
            b = bounds;
            b.Y += textmargineY;
            bounds.X += U.DrawText(text, g, normalFont, brush, isWithDraw, b);
            bounds.Y += ListBoxEx.OWNER_DRAW_ICON_SIZE;

            brush.Dispose();
            return new Size(bounds.X, bounds.Y);
        }

        private void WriteButton_Click(object sender, EventArgs e)
        {
            //スキルアニメポインタを追加書き込み. //別テーブルでアドレスが違う.
            uint anime = GetSkillAnimInfo((uint)AddressList.SelectedIndex);
            Program.Undo.Push("SKILL ANIME P", anime, 4);
            Program.ROM.write_p32(anime, (uint)ANIMATION.Value);
        }


        private void AddressList_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.SKILLICON.Image = DrawSkillIcon((uint)AddressList.SelectedIndex);
            this.IconAddr.Value = GetSkillIcon((uint)AddressList.SelectedIndex);

            uint anime = GetSkillAnimInfo((uint)AddressList.SelectedIndex);
            uint a = Program.ROM.p32(anime);
            ANIMATION.Value = a;
            if (U.isSafetyOffset(a))
            {
                AnimationPanel.Show();
                AnimationExportButton.Show();
                ShowFrameUpDown.Value = 0;
                ShowFrameUpDown_ValueChanged(null, null);
            }
            else
            {
                AnimationPanel.Hide();
                AnimationExportButton.Hide();
            }
        }

        private void AnimationExportButton_Click(object sender, EventArgs e)
        {
            string title = R._("保存するファイル名を選択してください");
            string filter = R._("スキルアニメスクリプト|*.txt|アニメGIF|*.gif|Dump All|*.txt|All files|*");

            SaveFileDialog save = new SaveFileDialog();
            save.Title = title;
            save.Filter = filter;
            save.AddExtension = true;
            Program.LastSelectedFilename.Load(this, "", save, "skill_" + this.AddressList.Text.Trim());

            DialogResult dr = save.ShowDialog();
            if (dr != DialogResult.OK)
            {
                return;
            }
            if (save.FileNames.Length <= 0 || !U.CanWriteFileRetry(save.FileNames[0]))
            {
                return;
            }
            string filename = save.FileNames[0];
            Program.LastSelectedFilename.Save(this, "", save);

            if (save.FilterIndex == 2)
            {//GIF
                ImageUtilSkillSystemsAnimeCreator.ExportGif(filename, (uint)ANIMATION.Value);
            }
            else if (save.FilterIndex == 3)
            {//All
                ImageUtilSkillSystemsAnimeCreator.Export(filename, (uint)ANIMATION.Value);
                filename = U.ChangeExtFilename(filename, ".gif");
                ImageUtilSkillSystemsAnimeCreator.ExportGif(filename, (uint)ANIMATION.Value);
            }
            else
            {//Script
                ImageUtilSkillSystemsAnimeCreator.Export(filename, (uint)ANIMATION.Value);
            }

            //エクスプローラで選択しよう
            U.SelectFileByExplorer(filename);
        }

        public string SkillAnimeImportDirect(int id, string filename)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {//2重割り込み禁止
                return R._("現在他の処理中です");
            }

            if (id <= 0)
            {
                return R._("指定されたID({0})は存在しません。", U.To0xHexString(id));
            }
            uint animePointer = GetSkillAnimInfo((uint)id);

            string error = "";

            //少し時間がかかるので、しばらくお待ちください表示.
            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            {
                error = ImageUtilSkillSystemsAnimeCreator.Import(filename, animePointer);
            }

            if (error != "")
            {
                return error;
            }

            U.ReSelectList(this.AddressList);
            //書き込み通知
            InputFormRef.ShowWriteNotifyAnimation(this, 0);

            return "";
        }

        private void X_N_JumpEditor_Click(object sender, EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {//2重割り込み禁止
                return;
            }
            uint ID = (uint)AddressList.SelectedIndex;

            string filehint = AddressList.Text;

            //少し時間がかかるので、しばらくお待ちください表示.
            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(this))
            //テンポラリディレクトリを利用する
            using (U.MakeTempDirectory tempdir = new U.MakeTempDirectory())
            {
                string filename = Path.Combine(tempdir.Dir, "anime.txt");
                ImageUtilSkillSystemsAnimeCreator.Export(filename, (uint)ANIMATION.Value);
                if (!File.Exists(filename))
                {
                    R.ShowStopError("アニメーションエディタを表示するために、アニメーションをエクスポートしようとしましたが、アニメをファイルにエクスポートできませんでした。\r\n\r\nファイル:{0}", filename);
                    return;
                }

                ToolAnimationCreatorForm f = (ToolAnimationCreatorForm)InputFormRef.JumpFormLow<ToolAnimationCreatorForm>();
                f.Init(ToolAnimationCreatorUserControl.AnimationTypeEnum.Skill
                    , ID, filehint, filename);
                f.Show();
            }
        }

        private void ShowZoomComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ShowZoomComboBox.SelectedIndex == 0)
            {
                AnimationPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            }
            else
            {
                AnimationPictureBox.SizeMode = PictureBoxSizeMode.Normal;
            }
        }

        private void ShowFrameUpDown_ValueChanged(object sender, EventArgs e)
        {
            uint anime = (uint)ANIMATION.Value;
            uint frame = (uint)ShowFrameUpDown.Value;
            string log;
            AnimationPictureBox.Image = ImageUtilSkillSystemsAnimeCreator.Draw(anime, frame, out log);
            BinInfo.Text = log;
        }

        private void AnimationImportButton_Click(object sender, EventArgs e)
        {
            if (InputFormRef.IsPleaseWaitDialog(this))
            {//2重割り込み禁止
                return;
            }

            string filename;
            if (ImageFormRef.GetDragFilePath(out filename))
            {
            }
            else
            {

                string title = R._("開くファイル名を選択してください");
                string filter = R._("スキルアニメスクリプト|*.txt|All files|*");

                OpenFileDialog open = new OpenFileDialog();
                open.Title = title;
                open.Filter = filter;
                open.FileName = "skill_" + this.AddressList.Text.Trim();
                DialogResult dr = open.ShowDialog();
                if (dr != DialogResult.OK)
                {
                    return;
                }
                if (!U.CanReadFileRetry(open))
                {
                    return;
                }
                filename = open.FileNames[0];
                Program.LastSelectedFilename.Save(this, "", open);
            }

            uint id = (uint)this.AddressList.SelectedIndex;
            string error = SkillAnimeImportDirect((int)id, filename);

            if (error != "")
            {
                R.ShowStopError(error);
                return;
            }
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            Bitmap bitmap = ImageFormRef.ImportFilenameDialog(this);
            if (bitmap == null)
            {
                return;
            }
            int width = 2 * 8;
            int height = 2 * 8;
            if (bitmap.Width != width || bitmap.Height != height)
            {
                R.ShowStopError("画像サイズが正しくありません。\r\nWidth:{2} Height:{3} でなければなりません。\r\n\r\n選択された画像のサイズ Width:{0} Height:{1}", bitmap.Width, bitmap.Height, width, height);
                return;
            }

            //check palette
            {
                string palette_error =
                    ImageUtil.CheckPalette(bitmap.Palette
                        , Program.ROM.Data
                        , Program.ROM.p32(SkillPalettePointer)
                        , U.NOT_FOUND
                        , ""
                        );
                if (palette_error != "")
                {
                    ErrorPaletteShowForm f = (ErrorPaletteShowForm)InputFormRef.JumpFormLow<ErrorPaletteShowForm>();
                    f.SetErrorMessage(palette_error);
                    f.SetOrignalImage(ImageUtil.OverraidePalette(bitmap, Program.ROM.Data, Program.ROM.p32(SkillPalettePointer)));
                    f.SetReOrderImage1(ImageUtil.ReOrderPalette(bitmap, Program.ROM.Data, Program.ROM.p32(SkillPalettePointer)));
                    f.ShowForceButton();
                    f.ShowDialog();

                    bitmap = f.GetResultBitmap();
                    if (bitmap == null)
                    {
                        return;
                    }
                }
            }

            int index = this.AddressList.SelectedIndex;
            uint addr = GetSkillIcon((uint)index);

            byte[] image = ImageUtil.ImageToByte16Tile(bitmap, width, height);

            //画像等データの書き込み
            Undo.UndoData undodata = Program.Undo.NewUndoData(this);

            Program.ROM.write_range(U.toOffset(addr), image, undodata);
            Program.Undo.Push(undodata);

            InputFormRef.ReloadAddressList();
            InputFormRef.ShowWriteNotifyAnimation(this, addr);
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            Bitmap bitmap = DrawSkillIcon((uint)AddressList.SelectedIndex);
            ImageFormRef.ExportImage(this, bitmap, InputFormRef.MakeSaveImageFilename());
        }
    }
}
