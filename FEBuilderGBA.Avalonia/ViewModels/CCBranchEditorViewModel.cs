using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class CCBranchEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        string _className = "";
        uint _promotionClass1, _promotionClass2;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public uint PromotionClass1 { get => _promotionClass1; set => SetField(ref _promotionClass1, value); }
        public uint PromotionClass2 { get => _promotionClass2; set => SetField(ref _promotionClass2, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadCCBranchList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ccbranch_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Get class count from class_pointer for iteration limit
            uint classPtr = rom.RomInfo.class_pointer;
            uint classBase = (classPtr != 0) ? rom.p32(classPtr) : 0;
            uint classDataSize = rom.RomInfo.class_datasize;
            int classCount = 0;
            if (classBase != 0 && U.isSafetyOffset(classBase) && classDataSize > 0)
            {
                for (uint i = 0; i <= 0xFF; i++)
                {
                    uint classAddr = (uint)(classBase + i * classDataSize);
                    if (classAddr + classDataSize > (uint)rom.Data.Length) break;
                    if (i > 0 && rom.u8(classAddr + 4) == 0) break;
                    classCount++;
                }
            }
            if (classCount == 0) classCount = 0x80;

            var result = new List<AddrResult>();
            for (uint i = 0; i < (uint)classCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 2);
                if (addr + 1 >= (uint)rom.Data.Length) break;

                // Try to get class name
                string className;
                try
                {
                    if (classBase != 0 && classDataSize > 0)
                    {
                        uint classAddr = (uint)(classBase + i * classDataSize);
                        if (classAddr + 2 <= (uint)rom.Data.Length)
                        {
                            uint nameId = rom.u16(classAddr);
                            className = FETextDecode.Direct(nameId);
                        }
                        else className = "???";
                    }
                    else className = "???";
                }
                catch { className = "???"; }

                string name = U.ToHexString(i) + " " + className;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadCCBranch(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            PromotionClass1 = rom.u8(addr + 0);
            PromotionClass2 = rom.u8(addr + 1);
            CanWrite = true;
        }

        public void WriteCCBranch()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            rom.write_u8(CurrentAddr + 0, PromotionClass1);
            rom.write_u8(CurrentAddr + 1, PromotionClass2);
        }
    }
}
