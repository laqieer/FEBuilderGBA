using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class BattleTerrainViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _terrainName = "";
        uint _nameChar0, _nameChar1, _nameChar2, _nameChar3;
        uint _nameChar4, _nameChar5, _nameChar6, _nameChar7;
        uint _nameChar8, _nameChar9, _nameChar10, _nameChar11;
        uint _imagePointer, _palettePointer;
        uint _unknownD20;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }
        public uint NameChar0 { get => _nameChar0; set => SetField(ref _nameChar0, value); }
        public uint NameChar1 { get => _nameChar1; set => SetField(ref _nameChar1, value); }
        public uint NameChar2 { get => _nameChar2; set => SetField(ref _nameChar2, value); }
        public uint NameChar3 { get => _nameChar3; set => SetField(ref _nameChar3, value); }
        public uint NameChar4 { get => _nameChar4; set => SetField(ref _nameChar4, value); }
        public uint NameChar5 { get => _nameChar5; set => SetField(ref _nameChar5, value); }
        public uint NameChar6 { get => _nameChar6; set => SetField(ref _nameChar6, value); }
        public uint NameChar7 { get => _nameChar7; set => SetField(ref _nameChar7, value); }
        public uint NameChar8 { get => _nameChar8; set => SetField(ref _nameChar8, value); }
        public uint NameChar9 { get => _nameChar9; set => SetField(ref _nameChar9, value); }
        public uint NameChar10 { get => _nameChar10; set => SetField(ref _nameChar10, value); }
        public uint NameChar11 { get => _nameChar11; set => SetField(ref _nameChar11, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public uint UnknownD20 { get => _unknownD20; set => SetField(ref _unknownD20, value); }

        public List<AddrResult> LoadBattleTerrainList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.battle_terrain_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 24);
                if (addr + 24 > (uint)rom.Data.Length) break;

                // Entry validity: offset 12 should be a pointer
                uint ptr12 = rom.u32(addr + 12);
                if (!U.isPointer(ptr12)) break;

                // Name is stored as ASCII at offset 0, up to 11 bytes
                string tname = "";
                try
                {
                    for (int c = 0; c < 11; c++)
                    {
                        uint b = rom.u8((uint)(addr + c));
                        if (b == 0) break;
                        tname += (char)b;
                    }
                }
                catch { }

                string name = U.ToHexString(i) + " " + tname;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadBattleTerrain(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;

            // Read terrain name from offset 0 (up to 11 bytes ASCII)
            string tname = "";
            try
            {
                for (int c = 0; c < 11; c++)
                {
                    uint b = rom.u8((uint)(addr + c));
                    if (b == 0) break;
                    tname += (char)b;
                }
            }
            catch { }
            TerrainName = tname;

            NameChar0 = rom.u8(addr + 0);
            NameChar1 = rom.u8(addr + 1);
            NameChar2 = rom.u8(addr + 2);
            NameChar3 = rom.u8(addr + 3);
            NameChar4 = rom.u8(addr + 4);
            NameChar5 = rom.u8(addr + 5);
            NameChar6 = rom.u8(addr + 6);
            NameChar7 = rom.u8(addr + 7);
            NameChar8 = rom.u8(addr + 8);
            NameChar9 = rom.u8(addr + 9);
            NameChar10 = rom.u8(addr + 10);
            NameChar11 = rom.u8(addr + 11);
            // Image pointer at offset 12, palette at offset 16
            ImagePointer = rom.u32(addr + 12);
            PalettePointer = rom.u32(addr + 16);
            UnknownD20 = rom.u32(addr + 20);
            CanWrite = true;
        }

        public void WriteBattleTerrain()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)NameChar0);
            rom.write_u8(addr + 1, (byte)NameChar1);
            rom.write_u8(addr + 2, (byte)NameChar2);
            rom.write_u8(addr + 3, (byte)NameChar3);
            rom.write_u8(addr + 4, (byte)NameChar4);
            rom.write_u8(addr + 5, (byte)NameChar5);
            rom.write_u8(addr + 6, (byte)NameChar6);
            rom.write_u8(addr + 7, (byte)NameChar7);
            rom.write_u8(addr + 8, (byte)NameChar8);
            rom.write_u8(addr + 9, (byte)NameChar9);
            rom.write_u8(addr + 10, (byte)NameChar10);
            rom.write_u8(addr + 11, (byte)NameChar11);
            rom.write_u32(addr + 12, ImagePointer);
            rom.write_u32(addr + 16, PalettePointer);
            rom.write_u32(addr + 20, UnknownD20);
        }

        /// <summary>
        /// Try to load battle terrain image.
        /// Image and palette are LZ77-compressed.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint imgPtr = ImagePointer;
                uint palPtr = PalettePointer;
                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                uint palAddr = U.toOffset(palPtr);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Palette is raw GBA data (not compressed)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                int tilesX = 32;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;

                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadBattleTerrainList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainName"] = TerrainName,
                ["B0_NameChar0"] = $"0x{NameChar0:X02}",
                ["B1_NameChar1"] = $"0x{NameChar1:X02}",
                ["B2_NameChar2"] = $"0x{NameChar2:X02}",
                ["B3_NameChar3"] = $"0x{NameChar3:X02}",
                ["B4_NameChar4"] = $"0x{NameChar4:X02}",
                ["B5_NameChar5"] = $"0x{NameChar5:X02}",
                ["B6_NameChar6"] = $"0x{NameChar6:X02}",
                ["B7_NameChar7"] = $"0x{NameChar7:X02}",
                ["B8_NameChar8"] = $"0x{NameChar8:X02}",
                ["B9_NameChar9"] = $"0x{NameChar9:X02}",
                ["B10_NameChar10"] = $"0x{NameChar10:X02}",
                ["B11_NameChar11"] = $"0x{NameChar11:X02}",
                ["P12_ImagePointer"] = $"0x{ImagePointer:X08}",
                ["P16_PalettePointer"] = $"0x{PalettePointer:X08}",
                ["D20_Unknown"] = $"0x{UnknownD20:X08}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
            };
        }
    }
}
