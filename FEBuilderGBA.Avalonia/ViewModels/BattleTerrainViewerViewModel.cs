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
        uint _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9, _b10, _b11;
        uint _imagePointer, _palettePointer;
        uint _d20;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public uint D20 { get => _d20; set => SetField(ref _d20, value); }

        public List<AddrResult> LoadBattleTerrainList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.battle_terrain_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

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

            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            // Image pointer at offset 12, palette at offset 16
            ImagePointer = rom.u32(addr + 12);
            PalettePointer = rom.u32(addr + 16);
            D20 = rom.u32(addr + 20);
            CanWrite = true;
        }

        public void WriteBattleTerrain()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)B0);
            rom.write_u8(addr + 1, (byte)B1);
            rom.write_u8(addr + 2, (byte)B2);
            rom.write_u8(addr + 3, (byte)B3);
            rom.write_u8(addr + 4, (byte)B4);
            rom.write_u8(addr + 5, (byte)B5);
            rom.write_u8(addr + 6, (byte)B6);
            rom.write_u8(addr + 7, (byte)B7);
            rom.write_u8(addr + 8, (byte)B8);
            rom.write_u8(addr + 9, (byte)B9);
            rom.write_u8(addr + 10, (byte)B10);
            rom.write_u8(addr + 11, (byte)B11);
            rom.write_u32(addr + 12, ImagePointer);
            rom.write_u32(addr + 16, PalettePointer);
            rom.write_u32(addr + 20, D20);
        }

        /// <summary>
        /// Try to load battle terrain image as RGBA pixels.
        /// Returns null on failure.
        /// </summary>
        public byte[] TryLoadImage(out int width, out int height)
        {
            width = 0; height = 0;
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

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                // Render as 32-tile wide strip (256 px)
                int tilesX = 32;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;

                width = tilesX * 8;
                height = tilesY * 8;

                if (CoreState.ImageService == null) return null;
                var image = CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
                if (image == null) return null;
                return image.GetPixelData();
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
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
                ["D20"] = $"0x{D20:X08}",
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
