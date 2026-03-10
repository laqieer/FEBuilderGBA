using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageTSAAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0, _p4, _p8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // P0: Image data pointer
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        // P4: Palette pointer
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        // P8: TSA pointer
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Load TSA anime resource file to find known pointers
            // Format: pointer<tab>count<tab>name — LoadTSVResource1 gives pointer → count
            var tsaAnime = U.LoadTSVResource1(U.ConfigDataFilename("tsaanime_"), false);
            if (tsaAnime == null || tsaAnime.Count == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // For each TSA anime category, load the first entry's pointers
            foreach (var pair in tsaAnime)
            {
                uint pointer = pair.Key;
                uint ptrOff = U.toOffset(pointer);
                if (!U.isSafetyOffset(ptrOff)) continue;

                uint baseAddr = rom.p32(ptrOff);
                if (!U.isSafetyOffset(baseAddr)) continue;

                // First entry at baseAddr: 12 bytes (image, palette, tsa)
                if (baseAddr + SIZE > (uint)rom.Data.Length) continue;
                uint img = rom.u32(baseAddr);
                if (!U.isPointer(img)) continue;

                string name = U.ToHexString(pointer) + " " + pair.Value;
                result.Add(new AddrResult(baseAddr, name, (uint)result.Count));

                if (result.Count >= 20) break; // Limit to avoid huge lists
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
        /// Try to load TSA animation image. P0=image(LZ77), P4=palette(raw ROM), P8=TSA(LZ77).
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                if (!U.isPointer(P0) || !U.isPointer(P4) || !U.isPointer(P8)) return null;
                uint imgAddr = U.toOffset(P0);
                uint palAddr = U.toOffset(P4);
                uint tsaAddr = U.toOffset(P8);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr) || !U.isSafetyOffset(tsaAddr))
                    return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // TSA is LZ77 compressed; decompress to determine dynamic height
                byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaData == null || tsaData.Length == 0) return null;

                // Palette is raw ROM data (not LZ77)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null || palette.Length == 0) return null;

                // Calculate height from TSA data (matching WinForms CalcHeightbyTSA)
                int widthTiles = 32;
                int totalTSAEntries = tsaData.Length / 2;
                int heightTiles = (totalTSAEntries + widthTiles - 1) / widthTiles;
                if (heightTiles < 20) heightTiles = 20;

                return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, widthTiles, heightTiles);
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
