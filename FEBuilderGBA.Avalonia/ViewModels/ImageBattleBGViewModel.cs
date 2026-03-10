using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageBattleBGViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _imagePointer, _tsaPointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // D0: Image data pointer
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        // D4: TSA data pointer
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        // D8: Palette data pointer
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadList()
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
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint img = rom.u32(addr + 0);
                uint tsa = rom.u32(addr + 4);
                if (!U.isPointer(img) || !U.isPointer(tsa)) break;

                string name = U.ToHexString(i) + " Battle BG";
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

            ImagePointer = rom.u32(addr + 0);
            TSAPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
            rom.write_u32(addr + 4, TSAPointer);
            rom.write_u32(addr + 8, PalettePointer);
        }

        /// <summary>
        /// Render the battle BG image. All 3 components (image, TSA, palette) are LZ77-compressed.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                if (!U.isPointer(ImagePointer) || !U.isPointer(TSAPointer) || !U.isPointer(PalettePointer))
                    return null;

                uint imgAddr = U.toOffset(ImagePointer);
                uint tsaAddr = U.toOffset(TSAPointer);
                uint palAddr = U.toOffset(PalettePointer);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(tsaAddr) || !U.isSafetyOffset(palAddr))
                    return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;
                byte[] palette = LZ77.decompress(rom.Data, palAddr);
                if (palette == null || palette.Length == 0) return null;
                byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaData == null || tsaData.Length == 0) return null;

                return ImageUtilCore.DecodeTSA(tileData, tsaData, palette, 30, 20, true);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
