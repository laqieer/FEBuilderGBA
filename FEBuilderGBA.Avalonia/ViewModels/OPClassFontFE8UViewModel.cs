using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Font editor (FE8U variant).
    /// Data: op_class_font_pointer, datasize=4 (pointer per entry), up to 0x7B entries.
    /// Each entry is a pointer to LZ77-compressed 4bpp font tile data.
    /// </summary>
    public class OPClassFontFE8UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer;
        string _unavailableMessage = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_font_pointer;
            if (baseAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // datasize=4, up to 0x7B entries
            for (uint i = 0; i <= 0x7a; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                string name = U.ToHexString(i) + " OP Class Font (FE8U)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ImagePointer = rom.u32(addr);
            CanWrite = true;
        }

        /// <summary>
        /// Try to load OP class font image as RGBA pixels.
        /// FE8U uses 2x4 tiles (16x32 px).
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
                if (!U.isPointer(imgPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                if (!U.isSafetyOffset(imgAddr)) return null;

                uint palPtrAddr = rom.RomInfo.op_class_font_palette_pointer;
                if (palPtrAddr == 0) return null;

                uint palAddr = rom.p32(palPtrAddr);
                if (!U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // FE8U renders as 2x4 tiles (16x32 px)
                width = 2 * 8;
                height = 4 * 8;

                int totalTiles = tileData.Length / 32;
                if (totalTiles < 8)
                {
                    int tilesX = 2;
                    int tilesY = (totalTiles + tilesX - 1) / tilesX;
                    if (tilesY <= 0) tilesY = 1;
                    height = tilesY * 8;
                }

                if (CoreState.ImageService == null) return null;
                var image = CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
                if (image == null) return null;
                return image.GetPixelData();
            }
            catch { return null; }
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ImagePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
