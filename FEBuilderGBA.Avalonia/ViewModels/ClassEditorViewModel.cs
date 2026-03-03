using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        string _name = "";
        uint _nameId, _descId, _classNumber;
        uint _baseHp, _baseStr, _baseSkl, _baseSpd, _baseDef, _baseRes;
        uint _growHp, _growStr, _growSkl, _growSpd, _growDef, _growRes, _growLck;
        uint _mov;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        public uint ClassNumber { get => _classNumber; set => SetField(ref _classNumber, value); }
        public uint BaseHp { get => _baseHp; set => SetField(ref _baseHp, value); }
        public uint BaseStr { get => _baseStr; set => SetField(ref _baseStr, value); }
        public uint BaseSkl { get => _baseSkl; set => SetField(ref _baseSkl, value); }
        public uint BaseSpd { get => _baseSpd; set => SetField(ref _baseSpd, value); }
        public uint BaseDef { get => _baseDef; set => SetField(ref _baseDef, value); }
        public uint BaseRes { get => _baseRes; set => SetField(ref _baseRes, value); }
        public uint GrowHp { get => _growHp; set => SetField(ref _growHp, value); }
        public uint GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public uint GrowSkl { get => _growSkl; set => SetField(ref _growSkl, value); }
        public uint GrowSpd { get => _growSpd; set => SetField(ref _growSpd, value); }
        public uint GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public uint GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public uint GrowLck { get => _growLck; set => SetField(ref _growLck, value); }
        public uint Mov { get => _mov; set => SetField(ref _mov, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadClassList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Validity check: class number at offset +4 must be non-zero for i > 0
                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadClass(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint minSize = rom.RomInfo.class_datasize;
            if (minSize < 34) minSize = 34;
            if (addr + minSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            NameId = rom.u16(addr + 0);
            DescId = rom.u16(addr + 2);
            ClassNumber = rom.u8(addr + 4);
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // Base stats (offsets approximate, based on WinForms ClassForm)
            BaseHp = rom.u8(addr + 11);
            BaseStr = rom.u8(addr + 12);
            BaseSkl = rom.u8(addr + 13);
            BaseSpd = rom.u8(addr + 14);
            BaseDef = rom.u8(addr + 15);
            BaseRes = rom.u8(addr + 16);

            // Growth rates
            GrowHp = rom.u8(addr + 27);
            GrowStr = rom.u8(addr + 28);
            GrowSkl = rom.u8(addr + 29);
            GrowSpd = rom.u8(addr + 30);
            GrowDef = rom.u8(addr + 31);
            GrowRes = rom.u8(addr + 32);
            GrowLck = rom.u8(addr + 33);

            // Movement
            Mov = rom.u8(addr + 17);

            CanWrite = true;
        }

        public void WriteClass()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u8(addr + 4, ClassNumber);

            rom.write_u8(addr + 11, BaseHp);
            rom.write_u8(addr + 12, BaseStr);
            rom.write_u8(addr + 13, BaseSkl);
            rom.write_u8(addr + 14, BaseSpd);
            rom.write_u8(addr + 15, BaseDef);
            rom.write_u8(addr + 16, BaseRes);

            rom.write_u8(addr + 27, GrowHp);
            rom.write_u8(addr + 28, GrowStr);
            rom.write_u8(addr + 29, GrowSkl);
            rom.write_u8(addr + 30, GrowSpd);
            rom.write_u8(addr + 31, GrowDef);
            rom.write_u8(addr + 32, GrowRes);
            rom.write_u8(addr + 33, GrowLck);

            rom.write_u8(addr + 17, Mov);
        }
    }
}
