using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Main game-font glyph editor view-model (#1165). Enumerates the item/serif
    /// font hash table into a glyph list, renders the selected glyph, and feeds
    /// the per-glyph PNG import + bulk export/import flows. All ROM logic lives in
    /// <see cref="FontGlyphRenderCore"/> (GUI-free).
    /// </summary>
    public class FontEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _currentMoji;
        int _currentWidth;
        string _currentName = "";
        bool _isLoaded;
        // 0 = Item font, 1 = Serif font (matches WinForms FontType combo order).
        int _fontTypeIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint CurrentMoji { get => _currentMoji; set => SetField(ref _currentMoji, value); }
        public int CurrentWidth { get => _currentWidth; set => SetField(ref _currentWidth, value); }
        public string CurrentName { get => _currentName; set => SetField(ref _currentName, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int FontTypeIndex { get => _fontTypeIndex; set => SetField(ref _fontTypeIndex, value); }

        /// <summary>True when the item font (index 0) is selected.</summary>
        public bool IsItemFont => _fontTypeIndex == 0;

        /// <summary>
        /// Enumerate every glyph in the currently-selected font into address-list
        /// rows. Each row's <c>addr</c> is the glyph struct address and its
        /// <c>tag</c> carries the engine character code (moji) so selection can
        /// re-key the glyph for import.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var glyphs = FontGlyphRenderCore.EnumerateGlyphs(rom, IsItemFont);
            var result = new List<AddrResult>();
            foreach (var g in glyphs)
            {
                string label = U.ToHexString(g.Moji) + " " + g.Name;
                result.Add(new AddrResult(g.Addr, label, g.Moji));
            }
            return result;
        }

        /// <summary>
        /// Load the glyph at <paramref name="addr"/> (the row's address). The row
        /// tag holds the character code, so we read width/name from the struct.
        /// </summary>
        public void LoadEntry(uint addr, uint moji)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            CurrentMoji = moji;
            CurrentWidth = U.isSafetyOffset(addr, rom) && (ulong)addr + 6 <= (ulong)rom.Data.Length
                ? (int)rom.u8(addr + 5)
                : 0;
            IsLoaded = true;
        }

        /// <summary>Render the currently-selected glyph (16x16 RGBA) or null.</summary>
        public IImage TryRenderGlyph()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try { return FontGlyphRenderCore.RenderGlyph(rom, CurrentAddr, IsItemFont); }
            catch { return null; }
        }

        // ---- IDataVerifiable ----

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport() => new()
        {
            ["addr"] = $"0x{CurrentAddr:X08}",
            ["moji"] = $"0x{CurrentMoji:X04}",
            ["width"] = CurrentWidth.ToString(),
        };

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0 || !U.isSafetyOffset(CurrentAddr, rom))
                return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["width"] = "u8@0x05",
        };
    }
}
