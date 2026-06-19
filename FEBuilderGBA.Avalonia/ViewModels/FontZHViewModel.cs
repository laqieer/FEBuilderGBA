using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Chinese (ZH) game-font glyph editor view-model (#1166). Enumerates the
    /// item/serif ZH font (a directly-referenced codeB array) into a glyph list,
    /// renders the selected glyph, and feeds the per-glyph PNG import flow. All ROM
    /// logic lives in <see cref="FontGlyphZHCore"/> (GUI-free). Structural twin of
    /// <see cref="FontEditorViewModel"/> (the #1165 main-font editor).
    /// </summary>
    public class FontZHViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _currentMoji;
        int _currentWidth;
        bool _isLoaded;
        // 0 = Item font, 1 = Serif font (matches the WinForms FontZHForm FontType combo).
        int _fontTypeIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint CurrentMoji { get => _currentMoji; set => SetField(ref _currentMoji, value); }
        public int CurrentWidth { get => _currentWidth; set => SetField(ref _currentWidth, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int FontTypeIndex { get => _fontTypeIndex; set => SetField(ref _fontTypeIndex, value); }

        /// <summary>True when the item font (index 0) is selected.</summary>
        public bool IsItemFont => _fontTypeIndex == 0;

        /// <summary>
        /// Enumerate every glyph in the currently-selected ZH font into address-list
        /// rows. Each row's <c>addr</c> is the glyph struct address and its <c>tag</c>
        /// carries the engine character code (moji) so selection can re-key the glyph
        /// for import.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var glyphs = FontGlyphZHCore.EnumerateGlyphsZH(rom, IsItemFont);
            var result = new List<AddrResult>();
            foreach (var g in glyphs)
            {
                string label = U.ToHexString(g.Moji) + " " + g.Name;
                result.Add(new AddrResult(g.Addr, label, g.Moji));
            }
            return result;
        }

        /// <summary>
        /// Load the glyph at <paramref name="addr"/> (the row's address). The row tag
        /// holds the character code, so we read the width from the struct (byte @ +1).
        /// </summary>
        public void LoadEntry(uint addr, uint moji)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            CurrentMoji = moji;
            CurrentWidth = U.isSafetyOffset(addr, rom) && (ulong)addr + 2 <= (ulong)rom.Data.Length
                ? (int)rom.u8(addr + 1)
                : 0;
            IsLoaded = true;
        }

        /// <summary>
        /// Address of the glyph for <paramref name="moji"/> in the current font, or 0
        /// if not present. Used to re-select a glyph by char code after a list reload.
        /// </summary>
        public uint FindAddrByMoji(uint moji)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint addr = FontGlyphZHCore.FindGlyphZH(rom, IsItemFont, moji);
            return addr == U.NOT_FOUND ? 0 : addr;
        }

        /// <summary>Render the currently-selected glyph (16x13 RGBA) or null.</summary>
        public IImage TryRenderGlyph()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try { return FontGlyphZHCore.RenderGlyphZH(rom, CurrentAddr, IsItemFont); }
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
            uint a = CurrentAddr;
            // Guard the full +1 read (isSafetyOffset only checks the first byte) so a
            // near-EOF CurrentAddr can't throw during data verification.
            if (rom == null || a == 0 || !U.isSafetyOffset(a, rom)
                || (ulong)a + 2 > (ulong)rom.Data.Length)
                return new Dictionary<string, string>();
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["width"] = "u8@0x01",
        };
    }
}
