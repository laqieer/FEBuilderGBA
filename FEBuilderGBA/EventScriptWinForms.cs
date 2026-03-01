using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    // WinForms-dependent EventScript methods that depend on EventScriptForm.ScanScript.
    // The core EventScript class lives in FEBuilderGBA.Core/EventScript.cs.
    public static class EventScriptWinForms
    {
        //イベントから呼び出される特殊指定の領域を調べます.
        public static void MakeEventASMMAPList(List<Address> list)
        {
            MakeEventASMMAPList(list, false, "CALL_ASM_FROM_EVENT ",false);
            MakeEventASMMAPList(list, true, "CALL_EVENT ", false);
        }

        public static void MakeEventASMMAPList(List<Address> list, bool isEventOnly, string prefix, bool isPointerOnly)
        {
            List<uint> tracelist = new List<uint>();
            for (int i = 0; i < Program.EventScript.Scripts.Length; i++)
            {
                EventScript.Script script = Program.EventScript.Scripts[i];
                int length = script.Data.Length / 4;
                for (int n = 0; n < length; n++)
                {
                    uint addr = U.u32(script.Data, (uint)n * 4);
                    if (!U.isSafetyPointer(addr))
                    {
                        continue;
                    }

                    string name = string.Join("", script.Info);
                    if ((addr & 0x01) == 0x01)
                    {//thumb
                        if (isEventOnly == true)
                        {
                            continue;
                        }
                        addr = U.toOffset(addr);
                        addr = DisassemblerTrumb.ProgramAddrToPlain(addr);

                        FEBuilderGBA.Address.AddAddress(list, addr, 0, U.NOT_FOUND, "CALL_ASM_FROM_EVENT " + name, Address.DataTypeEnum.ASM);
                    }
                    else
                    {//普通のイベント命令
                        if (isEventOnly == false)
                        {
                            continue;
                        }
                        if (isPointerOnly)
                        {
                            addr = U.toOffset(addr);
                            FEBuilderGBA.Address.AddAddress(list, addr, 0, U.NOT_FOUND, "CALL_EVENT " + name, Address.DataTypeEnum.EVENTSCRIPT);
                        }
                        else
                        {
                            uint eventPointer = U.GrepPointer(Program.ROM.Data, addr, 0x0500000);
                            if (eventPointer != U.NOT_FOUND)
                            {//イベント探索はポインタが必要なので、探す...
                                EventScriptForm.ScanScript( list, eventPointer, true, false, "CALL_EVENT " + name , tracelist);
                            }
                            else
                            {
                                addr = U.toOffset(addr);
                                FEBuilderGBA.Address.AddAddress(list, addr, 0, U.NOT_FOUND, "CALL_EVENT " + name, Address.DataTypeEnum.EVENTSCRIPT);
                            }
                        }
                    }
                }
            }
        }
    }
}
