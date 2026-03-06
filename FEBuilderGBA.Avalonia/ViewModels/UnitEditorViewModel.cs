using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _selectedId;
        string _name = "";
        bool _canWrite;
        bool _isFE6;

        // Identity (offsets 0-11)
        uint _nameId, _descId, _unitId, _classId;
        uint _portraitId, _mapFace, _affinity, _sortOrder, _level;

        // Base stats (offsets 12-19, signed bytes in WinForms)
        uint _hp, _str, _skl, _spd, _def, _res, _lck, _con;

        // Weapon levels (offsets 20-27)
        uint _wepSword, _wepLance, _wepAxe, _wepBow;
        uint _wepStaff, _wepAnima, _wepLight, _wepDark;

        // Growth rates (offsets 28-34)
        uint _growHP, _growStr, _growSkl, _growSpd, _growDef, _growRes, _growLck;

        // Unknown (offsets 35-39)
        uint _unk35, _unk36, _unk37, _unk38, _unk39;

        // Ability flags (offsets 40-43)
        uint _ability1, _ability2, _ability3, _ability4;

        // Support pointer (offset 44, 4 bytes)
        uint _supportPtr;

        // FE7/8 only (offsets 48-51)
        uint _talkGroup, _unk49, _unk50, _unk51;

        // Portrait image loaded from portrait table
        IImage? _portraitImage;

        // Properties — Identity
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public bool IsFE6 { get => _isFE6; set => SetField(ref _isFE6, value); }

        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint PortraitId { get => _portraitId; set => SetField(ref _portraitId, value); }
        public uint MapFace { get => _mapFace; set => SetField(ref _mapFace, value); }
        public uint Affinity { get => _affinity; set => SetField(ref _affinity, value); }
        public uint SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }

        // Properties — Base stats
        public uint HP { get => _hp; set => SetField(ref _hp, value); }
        public uint Str { get => _str; set => SetField(ref _str, value); }
        public uint Skl { get => _skl; set => SetField(ref _skl, value); }
        public uint Spd { get => _spd; set => SetField(ref _spd, value); }
        public uint Def { get => _def; set => SetField(ref _def, value); }
        public uint Res { get => _res; set => SetField(ref _res, value); }
        public uint Lck { get => _lck; set => SetField(ref _lck, value); }
        public uint Con { get => _con; set => SetField(ref _con, value); }

        // Properties — Weapon levels
        public uint WepSword { get => _wepSword; set => SetField(ref _wepSword, value); }
        public uint WepLance { get => _wepLance; set => SetField(ref _wepLance, value); }
        public uint WepAxe { get => _wepAxe; set => SetField(ref _wepAxe, value); }
        public uint WepBow { get => _wepBow; set => SetField(ref _wepBow, value); }
        public uint WepStaff { get => _wepStaff; set => SetField(ref _wepStaff, value); }
        public uint WepAnima { get => _wepAnima; set => SetField(ref _wepAnima, value); }
        public uint WepLight { get => _wepLight; set => SetField(ref _wepLight, value); }
        public uint WepDark { get => _wepDark; set => SetField(ref _wepDark, value); }

        // Properties — Growth rates
        public uint GrowHP { get => _growHP; set => SetField(ref _growHP, value); }
        public uint GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public uint GrowSkl { get => _growSkl; set => SetField(ref _growSkl, value); }
        public uint GrowSpd { get => _growSpd; set => SetField(ref _growSpd, value); }
        public uint GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public uint GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public uint GrowLck { get => _growLck; set => SetField(ref _growLck, value); }

        // Properties — Unknown 35-39
        public uint Unk35 { get => _unk35; set => SetField(ref _unk35, value); }
        public uint Unk36 { get => _unk36; set => SetField(ref _unk36, value); }
        public uint Unk37 { get => _unk37; set => SetField(ref _unk37, value); }
        public uint Unk38 { get => _unk38; set => SetField(ref _unk38, value); }
        public uint Unk39 { get => _unk39; set => SetField(ref _unk39, value); }

        // Properties — Ability flags
        public uint Ability1 { get => _ability1; set => SetField(ref _ability1, value); }
        public uint Ability2 { get => _ability2; set => SetField(ref _ability2, value); }
        public uint Ability3 { get => _ability3; set => SetField(ref _ability3, value); }
        public uint Ability4 { get => _ability4; set => SetField(ref _ability4, value); }

        // Properties — Support
        public uint SupportPtr { get => _supportPtr; set => SetField(ref _supportPtr, value); }

        // Properties — FE7/8 only
        public uint TalkGroup { get => _talkGroup; set => SetField(ref _talkGroup, value); }
        public uint Unk49 { get => _unk49; set => SetField(ref _unk49, value); }
        public uint Unk50 { get => _unk50; set => SetField(ref _unk50, value); }
        public uint Unk51 { get => _unk51; set => SetField(ref _unk51, value); }

        // Portrait image
        public IImage? PortraitImage => _portraitImage;

        public List<AddrResult> LoadUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            IsFE6 = rom.RomInfo.version == 6;

            // FE6: skip first entry (entry 0 is a null/pointer entry)
            if (IsFE6)
            {
                baseAddr += dataSize;
            }

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                // 1-based IDs to match WinForms UnitForm
                string name = U.ToHexString(i + 1) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            IsFE6 = rom.RomInfo.version == 6;
            CurrentAddr = addr;

            // Identity (common to all versions)
            NameId = rom.u16(addr + 0);       // W0
            DescId = rom.u16(addr + 2);       // W2
            UnitId = rom.u8(addr + 4);        // B4
            ClassId = rom.u8(addr + 5);       // B5
            PortraitId = rom.u16(addr + 6);   // W6
            MapFace = rom.u8(addr + 8);       // B8
            Affinity = rom.u8(addr + 9);      // B9
            SortOrder = rom.u8(addr + 10);    // B10
            Level = rom.u8(addr + 11);        // B11

            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // Base stats (b12-b19)
            HP = rom.u8(addr + 12);
            Str = rom.u8(addr + 13);
            Skl = rom.u8(addr + 14);
            Spd = rom.u8(addr + 15);
            Def = rom.u8(addr + 16);
            Res = rom.u8(addr + 17);
            Lck = rom.u8(addr + 18);
            Con = rom.u8(addr + 19);

            // Weapon levels (B20-B27)
            WepSword = rom.u8(addr + 20);
            WepLance = rom.u8(addr + 21);
            WepAxe = rom.u8(addr + 22);
            WepBow = rom.u8(addr + 23);
            WepStaff = rom.u8(addr + 24);
            WepAnima = rom.u8(addr + 25);
            WepLight = rom.u8(addr + 26);
            WepDark = rom.u8(addr + 27);

            // Growth rates (B28-B34)
            GrowHP = rom.u8(addr + 28);
            GrowStr = rom.u8(addr + 29);
            GrowSkl = rom.u8(addr + 30);
            GrowSpd = rom.u8(addr + 31);
            GrowDef = rom.u8(addr + 32);
            GrowRes = rom.u8(addr + 33);
            GrowLck = rom.u8(addr + 34);

            // Unknown (B35-B39)
            Unk35 = rom.u8(addr + 35);
            Unk36 = rom.u8(addr + 36);
            Unk37 = rom.u8(addr + 37);
            Unk38 = rom.u8(addr + 38);
            Unk39 = rom.u8(addr + 39);

            // Ability flags (B40-B43)
            Ability1 = rom.u8(addr + 40);
            Ability2 = rom.u8(addr + 41);
            Ability3 = rom.u8(addr + 42);
            Ability4 = rom.u8(addr + 43);

            // Support pointer (P44) — store raw value
            SupportPtr = rom.u32(addr + 44);

            // FE7/8 extended fields (B48-B51)
            if (!IsFE6 && addr + 52 <= (uint)rom.Data.Length)
            {
                TalkGroup = rom.u8(addr + 48);
                Unk49 = rom.u8(addr + 49);
                Unk50 = rom.u8(addr + 50);
                Unk51 = rom.u8(addr + 51);
            }
            else
            {
                TalkGroup = 0;
                Unk49 = 0;
                Unk50 = 0;
                Unk51 = 0;
            }

            CanWrite = true;
        }

        public int GetListCount() => LoadUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["NameId"] = $"0x{NameId:X04}",
                ["DescId"] = $"0x{DescId:X04}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["PortraitId"] = $"0x{PortraitId:X04}",
                ["MapFace"] = $"0x{MapFace:X02}",
                ["Affinity"] = $"0x{Affinity:X02}",
                ["SortOrder"] = $"0x{SortOrder:X02}",
                ["Level"] = $"0x{Level:X02}",
                ["HP"] = $"0x{HP:X02}",
                ["Str"] = $"0x{Str:X02}",
                ["Skl"] = $"0x{Skl:X02}",
                ["Spd"] = $"0x{Spd:X02}",
                ["Def"] = $"0x{Def:X02}",
                ["Res"] = $"0x{Res:X02}",
                ["Lck"] = $"0x{Lck:X02}",
                ["Con"] = $"0x{Con:X02}",
                ["WepSword"] = $"0x{WepSword:X02}",
                ["WepLance"] = $"0x{WepLance:X02}",
                ["WepAxe"] = $"0x{WepAxe:X02}",
                ["WepBow"] = $"0x{WepBow:X02}",
                ["WepStaff"] = $"0x{WepStaff:X02}",
                ["WepAnima"] = $"0x{WepAnima:X02}",
                ["WepLight"] = $"0x{WepLight:X02}",
                ["WepDark"] = $"0x{WepDark:X02}",
                ["GrowHP"] = $"0x{GrowHP:X02}",
                ["GrowStr"] = $"0x{GrowStr:X02}",
                ["GrowSkl"] = $"0x{GrowSkl:X02}",
                ["GrowSpd"] = $"0x{GrowSpd:X02}",
                ["GrowDef"] = $"0x{GrowDef:X02}",
                ["GrowRes"] = $"0x{GrowRes:X02}",
                ["GrowLck"] = $"0x{GrowLck:X02}",
                ["Ability1"] = $"0x{Ability1:X02}",
                ["Ability2"] = $"0x{Ability2:X02}",
                ["Ability3"] = $"0x{Ability3:X02}",
                ["Ability4"] = $"0x{Ability4:X02}",
                ["SupportPtr"] = $"0x{SupportPtr:X08}",
            };

            if (!IsFE6)
            {
                report["TalkGroup"] = $"0x{TalkGroup:X02}";
            }

            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23"] = $"0x{rom.u8(a + 35):X02}",
                ["u8@0x24"] = $"0x{rom.u8(a + 36):X02}",
                ["u8@0x25"] = $"0x{rom.u8(a + 37):X02}",
                ["u8@0x26"] = $"0x{rom.u8(a + 38):X02}",
                ["u8@0x27"] = $"0x{rom.u8(a + 39):X02}",
                ["u8@0x28"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29"] = $"0x{rom.u8(a + 41):X02}",
                ["u8@0x2A"] = $"0x{rom.u8(a + 42):X02}",
                ["u8@0x2B"] = $"0x{rom.u8(a + 43):X02}",
                ["u32@0x2C"] = $"0x{rom.u32(a + 44):X08}",
            };

            if (!IsFE6)
            {
                report["u8@0x30"] = $"0x{rom.u8(a + 48):X02}";
                report["u8@0x31"] = $"0x{rom.u8(a + 49):X02}";
                report["u8@0x32"] = $"0x{rom.u8(a + 50):X02}";
                report["u8@0x33"] = $"0x{rom.u8(a + 51):X02}";
            }

            return report;
        }

        public void WriteUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            // Identity
            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u8(addr + 4, UnitId);
            rom.write_u8(addr + 5, ClassId);
            rom.write_u16(addr + 6, PortraitId);
            rom.write_u8(addr + 8, MapFace);
            rom.write_u8(addr + 9, Affinity);
            rom.write_u8(addr + 10, SortOrder);
            rom.write_u8(addr + 11, Level);

            // Base stats
            rom.write_u8(addr + 12, HP);
            rom.write_u8(addr + 13, Str);
            rom.write_u8(addr + 14, Skl);
            rom.write_u8(addr + 15, Spd);
            rom.write_u8(addr + 16, Def);
            rom.write_u8(addr + 17, Res);
            rom.write_u8(addr + 18, Lck);
            rom.write_u8(addr + 19, Con);

            // Weapon levels
            rom.write_u8(addr + 20, WepSword);
            rom.write_u8(addr + 21, WepLance);
            rom.write_u8(addr + 22, WepAxe);
            rom.write_u8(addr + 23, WepBow);
            rom.write_u8(addr + 24, WepStaff);
            rom.write_u8(addr + 25, WepAnima);
            rom.write_u8(addr + 26, WepLight);
            rom.write_u8(addr + 27, WepDark);

            // Growth rates
            rom.write_u8(addr + 28, GrowHP);
            rom.write_u8(addr + 29, GrowStr);
            rom.write_u8(addr + 30, GrowSkl);
            rom.write_u8(addr + 31, GrowSpd);
            rom.write_u8(addr + 32, GrowDef);
            rom.write_u8(addr + 33, GrowRes);
            rom.write_u8(addr + 34, GrowLck);

            // Unknown
            rom.write_u8(addr + 35, Unk35);
            rom.write_u8(addr + 36, Unk36);
            rom.write_u8(addr + 37, Unk37);
            rom.write_u8(addr + 38, Unk38);
            rom.write_u8(addr + 39, Unk39);

            // Ability flags
            rom.write_u8(addr + 40, Ability1);
            rom.write_u8(addr + 41, Ability2);
            rom.write_u8(addr + 42, Ability3);
            rom.write_u8(addr + 43, Ability4);

            // Support pointer (write raw u32, not as GBA pointer)
            rom.write_u32(addr + 44, SupportPtr);

            // FE7/8 extended
            if (!IsFE6)
            {
                rom.write_u8(addr + 48, TalkGroup);
                rom.write_u8(addr + 49, Unk49);
                rom.write_u8(addr + 50, Unk50);
                rom.write_u8(addr + 51, Unk51);
            }
        }

        /// <summary>
        /// Load portrait image for the current unit's PortraitId.
        /// Sets PortraitRgba/Width/Height if successful.
        /// </summary>
        public void LoadPortraitImage()
        {
            _portraitImage?.Dispose();
            _portraitImage = null;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || PortraitId == 0) return;

            try
            {
                uint ptr = rom.RomInfo.portrait_pointer;
                if (ptr == 0) return;

                uint portraitBase = rom.p32(ptr);
                if (!U.isSafetyOffset(portraitBase)) return;

                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0) dataSize = 28;

                uint portraitAddr = portraitBase + PortraitId * dataSize;
                if (portraitAddr + dataSize > (uint)rom.Data.Length) return;

                uint imgPtr = rom.u32(portraitAddr + 4);  // offset 4 = map/mini face
                uint palPtr = rom.u32(portraitAddr + 8);

                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return;

                uint imgAddr = imgPtr - 0x08000000;
                uint palAddr = palPtr - 0x08000000;

                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return;

                _portraitImage = ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, true);
            }
            catch
            {
                _portraitImage = null;
            }
        }
    }
}
