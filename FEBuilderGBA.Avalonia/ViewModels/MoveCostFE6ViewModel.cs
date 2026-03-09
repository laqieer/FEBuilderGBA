using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveCostFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _className = "";
        byte[] _moveCosts = Array.Empty<byte>();

        // W0: u16 name text ID at class struct offset 0
        ushort _nameTextId;
        // D52: u32 move cost pointer at class struct offset 52
        uint _moveCostPointer;

        // 51 terrain cost bytes (B0-B50), one per terrain type
        byte _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9;
        byte _b10, _b11, _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        byte _b20, _b21, _b22, _b23, _b24, _b25, _b26, _b27, _b28, _b29;
        byte _b30, _b31, _b32, _b33, _b34, _b35, _b36, _b37, _b38, _b39;
        byte _b40, _b41, _b42, _b43, _b44, _b45, _b46, _b47, _b48, _b49;
        byte _b50;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public byte[] MoveCosts { get => _moveCosts; set => SetField(ref _moveCosts, value); }

        // W0: u16 at offset 0 (name text ID)
        public ushort NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }
        // D52: u32 at offset 52 (move cost pointer)
        public uint MoveCostPointer { get => _moveCostPointer; set => SetField(ref _moveCostPointer, value); }

        // 51 individual byte properties (B0 through B50)
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

        public void LoadMoveCost(uint classAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.class_datasize;
            if (classAddr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = classAddr;

            // W0: u16 name text ID at offset 0
            NameTextId = (ushort)rom.u16(classAddr + 0);
            try { ClassName = FETextDecode.Direct(NameTextId); }
            catch { ClassName = "???"; }

            // D52: u32 move cost pointer at offset 52
            uint moveCostPtrOffset = 52;

            if (classAddr + moveCostPtrOffset + 3 >= (uint)rom.Data.Length)
            {
                MoveCostPointer = 0;
                ClearAllB();
                MoveCosts = Array.Empty<byte>();
                CanWrite = true;
                return;
            }

            MoveCostPointer = rom.u32(classAddr + moveCostPtrOffset);
            if (!U.isPointer(MoveCostPointer))
            {
                ClearAllB();
                MoveCosts = Array.Empty<byte>();
                CanWrite = true;
                return;
            }

            uint moveCostAddr = MoveCostPointer - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr))
            {
                ClearAllB();
                MoveCosts = Array.Empty<byte>();
                CanWrite = true;
                return;
            }

            // Read 51 terrain cost bytes (B0-B50)
            int terrainCount = 51;
            if (moveCostAddr + terrainCount > (uint)rom.Data.Length)
                terrainCount = (int)((uint)rom.Data.Length - moveCostAddr);

            byte[] costs = new byte[terrainCount];
            for (int i = 0; i < terrainCount; i++)
                costs[i] = (byte)rom.u8((uint)(moveCostAddr + i));

            MoveCosts = costs;

            // Populate individual B# properties
            B0  = terrainCount >  0 ? costs[ 0] : (byte)0;
            B1  = terrainCount >  1 ? costs[ 1] : (byte)0;
            B2  = terrainCount >  2 ? costs[ 2] : (byte)0;
            B3  = terrainCount >  3 ? costs[ 3] : (byte)0;
            B4  = terrainCount >  4 ? costs[ 4] : (byte)0;
            B5  = terrainCount >  5 ? costs[ 5] : (byte)0;
            B6  = terrainCount >  6 ? costs[ 6] : (byte)0;
            B7  = terrainCount >  7 ? costs[ 7] : (byte)0;
            B8  = terrainCount >  8 ? costs[ 8] : (byte)0;
            B9  = terrainCount >  9 ? costs[ 9] : (byte)0;
            B10 = terrainCount > 10 ? costs[10] : (byte)0;
            B11 = terrainCount > 11 ? costs[11] : (byte)0;
            B12 = terrainCount > 12 ? costs[12] : (byte)0;
            B13 = terrainCount > 13 ? costs[13] : (byte)0;
            B14 = terrainCount > 14 ? costs[14] : (byte)0;
            B15 = terrainCount > 15 ? costs[15] : (byte)0;
            B16 = terrainCount > 16 ? costs[16] : (byte)0;
            B17 = terrainCount > 17 ? costs[17] : (byte)0;
            B18 = terrainCount > 18 ? costs[18] : (byte)0;
            B19 = terrainCount > 19 ? costs[19] : (byte)0;
            B20 = terrainCount > 20 ? costs[20] : (byte)0;
            B21 = terrainCount > 21 ? costs[21] : (byte)0;
            B22 = terrainCount > 22 ? costs[22] : (byte)0;
            B23 = terrainCount > 23 ? costs[23] : (byte)0;
            B24 = terrainCount > 24 ? costs[24] : (byte)0;
            B25 = terrainCount > 25 ? costs[25] : (byte)0;
            B26 = terrainCount > 26 ? costs[26] : (byte)0;
            B27 = terrainCount > 27 ? costs[27] : (byte)0;
            B28 = terrainCount > 28 ? costs[28] : (byte)0;
            B29 = terrainCount > 29 ? costs[29] : (byte)0;
            B30 = terrainCount > 30 ? costs[30] : (byte)0;
            B31 = terrainCount > 31 ? costs[31] : (byte)0;
            B32 = terrainCount > 32 ? costs[32] : (byte)0;
            B33 = terrainCount > 33 ? costs[33] : (byte)0;
            B34 = terrainCount > 34 ? costs[34] : (byte)0;
            B35 = terrainCount > 35 ? costs[35] : (byte)0;
            B36 = terrainCount > 36 ? costs[36] : (byte)0;
            B37 = terrainCount > 37 ? costs[37] : (byte)0;
            B38 = terrainCount > 38 ? costs[38] : (byte)0;
            B39 = terrainCount > 39 ? costs[39] : (byte)0;
            B40 = terrainCount > 40 ? costs[40] : (byte)0;
            B41 = terrainCount > 41 ? costs[41] : (byte)0;
            B42 = terrainCount > 42 ? costs[42] : (byte)0;
            B43 = terrainCount > 43 ? costs[43] : (byte)0;
            B44 = terrainCount > 44 ? costs[44] : (byte)0;
            B45 = terrainCount > 45 ? costs[45] : (byte)0;
            B46 = terrainCount > 46 ? costs[46] : (byte)0;
            B47 = terrainCount > 47 ? costs[47] : (byte)0;
            B48 = terrainCount > 48 ? costs[48] : (byte)0;
            B49 = terrainCount > 49 ? costs[49] : (byte)0;
            B50 = terrainCount > 50 ? costs[50] : (byte)0;

            CanWrite = true;
        }

        void ClearAllB()
        {
            B0 = 0; B1 = 0; B2 = 0; B3 = 0; B4 = 0;
            B5 = 0; B6 = 0; B7 = 0; B8 = 0; B9 = 0;
            B10 = 0; B11 = 0; B12 = 0; B13 = 0; B14 = 0;
            B15 = 0; B16 = 0; B17 = 0; B18 = 0; B19 = 0;
            B20 = 0; B21 = 0; B22 = 0; B23 = 0; B24 = 0;
            B25 = 0; B26 = 0; B27 = 0; B28 = 0; B29 = 0;
            B30 = 0; B31 = 0; B32 = 0; B33 = 0; B34 = 0;
            B35 = 0; B36 = 0; B37 = 0; B38 = 0; B39 = 0;
            B40 = 0; B41 = 0; B42 = 0; B43 = 0; B44 = 0;
            B45 = 0; B46 = 0; B47 = 0; B48 = 0; B49 = 0;
            B50 = 0;
        }

        public void WriteMoveCost()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (!U.isPointer(MoveCostPointer)) return;

            uint moveCostAddr = MoveCostPointer - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr)) return;
            if (moveCostAddr + 51 > (uint)rom.Data.Length) return;

            rom.write_u8(moveCostAddr + 0, B0);
            rom.write_u8(moveCostAddr + 1, B1);
            rom.write_u8(moveCostAddr + 2, B2);
            rom.write_u8(moveCostAddr + 3, B3);
            rom.write_u8(moveCostAddr + 4, B4);
            rom.write_u8(moveCostAddr + 5, B5);
            rom.write_u8(moveCostAddr + 6, B6);
            rom.write_u8(moveCostAddr + 7, B7);
            rom.write_u8(moveCostAddr + 8, B8);
            rom.write_u8(moveCostAddr + 9, B9);
            rom.write_u8(moveCostAddr + 10, B10);
            rom.write_u8(moveCostAddr + 11, B11);
            rom.write_u8(moveCostAddr + 12, B12);
            rom.write_u8(moveCostAddr + 13, B13);
            rom.write_u8(moveCostAddr + 14, B14);
            rom.write_u8(moveCostAddr + 15, B15);
            rom.write_u8(moveCostAddr + 16, B16);
            rom.write_u8(moveCostAddr + 17, B17);
            rom.write_u8(moveCostAddr + 18, B18);
            rom.write_u8(moveCostAddr + 19, B19);
            rom.write_u8(moveCostAddr + 20, B20);
            rom.write_u8(moveCostAddr + 21, B21);
            rom.write_u8(moveCostAddr + 22, B22);
            rom.write_u8(moveCostAddr + 23, B23);
            rom.write_u8(moveCostAddr + 24, B24);
            rom.write_u8(moveCostAddr + 25, B25);
            rom.write_u8(moveCostAddr + 26, B26);
            rom.write_u8(moveCostAddr + 27, B27);
            rom.write_u8(moveCostAddr + 28, B28);
            rom.write_u8(moveCostAddr + 29, B29);
            rom.write_u8(moveCostAddr + 30, B30);
            rom.write_u8(moveCostAddr + 31, B31);
            rom.write_u8(moveCostAddr + 32, B32);
            rom.write_u8(moveCostAddr + 33, B33);
            rom.write_u8(moveCostAddr + 34, B34);
            rom.write_u8(moveCostAddr + 35, B35);
            rom.write_u8(moveCostAddr + 36, B36);
            rom.write_u8(moveCostAddr + 37, B37);
            rom.write_u8(moveCostAddr + 38, B38);
            rom.write_u8(moveCostAddr + 39, B39);
            rom.write_u8(moveCostAddr + 40, B40);
            rom.write_u8(moveCostAddr + 41, B41);
            rom.write_u8(moveCostAddr + 42, B42);
            rom.write_u8(moveCostAddr + 43, B43);
            rom.write_u8(moveCostAddr + 44, B44);
            rom.write_u8(moveCostAddr + 45, B45);
            rom.write_u8(moveCostAddr + 46, B46);
            rom.write_u8(moveCostAddr + 47, B47);
            rom.write_u8(moveCostAddr + 48, B48);
            rom.write_u8(moveCostAddr + 49, B49);
            rom.write_u8(moveCostAddr + 50, B50);
        }

        public int GetListCount() => LoadClassList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameTextId"] = $"0x{NameTextId:X04}",
                ["D52_MoveCostPointer"] = $"0x{MoveCostPointer:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["B18"] = $"0x{B18:X02}",
                ["B19"] = $"0x{B19:X02}",
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["B24"] = $"0x{B24:X02}",
                ["B25"] = $"0x{B25:X02}",
                ["B26"] = $"0x{B26:X02}",
                ["B27"] = $"0x{B27:X02}",
                ["B28"] = $"0x{B28:X02}",
                ["B29"] = $"0x{B29:X02}",
                ["B30"] = $"0x{B30:X02}",
                ["B31"] = $"0x{B31:X02}",
                ["B32"] = $"0x{B32:X02}",
                ["B33"] = $"0x{B33:X02}",
                ["B34"] = $"0x{B34:X02}",
                ["B35"] = $"0x{B35:X02}",
                ["B36"] = $"0x{B36:X02}",
                ["B37"] = $"0x{B37:X02}",
                ["B38"] = $"0x{B38:X02}",
                ["B39"] = $"0x{B39:X02}",
                ["B40"] = $"0x{B40:X02}",
                ["B41"] = $"0x{B41:X02}",
                ["B42"] = $"0x{B42:X02}",
                ["B43"] = $"0x{B43:X02}",
                ["B44"] = $"0x{B44:X02}",
                ["B45"] = $"0x{B45:X02}",
                ["B46"] = $"0x{B46:X02}",
                ["B47"] = $"0x{B47:X02}",
                ["B48"] = $"0x{B48:X02}",
                ["B49"] = $"0x{B49:X02}",
                ["B50"] = $"0x{B50:X02}",
            };
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
            if (a + 2 <= (uint)rom.Data.Length)
            {
                report["u16@0x00"] = $"0x{rom.u16(a + 0):X04}";
            }
            if (a + 56 <= (uint)rom.Data.Length)
            {
                report["u32@0x34"] = $"0x{rom.u32(a + 52):X08}";
            }
            // Read raw move cost bytes from the pointer target
            if (a + 56 > (uint)rom.Data.Length) return report;
            uint ptr = rom.u32(a + 52);
            if (!U.isPointer(ptr)) return report;
            uint mc = ptr - 0x08000000;
            if (!U.isSafetyOffset(mc) || mc + 51 > (uint)rom.Data.Length) return report;
            report["u8@0x00"] = $"0x{rom.u8(mc + 0):X02}";
            report["u8@0x01"] = $"0x{rom.u8(mc + 1):X02}";
            report["u8@0x02"] = $"0x{rom.u8(mc + 2):X02}";
            report["u8@0x03"] = $"0x{rom.u8(mc + 3):X02}";
            report["u8@0x04"] = $"0x{rom.u8(mc + 4):X02}";
            report["u8@0x05"] = $"0x{rom.u8(mc + 5):X02}";
            report["u8@0x06"] = $"0x{rom.u8(mc + 6):X02}";
            report["u8@0x07"] = $"0x{rom.u8(mc + 7):X02}";
            report["u8@0x08"] = $"0x{rom.u8(mc + 8):X02}";
            report["u8@0x09"] = $"0x{rom.u8(mc + 9):X02}";
            report["u8@0x0A"] = $"0x{rom.u8(mc + 10):X02}";
            report["u8@0x0B"] = $"0x{rom.u8(mc + 11):X02}";
            report["u8@0x0C"] = $"0x{rom.u8(mc + 12):X02}";
            report["u8@0x0D"] = $"0x{rom.u8(mc + 13):X02}";
            report["u8@0x0E"] = $"0x{rom.u8(mc + 14):X02}";
            report["u8@0x0F"] = $"0x{rom.u8(mc + 15):X02}";
            report["u8@0x10"] = $"0x{rom.u8(mc + 16):X02}";
            report["u8@0x11"] = $"0x{rom.u8(mc + 17):X02}";
            report["u8@0x12"] = $"0x{rom.u8(mc + 18):X02}";
            report["u8@0x13"] = $"0x{rom.u8(mc + 19):X02}";
            report["u8@0x14"] = $"0x{rom.u8(mc + 20):X02}";
            report["u8@0x15"] = $"0x{rom.u8(mc + 21):X02}";
            report["u8@0x16"] = $"0x{rom.u8(mc + 22):X02}";
            report["u8@0x17"] = $"0x{rom.u8(mc + 23):X02}";
            report["u8@0x18"] = $"0x{rom.u8(mc + 24):X02}";
            report["u8@0x19"] = $"0x{rom.u8(mc + 25):X02}";
            report["u8@0x1A"] = $"0x{rom.u8(mc + 26):X02}";
            report["u8@0x1B"] = $"0x{rom.u8(mc + 27):X02}";
            report["u8@0x1C"] = $"0x{rom.u8(mc + 28):X02}";
            report["u8@0x1D"] = $"0x{rom.u8(mc + 29):X02}";
            report["u8@0x1E"] = $"0x{rom.u8(mc + 30):X02}";
            report["u8@0x1F"] = $"0x{rom.u8(mc + 31):X02}";
            report["u8@0x20"] = $"0x{rom.u8(mc + 32):X02}";
            report["u8@0x21"] = $"0x{rom.u8(mc + 33):X02}";
            report["u8@0x22"] = $"0x{rom.u8(mc + 34):X02}";
            report["u8@0x23"] = $"0x{rom.u8(mc + 35):X02}";
            report["u8@0x24"] = $"0x{rom.u8(mc + 36):X02}";
            report["u8@0x25"] = $"0x{rom.u8(mc + 37):X02}";
            report["u8@0x26"] = $"0x{rom.u8(mc + 38):X02}";
            report["u8@0x27"] = $"0x{rom.u8(mc + 39):X02}";
            report["u8@0x28"] = $"0x{rom.u8(mc + 40):X02}";
            report["u8@0x29"] = $"0x{rom.u8(mc + 41):X02}";
            report["u8@0x2A"] = $"0x{rom.u8(mc + 42):X02}";
            report["u8@0x2B"] = $"0x{rom.u8(mc + 43):X02}";
            report["u8@0x2C"] = $"0x{rom.u8(mc + 44):X02}";
            report["u8@0x2D"] = $"0x{rom.u8(mc + 45):X02}";
            report["u8@0x2E"] = $"0x{rom.u8(mc + 46):X02}";
            report["u8@0x2F"] = $"0x{rom.u8(mc + 47):X02}";
            report["u8@0x30"] = $"0x{rom.u8(mc + 48):X02}";
            report["u8@0x31"] = $"0x{rom.u8(mc + 49):X02}";
            report["u8@0x32"] = $"0x{rom.u8(mc + 50):X02}";
            return report;
        }
    }
}
