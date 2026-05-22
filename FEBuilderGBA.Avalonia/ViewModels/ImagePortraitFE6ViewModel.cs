using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        // WF ImagePortraitFE6Form portrait entry layout: 16 bytes per entry
        //   +0  u32  Unit Face image pointer (LZ77 compressed)
        //   +4  u32  Mini/Map face pointer
        //   +8  u32  Palette pointer (raw, 16 colors = 32 bytes)
        //   +12 u8   Mouth coord X
        //   +13 u8   Mouth coord Y
        //   +14 u8   Unused (B14)
        //   +15 u8   Unused (B15)
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        uint _portraitImagePtr, _miniPortraitPtr, _palettePtr;
        uint _mouthX, _mouthY, _unused14, _unused15;

        // Read-only display values surfaced for the new top-of-list config
        // bar + selection bar (mirrors WF ReadStartAddress / ReadCount /
        // Size: / 選択アドレス: labels). These are updated by LoadList().
        uint _readStartAddress;
        uint _readCount;

        // Comment text — WF mirrors the Resource Cache entry for the
        // currently selected portrait. Avalonia round-trips it through the
        // VM so the View can bind a TextBox.
        string _comment = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Portrait image data pointer (Unit Face)
        public uint PortraitImagePtr { get => _portraitImagePtr; set => SetField(ref _portraitImagePtr, value); }
        // D4: Mini portrait / map sprite face pointer
        public uint MiniPortraitPtr { get => _miniPortraitPtr; set => SetField(ref _miniPortraitPtr, value); }
        // D8: Palette pointer
        public uint PalettePtr { get => _palettePtr; set => SetField(ref _palettePtr, value); }
        // B12: Mouth coordinate X
        public uint MouthX { get => _mouthX; set => SetField(ref _mouthX, value); }
        // B13: Mouth coordinate Y
        public uint MouthY { get => _mouthY; set => SetField(ref _mouthY, value); }
        // B14: Unused / reserved
        public uint Unused14 { get => _unused14; set => SetField(ref _unused14, value); }
        // B15: Unused / reserved
        public uint Unused15 { get => _unused15; set => SetField(ref _unused15, value); }

        /// <summary>Block size in bytes (matches WF `BlockSize` label = 16).</summary>
        public uint BlockSize => SIZE;

        /// <summary>
        /// Start address of the portrait table (matches WF `先頭アドレス`).
        /// Populated by LoadList() so the view's top-of-list bar can display it.
        /// </summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }

        /// <summary>
        /// Number of valid entries (matches WF `読込数`).
        /// Populated by LoadList().
        /// </summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        /// <summary>
        /// Per-portrait comment text (matches WF `コメント`).
        /// Round-trips through the VM; the View binds a TextBox to this.
        /// </summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value ?? string.Empty); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr))
            {
                ReadStartAddress = 0;
                ReadCount = 0;
                return new List<AddrResult>();
            }

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = SIZE;

            ReadStartAddress = baseAddr;

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint u0 = rom.u32(addr + 0);
                uint u4 = rom.u32(addr + 4);
                uint u8 = rom.u32(addr + 8);
                if (i > 0)
                {
                    if (!U.isPointerOrNULL(u0) || !U.isPointerOrNULL(u4) || !U.isPointerOrNULL(u8))
                        break;
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {
                        nullCount++;
                        if (nullCount >= 10) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait FE6";
                result.Add(new AddrResult(addr, name, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            PortraitImagePtr = rom.u32(addr + 0);
            MiniPortraitPtr = rom.u32(addr + 4);
            PalettePtr = rom.u32(addr + 8);
            MouthX = rom.u8(addr + 12);
            MouthY = rom.u8(addr + 13);
            Unused14 = rom.u8(addr + 14);
            Unused15 = rom.u8(addr + 15);

            IsLoaded = true;
        }

        /// <summary>
        /// Write the current entry to ROM. The caller passes its
        /// <see cref="UndoService"/> instance but does NOT open a Begin
        /// scope around the call — this method owns the single
        /// <c>Begin</c>/<c>Commit</c> pair (Rollback runs on exception)
        /// and every <c>rom.write_*</c> lives strictly between them.
        ///
        /// Single-owner pattern per Copilot CLI plan-review point 1 on
        /// https://github.com/laqieer/FEBuilderGBA/issues/435 — the View's
        /// <c>WriteButton_Click</c> delegates here without opening or
        /// nesting its own scope.
        /// </summary>
        public void Write(UndoService undoService)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;
            if (undoService == null) { Write(); return; }

            uint addr = CurrentAddr;
            undoService.Begin("Write Portrait FE6");
            try
            {
                rom.write_u32(addr + 0, PortraitImagePtr);
                rom.write_u32(addr + 4, MiniPortraitPtr);
                rom.write_u32(addr + 8, PalettePtr);
                rom.write_u8(addr + 12, MouthX);
                rom.write_u8(addr + 13, MouthY);
                rom.write_u8(addr + 14, Unused14);
                rom.write_u8(addr + 15, Unused15);
                undoService.Commit();
            }
            catch
            {
                undoService.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Parameterless legacy overload — kept for non-View callers (tests,
        /// CLI helpers). Spins up its own UndoService so the writes still
        /// register in the undo buffer.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;

            var undoService = new UndoService();
            Write(undoService);
        }

        /// <summary>
        /// Draw the FE6 portrait. Returns the assembled 96x80 portrait image, or the map sprite if no main face.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null || CurrentAddr == 0) return null;
            try
            {
                uint unitFace = PortraitImagePtr;
                uint mapFace = MiniPortraitPtr;
                uint palette = PalettePtr;

                // Try main portrait first
                if (U.isPointer(unitFace) && U.isPointer(palette))
                {
                    uint faceOff = U.toOffset(unitFace);
                    uint palOff = U.toOffset(palette);
                    if (U.isSafetyOffset(faceOff) && U.isSafetyOffset(palOff))
                    {
                        // FE6 portraits are LZ77 compressed
                        byte[] imageUZ = LZ77.decompress(rom.Data, faceOff);
                        if (imageUZ != null && imageUZ.Length > 0)
                        {
                            // Decode as 256x40 (32*8 x 5*8) tile sheet
                            byte[] gbaPalette = ImageUtilCore.GetPalette(palOff, 16);
                            if (gbaPalette != null)
                            {
                                // Return the raw sprite sheet (32*8 x 5*8) for comparison
                                return svc.Decode4bppTiles(imageUZ, 0, 32 * 8, 5 * 8, gbaPalette);
                            }
                        }
                    }
                }

                // Fallback to map portrait
                if (U.isPointer(mapFace) && U.isPointer(palette))
                    return PortraitRendererCore.DrawPortraitMap(mapFace, palette);

                return null;
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PortraitImagePtr"] = $"0x{PortraitImagePtr:X08}",
                ["MiniPortraitPtr"] = $"0x{MiniPortraitPtr:X08}",
                ["PalettePtr"] = $"0x{PalettePtr:X08}",
                ["MouthX"] = $"0x{MouthX:X02}",
                ["MouthY"] = $"0x{MouthY:X02}",
                ["Unused14"] = $"0x{Unused14:X02}",
                ["Unused15"] = $"0x{Unused15:X02}",
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
                ["u32@0_PortraitImagePtr"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4_MiniPortraitPtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8_PalettePtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@12_MouthX"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@13_MouthY"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@14_Unused14"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@15_Unused15"] = $"0x{rom.u8(a + 15):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["PortraitImagePtr"] = "u32@0_PortraitImagePtr",
            ["MiniPortraitPtr"] = "u32@4_MiniPortraitPtr",
            ["PalettePtr"] = "u32@8_PalettePtr",
            ["MouthX"] = "u8@12_MouthX",
            ["MouthY"] = "u8@13_MouthY",
            ["Unused14"] = "u8@14_Unused14",
            ["Unused15"] = "u8@15_Unused15",
        };
    }
}
