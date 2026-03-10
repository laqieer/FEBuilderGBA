using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageCGViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0, _p4, _p8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // P0: CG image data pointer
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        // P4: Palette pointer
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        // P8: TSA pointer
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.bigcg_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint p = rom.u32(addr);
                if (!U.isPointer(p) || !U.isSafetyPointer(p)) break;
                // Verify 10-split: first pointer in table must also be a pointer
                uint p2 = rom.u32(U.toOffset(p));
                if (!U.isPointer(p2) || !U.isSafetyPointer(p2)) break;

                string name = U.ToHexString(i) + " CG Image";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            P0 = rom.u32(addr + 0);
            P4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);

            IsLoaded = true;
        }

        /// <summary>
        /// Try to load CG image. P0=image(LZ77), P4=palette(LZ77), P8=TSA(LZ77).
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                if (!U.isPointer(P0) || !U.isPointer(P4)) return null;
                uint imgAddr = U.toOffset(P0);
                uint palAddr = U.toOffset(P4);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;
                byte[] palette = LZ77.decompress(rom.Data, palAddr);
                if (palette == null || palette.Length == 0) return null;

                if (U.isPointer(P8))
                {
                    uint tsaAddr = U.toOffset(P8);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                        if (tsaData != null && tsaData.Length > 0)
                            return ImageUtilCore.DecodeTSA(tileData, tsaData, palette, 30, 20, true);
                    }
                }

                if (CoreState.ImageService == null) return null;
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
                int tilesX = 30;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["P4"] = $"0x{P4:X08}",
                ["P8"] = $"0x{P8:X08}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
