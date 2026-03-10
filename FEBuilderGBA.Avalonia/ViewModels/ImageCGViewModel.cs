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
        /// Try to load CG image. ROM layout: P0=table(10-split), P4=TSA(raw), P8=palette(raw).
        /// The table at P0 contains 10 pointers to LZ77-compressed image parts.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                // P0=table, P4=TSA, P8=palette (matching WinForms ImageCGForm)
                if (!U.isPointer(P0) || !U.isPointer(P4) || !U.isPointer(P8)) return null;
                uint tableAddr = U.toOffset(P0);
                uint tsaAddr = U.toOffset(P4);
                uint palAddr = U.toOffset(P8);
                if (!U.isSafetyOffset(tableAddr) || !U.isSafetyOffset(tsaAddr) || !U.isSafetyOffset(palAddr))
                    return null;

                // Decompress 10-split image parts
                var imageUZList = new System.Collections.Generic.List<byte>();
                for (int i = 0; i < 10; i++)
                {
                    uint imagePtr = rom.u32((uint)(tableAddr + i * 4));
                    if (!U.isPointer(imagePtr)) return null;
                    byte[] imageUZ = LZ77.decompress(rom.Data, U.toOffset(imagePtr));
                    if (imageUZ == null || imageUZ.Length == 0) return null;
                    imageUZList.AddRange(imageUZ);
                }

                byte[] tileData = imageUZList.ToArray();
                if (tileData.Length == 0) return null;

                // Palette is raw ROM data (not LZ77)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null || palette.Length == 0) return null;

                // TSA is raw ROM data with header format
                int tsaLen = Math.Min(32 * 20 * 2 + 4, (int)((uint)rom.Data.Length - tsaAddr));
                if (tsaLen <= 0) return null;
                byte[] tsaData = new byte[tsaLen];
                Array.Copy(rom.Data, tsaAddr, tsaData, 0, tsaLen);

                return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, 32, 20);
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
