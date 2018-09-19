﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Reflection;

namespace FEBuilderGBA
{
    //リビルドしない領域にあるフリーエリア 再利用する場合に利用する.
    class ToolROMRebuildFreeArea
    {
        public ToolROMRebuildFreeArea( )
        {
        }

        List<Address> RecycleFreeAreaList = new List<Address>();
        public void MakeFreeAreaList(byte[] data, uint RebuildAddress, Dictionary<uint, uint> useMap)
        {
            MakeFreeDataList(RecycleFreeAreaList , 1024 , data, RebuildAddress , useMap);

            for (int i = 0; i < this.RecycleFreeAreaList.Count; i++)
            {
                Address p = this.RecycleFreeAreaList[i];
                //先頭16バイトは捨てましょう. 別データの終端データに使われているとまずい.
                p.ResizeAddress(p.Addr + 16, p.Length - 16 - 16);
                Log.Debug("FREEAREA " + U.To0xHexString(p.Addr) + " " + p.Length, " => " + U.To0xHexString(p.Addr + p.Length));
            }
        }

        //フリー領域と思われる部分を検出.
        void MakeFreeDataList(List<Address> list
            , uint needSize, byte[] data
            , uint length, Dictionary<uint, uint> useMap)
        {
            uint addr = U.Padding4(Program.ROM.RomInfo.compress_image_borderline_address());
            for (; addr < length; addr += 4)
            {
                byte filldata;
                if (data[addr] == 0x00 || data[addr] == 0xFF)
                {
                    if (useMap.ContainsKey(addr))
                    {
                        continue;
                    }
                    filldata = data[addr];

                    uint start = addr;
                    addr++;
                    for (; ; addr++)
                    {
                        if (addr >= length)
                        {
                            uint matchsize = addr - start;
                            if (matchsize >= needSize)
                            {
                                if (InputFormRef.DoEvents(null, "MakeFreeDataList " + U.ToHexString(addr))) return;
                                FEBuilderGBA.Address.AddAddress(list
                                    , start
                                    , matchsize
                                    , U.NOT_FOUND
                                    , ""
                                    , Address.DataTypeEnum.FFor00);
                            }
                            break;
                        }
                        if (data[addr] != filldata
                            ||  useMap.ContainsKey(addr) )
                        {
                            uint matchsize = addr - start;
                            if (matchsize >= needSize)
                            {
                                if (InputFormRef.DoEvents(null, "MakeFreeDataList " + U.ToHexString(addr))) return;
                                FEBuilderGBA.Address.AddAddress(list
                                    , start
                                    , matchsize
                                    , U.NOT_FOUND
                                    , ""
                                    , Address.DataTypeEnum.FFor00);
                            }
                            break;
                        }
                    }

                    addr = U.Padding4(addr);
                }
            }
        }

        
        
        //空き領域から割り当てができるか?
        public uint CanAllocFreeArea(uint needSize)
        {
            for (int i = 0; i < this.RecycleFreeAreaList.Count; i++)
            {
                Address p = this.RecycleFreeAreaList[i];
                if (p.Length >= needSize)
                {
                    uint use_addr = p.Addr;
                 
                    uint addr = U.Padding4(p.Addr + needSize);
                    uint length = U.Sub(p.Length, (addr - use_addr));

                    p.ResizeAddress(addr, length);
                    if (p.Length < 4)
                    {//もう空きがない.
                        this.RecycleFreeAreaList.RemoveAt(i);
                    }

                    return use_addr;
                }
            }
            //割当不可能
            return U.NOT_FOUND;
        }
    }
}