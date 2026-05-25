// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform port (#536) of the pure ROM-arithmetic FontForm static helpers
// that ToolTranslateROMFont / ToolTranslateROMWipeJPFont depend on:
//   FontForm.GetFontPointer / FindFontData (SJIS+UTF8+LAT1) / MakeNewFontData /
//   TransportFontStruct.
//
// The bitmap-rendering parts of FontForm (DrawFont / DrawFontString /
// ImageUtil.AutoGenerateFont) stay in WinForms because they depend on
// System.Drawing.Bitmap. The math/lookup portion - which is what
// ToolTranslateROMCore needs - is system-encoding-agnostic and lives here.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// ROM-only font-table helpers. Equivalent to the WinForms FontForm
    /// static methods, but takes the ROM as an explicit argument instead of
    /// depending on Program.ROM. The WinForms FontForm class continues to
    /// expose its own wrappers for back-compat; those wrappers now delegate
    /// here so both platforms share one source of truth.
    /// </summary>
    public static class FontCore
    {
        /// <summary>
        /// Return the head address of the font-pointer table on the given ROM.
        /// Item font (skill names, item names) vs. serif font (dialogue) live
        /// in two separate hash tables.
        /// </summary>
        public static uint GetFontPointer(bool isItemFont, ROM rom)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            return isItemFont ? rom.RomInfo.font_item_address : rom.RomInfo.font_serif_address;
        }

        // ---------- FindFontData ----------

        static uint FindFontDataSJIS(uint topaddress, uint moji, out uint prevaddr, ROM rom)
        {
            uint moji1 = ((moji >> 8) & 0xff);
            uint moji2 = (moji & 0xff);
            if (moji1 == 0)
            {
                // Extended half-width alphabet font lives under 0x40.
                moji1 = 0x40;
            }
            else if (moji1 < 0x1f)
            {
                prevaddr = U.NOT_FOUND;
                return U.NOT_FOUND;
            }

            uint list = topaddress + (moji1 << 2) - 0x100;
            prevaddr = list;

            if (!U.isSafetyOffset(list, rom)) return U.NOT_FOUND;
            uint p = rom.p32(list);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            while (p > 0)
            {
                uint next = rom.u32(p);
                uint check = rom.u8(p + 4);
                if (check == moji2) return p;

                prevaddr = p;
                if (next == 0) break;
                if (!U.isSafetyPointer(next, rom)) break;
                p = U.toOffset(next);
            }

            return U.NOT_FOUND;
        }

        static uint FindFontDataUTF8(uint topaddress, uint moji, out uint prevaddr, ROM rom)
        {
            uint moji1 = (moji & 0xff);
            uint list = topaddress + (moji1 << 2);
            prevaddr = list;

            if (!U.isSafetyOffset(list, rom)) return U.NOT_FOUND;
            uint p = rom.p32(list);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            uint moji2 = ((moji >> 8) & 0xff);
            uint moji3 = ((moji >> 16) & 0xff);
            uint moji4 = ((moji >> 24) & 0xff);

            while (p > 0)
            {
                uint next = rom.u32(p);
                uint check2 = rom.u8(p + 4);
                uint check3 = rom.u8(p + 6);
                uint check4 = rom.u8(p + 7);
                if (check2 == moji2 && check3 == moji3 && check4 == moji4) return p;

                prevaddr = p;
                if (next == 0) break;
                if (!U.isSafetyPointer(next, rom)) break;
                p = U.toOffset(next);
            }

            return U.NOT_FOUND;
        }

        static uint FindFontDataLat1(uint topaddress, uint moji, out uint prevaddr, ROM rom)
        {
            uint moji2 = (moji & 0xff);
            uint list = topaddress + (moji2 << 2);
            prevaddr = list;

            if (!U.isSafetyOffset(list, rom)) return U.NOT_FOUND;
            uint p = rom.p32(list);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            // English ROM: direct lookup, no list traversal.
            return p;
        }

        /// <summary>
        /// Locate the font-data struct for the given character code in the
        /// font hash table at <paramref name="topaddress"/>. Returns the data
        /// address on success, U.NOT_FOUND when the font isn't present.
        /// <paramref name="prevaddr"/> receives the previous link in the hash
        /// chain (useful for in-place edits / appends).
        /// </summary>
        public static uint FindFontData(uint topaddress, uint moji, out uint prevaddr,
            ROM rom, PRIORITY_CODE priorityCode)
        {
            if (!U.isSafetyOffset(topaddress, rom))
            {
                prevaddr = U.NOT_FOUND;
                return U.NOT_FOUND;
            }

            if (priorityCode == PRIORITY_CODE.UTF8)
            {
                return FindFontDataUTF8(topaddress, moji, out prevaddr, rom);
            }
            else if (rom.RomInfo.is_multibyte)
            {
                return FindFontDataSJIS(topaddress, moji, out prevaddr, rom);
            }
            else
            {
                if (moji > 0xff || priorityCode == PRIORITY_CODE.SJIS)
                {
                    return FindFontDataSJIS(topaddress, moji, out prevaddr, rom);
                }
                return FindFontDataLat1(topaddress, moji, out prevaddr, rom);
            }
        }

        // ---------- MakeNewFontData ----------

        static byte[] NewFontDataSJIS(uint moji, uint width, byte[] selectFontBitmapByte)
        {
            byte[] newFontData = new byte[8 + 64];
            uint moji2 = (moji & 0xff);
            U.write_u8(newFontData, 4, moji2);
            U.write_u8(newFontData, 5, width);
            U.write_u8(newFontData, 6, 0);
            U.write_u8(newFontData, 7, 0);
            U.write_range(newFontData, 8, selectFontBitmapByte);
            return newFontData;
        }

        static byte[] NewFontDataLAT1(uint moji, uint width, byte[] selectFontBitmapByte)
        {
            byte[] newFontData = new byte[8 + 64];
            U.write_u8(newFontData, 4, 0);
            U.write_u8(newFontData, 5, width);
            U.write_u8(newFontData, 6, 0);
            U.write_u8(newFontData, 7, 0);
            U.write_range(newFontData, 8, selectFontBitmapByte);
            return newFontData;
        }

        static byte[] NewFontDataUTF8(uint moji, uint width, byte[] selectFontBitmapByte)
        {
            byte[] newFontData = new byte[8 + 64];
            uint moji2 = ((moji >> 8) & 0xff);
            uint moji3 = ((moji >> 16) & 0xff);
            uint moji4 = ((moji >> 24) & 0xff);
            U.write_u8(newFontData, 4, moji2);
            U.write_u8(newFontData, 5, width);
            U.write_u8(newFontData, 6, moji3);
            U.write_u8(newFontData, 7, moji4);
            U.write_range(newFontData, 8, selectFontBitmapByte);
            return newFontData;
        }

        /// <summary>
        /// Construct a new 72-byte font data block (header + 4bpp 8x8 bitmap)
        /// suitable for appending to a font hash chain. The header layout
        /// depends on the priority code: SJIS / LAT1 / UTF8 fill different
        /// bytes.
        /// </summary>
        public static byte[] MakeNewFontData(uint moji, uint width, byte[] selectFontBitmapByte,
            ROM rom, PRIORITY_CODE priorityCode)
        {
            if (priorityCode == PRIORITY_CODE.UTF8)
            {
                return NewFontDataUTF8(moji, width, selectFontBitmapByte);
            }
            else if (rom.RomInfo.is_multibyte)
            {
                return NewFontDataSJIS(moji, width, selectFontBitmapByte);
            }
            else
            {
                if (moji > 0xff && priorityCode == PRIORITY_CODE.SJIS)
                {
                    return NewFontDataSJIS(moji, width, selectFontBitmapByte);
                }
                return NewFontDataLAT1(moji, width, selectFontBitmapByte);
            }
        }

        /// <summary>
        /// Re-encode the 72-byte font block from one priority-code layout to
        /// another. Used when porting fonts between ROMs that use different
        /// encodings (e.g. SJIS source -&gt; LAT1 destination).
        /// </summary>
        public static void TransportFontStruct(byte[] newFontData,
            uint moji,
            PRIORITY_CODE myselfPriorityCode,
            PRIORITY_CODE yourPriorityCode)
        {
            if (yourPriorityCode == myselfPriorityCode) return;

            if (myselfPriorityCode == PRIORITY_CODE.SJIS)
            {
                uint moji2 = (moji & 0xff);
                U.write_u8(newFontData, 4, moji2);
                U.write_u8(newFontData, 6, 0);
                U.write_u8(newFontData, 7, 0);
            }
            else if (myselfPriorityCode == PRIORITY_CODE.LAT1)
            {
                U.write_u8(newFontData, 4, 0);
                U.write_u8(newFontData, 6, 0);
                U.write_u8(newFontData, 7, 0);
            }
            else if (myselfPriorityCode == PRIORITY_CODE.UTF8)
            {
                uint moji2 = ((moji >> 8) & 0xff);
                uint moji3 = ((moji >> 16) & 0xff);
                uint moji4 = ((moji >> 24) & 0xff);
                U.write_u8(newFontData, 4, moji2);
                U.write_u8(newFontData, 6, moji3);
                U.write_u8(newFontData, 7, moji4);
            }
        }
    }
}
