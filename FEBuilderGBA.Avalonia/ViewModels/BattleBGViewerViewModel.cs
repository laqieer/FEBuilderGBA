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

            uint ptr = rom.RomInfo.battle_bg_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

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
        /// Try to load battle BG image.
        /// Battle BGs use LZ77-compressed 4bpp tiles + TSA + palette (all compressed).
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint imgPtr = ImagePointer;
                uint tsaPtr = TSAPointer;
                uint palPtr = PalettePointer;
                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                uint palAddr = U.toOffset(palPtr);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                // All 3 components are LZ77-compressed
                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                byte[] palette = LZ77.decompress(rom.Data, palAddr);
                if (palette == null || palette.Length == 0) return null;

                // If TSA is available, use TSA-based rendering (240x160)
                if (U.isPointer(tsaPtr))
                {
                    uint tsaAddr = U.toOffset(tsaPtr);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                        if (tsaData != null && tsaData.Length > 0)
                            return ImageUtilCore.DecodeTSA(tileData, tsaData, palette, 30, 20, true);
                    }
                }

                // Fallback: render tiles without TSA
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
                int tilesX = 30;
                int tilesY = 20;
                if (totalTiles < tilesX * tilesY)
                    tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
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
