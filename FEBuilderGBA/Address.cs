﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    public class Address
    {
        public uint Addr { get; private set; }
        public uint Length;
        public string Info { get; private set; }
        public uint Pointer{ get; private set; }
        public uint BlockSize { get; private set; } //IFRのみ
        public uint[] PointerIndexes { get; private set; } //IFRのみ それ以外は null

        public enum DataTypeEnum
        {
             MIX  //不明だけどポインタがあるかもしれないデータ
            ,BIN   //ポインタがないデータ
            ,LZ77IMG
            ,LZ77TSA
            ,LZ77PAL
            ,LZ77MAPMAR
            ,LZ77MAPCONFIG
            ,IMG  //無圧縮画像
            ,PAL  //パレット
            ,TSA  //TSA
            ,FONT  //フォント画像
            ,FONTCN  //フォント画像
            ,AP      //アニメ処理
            ,HEADERTSA //ヘッダーTSA
            ,InputFormRef     //通常のIFRポインタはデータ
            ,InputFormRef_ASM //ポインタがASMのIFR
            ,InputFormRef_MIX //ポインタがデータとASMが混在しているIFR
            ,EVENTTRAP //イベント条件のトラップ
            ,EVENTCOND_OBJECT //イベント条件
            ,EVENTCOND_TALK //イベント条件
            ,EVENTCOND_TURN //イベント条件
            ,EVENTCOND_ALWAYS //イベント条件
            ,EVENTSCRIPT //イベントスクリプト
            ,BATTLEFRAME   //戦闘FRAME この中に圧縮された0x85フレームポインタがあるよ
            ,BATTLEFRAMEIMG   //戦闘FRAME IMG
            ,BATTLEOAM   //戦闘OAM
            ,SONGSCORE //楽譜
            ,SONGINST  //楽器
            ,SONGINSTDIRECTSOUND  //楽器 DirectSoundのwavデータ
            ,SONGINSTWAVE        //楽器 16バイト固定
            ,SONGTRACK //曲ヘッダー
            ,CSTRING //C言語の文字列
            ,ASM         //asmコード
            ,PATCH_ASM   //patchで提供されるasmコード
            ,BL_ASM      //ユーザが定義したコードでBLで呼び出される関数 パッチ内でまれに存在する.
            ,PROCS
            ,OAMSP
            ,OAMSP12
            ,TEXTPOINTERS
            ,POINTER
            ,POINTER_ASM    //アセンブラ関数専用へのポインタ
            ,AISCRIPT
            ,MAGIC_APPEND_SPELLTABLE //追加魔法テーブル ポインタの塊 5*4
            ,MAGICFRAME_FEITORADV //魔法FRAME 圧縮されていないが0x85フレームポインタがあるよ
            ,MAGICFRAME_CSA   //魔法FRAME 圧縮されていないが0x85フレームポインタがあるよ
            ,MAGICOAM   //魔法拡張のOAM
            ,ROMANIMEFRAME //ROM内アニメのフレーム
            ,JUMPTOHACK //ハックへジャンプするコード
            ,FFor00
        };
        public DataTypeEnum DataType { get; private set; }

        public Address(uint addr, uint length, uint pointer, string info, DataTypeEnum type, uint blockSize = 0, uint[] pointerIndexes = null)
        {
            this.Addr = U.toOffset(addr);
            this.Length = length;
            this.Info = info;

            Debug.Assert(U.isSafetyOffset(this.Addr));

            if (! U.isSafetyLength(this.Addr, length))
            {//あまりにも長すぎる.
                length = 0;
            }

            if (pointer == U.NOT_FOUND)
            {
                this.Pointer = U.NOT_FOUND;
            }
            else
            {
                this.Pointer = U.toOffset(pointer);
                Debug.Assert(U.isSafetyOffset(this.Pointer));
            }
            this.DataType = type;
            this.BlockSize = blockSize;
            this.PointerIndexes = pointerIndexes;
#if DEBUG
            if (type == DataTypeEnum.InputFormRef)
            {
                Debug.Assert(blockSize > 0);
            }
#endif
        }

        public void ResizeAddress(uint addr,uint length)
        {
            Debug.Assert(length < 0x100000);
            Debug.Assert(U.isSafetyOffset(addr));
            Debug.Assert(U.isSafetyOffset(addr+length));

            this.Addr = addr;
            this.Length = length;
        }

        static public void AddPointer(List<Address> list, uint pointer, uint length, string info, DataTypeEnum type)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            list.Add( new Address(addr, length, pointer, info,type));
        }
        static public void AddAddress(List<Address> list, uint addr, uint length, uint pointer, string info, DataTypeEnum type)
        {
            if (pointer != U.NOT_FOUND) 
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            list.Add(new Address(addr, length, pointer, info, type));
        }
        static public void AddLZ77Address(List<Address> list, uint addr, uint pointer, string info, bool isPointerOnly, DataTypeEnum type)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            if (pointer != U.NOT_FOUND)
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = LZ77.getCompressedSize(Program.ROM.Data, addr);
            }
            list.Add(new Address(addr, length, pointer, info, type));
        }
        static public void AddLZ77Pointer(List<Address> list, uint pointer, string info, bool isPointerOnly, DataTypeEnum type)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            AddLZ77Address(list, addr, pointer, info, isPointerOnly, type);
        }
        static public void AddHeaderTSAPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ImageUtil.CalcByteLengthForHeaderTSAData(Program.ROM.Data, (int)U.toOffset(addr));
            }

            list.Add(new Address(addr, length, pointer, info , DataTypeEnum.HEADERTSA));
        }

        static public void AddAddress(List<Address> list, InputFormRef InputFormRef, string info, uint[] pointerIndexes, DataTypeEnum type = DataTypeEnum.InputFormRef)
        {
            uint addr = InputFormRef.BaseAddress;
            uint length = InputFormRef.BlockSize * (InputFormRef.DataCount + 1);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            uint pointer = InputFormRef.BasePointer;
            if (!U.isSafetyOffset(pointer))
            {
                pointer = U.NOT_FOUND;
            }
            list.Add(new Address(addr, length, pointer, info, type, InputFormRef.BlockSize, pointerIndexes));
        }
        static public void AddAddressButIgnorePointer(List<Address> list, InputFormRef InputFormRef, string info, uint[] pointerIndexes)
        {
            uint addr = InputFormRef.BaseAddress;
            uint length = InputFormRef.BlockSize * (InputFormRef.DataCount + 1);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            uint pointer = U.NOT_FOUND;
            list.Add(new Address(addr, length, pointer, info, DataTypeEnum.InputFormRef, InputFormRef.BlockSize, pointerIndexes));
        }
        public static void AddCString(List<Address> list, uint pointer)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }

            uint nameAddr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(nameAddr))
            {
                return ;
            }
            int length;
            string strname = Program.ROM.getString(
                U.toOffset(nameAddr), out length);

            list.Add( new Address(nameAddr
                , (uint)length + 1 //null
                , pointer
                , strname
                , DataTypeEnum.CSTRING));
        }

        public static void AddFunctions(List<Address> list, List<U.AddrResult> arlist, uint offset, string appendName)
        {
            for (int i = 0; i < arlist.Count; i++)
            {
                uint pointer = arlist[i].addr + offset;
                AddFunction(list, pointer, arlist[i].name + appendName);
            }
        }
        public static void AddFunction(List<Address> list, uint pointer, string strname)
        {
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            addr = DisassemblerTrumb.ProgramAddrToPlain(addr);

            list.Add(new Address(addr
                , 0
                , pointer
                , strname
                , FEBuilderGBA.Address.DataTypeEnum.ASM));
        }

        static void AddAPAddress(List<Address> list, uint addr, uint pointer, string info, bool isPointerOnly)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            if (pointer != U.NOT_FOUND)
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ImageUtilAP.CalcAPLength(addr);
            }
            list.Add(new Address(addr, length, pointer, info, DataTypeEnum.AP));
        }
        static public void AddAPPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            AddAPAddress(list, addr, pointer, info, isPointerOnly);
        }
        public static bool IsLZ77(DataTypeEnum type)
        {
            return
                (type == Address.DataTypeEnum.LZ77IMG
                || type == Address.DataTypeEnum.LZ77MAPCONFIG
                || type == Address.DataTypeEnum.LZ77MAPMAR
                || type == Address.DataTypeEnum.LZ77PAL
                || type == Address.DataTypeEnum.LZ77TSA
                || type == Address.DataTypeEnum.BATTLEFRAME
                || type == Address.DataTypeEnum.BATTLEFRAMEIMG
                || type == Address.DataTypeEnum.BATTLEOAM
                );

        }

        public static bool IsASMOnly(Address.DataTypeEnum dataType)
        {
            return 
                   dataType == Address.DataTypeEnum.ASM
                || dataType == Address.DataTypeEnum.PATCH_ASM
                || dataType == Address.DataTypeEnum.BL_ASM
                || dataType == Address.DataTypeEnum.POINTER_ASM
                || dataType == Address.DataTypeEnum.InputFormRef_ASM
                ;
        }
        public static bool IsMix_ASMOrData(Address.DataTypeEnum dataType)
        {
            return
                dataType == Address.DataTypeEnum.InputFormRef_MIX
                ;
        }
        public static bool IsIFR(Address.DataTypeEnum dataType)
        {
            return
                dataType == Address.DataTypeEnum.InputFormRef
             || dataType == Address.DataTypeEnum.InputFormRef_ASM
             || dataType == Address.DataTypeEnum.InputFormRef_MIX
                ;
        }

        //ポインタがあるかもしれないデータたち
        public static bool IsPointerableType(Address.DataTypeEnum dataType)
        {
            return dataType == Address.DataTypeEnum.EVENTCOND_ALWAYS
                || dataType == Address.DataTypeEnum.EVENTCOND_OBJECT
                || dataType == Address.DataTypeEnum.EVENTCOND_TALK
                || dataType == Address.DataTypeEnum.EVENTCOND_TURN
                || dataType == Address.DataTypeEnum.EVENTSCRIPT
                || dataType == Address.DataTypeEnum.BATTLEFRAME
                || dataType == Address.DataTypeEnum.SONGSCORE
                || dataType == Address.DataTypeEnum.SONGTRACK
                || dataType == Address.DataTypeEnum.ASM
                || dataType == Address.DataTypeEnum.PROCS
                || dataType == Address.DataTypeEnum.OAMSP
                || dataType == Address.DataTypeEnum.POINTER
                || dataType == Address.DataTypeEnum.POINTER_ASM
                || dataType == Address.DataTypeEnum.AISCRIPT
                || dataType == Address.DataTypeEnum.MAGICFRAME_CSA
                || dataType == Address.DataTypeEnum.MAGICFRAME_FEITORADV
                || dataType == Address.DataTypeEnum.MAGIC_APPEND_SPELLTABLE
                || dataType == Address.DataTypeEnum.FONT
                || dataType == Address.DataTypeEnum.FONTCN
                || dataType == Address.DataTypeEnum.TEXTPOINTERS
                || dataType == Address.DataTypeEnum.InputFormRef
                || dataType == Address.DataTypeEnum.InputFormRef_ASM
                || dataType == Address.DataTypeEnum.InputFormRef_MIX
                || dataType == Address.DataTypeEnum.MIX
                || dataType == Address.DataTypeEnum.JUMPTOHACK
                || dataType == Address.DataTypeEnum.PATCH_ASM
                || dataType == Address.DataTypeEnum.BL_ASM
                ;
        }

    }
}