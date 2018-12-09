﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;

using System.Text;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class SoundRoomForm : Form
    {
        public SoundRoomForm()
        {
            InitializeComponent();
            
            this.InputFormRef = Init(this);
            this.InputFormRef.MakeGeneralAddressListContextMenu(true);
        }

        public InputFormRef InputFormRef;
        static InputFormRef Init(Form self)
        {
            return new InputFormRef(self
                , ""
                , Program.ROM.RomInfo.sound_room_pointer()
                , Program.ROM.RomInfo.sound_room_datasize()
                , (int i, uint addr) =>
                {//読込最大値検索
                    if (Program.ROM.u32(addr) == 0xFFFFFFFF)
                    {
                        return false;
                    }
                    if (i > 10 
                        && Program.ROM.IsEmpty(addr, Program.ROM.RomInfo.sound_room_datasize() * 10))
                    {
                        return false;
                    }
                    return true;
                }
                , (int i, uint addr) =>
                {
                    return U.ToHexString(i) + " " + GetSongName((uint)i);
                }
                );
        }

        private void SoundRoomForm_Load(object sender, EventArgs e)
        {
        }

        public static string GetSongName(uint roomid)
        {
            InputFormRef InputFormRef = Init(null);
            uint addr = InputFormRef.IDToAddr(roomid);
            if (!U.isSafetyOffset(addr))
            {
                return "";
            }

            return GetSongNameLow(addr);
        }
        public static string GetSongNameLow(uint addr)
        {
            uint textid;
            if (Program.ROM.RomInfo.version() == 6)
            {
                textid = Program.ROM.u32(addr + 4);
                return TextForm.Direct(textid);
            }

            textid = Program.ROM.u32(addr + 12);
            return TextForm.Direct(textid);
        }

        public static List<U.AddrResult> MakeList()
        {
            InputFormRef InputFormRef = Init(null);
            return InputFormRef.MakeList();
        }

        public static string GetSongNameWhereSongID(uint song_id)
        {
            InputFormRef InputFormRef = Init(null);

            uint addr = InputFormRef.BaseAddress;
            for (int i = 0; i < InputFormRef.DataCount; i++)
            {
                uint a = Program.ROM.u32(addr);
                if (song_id == a)
                {
                    return GetSongNameLow(addr);
                }
                addr += InputFormRef.BlockSize;
            }
            return "";
        }

        public void JumpToSongID(uint song_id)
        {
            uint addr = InputFormRef.BaseAddress;
            for (int i = 0; i < InputFormRef.DataCount; i++)
            {
                uint a = Program.ROM.u32(addr);
                if (song_id == a)
                {
                    U.SelectedIndexSafety(this.AddressList, i);
                    return;
                }
                addr += InputFormRef.BlockSize;
            }
            return ;
        }
        //全データの取得
        public static void MakeAllDataLength(List<Address> list)
        {
            string name = "SoundRoom";

            InputFormRef InputFormRef = Init(null);
            FEBuilderGBA.Address.AddAddress(list
                , InputFormRef
                , name
                , new uint[] { 8, 12 }
                , FEBuilderGBA.Address.DataTypeEnum.InputFormRef_MIX
                );

            if (Program.ROM.RomInfo.version() == 7)
            {
                //FE7だと、曲名は C-String
                uint addr = InputFormRef.BaseAddress;
                for (int i = 0; i < InputFormRef.DataCount; i++, addr += InputFormRef.BlockSize)
                {
                    uint songname = Program.ROM.p32(addr + 12);
                    int length;
                    Program.ROM.getString(songname, out length);
                    FEBuilderGBA.Address.AddAddress(list,songname, (uint)length , addr+12 ,name , FEBuilderGBA.Address.DataTypeEnum.BIN);
                }
            }
        }
        public static void MakeTextIDArray(List<TextID> list)
        {
            InputFormRef InputFormRef = Init(null);
            TextID.AppendTextID(list, FELint.Type.SOUNDROOM, InputFormRef, new uint[] { 12 });
        }

        public static void MakeCheckError(List<FELint.ErrorSt> errors)
        {
            InputFormRef InputFormRef = Init(null);
            if (InputFormRef.DataCount < 10)
            {
                errors.Add(new FELint.ErrorSt(FELint.Type.SOUNDROOM, U.NOT_FOUND
                    , R._("サウンドルームが極端に少ないです。破損している可能性があります。")));
            }

            uint soundroom_addr = InputFormRef.BaseAddress;
            for (uint i = 0; i < InputFormRef.DataCount; i++, soundroom_addr += InputFormRef.BlockSize)
            {
                uint name = Program.ROM.u32(soundroom_addr + 12);
                FELint.CheckText(name, "SOUND1", errors, FELint.Type.SOUNDROOM, soundroom_addr, i);

                uint asm = Program.ROM.u32(soundroom_addr + 8);
                if (asm != 0)
                {
                    FELint.CheckASMPointerErrors(asm, errors, FELint.Type.SOUNDROOM, soundroom_addr);
                }
            }
        }
    }
}
