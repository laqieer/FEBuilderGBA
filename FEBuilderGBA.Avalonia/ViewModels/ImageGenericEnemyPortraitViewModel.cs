using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageGenericEnemyPortraitViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 4;

        // The palette pointer slot is FIXED at entryAddr + 0x20 (8 image slots +
        // 8 palette slots are reserved per table) across FE6/FE7/FE8 — NOT
        // count*4. Mirrors WF ImageGenericEnemyPortraitForm.cs (ROM.u32(addr) for
        // the image, ROM.u32(addr + 8*4) for the palette). #907 CORRECTION 4.
        public const uint PALETTE_SLOT_OFFSET = 8 * 4; // 0x20

        uint _currentAddr;
        bool _isLoaded;
        uint _imagePointer;
        uint _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Image data pointer (J_0: "Image")
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }

        // Palette data pointer (fixed slot @ entryAddr + 0x20).
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.generic_enemy_portrait_pointer;
            uint count = rom.RomInfo.generic_enemy_portrait_count;
            if (pointer == 0 || count == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = baseAddr + i * SIZE;
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint imgPtr = rom.u32(addr);
                string ptrStr = U.isPointer(imgPtr) ? $"0x{imgPtr:X08}" : "NULL";
                result.Add(new AddrResult(addr, $"0x{i:X2} {ptrStr}", i));
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
            // Palette pointer lives at the FIXED +0x20 slot (NOT count*4).
            PalettePointer = ReadPalettePointer(rom, addr);

            IsLoaded = true;
        }

        /// <summary>
        /// Read the palette pointer for an entry at the FIXED +0x20 slot.
        /// Returns 0 when the slot is out of bounds (no throw).
        /// </summary>
        static uint ReadPalettePointer(ROM rom, uint addr)
        {
            ulong palSlot = (ulong)addr + PALETTE_SLOT_OFFSET;
            if (palSlot + SIZE > (ulong)rom.Data.Length) return 0;
            return rom.u32((uint)palSlot);
        }

        /// <summary>
        /// Render the selected 32x32 4bpp portrait (RAW image at the image
        /// pointer, decoded against the RAW 16-color palette at the palette
        /// pointer). Returns null (no throw) when either pointer is unset /
        /// out of range, mirroring WF Draw()'s BlankDummy fallback.
        /// </summary>
        public IImage RenderPreview()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null) return null;
            if (!IsLoaded) return null;

            uint imgPtr = ImagePointer;
            uint palPtr = PalettePointer;
            if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

            uint imgOffset = U.toOffset(imgPtr);
            uint palOffset = U.toOffset(palPtr);
            if (!U.isSafetyOffset(imgOffset, rom) || !U.isSafetyOffset(palOffset, rom)) return null;

            try
            {
                // 16-color RAW palette (32 bytes) at the palette pointer.
                byte[] palette = ImageUtilCore.GetPalette(palOffset, 16);
                if (palette == null) return null;

                // 32x32 = 4x4 tiles, RAW 4bpp (isCompressed: false).
                return ImageUtilCore.LoadROMTiles4bpp(imgOffset, palette, 4, 4, isCompressed: false);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageGenericEnemyPortraitViewModel.RenderPreview failed: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Read the RAW 16-color (32-byte) palette at the selected entry's
        /// palette pointer, for LoadAndRemapFromFile's closest-color remap.
        /// Returns null (no throw) when the pointer is unset / out of range.
        /// </summary>
        public byte[] ReadActivePaletteBytes()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !IsLoaded) return null;
            uint palPtr = PalettePointer;
            if (!U.isPointer(palPtr)) return null;
            uint palOffset = U.toOffset(palPtr);
            if (!U.isSafetyOffset(palOffset, rom)) return null;
            return ImageUtilCore.GetPalette(palOffset, 16);
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
        }

        public int GetListCount()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return (int)rom.RomInfo.generic_enemy_portrait_count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0_ImagePtr"] = $"0x{rom.u32(a + 0):X08}",
            };
            ulong palSlot = (ulong)a + PALETTE_SLOT_OFFSET;
            if (palSlot + SIZE <= (ulong)rom.Data.Length)
                report["u32@20_PalettePtr"] = $"0x{rom.u32((uint)palSlot):X08}";
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["ImagePointer"] = "u32@0_ImagePtr",
            ["PalettePointer"] = "u32@20_PalettePtr",
        };
    }
}
