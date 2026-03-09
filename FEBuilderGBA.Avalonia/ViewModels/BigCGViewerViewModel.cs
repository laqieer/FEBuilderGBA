using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class BigCGViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _tablePointer, _tsaPointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint TablePointer { get => _tablePointer; set => SetField(ref _tablePointer, value); }
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadBigCGList()
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
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                uint ptr0 = rom.u32(addr);
                if (!U.isPointer(ptr0)) break;

                string name = U.ToHexString(i) + " CG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadBigCG(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            TablePointer = rom.u32(addr + 0);
            TSAPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);
            CanWrite = true;
        }

        public void WriteBigCG()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, TablePointer);
            rom.write_u32(addr + 4, TSAPointer);
            rom.write_u32(addr + 8, PalettePointer);
        }

        /// <summary>
        /// Try to load Big CG image.
        /// CG images are complex (multi-part LZ77 + TSA), so this may fail.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint tablePtr = TablePointer;
                uint palPtr = PalettePointer;
                if (!U.isPointer(tablePtr) || !U.isPointer(palPtr)) return null;

                uint tableAddr = U.toOffset(tablePtr);
                uint palAddr = U.toOffset(palPtr);
                if (!U.isSafetyOffset(tableAddr) || !U.isSafetyOffset(palAddr)) return null;

                var tileDataList = new List<byte>();
                for (int i = 0; i < 10; i++)
                {
                    uint partPtr = rom.u32((uint)(tableAddr + i * 4));
                    if (!U.isPointer(partPtr)) return null;
                    uint partAddr = U.toOffset(partPtr);
                    if (!U.isSafetyOffset(partAddr)) return null;
                    byte[] partData = LZ77.decompress(rom.Data, partAddr);
                    if (partData == null) return null;
                    tileDataList.AddRange(partData);
                }

                byte[] tileData = tileDataList.ToArray();
                if (tileData.Length == 0) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                int tilesX = 32;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;

                int width = tilesX * 8;
                int height = tilesY * 8;

                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadBigCGList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{TablePointer:X08}",
                ["P4"] = $"0x{TSAPointer:X08}",
                ["P8"] = $"0x{PalettePointer:X08}",
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
