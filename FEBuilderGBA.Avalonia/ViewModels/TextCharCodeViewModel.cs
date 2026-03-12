using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Character code table viewer ViewModel.
    /// WinForms: TextCharCodeForm — record size 4 bytes.
    /// W0 / J_0 = ASCII/character code (u16@0).
    /// W2 / J_2 = Terminator value FFFF (u16@2).
    /// L_0_WSPLITSTRING_0 = character display string.
    /// J_0_FONT_ITEM / ItemFontPictureBox = item font preview.
    /// J_0_FONT_SERIF / SerifFontPictureBox = serif font preview.
    /// Search panel: SEARCH_CHAR / SEARCH_CHAR_BUTTON = character search.
    /// Frequency panel: SEARCH_COUNT / SEARCH_COUNT_BUTTON / SEARCH_COUNT_LIST.
    /// </summary>
    public class TextCharCodeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _selectedCode = "";
        ObservableCollection<string> _charCodes = new();
        uint _charCode, _terminatorValue;
        string _characterDisplay = "";
        string _searchChar = "";
        uint _searchFrequencyThreshold;
        int _itemFontWidth;
        int _serifFontWidth;
        string _itemFontWidthText = "";
        string _serifFontWidthText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedCode { get => _selectedCode; set => SetField(ref _selectedCode, value); }
        public ObservableCollection<string> CharCodes { get => _charCodes; set => SetField(ref _charCodes, value); }

        /// <summary>Character code value (u16@0). WinForms: W0 / J_0 "ASCII".</summary>
        public uint CharCode { get => _charCode; set => SetField(ref _charCode, value); }

        /// <summary>Terminator/secondary value (u16@2). WinForms: W2 / J_2 "FFFF".</summary>
        public uint TerminatorValue { get => _terminatorValue; set => SetField(ref _terminatorValue, value); }

        /// <summary>Display string for the character. WinForms: L_0_WSPLITSTRING_0.</summary>
        public string CharacterDisplay { get => _characterDisplay; set => SetField(ref _characterDisplay, value); }

        /// <summary>Character to search for. WinForms: SEARCH_CHAR.</summary>
        public string SearchChar { get => _searchChar; set => SetField(ref _searchChar, value); }

        /// <summary>Frequency threshold for search. WinForms: SEARCH_COUNT.</summary>
        public uint SearchFrequencyThreshold { get => _searchFrequencyThreshold; set => SetField(ref _searchFrequencyThreshold, value); }

        /// <summary>Item font glyph width in pixels.</summary>
        public int ItemFontWidth { get => _itemFontWidth; set { SetField(ref _itemFontWidth, value); ItemFontWidthText = value > 0 ? $"Width: {value}px" : ""; } }

        /// <summary>Serif font glyph width in pixels.</summary>
        public int SerifFontWidth { get => _serifFontWidth; set { SetField(ref _serifFontWidth, value); SerifFontWidthText = value > 0 ? $"Width: {value}px" : ""; } }

        /// <summary>Display text for item font width.</summary>
        public string ItemFontWidthText { get => _itemFontWidthText; set => SetField(ref _itemFontWidthText, value); }

        /// <summary>Display text for serif font width.</summary>
        public string SerifFontWidthText { get => _serifFontWidthText; set => SetField(ref _serifFontWidthText, value); }

        // Legacy aliases
        public uint W0 { get => CharCode; set => CharCode = value; }
        public uint W2 { get => TerminatorValue; set => TerminatorValue = value; }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            CharCode = rom.u16(addr + 0);
            TerminatorValue = rom.u16(addr + 2);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 3 >= (uint)rom.Data.Length) return;

            rom.write_u16(CurrentAddr + 0, (ushort)CharCode);
            rom.write_u16(CurrentAddr + 2, (ushort)TerminatorValue);
        }

        public int GetListCount() => CharCodes.Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["CharCode"] = $"0x{CharCode:X04}",
                ["TerminatorValue"] = $"0x{TerminatorValue:X04}",
                ["CharacterDisplay"] = CharacterDisplay,
                ["CharCodeCount"] = CharCodes.Count.ToString(),
                ["SelectedCode"] = SelectedCode,
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
            };
        }

        // ---- Font glyph rendering ----

        /// <summary>
        /// Render a 2bpp font glyph for the given character code.
        /// Returns null if the font data cannot be found.
        /// </summary>
        /// <param name="charCode">Character code from the text encoding table.</param>
        /// <param name="isItemFont">true for item font, false for serif font.</param>
        /// <param name="outWidth">Glyph width in pixels (from font metadata).</param>
        public byte[]? RenderGlyphRgba(uint charCode, bool isItemFont, out int outWidth)
        {
            outWidth = 0;
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint fontAddr = isItemFont
                ? rom.RomInfo.font_item_address
                : rom.RomInfo.font_serif_address;
            if (fontAddr == 0) return null;

            uint dataAddr = FindFontData(rom, fontAddr, charCode);
            if (dataAddr == U.NOT_FOUND) return null;

            // Font data layout: [4 bytes next ptr] [1 byte moji2] [1 byte width] [2 bytes pad] [64 bytes bitmap]
            outWidth = (int)rom.u8(dataAddr + 5);
            byte[] bitmapData = rom.getBinaryData(dataAddr + 8, 64);

            // 2bpp decode into 16x16 RGBA
            return Decode2bppToRgba(bitmapData, 16, 16, isItemFont);
        }

        /// <summary>
        /// Find font data address for a character code.
        /// Supports Latin1 (English) and SJIS (Japanese) font table layouts.
        /// </summary>
        static uint FindFontData(ROM rom, uint fontTableAddr, uint charCode)
        {
            if (!U.isSafetyOffset(fontTableAddr)) return U.NOT_FOUND;

            if (rom.RomInfo.is_multibyte)
            {
                return FindFontDataSJIS(rom, fontTableAddr, charCode);
            }
            else
            {
                if (charCode > 0xFF)
                    return FindFontDataSJIS(rom, fontTableAddr, charCode);
                return FindFontDataLat1(rom, fontTableAddr, charCode);
            }
        }

        /// <summary>Latin1 font lookup: simple pointer array indexed by character code.</summary>
        static uint FindFontDataLat1(ROM rom, uint fontTableAddr, uint charCode)
        {
            uint idx = charCode & 0xFF;
            uint ptrAddr = fontTableAddr + (idx << 2);
            if (!U.isSafetyOffset(ptrAddr)) return U.NOT_FOUND;
            uint p = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;
            return p;
        }

        /// <summary>SJIS font lookup: hash by high byte, then linked-list search by low byte.</summary>
        static uint FindFontDataSJIS(ROM rom, uint fontTableAddr, uint charCode)
        {
            uint moji1 = (charCode >> 8) & 0xFF;
            uint moji2 = charCode & 0xFF;

            if (moji1 == 0) moji1 = 0x40; // half-width ASCII extension
            else if (moji1 < 0x1F) return U.NOT_FOUND; // control codes have no font

            uint listAddr = fontTableAddr + (moji1 << 2) - 0x100;
            if (!U.isSafetyOffset(listAddr)) return U.NOT_FOUND;
            uint p = rom.p32(listAddr);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            // Walk linked list: struct { u32 next; u8 moji2; u8 width; u8 pad[2]; byte bitmap[64]; }
            int maxIter = 4096; // safety limit
            while (p > 0 && maxIter-- > 0)
            {
                uint check = rom.u8(p + 4);
                if (check == moji2) return p;

                uint next = rom.u32(p);
                if (next == 0) break;
                if (!U.isSafetyPointer(next, rom)) break;
                p = U.toOffset(next);
            }
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Decode 2bpp font bitmap data (64 bytes) into RGBA pixel array (16x16).
        /// 2bpp encoding: each byte = 4 pixels (2 bits each, LSB first).
        /// Palette: 0=background, 1=gray(0xA8), 2=white(0xF8), 3=dark(0x28).
        /// </summary>
        static byte[] Decode2bppToRgba(byte[] data, int width, int height, bool isItemFont)
        {
            // Font palette colors matching WinForms FontForm
            // Item font BG: (0x68, 0x88, 0xA8), Serif font BG: (0xE0, 0xE0, 0xE0)
            byte bgR, bgG, bgB;
            if (isItemFont) { bgR = 0x68; bgG = 0x88; bgB = 0xA8; }
            else            { bgR = 0xE0; bgG = 0xE0; bgB = 0xE0; }

            // 4-color palette: [bg, gray, white, dark]
            byte[][] palette = new byte[][]
            {
                new byte[] { bgR, bgG, bgB, 0xFF },       // 0: background
                new byte[] { 0xA8, 0xA8, 0xA7, 0xFF },    // 1: gray
                new byte[] { 0xF8, 0xF8, 0xF8, 0xFF },    // 2: white (text)
                new byte[] { 0x28, 0x28, 0x28, 0xFF },     // 3: dark
            };

            byte[] rgba = new byte[width * height * 4];
            int x = 0, y = 0;
            int length = Math.Min(data.Length, (width * height) / 4);

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                // Each byte = 4 pixels, 2 bits each, LSB first
                for (int bit = 0; bit < 4; bit++)
                {
                    int colorIdx = (b >> (bit * 2)) & 0x03;
                    int px = x + bit;
                    if (px < width && y < height)
                    {
                        int offset = (y * width + px) * 4;
                        rgba[offset + 0] = palette[colorIdx][0];
                        rgba[offset + 1] = palette[colorIdx][1];
                        rgba[offset + 2] = palette[colorIdx][2];
                        rgba[offset + 3] = palette[colorIdx][3];
                    }
                }
                x += 4;
                if (x >= width)
                {
                    x = 0;
                    y++;
                }
            }
            return rgba;
        }
    }
}
