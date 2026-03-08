using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class BattleBGViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer, _tsaPointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadBattleBGList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.battle_bg_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                uint img = rom.u32(addr + 0);
                uint tsa = rom.u32(addr + 4);
                if (!U.isPointer(img) || !U.isPointer(tsa)) break;

                string name = U.ToHexString(i + 1) + " Battle BG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadBattleBG(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.u32(addr + 0);
            TSAPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);
            CanWrite = true;
        }

        public void WriteBattleBG()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
            rom.write_u32(addr + 4, TSAPointer);
            rom.write_u32(addr + 8, PalettePointer);
        }

        /// <summary>
        /// Try to load battle BG image as RGBA pixels.
        /// Battle BGs use LZ77-compressed 4bpp tiles + TSA + palette.
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

                // Calculate dimensions from decompressed data
                // Each tile is 32 bytes at 4bpp; render as wide strip
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                int tilesX = 32; // 256 pixels wide
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

        public int GetListCount() => LoadBattleBGList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["TSAPointer"] = $"0x{TSAPointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
