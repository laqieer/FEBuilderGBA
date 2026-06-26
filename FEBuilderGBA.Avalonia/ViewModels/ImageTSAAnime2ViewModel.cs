using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageTSAAnime2ViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _unknown0, _unknown2, _unknown4, _unknown6;
        uint _tsaHeaderPointer;

        // #1456: each per-category 12-byte entry maps to its category's SHARED
        // header base (dataAddr). IMAGE/PALETTE live at headerBase+16/+4 (shared
        // by every entry in the category); only the TSA pointer (entryAddr+8)
        // varies per entry. Keyed on the entry address so HeaderBase resolves
        // correctly for entry[i>0] without relying on stale "last loaded" state.
        // Rebuilt on every LoadList; lookup falls back to the entry[0] formula
        // for raw-navigation addresses not produced by LoadList.
        readonly Dictionary<uint, uint> _entryHeaderBase = new Dictionary<uint, uint>();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // #1456: the list exposes EVERY 12-byte entry in a category, each living
        // at headerBase + 20 + i*12 (see LoadList). All entries in a category
        // share ONE header, so the IMAGE/PALETTE pointers are common while the
        // TSA pointer varies per entry. The header base therefore comes from the
        // per-entry map (HeaderBase, keyed on CurrentAddr), NOT an unconditional
        // CurrentAddr - HEADER_SIZE (that formula is only valid for entry[0] and
        // remains the fallback for raw-navigation addresses). The coupled
        // pointers, relative to the resolved header base:
        //   palette pointer  @ headerBase + 4   (raw 0x20)
        //   image   pointer  @ headerBase + 16  (LZ77)
        //   per-entry TSA ptr @ CurrentAddr + 8 (raw header-wrapped TSA)
        // This mirrors WinForms ImageTSAAnime2Form.MakeAllDataLength.
        public const uint HEADER_SIZE = 20;

        /// <summary>
        /// Shared-header base address for the selected v2 entry.
        /// <para>
        /// #1456: a category exposes N 12-byte entries (entry[i] at
        /// <c>headerBase + 20 + i*12</c>), all sharing one header. The IMAGE/
        /// PALETTE pointers live in that header, so the base must come from the
        /// per-entry map (keyed on <see cref="CurrentAddr"/>) — NOT the
        /// <c>CurrentAddr - HEADER_SIZE</c> formula, which is only valid for
        /// entry[0]. The formula remains the fallback for raw-navigation
        /// addresses (e.g. <c>NavigateTo</c>) that LoadList never enumerated;
        /// keying on <see cref="CurrentAddr"/> means a stale map entry can never
        /// resolve the header for a different active entry.
        /// </para>
        /// </summary>
        public uint HeaderBase
        {
            get
            {
                if (_entryHeaderBase.TryGetValue(CurrentAddr, out uint hb))
                    return hb;
                return (CurrentAddr >= HEADER_SIZE) ? CurrentAddr - HEADER_SIZE : 0;
            }
        }
        /// <summary>ROM offset of the header IMAGE (LZ77) pointer slot.</summary>
        public uint ImagePointerAddr => HeaderBase + 16;
        /// <summary>ROM offset of the header PALETTE (raw 0x20) pointer slot.</summary>
        public uint PalettePointerAddr => HeaderBase + 4;
        /// <summary>ROM offset of the per-entry TSA (raw header-wrapped) pointer slot.</summary>
        public uint TSAPointerAddr => CurrentAddr + 8;

        // W0: Unknown parameter 0
        public uint Unknown0 { get => _unknown0; set => SetField(ref _unknown0, value); }
        // W2: Unknown parameter 2
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        // W4: Unknown parameter 4
        public uint Unknown4 { get => _unknown4; set => SetField(ref _unknown4, value); }
        // W6: Unknown parameter 6
        public uint Unknown6 { get => _unknown6; set => SetField(ref _unknown6, value); }
        // D8: TSA header pointer
        public uint TSAHeaderPointer { get => _tsaHeaderPointer; set => SetField(ref _tsaHeaderPointer, value); }

        /// <summary>
        /// Count the 12-byte entries inside a v2 category header. Mirrors the
        /// WinForms <c>ImageTSAAnime2Form</c> main <c>InputFormRef</c>
        /// (BlockSize 12, count-callback <c>isPointer(u32(addr+8))</c>) walked by
        /// <c>ROM.getBlockDataCount</c>: starting at <paramref name="entry0Addr"/>
        /// (= dataAddr + 20), count entry <c>i</c> while the per-entry TSA pointer
        /// slot (<c>addr + 8</c>) is pointer-shaped, stopping at the first entry
        /// that is not (or that would read past EOF). #1456.
        /// </summary>
        internal static uint CountCategoryEntries(ROM rom, uint entry0Addr)
        {
            if (rom?.Data == null) return 0;
            uint len = (uint)rom.Data.Length;
            uint addr = entry0Addr;
            uint i = 0;
            // EOF-hard-bound: need the full 12-byte entry (the +8 pointer slot is
            // the last 4 bytes) before reading it.
            while (addr + SIZE <= len)
            {
                // List-time check is pointer SHAPE only (matches WinForms, which
                // does NOT require the TSA target to be a safety offset here).
                if (!U.isPointer(rom.u32(addr + 8))) break;
                addr += SIZE;
                i++;
            }
            return i;
        }

        public List<AddrResult> LoadList()
        {
            _entryHeaderBase.Clear();

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Load TSA anime entries from config resource (matches WinForms g_TSAAnime)
            var tsaAnime = U.LoadTSVResource1(U.ConfigDataFilename("tsaanime2_"), false);
            if (tsaAnime == null || tsaAnime.Count == 0)
                return new List<AddrResult>();

            var result = new List<AddrResult>();
            foreach (var pair in tsaAnime)
            {
                uint pointer = pair.Key;
                string catName = pair.Value;

                // Resolve the pointer to get the actual data address (category header base)
                uint offset = U.toOffset(pointer);
                if (!U.isSafetyOffset(offset, rom)) continue;

                uint dataAddr = rom.p32(offset);
                if (!U.isSafetyOffset(dataAddr, rom)) continue;

                // #1456: enumerate EVERY 12-byte entry in this category, not just
                // entry[0]. The per-entry list starts 20 bytes after the header
                // (WinForms ReInit(addr + 20)); stride 12 == the loader record size
                // read by LoadEntry (addr+0..+8), so list geometry == data geometry.
                uint entry0Addr = dataAddr + 20;
                uint count = CountCategoryEntries(rom, entry0Addr);
                for (uint i = 0; i < count; i++)
                {
                    uint entryAddr = entry0Addr + i * SIZE;
                    // Map every entry to its SHARED category header base so
                    // HeaderBase (and thus IMAGE/PALETTE) resolves correctly for
                    // entry[i>0].
                    _entryHeaderBase[entryAddr] = dataAddr;
                    string name = U.ToHexString(pointer) + " " + catName + " " + U.To0xHexString(i);
                    result.Add(new AddrResult(entryAddr, name, pointer));
                }
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            Unknown0 = rom.u16(addr + 0);
            Unknown2 = rom.u16(addr + 2);
            Unknown4 = rom.u16(addr + 4);
            Unknown6 = rom.u16(addr + 6);
            TSAHeaderPointer = rom.u32(addr + 8);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u16(addr + 0, Unknown0);
            rom.write_u16(addr + 2, Unknown2);
            rom.write_u16(addr + 4, Unknown4);
            rom.write_u16(addr + 6, Unknown6);
            rom.write_u32(addr + 8, TSAHeaderPointer);
        }

        // WinForms ImageTSAAnime2Form renders the per-entry preview with a
        // 32-tile (256px) TSA stride: DrawTSAAnime2 calls
        // ByteToImage16TileHeaderTSA(32*8, height, ...). We mirror that here.
        const int PREVIEW_WIDTH_TILES = 32;

        /// <summary>
        /// Decode the v2 entry preview the same way WinForms
        /// <c>ImageTSAAnime2Form.DrawTSAAnime2</c> does: image tiles (LZ77) from
        /// the shared header (<see cref="ImagePointerAddr"/>), raw 0x20 palette
        /// (<see cref="PalettePointerAddr"/>), and the per-entry RAW header-wrapped
        /// TSA (<see cref="TSAPointerAddr"/>). Returns null on any bad pointer.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr < HEADER_SIZE) return null;
            try
            {
                uint imgPtr = rom.u32(ImagePointerAddr);
                uint palPtr = rom.u32(PalettePointerAddr);
                uint tsaPtr = rom.u32(TSAPointerAddr);
                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr) || !U.isPointer(tsaPtr)) return null;

                uint imgOff = U.toOffset(imgPtr);
                uint palOff = U.toOffset(palPtr);
                uint tsaOff = U.toOffset(tsaPtr);
                if (!U.isSafetyOffset(imgOff, rom) || !U.isSafetyOffset(palOff, rom) || !U.isSafetyOffset(tsaOff, rom))
                    return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgOff);
                if (tileData == null || tileData.Length == 0) return null;

                // v2 TSA is RAW header-wrapped (NOT LZ77) - read the raw bytes.
                // The header's first two bytes give masterHeaderX/Y, so the byte
                // length is 2 + (mhx+1)*(mhy+1)*2.
                byte[] tsaData = ReadHeaderTSABytes(rom.Data, tsaOff);
                if (tsaData == null || tsaData.Length < 2) return null;

                // Palette is raw ROM data (not LZ77), 16 colors = 0x20 bytes.
                byte[] palette = ImageUtilCore.GetPalette(rom, palOff, 16);
                if (palette == null || palette.Length == 0) return null;

                int heightTiles = tsaData[1] + 1; // masterHeaderY + 1
                if (heightTiles < 1) heightTiles = 1;

                // WinForms ImageTSAAnime2Form.DrawTSAAnime2 -> ByteToImage16TileInner
                // skips ONLY 0xFFFF; tile index 0 is a valid drawn tile (and the
                // import encoder assigns the first unique tile index 0). Pass
                // skipTile0:false for WinForms parity so a tile-0 cell renders.
                return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette,
                    PREVIEW_WIDTH_TILES, heightTiles, is4bpp: true, tsaAddend: 0,
                    paletteShift: 0, skipTile0: false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Read a RAW header-wrapped TSA block from <paramref name="data"/> at
        /// <paramref name="offset"/>. Length = 2 + (mhx+1)*(mhy+1)*2 (mirrors
        /// WinForms <c>ImageUtil.CalcByteLengthForHeaderTSAData</c>). Returns null
        /// when the header is out of range.
        /// </summary>
        internal static byte[] ReadHeaderTSABytes(byte[] data, uint offset)
        {
            if (data == null || offset + 2 > (uint)data.Length) return null;
            int mhx = data[offset];
            int mhy = data[offset + 1];
            // Header bytes are 0..255 each, so the max length is
            // 2 + 256*256*2 = 131074 — comfortably an int, no truncation risk.
            int len = 2 + (mhx + 1) * (mhy + 1) * 2;
            if ((long)offset + len > data.Length) return null;
            byte[] tsa = new byte[len];
            Array.Copy(data, offset, tsa, 0, len);
            return tsa;
        }

        /// <summary>
        /// Right-pad indexed pixels from <paramref name="srcWidth"/> to
        /// <paramref name="dstWidth"/> with index-0 (transparent) columns. Mirrors
        /// WinForms <c>ImageTSAAnimeForm.ImportButton_Click</c>, which accepts a
        /// 30*8 (240px) image and copies it onto a 32*8 (256px) canvas so the
        /// header-TSA's 2-tile right margin is auto-inserted. No-op when the widths
        /// match. Returns the original array when args are invalid.
        /// </summary>
        public static byte[] PadIndexedWidth(byte[] pixels, int srcWidth, int height, int dstWidth)
        {
            if (pixels == null || srcWidth <= 0 || height <= 0 || dstWidth < srcWidth) return pixels;
            if (dstWidth == srcWidth) return pixels;
            if (pixels.Length < srcWidth * height) return pixels;
            byte[] padded = new byte[dstWidth * height]; // zero-filled = index 0
            for (int y = 0; y < height; y++)
                Array.Copy(pixels, y * srcWidth, padded, y * dstWidth, srcWidth);
            return padded;
        }

        public int GetListCount()
        {
            return LoadList().Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unknown0"] = $"0x{Unknown0:X04}",
                ["Unknown2"] = $"0x{Unknown2:X04}",
                ["Unknown4"] = $"0x{Unknown4:X04}",
                ["Unknown6"] = $"0x{Unknown6:X04}",
                ["TSAHeaderPointer"] = $"0x{TSAHeaderPointer:X08}",
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
                ["u16@0"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@2"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@4"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@6"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Unknown0"] = "u16@0",
            ["Unknown2"] = "u16@2",
            ["Unknown4"] = "u16@4",
            ["Unknown6"] = "u16@6",
            ["TSAHeaderPointer"] = "u32@8",
        };
    }
}
