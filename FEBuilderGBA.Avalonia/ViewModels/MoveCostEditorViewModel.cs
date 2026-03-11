using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveCostEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _className = "";
        bool _canWrite;
        uint _moveCostAddr;

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
                B0,  B1,  B2,  B3,  B4,  B5,  B6,  B7,  B8,  B9,
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
                try { decoded = FETextDecode.Direct(nameId); }
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
                        // Each terrain name entry is a 4-byte pointer to a string
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
        /// Load move cost table for a class.
        /// The move cost pointer is at a version-specific offset within the class struct.
        /// FE6: offset 52 (sunny move cost), FE7/FE8: offset 56.
        /// </summary>
        public void LoadMoveCost(uint classAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.class_datasize;
            if (classAddr + dataSize > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = classAddr;

            uint nameId = rom.u16(classAddr + 0);
            try { ClassName = FETextDecode.Direct(nameId); }
            catch { ClassName = "???"; }

            // Move cost pointer offset varies by version:
            // FE6: offset 52 (sunny move cost)
            // FE7/FE8: offset 56 (sunny move cost)
            uint moveCostPtrOffset;
            if (rom.RomInfo.version == 6)
                moveCostPtrOffset = 52;
            else
                moveCostPtrOffset = 56;

            if (classAddr + moveCostPtrOffset + 3 >= (uint)rom.Data.Length)
            {
                ClearAllFields();
                CanWrite = false;
                return;
            }

            uint moveCostPtr = rom.u32(classAddr + moveCostPtrOffset);
            if (!U.isPointer(moveCostPtr))
            {
                ClearAllFields();
                CanWrite = false;
                return;
            }

            uint moveCostAddr = moveCostPtr - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr))
            {
                ClearAllFields();
                CanWrite = false;
                return;
            }

            MoveCostAddr = moveCostAddr;

            // Read all 65 terrain move costs (B0 through B64)
            if (moveCostAddr + TerrainCount > (uint)rom.Data.Length)
            {
                ClearAllFields();
                CanWrite = false;
                return;
            }

            B0 = (byte)rom.u8(moveCostAddr + 0);
            B1 = (byte)rom.u8(moveCostAddr + 1);
            B2 = (byte)rom.u8(moveCostAddr + 2);
            B3 = (byte)rom.u8(moveCostAddr + 3);
            B4 = (byte)rom.u8(moveCostAddr + 4);
            B5 = (byte)rom.u8(moveCostAddr + 5);
            B6 = (byte)rom.u8(moveCostAddr + 6);
            B7 = (byte)rom.u8(moveCostAddr + 7);
            B8 = (byte)rom.u8(moveCostAddr + 8);
            B9 = (byte)rom.u8(moveCostAddr + 9);
            B10 = (byte)rom.u8(moveCostAddr + 10);
            B11 = (byte)rom.u8(moveCostAddr + 11);
            B12 = (byte)rom.u8(moveCostAddr + 12);
            B13 = (byte)rom.u8(moveCostAddr + 13);
            B14 = (byte)rom.u8(moveCostAddr + 14);
            B15 = (byte)rom.u8(moveCostAddr + 15);
            B16 = (byte)rom.u8(moveCostAddr + 16);
            B17 = (byte)rom.u8(moveCostAddr + 17);
            B18 = (byte)rom.u8(moveCostAddr + 18);
            B19 = (byte)rom.u8(moveCostAddr + 19);
            B20 = (byte)rom.u8(moveCostAddr + 20);
            B21 = (byte)rom.u8(moveCostAddr + 21);
            B22 = (byte)rom.u8(moveCostAddr + 22);
            B23 = (byte)rom.u8(moveCostAddr + 23);
            B24 = (byte)rom.u8(moveCostAddr + 24);
            B25 = (byte)rom.u8(moveCostAddr + 25);
            B26 = (byte)rom.u8(moveCostAddr + 26);
            B27 = (byte)rom.u8(moveCostAddr + 27);
            B28 = (byte)rom.u8(moveCostAddr + 28);
            B29 = (byte)rom.u8(moveCostAddr + 29);
            B30 = (byte)rom.u8(moveCostAddr + 30);
            B31 = (byte)rom.u8(moveCostAddr + 31);
            B32 = (byte)rom.u8(moveCostAddr + 32);
            B33 = (byte)rom.u8(moveCostAddr + 33);
            B34 = (byte)rom.u8(moveCostAddr + 34);
            B35 = (byte)rom.u8(moveCostAddr + 35);
            B36 = (byte)rom.u8(moveCostAddr + 36);
            B37 = (byte)rom.u8(moveCostAddr + 37);
            B38 = (byte)rom.u8(moveCostAddr + 38);
            B39 = (byte)rom.u8(moveCostAddr + 39);
            B40 = (byte)rom.u8(moveCostAddr + 40);
            B41 = (byte)rom.u8(moveCostAddr + 41);
            B42 = (byte)rom.u8(moveCostAddr + 42);
            B43 = (byte)rom.u8(moveCostAddr + 43);
            B44 = (byte)rom.u8(moveCostAddr + 44);
            B45 = (byte)rom.u8(moveCostAddr + 45);
            B46 = (byte)rom.u8(moveCostAddr + 46);
            B47 = (byte)rom.u8(moveCostAddr + 47);
            B48 = (byte)rom.u8(moveCostAddr + 48);
            B49 = (byte)rom.u8(moveCostAddr + 49);
            B50 = (byte)rom.u8(moveCostAddr + 50);
            B51 = (byte)rom.u8(moveCostAddr + 51);
            B52 = (byte)rom.u8(moveCostAddr + 52);
            B53 = (byte)rom.u8(moveCostAddr + 53);
            B54 = (byte)rom.u8(moveCostAddr + 54);
            B55 = (byte)rom.u8(moveCostAddr + 55);
            B56 = (byte)rom.u8(moveCostAddr + 56);
            B57 = (byte)rom.u8(moveCostAddr + 57);
            B58 = (byte)rom.u8(moveCostAddr + 58);
            B59 = (byte)rom.u8(moveCostAddr + 59);
            B60 = (byte)rom.u8(moveCostAddr + 60);
            B61 = (byte)rom.u8(moveCostAddr + 61);
            B62 = (byte)rom.u8(moveCostAddr + 62);
            B63 = (byte)rom.u8(moveCostAddr + 63);
            B64 = (byte)rom.u8(moveCostAddr + 64);

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

            rom.write_u8(addr + 0, B0);
            rom.write_u8(addr + 1, B1);
            rom.write_u8(addr + 2, B2);
            rom.write_u8(addr + 3, B3);
            rom.write_u8(addr + 4, B4);
            rom.write_u8(addr + 5, B5);
            rom.write_u8(addr + 6, B6);
            rom.write_u8(addr + 7, B7);
            rom.write_u8(addr + 8, B8);
            rom.write_u8(addr + 9, B9);
            rom.write_u8(addr + 10, B10);
            rom.write_u8(addr + 11, B11);
            rom.write_u8(addr + 12, B12);
            rom.write_u8(addr + 13, B13);
            rom.write_u8(addr + 14, B14);
            rom.write_u8(addr + 15, B15);
            rom.write_u8(addr + 16, B16);
            rom.write_u8(addr + 17, B17);
            rom.write_u8(addr + 18, B18);
            rom.write_u8(addr + 19, B19);
            rom.write_u8(addr + 20, B20);
            rom.write_u8(addr + 21, B21);
            rom.write_u8(addr + 22, B22);
            rom.write_u8(addr + 23, B23);
            rom.write_u8(addr + 24, B24);
            rom.write_u8(addr + 25, B25);
            rom.write_u8(addr + 26, B26);
            rom.write_u8(addr + 27, B27);
            rom.write_u8(addr + 28, B28);
            rom.write_u8(addr + 29, B29);
            rom.write_u8(addr + 30, B30);
            rom.write_u8(addr + 31, B31);
            rom.write_u8(addr + 32, B32);
            rom.write_u8(addr + 33, B33);
            rom.write_u8(addr + 34, B34);
            rom.write_u8(addr + 35, B35);
            rom.write_u8(addr + 36, B36);
            rom.write_u8(addr + 37, B37);
            rom.write_u8(addr + 38, B38);
            rom.write_u8(addr + 39, B39);
            rom.write_u8(addr + 40, B40);
            rom.write_u8(addr + 41, B41);
            rom.write_u8(addr + 42, B42);
            rom.write_u8(addr + 43, B43);
            rom.write_u8(addr + 44, B44);
            rom.write_u8(addr + 45, B45);
            rom.write_u8(addr + 46, B46);
            rom.write_u8(addr + 47, B47);
            rom.write_u8(addr + 48, B48);
            rom.write_u8(addr + 49, B49);
            rom.write_u8(addr + 50, B50);
            rom.write_u8(addr + 51, B51);
            rom.write_u8(addr + 52, B52);
            rom.write_u8(addr + 53, B53);
            rom.write_u8(addr + 54, B54);
            rom.write_u8(addr + 55, B55);
            rom.write_u8(addr + 56, B56);
            rom.write_u8(addr + 57, B57);
            rom.write_u8(addr + 58, B58);
            rom.write_u8(addr + 59, B59);
            rom.write_u8(addr + 60, B60);
            rom.write_u8(addr + 61, B61);
            rom.write_u8(addr + 62, B62);
            rom.write_u8(addr + 63, B63);
            rom.write_u8(addr + 64, B64);
        }

        /// <summary>
        /// Get the byte value at a given terrain index (0-64).
        /// </summary>
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

        /// <summary>
        /// Set the byte value at a given terrain index (0-64).
        /// </summary>
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
            };

            // Read the move cost pointer from class struct
            uint moveCostPtrOffset = (rom.RomInfo.version == 6) ? 52u : 56u;
            if (a + moveCostPtrOffset + 3 < (uint)rom.Data.Length)
            {
                report[$"u32@0x{moveCostPtrOffset:X02}"] = $"0x{rom.u32(a + moveCostPtrOffset):X08}";
            }

            // Also report the raw move cost bytes at MoveCostAddr
            if (MoveCostAddr == 0 || !U.isSafetyOffset(MoveCostAddr)) return report;
            uint m = MoveCostAddr;
            if (m + TerrainCount > (uint)rom.Data.Length) return report;
            report["u8@0x00"] = $"0x{rom.u8(m + 0):X02}";
            report["u8@0x01"] = $"0x{rom.u8(m + 1):X02}";
            report["u8@0x02"] = $"0x{rom.u8(m + 2):X02}";
            report["u8@0x03"] = $"0x{rom.u8(m + 3):X02}";
            report["u8@0x04"] = $"0x{rom.u8(m + 4):X02}";
            report["u8@0x05"] = $"0x{rom.u8(m + 5):X02}";
            report["u8@0x06"] = $"0x{rom.u8(m + 6):X02}";
            report["u8@0x07"] = $"0x{rom.u8(m + 7):X02}";
            report["u8@0x08"] = $"0x{rom.u8(m + 8):X02}";
            report["u8@0x09"] = $"0x{rom.u8(m + 9):X02}";
            report["u8@0x0A"] = $"0x{rom.u8(m + 10):X02}";
            report["u8@0x0B"] = $"0x{rom.u8(m + 11):X02}";
            report["u8@0x0C"] = $"0x{rom.u8(m + 12):X02}";
            report["u8@0x0D"] = $"0x{rom.u8(m + 13):X02}";
            report["u8@0x0E"] = $"0x{rom.u8(m + 14):X02}";
            report["u8@0x0F"] = $"0x{rom.u8(m + 15):X02}";
            report["u8@0x10"] = $"0x{rom.u8(m + 16):X02}";
            report["u8@0x11"] = $"0x{rom.u8(m + 17):X02}";
            report["u8@0x12"] = $"0x{rom.u8(m + 18):X02}";
            report["u8@0x13"] = $"0x{rom.u8(m + 19):X02}";
            report["u8@0x14"] = $"0x{rom.u8(m + 20):X02}";
            report["u8@0x15"] = $"0x{rom.u8(m + 21):X02}";
            report["u8@0x16"] = $"0x{rom.u8(m + 22):X02}";
            report["u8@0x17"] = $"0x{rom.u8(m + 23):X02}";
            report["u8@0x18"] = $"0x{rom.u8(m + 24):X02}";
            report["u8@0x19"] = $"0x{rom.u8(m + 25):X02}";
            report["u8@0x1A"] = $"0x{rom.u8(m + 26):X02}";
            report["u8@0x1B"] = $"0x{rom.u8(m + 27):X02}";
            report["u8@0x1C"] = $"0x{rom.u8(m + 28):X02}";
            report["u8@0x1D"] = $"0x{rom.u8(m + 29):X02}";
            report["u8@0x1E"] = $"0x{rom.u8(m + 30):X02}";
            report["u8@0x1F"] = $"0x{rom.u8(m + 31):X02}";
            report["u8@0x20"] = $"0x{rom.u8(m + 32):X02}";
            report["u8@0x21"] = $"0x{rom.u8(m + 33):X02}";
            report["u8@0x22"] = $"0x{rom.u8(m + 34):X02}";
            report["u8@0x23"] = $"0x{rom.u8(m + 35):X02}";
            report["u8@0x24"] = $"0x{rom.u8(m + 36):X02}";
            report["u8@0x25"] = $"0x{rom.u8(m + 37):X02}";
            report["u8@0x26"] = $"0x{rom.u8(m + 38):X02}";
            report["u8@0x27"] = $"0x{rom.u8(m + 39):X02}";
            report["u8@0x28"] = $"0x{rom.u8(m + 40):X02}";
            report["u8@0x29"] = $"0x{rom.u8(m + 41):X02}";
            report["u8@0x2A"] = $"0x{rom.u8(m + 42):X02}";
            report["u8@0x2B"] = $"0x{rom.u8(m + 43):X02}";
            report["u8@0x2C"] = $"0x{rom.u8(m + 44):X02}";
            report["u8@0x2D"] = $"0x{rom.u8(m + 45):X02}";
            report["u8@0x2E"] = $"0x{rom.u8(m + 46):X02}";
            report["u8@0x2F"] = $"0x{rom.u8(m + 47):X02}";
            report["u8@0x30"] = $"0x{rom.u8(m + 48):X02}";
            report["u8@0x31"] = $"0x{rom.u8(m + 49):X02}";
            report["u8@0x32"] = $"0x{rom.u8(m + 50):X02}";
            report["u8@0x33"] = $"0x{rom.u8(m + 51):X02}";
            report["u8@0x34"] = $"0x{rom.u8(m + 52):X02}";
            report["u8@0x35"] = $"0x{rom.u8(m + 53):X02}";
            report["u8@0x36"] = $"0x{rom.u8(m + 54):X02}";
            report["u8@0x37"] = $"0x{rom.u8(m + 55):X02}";
            report["u8@0x38"] = $"0x{rom.u8(m + 56):X02}";
            report["u8@0x39"] = $"0x{rom.u8(m + 57):X02}";
            report["u8@0x3A"] = $"0x{rom.u8(m + 58):X02}";
            report["u8@0x3B"] = $"0x{rom.u8(m + 59):X02}";
            report["u8@0x3C"] = $"0x{rom.u8(m + 60):X02}";
            report["u8@0x3D"] = $"0x{rom.u8(m + 61):X02}";
            report["u8@0x3E"] = $"0x{rom.u8(m + 62):X02}";
            report["u8@0x3F"] = $"0x{rom.u8(m + 63):X02}";
            report["u8@0x40"] = $"0x{rom.u8(m + 64):X02}";
            return report;
        }
    }
}
