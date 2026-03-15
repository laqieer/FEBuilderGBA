using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Identifies which terrain cost table to load from the class struct.
    /// FE7/FE8 have all 6 per-class types; FE6 lacks Rain and Snow.
    /// Additional global tables (Recovery, Ballista) are also supported.
    /// </summary>
    public enum CostType
    {
        MoveCostNormal = 0,
        MoveCostRain = 1,
        MoveCostSnow = 2,
        TerrainAvoid = 3,
        TerrainDefense = 4,
        TerrainResistance = 5,
        TerrainRecovery = 6,
        BallistaMoveCost = 9,
    }

    /// <summary>
    /// Represents a cost type combo item for display in the UI.
    /// </summary>
    public class CostTypeItem
    {
        public CostType CostType { get; }
        public string DisplayName { get; }

        public CostTypeItem(CostType costType, string displayName)
        {
            CostType = costType;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    public class MoveCostEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _className = "";
        bool _canWrite;
        uint _moveCostAddr;
        CostType _selectedCostType = CostType.MoveCostNormal;
        List<CostTypeItem> _costTypeItems = new();
        int _selectedCostTypeIndex;

        // 65 terrain cost bytes (B0-B64), one per terrain type
        byte _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9;
        byte _b10, _b11, _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        byte _b20, _b21, _b22, _b23, _b24, _b25, _b26, _b27, _b28, _b29;
        byte _b30, _b31, _b32, _b33, _b34, _b35, _b36, _b37, _b38, _b39;
        byte _b40, _b41, _b42, _b43, _b44, _b45, _b46, _b47, _b48, _b49;
        byte _b50, _b51, _b52, _b53, _b54, _b55, _b56, _b57, _b58, _b59;
        byte _b60, _b61, _b62, _b63, _b64;

        // Terrain names
        string[] _terrainNames = Array.Empty<string>();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint MoveCostAddr { get => _moveCostAddr; set => SetField(ref _moveCostAddr, value); }
        public string[] TerrainNames { get => _terrainNames; set => SetField(ref _terrainNames, value); }
        public CostType SelectedCostType { get => _selectedCostType; set => SetField(ref _selectedCostType, value); }
        public List<CostTypeItem> CostTypeItems { get => _costTypeItems; set => SetField(ref _costTypeItems, value); }
        public int SelectedCostTypeIndex { get => _selectedCostTypeIndex; set => SetField(ref _selectedCostTypeIndex, value); }

        // 65 individual byte properties (B0 through B64)
        public byte B0 { get => _b0; set => SetField(ref _b0, value); }
        public byte B1 { get => _b1; set => SetField(ref _b1, value); }
        public byte B2 { get => _b2; set => SetField(ref _b2, value); }
        public byte B3 { get => _b3; set => SetField(ref _b3, value); }
        public byte B4 { get => _b4; set => SetField(ref _b4, value); }
        public byte B5 { get => _b5; set => SetField(ref _b5, value); }
        public byte B6 { get => _b6; set => SetField(ref _b6, value); }
        public byte B7 { get => _b7; set => SetField(ref _b7, value); }
        public byte B8 { get => _b8; set => SetField(ref _b8, value); }
        public byte B9 { get => _b9; set => SetField(ref _b9, value); }
        public byte B10 { get => _b10; set => SetField(ref _b10, value); }
        public byte B11 { get => _b11; set => SetField(ref _b11, value); }
        public byte B12 { get => _b12; set => SetField(ref _b12, value); }
        public byte B13 { get => _b13; set => SetField(ref _b13, value); }
        public byte B14 { get => _b14; set => SetField(ref _b14, value); }
        public byte B15 { get => _b15; set => SetField(ref _b15, value); }
        public byte B16 { get => _b16; set => SetField(ref _b16, value); }
        public byte B17 { get => _b17; set => SetField(ref _b17, value); }
        public byte B18 { get => _b18; set => SetField(ref _b18, value); }
        public byte B19 { get => _b19; set => SetField(ref _b19, value); }
        public byte B20 { get => _b20; set => SetField(ref _b20, value); }
        public byte B21 { get => _b21; set => SetField(ref _b21, value); }
        public byte B22 { get => _b22; set => SetField(ref _b22, value); }
        public byte B23 { get => _b23; set => SetField(ref _b23, value); }
        public byte B24 { get => _b24; set => SetField(ref _b24, value); }
        public byte B25 { get => _b25; set => SetField(ref _b25, value); }
        public byte B26 { get => _b26; set => SetField(ref _b26, value); }
        public byte B27 { get => _b27; set => SetField(ref _b27, value); }
        public byte B28 { get => _b28; set => SetField(ref _b28, value); }
        public byte B29 { get => _b29; set => SetField(ref _b29, value); }
        public byte B30 { get => _b30; set => SetField(ref _b30, value); }
        public byte B31 { get => _b31; set => SetField(ref _b31, value); }
        public byte B32 { get => _b32; set => SetField(ref _b32, value); }
        public byte B33 { get => _b33; set => SetField(ref _b33, value); }
        public byte B34 { get => _b34; set => SetField(ref _b34, value); }
        public byte B35 { get => _b35; set => SetField(ref _b35, value); }
        public byte B36 { get => _b36; set => SetField(ref _b36, value); }
        public byte B37 { get => _b37; set => SetField(ref _b37, value); }
        public byte B38 { get => _b38; set => SetField(ref _b38, value); }
        public byte B39 { get => _b39; set => SetField(ref _b39, value); }
        public byte B40 { get => _b40; set => SetField(ref _b40, value); }
        public byte B41 { get => _b41; set => SetField(ref _b41, value); }
        public byte B42 { get => _b42; set => SetField(ref _b42, value); }
        public byte B43 { get => _b43; set => SetField(ref _b43, value); }
        public byte B44 { get => _b44; set => SetField(ref _b44, value); }
        public byte B45 { get => _b45; set => SetField(ref _b45, value); }
        public byte B46 { get => _b46; set => SetField(ref _b46, value); }
        public byte B47 { get => _b47; set => SetField(ref _b47, value); }
        public byte B48 { get => _b48; set => SetField(ref _b48, value); }
        public byte B49 { get => _b49; set => SetField(ref _b49, value); }
        public byte B50 { get => _b50; set => SetField(ref _b50, value); }
        public byte B51 { get => _b51; set => SetField(ref _b51, value); }
        public byte B52 { get => _b52; set => SetField(ref _b52, value); }
        public byte B53 { get => _b53; set => SetField(ref _b53, value); }
        public byte B54 { get => _b54; set => SetField(ref _b54, value); }
        public byte B55 { get => _b55; set => SetField(ref _b55, value); }
        public byte B56 { get => _b56; set => SetField(ref _b56, value); }
        public byte B57 { get => _b57; set => SetField(ref _b57, value); }
        public byte B58 { get => _b58; set => SetField(ref _b58, value); }
        public byte B59 { get => _b59; set => SetField(ref _b59, value); }
        public byte B60 { get => _b60; set => SetField(ref _b60, value); }
        public byte B61 { get => _b61; set => SetField(ref _b61, value); }
        public byte B62 { get => _b62; set => SetField(ref _b62, value); }
        public byte B63 { get => _b63; set => SetField(ref _b63, value); }
        public byte B64 { get => _b64; set => SetField(ref _b64, value); }

        /// <summary>
        /// Convenience property to get/set all 65 move costs as an array.
        /// </summary>
        public byte[] MoveCosts
        {
            get => new byte[]
            {
                B0, B1, B2, B3, B4, B5, B6, B7, B8, B9,
                B10, B11, B12, B13, B14, B15, B16, B17, B18, B19,
                B20, B21, B22, B23, B24, B25, B26, B27, B28, B29,
                B30, B31, B32, B33, B34, B35, B36, B37, B38, B39,
                B40, B41, B42, B43, B44, B45, B46, B47, B48, B49,
                B50, B51, B52, B53, B54, B55, B56, B57, B58, B59,
                B60, B61, B62, B63, B64,
            };
        }

        /// <summary>Total terrain count: 65 (indices 0 through 64).</summary>
        public const int TerrainCount = 65;

        /// <summary>
        /// Build the list of available cost types based on the ROM version.
        /// FE6 lacks Rain and Snow cost types.
        /// </summary>
        public void BuildCostTypeItems()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            var items = new List<CostTypeItem>();
            items.Add(new CostTypeItem(CostType.MoveCostNormal, "Move Cost (Normal)"));
            if (rom.RomInfo.version != 6)
            {
                items.Add(new CostTypeItem(CostType.MoveCostRain, "Move Cost (Rain)"));
                items.Add(new CostTypeItem(CostType.MoveCostSnow, "Move Cost (Snow)"));
            }
            items.Add(new CostTypeItem(CostType.TerrainAvoid, "Terrain Avoid"));
            items.Add(new CostTypeItem(CostType.TerrainDefense, "Terrain Defense"));
            items.Add(new CostTypeItem(CostType.TerrainResistance, "Terrain Resistance"));
            items.Add(new CostTypeItem(CostType.TerrainRecovery, "Terrain Recovery"));
            if (rom.RomInfo.version != 6)
            {
                items.Add(new CostTypeItem(CostType.BallistaMoveCost, "Ballista Move Cost"));
            }
            CostTypeItems = items;
            if (items.Count > 0)
            {
                SelectedCostTypeIndex = 0;
                SelectedCostType = items[0].CostType;
            }
        }

        /// <summary>
        /// Get the ROM address that contains the pointer to the cost table
        /// for a given class address and cost type.
        /// Returns 0 if the cost type is not available for this ROM version.
        /// </summary>
        public static uint GetMoveCostPointerAddr(uint classAddr, CostType costType)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !U.isSafetyOffset(classAddr)) return 0;

            if (rom.RomInfo.version == 6)
            {
                // FE6: class_datasize = 72
                switch (costType)
                {
                    case CostType.MoveCostNormal: return classAddr + 52;
                    case CostType.MoveCostRain: return 0; // FE6 has no rain
                    case CostType.MoveCostSnow: return 0; // FE6 has no snow
                    case CostType.TerrainAvoid: return classAddr + 56;
                    case CostType.TerrainDefense: return classAddr + 60;
                    case CostType.TerrainResistance: return classAddr + 64;
                    case CostType.TerrainRecovery: return rom.RomInfo.terrain_recovery_pointer;
                    default: return 0;
                }
            }
            else
            {
                // FE7/FE8: class_datasize = 84
                switch (costType)
                {
                    case CostType.MoveCostNormal: return classAddr + 56;
                    case CostType.MoveCostRain: return classAddr + 60;
                    case CostType.MoveCostSnow: return classAddr + 64;
                    case CostType.TerrainAvoid: return classAddr + 68;
                    case CostType.TerrainDefense: return classAddr + 72;
                    case CostType.TerrainResistance: return classAddr + 76;
                    case CostType.TerrainRecovery: return rom.RomInfo.terrain_recovery_pointer;
                    case CostType.BallistaMoveCost: return rom.RomInfo.ballista_movcost_pointer;
                    default: return 0;
                }
            }
        }

        /// <summary>
        /// Load class list (same as ClassEditor but we read the move cost table pointer).
        /// </summary>
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

                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Load terrain names from ROM (terrain name table).
        /// </summary>
        public void LoadTerrainNames()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            var names = new string[TerrainCount];
            uint terrainNamePtr = rom.RomInfo.map_terrain_name_pointer;
            uint terrainNameBase = 0;
            if (terrainNamePtr != 0 && U.isSafetyOffset(terrainNamePtr))
                terrainNameBase = rom.p32(terrainNamePtr);

            for (int i = 0; i < TerrainCount; i++)
            {
                string name = $"0x{i:X2}";
                if (terrainNameBase != 0)
                {
                    try
                    {
                        uint entryAddr = (uint)(terrainNameBase + i * 4);
                        if (U.isSafetyOffset(entryAddr + 3))
                        {
                            uint strPtr = rom.p32(entryAddr);
                            if (U.isSafetyOffset(strPtr))
                            {
                                string decoded = rom.getString(strPtr);
                                if (!string.IsNullOrEmpty(decoded))
                                    name = $"0x{i:X2} {decoded}";
                            }
                        }
                    }
                    catch { /* ignore decode errors */ }
                }
                names[i] = name;
            }
            TerrainNames = names;
        }

        /// <summary>
        /// Load move cost table for a class using the currently selected cost type.
        /// </summary>
        public void LoadMoveCost(uint classAddr)
        {
            LoadMoveCost(classAddr, SelectedCostType);
        }

        /// <summary>
        /// Load move cost table for a class with a specific cost type.
        /// </summary>
        public void LoadMoveCost(uint classAddr, CostType costType)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.class_datasize;
            if (classAddr + dataSize > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = classAddr;
            SelectedCostType = costType;

            uint nameId = rom.u16(classAddr + 0);
            try { ClassName = NameResolver.GetTextById(nameId); }
            catch { ClassName = "???"; }

            uint pointerAddr = GetMoveCostPointerAddr(classAddr, costType);
            if (pointerAddr == 0 || !U.isSafetyOffset(pointerAddr))
            {
                ClearAllFields();
                CanWrite = false;
                IsLoading = false;
                return;
            }

            if (pointerAddr + 3 >= (uint)rom.Data.Length)
            {
                ClearAllFields();
                CanWrite = false;
                IsLoading = false;
                return;
            }

            uint moveCostPtr = rom.u32(pointerAddr);
            if (!U.isPointer(moveCostPtr))
            {
                ClearAllFields();
                CanWrite = false;
                IsLoading = false;
                return;
            }

            uint moveCostAddr = moveCostPtr - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr))
            {
                ClearAllFields();
                CanWrite = false;
                IsLoading = false;
                return;
            }

            MoveCostAddr = moveCostAddr;

            if (moveCostAddr + TerrainCount > (uint)rom.Data.Length)
            {
                ClearAllFields();
                CanWrite = false;
                IsLoading = false;
                return;
            }

            for (int i = 0; i < TerrainCount; i++)
                SetCost(i, (byte)rom.u8((uint)(moveCostAddr + i)));

            CanWrite = true;
            IsLoading = false;
            MarkClean();
        }

        /// <summary>
        /// Write all 65 move cost bytes back to ROM.
        /// </summary>
        public void WriteMoveCost()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !CanWrite) return;

            uint addr = MoveCostAddr;
            if (!U.isSafetyOffset(addr) || addr + TerrainCount > (uint)rom.Data.Length) return;

            for (int i = 0; i < TerrainCount; i++)
                rom.write_u8((uint)(addr + i), GetCost(i));
        }

        public byte GetCost(int index)
        {
            return index switch
            {
                0 => B0, 1 => B1, 2 => B2, 3 => B3, 4 => B4,
                5 => B5, 6 => B6, 7 => B7, 8 => B8, 9 => B9,
                10 => B10, 11 => B11, 12 => B12, 13 => B13, 14 => B14,
                15 => B15, 16 => B16, 17 => B17, 18 => B18, 19 => B19,
                20 => B20, 21 => B21, 22 => B22, 23 => B23, 24 => B24,
                25 => B25, 26 => B26, 27 => B27, 28 => B28, 29 => B29,
                30 => B30, 31 => B31, 32 => B32, 33 => B33, 34 => B34,
                35 => B35, 36 => B36, 37 => B37, 38 => B38, 39 => B39,
                40 => B40, 41 => B41, 42 => B42, 43 => B43, 44 => B44,
                45 => B45, 46 => B46, 47 => B47, 48 => B48, 49 => B49,
                50 => B50, 51 => B51, 52 => B52, 53 => B53, 54 => B54,
                55 => B55, 56 => B56, 57 => B57, 58 => B58, 59 => B59,
                60 => B60, 61 => B61, 62 => B62, 63 => B63, 64 => B64,
                _ => 0
            };
        }

        public void SetCost(int index, byte value)
        {
            switch (index)
            {
                case 0: B0 = value; break; case 1: B1 = value; break;
                case 2: B2 = value; break; case 3: B3 = value; break;
                case 4: B4 = value; break; case 5: B5 = value; break;
                case 6: B6 = value; break; case 7: B7 = value; break;
                case 8: B8 = value; break; case 9: B9 = value; break;
                case 10: B10 = value; break; case 11: B11 = value; break;
                case 12: B12 = value; break; case 13: B13 = value; break;
                case 14: B14 = value; break; case 15: B15 = value; break;
                case 16: B16 = value; break; case 17: B17 = value; break;
                case 18: B18 = value; break; case 19: B19 = value; break;
                case 20: B20 = value; break; case 21: B21 = value; break;
                case 22: B22 = value; break; case 23: B23 = value; break;
                case 24: B24 = value; break; case 25: B25 = value; break;
                case 26: B26 = value; break; case 27: B27 = value; break;
                case 28: B28 = value; break; case 29: B29 = value; break;
                case 30: B30 = value; break; case 31: B31 = value; break;
                case 32: B32 = value; break; case 33: B33 = value; break;
                case 34: B34 = value; break; case 35: B35 = value; break;
                case 36: B36 = value; break; case 37: B37 = value; break;
                case 38: B38 = value; break; case 39: B39 = value; break;
                case 40: B40 = value; break; case 41: B41 = value; break;
                case 42: B42 = value; break; case 43: B43 = value; break;
                case 44: B44 = value; break; case 45: B45 = value; break;
                case 46: B46 = value; break; case 47: B47 = value; break;
                case 48: B48 = value; break; case 49: B49 = value; break;
                case 50: B50 = value; break; case 51: B51 = value; break;
                case 52: B52 = value; break; case 53: B53 = value; break;
                case 54: B54 = value; break; case 55: B55 = value; break;
                case 56: B56 = value; break; case 57: B57 = value; break;
                case 58: B58 = value; break; case 59: B59 = value; break;
                case 60: B60 = value; break; case 61: B61 = value; break;
                case 62: B62 = value; break; case 63: B63 = value; break;
                case 64: B64 = value; break;
            }
        }

        void ClearAllFields()
        {
            for (int i = 0; i < TerrainCount; i++)
                SetCost(i, 0);
            MoveCostAddr = 0;
        }

        public int GetListCount() => LoadClassList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["moveCostAddr"] = $"0x{MoveCostAddr:X08}",
                ["costType"] = SelectedCostType.ToString(),
            };
            for (int i = 0; i < TerrainCount; i++)
            {
                report[$"B{i}"] = $"0x{GetCost(i):X02}";
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
                ["costType"] = SelectedCostType.ToString(),
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",  // nameId
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",   // class type
            };

            uint pointerAddr = GetMoveCostPointerAddr(a, SelectedCostType);
            if (pointerAddr != 0 && U.isSafetyOffset(pointerAddr) && pointerAddr + 3 < (uint)rom.Data.Length)
            {
                if (pointerAddr >= a && pointerAddr < a + rom.RomInfo.class_datasize)
                {
                    uint offset = pointerAddr - a;
                    report[$"u32@0x{offset:X02}"] = $"0x{rom.u32(pointerAddr):X08}";
                }
                else
                {
                    report[$"u32@0x{pointerAddr:X08}"] = $"0x{rom.u32(pointerAddr):X08}";
                }
            }

            if (MoveCostAddr == 0 || !U.isSafetyOffset(MoveCostAddr)) return report;
            uint m = MoveCostAddr;
            if (m + TerrainCount > (uint)rom.Data.Length) return report;
            for (int i = 0; i < TerrainCount; i++)
            {
                report[$"u8@0x{i:X02}"] = $"0x{rom.u8((uint)(m + i)):X02}";
            }
            return report;
        }
    }
}
