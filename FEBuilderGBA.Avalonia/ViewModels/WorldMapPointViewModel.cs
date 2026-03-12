using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapPointViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "B0", "B1", "B2", "B3", "B4", "B5", "W6",
                "B8", "B9", "B10", "B11",
                "D12", "D16", "D20",
                "W24", "W26", "W28", "B30", "B31"
            });

        uint _currentAddr;
        bool _canWrite;
        uint _alwaysAccessible;
        uint _freeMapType;
        uint _preClearIcon;
        uint _postClearIcon;
        uint _chapterId1;
        uint _chapterId2;
        uint _eventBranchFlag;
        uint _nextNodeEirika;
        uint _nextNodeEphraim;
        uint _nextNodeEirika2nd;
        uint _nextNodeEphraim2nd;
        uint _armoryPointer;
        uint _vendorPointer;
        uint _secretShopPointer;
        uint _coordinateX;
        uint _coordinateY;
        uint _nameTextId;
        uint _shipSetting;
        uint _unknown31;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>B0: Whether this node is always accessible</summary>
        public uint AlwaysAccessible { get => _alwaysAccessible; set => SetField(ref _alwaysAccessible, value); }
        /// <summary>B1: Free map type (no monsters, monsters, tower/ruins)</summary>
        public uint FreeMapType { get => _freeMapType; set => SetField(ref _freeMapType, value); }
        /// <summary>B2: Icon shown before clearing the chapter</summary>
        public uint PreClearIcon { get => _preClearIcon; set => SetField(ref _preClearIcon, value); }
        /// <summary>B3: Icon shown after clearing the chapter</summary>
        public uint PostClearIcon { get => _postClearIcon; set => SetField(ref _postClearIcon, value); }
        /// <summary>B4: Chapter ID loaded when entering this node</summary>
        public uint ChapterId1 { get => _chapterId1; set => SetField(ref _chapterId1, value); }
        /// <summary>B5: Second chapter ID (used with event branch flag)</summary>
        public uint ChapterId2 { get => _chapterId2; set => SetField(ref _chapterId2, value); }
        /// <summary>W6: Event branch flag for route splitting</summary>
        public uint EventBranchFlag { get => _eventBranchFlag; set => SetField(ref _eventBranchFlag, value); }
        /// <summary>B8: Next node ID for Eirika route</summary>
        public uint NextNodeEirika { get => _nextNodeEirika; set => SetField(ref _nextNodeEirika, value); }
        /// <summary>B9: Next node ID for Ephraim route</summary>
        public uint NextNodeEphraim { get => _nextNodeEphraim; set => SetField(ref _nextNodeEphraim, value); }
        /// <summary>B10: Next node ID for Eirika 2nd visit</summary>
        public uint NextNodeEirika2nd { get => _nextNodeEirika2nd; set => SetField(ref _nextNodeEirika2nd, value); }
        /// <summary>B11: Next node ID for Ephraim 2nd visit</summary>
        public uint NextNodeEphraim2nd { get => _nextNodeEphraim2nd; set => SetField(ref _nextNodeEphraim2nd, value); }
        /// <summary>P12: Armory shop item list pointer</summary>
        public uint ArmoryPointer { get => _armoryPointer; set => SetField(ref _armoryPointer, value); }
        /// <summary>P16: Vendor shop item list pointer</summary>
        public uint VendorPointer { get => _vendorPointer; set => SetField(ref _vendorPointer, value); }
        /// <summary>P20: Secret shop item list pointer</summary>
        public uint SecretShopPointer { get => _secretShopPointer; set => SetField(ref _secretShopPointer, value); }
        /// <summary>W24: X coordinate on world map</summary>
        public uint CoordinateX { get => _coordinateX; set => SetField(ref _coordinateX, value); }
        /// <summary>W26: Y coordinate on world map</summary>
        public uint CoordinateY { get => _coordinateY; set => SetField(ref _coordinateY, value); }
        /// <summary>W28: Name text ID</summary>
        public uint NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }
        /// <summary>B30: Ship setting (0=no ship, 1=use ship)</summary>
        public uint ShipSetting { get => _shipSetting; set => SetField(ref _shipSetting, value); }
        /// <summary>B31: Unknown</summary>
        public uint Unknown31 { get => _unknown31; set => SetField(ref _unknown31, value); }

        public List<AddrResult> LoadWorldMapPointList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                // Termination: pointers at +12, +16, +20 must be pointer-or-null
                if (!U.isPointerOrNULL(rom.u32(addr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 16))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 20))) break;

                uint nameTextId = rom.u16(addr + 28);
                string pointName = nameTextId != 0 ? NameResolver.GetTextById(nameTextId) : "???";
                string name = $"{U.ToHexString(i)} {pointName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadWorldMapPoint(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            AlwaysAccessible = values["B0"];
            FreeMapType = values["B1"];
            PreClearIcon = values["B2"];
            PostClearIcon = values["B3"];
            ChapterId1 = values["B4"];
            ChapterId2 = values["B5"];
            EventBranchFlag = values["W6"];
            NextNodeEirika = values["B8"];
            NextNodeEphraim = values["B9"];
            NextNodeEirika2nd = values["B10"];
            NextNodeEphraim2nd = values["B11"];
            ArmoryPointer = values["D12"];
            VendorPointer = values["D16"];
            SecretShopPointer = values["D20"];
            CoordinateX = values["W24"];
            CoordinateY = values["W26"];
            NameTextId = values["W28"];
            ShipSetting = values["B30"];
            Unknown31 = values["B31"];
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 32 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = AlwaysAccessible, ["B1"] = FreeMapType,
                ["B2"] = PreClearIcon, ["B3"] = PostClearIcon,
                ["B4"] = ChapterId1, ["B5"] = ChapterId2,
                ["W6"] = EventBranchFlag,
                ["B8"] = NextNodeEirika, ["B9"] = NextNodeEphraim,
                ["B10"] = NextNodeEirika2nd, ["B11"] = NextNodeEphraim2nd,
                ["D12"] = ArmoryPointer, ["D16"] = VendorPointer,
                ["D20"] = SecretShopPointer,
                ["W24"] = CoordinateX, ["W26"] = CoordinateY,
                ["W28"] = NameTextId, ["B30"] = ShipSetting,
                ["B31"] = Unknown31,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadWorldMapPointList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AlwaysAccessible"] = $"0x{AlwaysAccessible:X02}",
                ["FreeMapType"] = $"0x{FreeMapType:X02}",
                ["PreClearIcon"] = $"0x{PreClearIcon:X02}",
                ["PostClearIcon"] = $"0x{PostClearIcon:X02}",
                ["ChapterId1"] = $"0x{ChapterId1:X02}",
                ["ChapterId2"] = $"0x{ChapterId2:X02}",
                ["EventBranchFlag"] = $"0x{EventBranchFlag:X04}",
                ["NextNodeEirika"] = $"0x{NextNodeEirika:X02}",
                ["NextNodeEphraim"] = $"0x{NextNodeEphraim:X02}",
                ["NextNodeEirika2nd"] = $"0x{NextNodeEirika2nd:X02}",
                ["NextNodeEphraim2nd"] = $"0x{NextNodeEphraim2nd:X02}",
                ["ArmoryPointer"] = $"0x{ArmoryPointer:X08}",
                ["VendorPointer"] = $"0x{VendorPointer:X08}",
                ["SecretShopPointer"] = $"0x{SecretShopPointer:X08}",
                ["CoordinateX"] = $"0x{CoordinateX:X04}",
                ["CoordinateY"] = $"0x{CoordinateY:X04}",
                ["NameTextId"] = $"0x{NameTextId:X04}",
                ["ShipSetting"] = $"0x{ShipSetting:X02}",
                ["Unknown31"] = $"0x{Unknown31:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["AlwaysAccessible@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["FreeMapType@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["PreClearIcon@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["PostClearIcon@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["ChapterId1@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["ChapterId2@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["EventBranchFlag@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["NextNodeEirika@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["NextNodeEphraim@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["NextNodeEirika2nd@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["NextNodeEphraim2nd@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["ArmoryPointer@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["VendorPointer@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["SecretShopPointer@0x14"] = $"0x{rom.u32(a + 20):X08}",
                ["CoordinateX@0x18"] = $"0x{rom.u16(a + 24):X04}",
                ["CoordinateY@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["NameTextId@0x1C"] = $"0x{rom.u16(a + 28):X04}",
                ["ShipSetting@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["Unknown31@0x1F"] = $"0x{rom.u8(a + 31):X02}",
            };
        }
    }
}
