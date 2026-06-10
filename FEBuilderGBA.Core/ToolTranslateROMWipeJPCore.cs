// SPDX-License-Identifier: GPL-3.0-or-later
//
// #1029 — Cross-platform Core port of the three WinForms JP-font-wipe helpers
// used by the ROM Translation Tool's "Override JP Font" flow:
//   * ToolTranslateROMWipeJPFont.cs        -> WipeJPFontHelper
//   * ToolTranslateROMWipeJPChapterName.cs -> WipeJPChapterNameHelper
//   * ToolTranslateROMWipeJPClassReelFont.cs -> WipeJPClassReelFontHelper
//
// These are ROM-MUTATING when run, so every write threads the caller's
// Undo.UndoData (the WF AddJPFonts cleared the item/text font tables WITHOUT undo;
// this port routes those write_fill clears through the undo-aware overload so they
// roll back). The pointer rewrites and the recycle-pool WriteBackFont allocations
// also go through undo / RecycleAddress. Nothing mutates the ROM outside undo.
//
// WinForms-free swaps:
//   FontForm.GetFontPointer/FindFontData/MakeNewFontData -> FontCore.*
//   OptionForm.textencoding()                            -> CoreState.TextEncoding
//   PatchUtil.SearchPriorityCode()                       -> PriorityCodeUtil.SearchPriorityCode(rom)
//   U.ConvertMojiCharToUnit                              -> ToolTranslateROMCore.ConvertMojiCharToUnit
//   ImageChapterTitleForm.MakeList()                     -> ChapterTitleCore.MakeList(rom)
//   OPClassFontForm/FE8UForm.MakeList()                  -> OPClassFontListCore.MakeList(rom)
//   HowDoYouLikePatchForm.CheckAndShowPopupDialog(ChapterNameText)
//                                                        -> injected Func<bool> precondition
//   Program.ROM                                          -> passed ROM rom
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core port of WinForms <c>ToolTranslateROMWipeJPFont</c>.
    /// Wipes the FE8J Japanese font hash tables (freeing ~100 KB), preserving a
    /// small set of always-keep glyphs (digits, item-name symbols, etc.) which
    /// are re-allocated from the optimized recycle pool by
    /// <see cref="WriteBackFont"/>.
    /// </summary>
    public sealed class WipeJPFontHelper
    {
        /// <summary>A glyph that must survive the wipe and be re-appended afterward.</summary>
        sealed class KeepFont
        {
            public bool IsItemFont;
            public string Moji;
            public uint MojiCode;
            public uint Width;
            public byte[] Data;
            public uint RewritePointer;
        }

        readonly List<KeepFont> KeepFontList = new List<KeepFont>();
        readonly ROM Rom;
        readonly PRIORITY_CODE PriorityCode;
        readonly Undo.UndoData UndoData;

        // The two font hash-pointer table regions cleared by write_fill (in OFFSET
        // form). WriteBackFont's hash-head splice lands inside these regions; the
        // fill already snapshotted the ORIGINAL bytes there, so re-recording the
        // splice would double-cover the address and (because the undo system
        // applies records forward / last-wins) clobber the original on rollback.
        // We therefore write splices that fall inside a filled region WITHOUT undo
        // — the fill's record is the single source of truth for those bytes,
        // keeping rollback byte-identical (#1029 finding-5 safety).
        readonly List<(uint start, uint end)> ClearedRanges = new List<(uint, uint)>();

        public WipeJPFontHelper(ROM rom, Undo.UndoData undoData)
        {
            this.Rom = rom;
            this.UndoData = undoData;
            this.PriorityCode = PriorityCodeUtil.SearchPriorityCode(rom);
        }

        bool InClearedRange(uint addr)
        {
            foreach (var (start, end) in ClearedRanges)
                if (addr >= start && addr < end) return true;
            return false;
        }

        // Write a 4-byte pointer; thread undo only when the address is NOT already
        // covered by a fill snapshot (see ClearedRanges note above).
        void WritePointer(uint addr, uint pointer)
        {
            if (InClearedRange(addr))
                Rom.write_u32(addr, pointer);           // fill snapshot covers it
            else
                Rom.write_u32(addr, pointer, UndoData); // record the original
        }

        void AddKeepFont(bool isItemFont, uint moji, uint rewritePointer = U.NOT_FOUND)
        {
            uint topaddress = FontCore.GetFontPointer(isItemFont, Rom);
            uint fontaddress = FontCore.FindFontData(topaddress, moji, out _, Rom, PriorityCode);
            if (fontaddress == U.NOT_FOUND) return;

            var kf = new KeepFont
            {
                IsItemFont = isItemFont,
                Moji = "Code" + U.To0xHexString(moji),
                MojiCode = moji,
                Width = Rom.u8(fontaddress + 5),
                Data = Rom.getBinaryData(fontaddress + 8, 64),
                RewritePointer = rewritePointer,
            };
            KeepFontList.Add(kf);
        }

        void AddKeepFont(bool isItemFont, string one, uint rewritePointer = U.NOT_FOUND)
        {
            uint moji = ToolTranslateROMCore.ConvertMojiCharToUnit(one, PriorityCode);
            uint topaddress = FontCore.GetFontPointer(isItemFont, Rom);
            uint fontaddress = FontCore.FindFontData(topaddress, moji, out _, Rom, PriorityCode);
            if (fontaddress == U.NOT_FOUND) return;

            var kf = new KeepFont
            {
                IsItemFont = isItemFont,
                Moji = one,
                MojiCode = moji,
                Width = Rom.u8(fontaddress + 5),
                Data = Rom.getBinaryData(fontaddress + 8, 64),
                RewritePointer = rewritePointer,
            };
            KeepFontList.Add(kf);
        }

        /// <summary>
        /// Build the recycle list of wipeable JP-font regions and clear the two
        /// font hash-pointer tables. FE8J-only (multibyte, version 8, not ZH_TBL);
        /// no-op otherwise. The two <c>write_fill</c> font-table clears thread the
        /// caller's undo so they roll back (the WF original did NOT — #1029 fix).
        /// </summary>
        public void AddJPFonts(List<Address> list)
        {
            if (Rom?.RomInfo == null) return;
            if (!Rom.RomInfo.is_multibyte)
            {
                // English ROM — irrelevant.
                return;
            }
            if (CoreState.TextEncoding == TextEncodingEnum.ZH_TBL)
            {
                // The ZH font system is different — can't wipe it this way.
                // (Avalonia/CLI leave TextEncoding at Auto, so this guard only
                // fires when the WinForms user explicitly chose the ZH TBL.)
                return;
            }
            if (Rom.RomInfo.version != 8)
            {
                // FE8 only.
                return;
            }

            // Register the glyphs we must NOT lose.
            MakeKeepFontFE8J();

            // Existing text-font table region.
            const uint textFontStart = 0x5942F4;
            const uint textFontEnd = 0x5B8CDC;
            SearchCustomFonts(false, textFontEnd, list);
            Address.AddAddress(list, textFontStart, textFontEnd - textFontStart,
                U.NOT_FOUND, "TextFont Wipe", Address.DataTypeEnum.BIN);

            // Existing item-font table region.
            const uint itemFontStart = 0x579CCC;
            const uint itemFontEnd = 0x593ECC;
            SearchCustomFonts(true, itemFontEnd, list);
            Address.AddAddress(list, itemFontStart, itemFontEnd - itemFontStart,
                U.NOT_FOUND, "ItemFont Wipe", Address.DataTypeEnum.BIN);

            // Clear the two font hash-pointer tables (896 bytes each) — WITH undo.
            // Resolve the table heads from RomInfo (via FontCore.GetFontPointer)
            // rather than hard-coding the FE8J offsets, so the version-specific
            // addresses stay centralized. For FE8J these are font_item_address
            // (0x57994C) and font_serif_address (0x593F74). (Copilot review #1101.)
            uint itemTable = U.toOffset(FontCore.GetFontPointer(true, Rom));
            uint textTable = U.toOffset(FontCore.GetFontPointer(false, Rom));
            const uint TableBytes = 896;
            Rom.write_fill(itemTable, TableBytes, 0, UndoData);
            ClearedRanges.Add((itemTable, itemTable + TableBytes));
            Rom.write_fill(textTable, TableBytes, 0, UndoData);
            ClearedRanges.Add((textTable, textTable + TableBytes));
        }

        void SearchCustomFonts(bool isItemFont, uint ignoreEnd, List<Address> list)
        {
            uint topaddress = FontCore.GetFontPointer(isItemFont, Rom);

            for (uint moji1 = 0x1f; moji1 <= 0xff; moji1++)
            {
                // Move to the hash-list head pointer.
                uint fontlist = topaddress + (moji1 << 2) - 0x100;
                if (!U.isSafetyOffset(fontlist, Rom)) continue;

                uint p = Rom.p32(fontlist);
                if (!U.isSafetyOffset(p, Rom)) continue;
                uint before_pointer = fontlist;

                // struct{ void* next; byte moji2; byte width; byte nazo1; byte nazo2; } // 8
                //   + 64-byte 4bpp bitmap
                while (p > 0)
                {
                    if (p >= ignoreEnd)
                    {
                        Address.AddAddress(list, p, 8 + 64, before_pointer,
                            "WipeJP Font", Address.DataTypeEnum.BIN);
                    }

                    uint next = Rom.u32(p);
                    if (next == 0) break;                       // list terminator
                    if (!U.isSafetyPointer(next, Rom)) break;   // corrupt list

                    before_pointer = p;
                    p = U.toOffset(next);                       // next node
                }
            }
        }

        void MakeKeepFontFE8J()
        {
            AddKeepFont(false, "０");
            AddKeepFont(false, "１");
            AddKeepFont(false, "２");
            AddKeepFont(false, "３");
            AddKeepFont(false, "４");
            AddKeepFont(false, "５");
            AddKeepFont(false, "６");
            AddKeepFont(false, "７");
            AddKeepFont(false, "８");
            AddKeepFont(false, "９");
            AddKeepFont(false, 0x7a81); // heart
            AddKeepFont(true, 0x7a81);  // heart
            AddKeepFont(true, "０", 0x593ECC);
            AddKeepFont(true, "１", 0x593ED0);
            AddKeepFont(true, "２", 0x593ED4);
            AddKeepFont(true, "３", 0x593ED8);
            AddKeepFont(true, "４", 0x593EDC);
            AddKeepFont(true, "５", 0x593EE0);
            AddKeepFont(true, "６", 0x593EE4);
            AddKeepFont(true, "７", 0x593EE8);
            AddKeepFont(true, "８", 0x593EEC);
            AddKeepFont(true, "９", 0x593EF0);
            AddKeepFont(true, "ⅹ", 0x593EF4);
            AddKeepFont(true, "ⅰ", 0x593EF8);
            AddKeepFont(true, "ⅱ", 0x593EFC);
            AddKeepFont(true, "ⅲ", 0x593F00);
            AddKeepFont(true, "ⅳ", 0x593F04);
            AddKeepFont(true, "ⅴ", 0x593F08);
            AddKeepFont(true, "ⅵ", 0x593F0C);
            AddKeepFont(true, "ⅶ", 0x593F10);
            AddKeepFont(true, "ⅷ", 0x593F14);
            AddKeepFont(true, "ⅸ", 0x593F18);
            AddKeepFont(true, "ー", 0x593F1C);
            AddKeepFont(true, "＋", 0x593F20);
            AddKeepFont(true, "／", 0x593F24);
            AddKeepFont(true, "～", 0x593F28);
            AddKeepFont(true, "Ｓ", 0x593F2C);
            AddKeepFont(true, "Ａ", 0x593F30);
            AddKeepFont(true, "Ｂ", 0x593F34);
            AddKeepFont(true, "Ｃ", 0x593F38);
            AddKeepFont(true, "Ｄ", 0x593F3C);
            AddKeepFont(true, "Ｅ", 0x593F40);
            AddKeepFont(true, "Ｇ", 0x593F44);
            AddKeepFont(true, "ε", 0x593F48);
            AddKeepFont(true, "：", 0x593F4C);
            AddKeepFont(true, "．", 0x593F50);
            AddKeepFont(true, "Ｈ", 0x593F54);
            AddKeepFont(true, "Ｐ", 0x593F58);
            AddKeepFont(true, "＃", 0x593F5C);
            AddKeepFont(true, "＊", 0x593F60);
            AddKeepFont(true, "→", 0x593F64);
            AddKeepFont(true, "⊆", 0x593F68);
            AddKeepFont(true, "⊇", 0x593F6C);
            AddKeepFont(true, "％", 0x593F70);
        }

        void WriteBackFontKF(KeepFont kf, RecycleAddress ra)
        {
            uint topaddress = FontCore.GetFontPointer(kf.IsItemFont, Rom);

            uint fontaddr = FontCore.FindFontData(topaddress, kf.MojiCode, out uint prevaddr, Rom, PriorityCode);
            if (fontaddr != U.NOT_FOUND) return; // already present
            if (prevaddr == U.NOT_FOUND) return; // can't append

            byte[] newFontData = FontCore.MakeNewFontData(kf.MojiCode, kf.Width, kf.Data, Rom, PriorityCode);
            U.write_u32(newFontData, 0, 0); // NULL — appending to the list tail

            uint newaddr = ra.Write(newFontData, UndoData);
            if (newaddr == U.NOT_FOUND) return; // allocation error

            // Splice into the hash chain: point the previous node at the new tail.
            // WritePointer threads undo only when the target wasn't already
            // captured by a fill snapshot (keeps rollback byte-identical).
            WritePointer(prevaddr + 0, U.toPointer(newaddr));

            if (kf.RewritePointer != U.NOT_FOUND)
            {
                WritePointer(kf.RewritePointer, U.toPointer(newaddr));
            }
        }

        /// <summary>
        /// Re-append every preserved glyph, allocating each from the OPTIMIZED
        /// recycle pool. MUST be called after <c>recycle.RecycleOptimize()</c>.
        /// </summary>
        public void WriteBackFont(RecycleAddress ra)
        {
            foreach (KeepFont kf in KeepFontList)
            {
                WriteBackFontKF(kf, ra);
            }
        }
    }

    /// <summary>
    /// Cross-platform Core port of WinForms <c>ToolTranslateROMWipeJPChapterName</c>.
    /// Repoints every chapter-title image pointer (except the last) at the last
    /// entry's image and zeros the chapter Number / Title pointers, freeing those
    /// LZ77 images for recycling. FE8-only; gated by an injected precondition that
    /// mirrors WF <c>CheckAndShowPopupDialog(ChapterNameText)</c> (skip when the
    /// ChapterNameToText patch is absent / the user declines).
    /// </summary>
    public sealed class WipeJPChapterNameHelper
    {
        readonly ROM Rom;
        readonly Undo.UndoData UndoData;

        public WipeJPChapterNameHelper(ROM rom, Undo.UndoData undoData)
        {
            this.Rom = rom;
            this.UndoData = undoData;
        }

        /// <summary>
        /// Wipe the JP chapter-name images. <paramref name="chapterNameTextPrecondition"/>
        /// gates the wipe (true ⇒ proceed, false ⇒ skip) — pass a delegate that
        /// surfaces the ChapterNameToText patch recommendation; null defaults to a
        /// headless <see cref="PatchDetection.SearchChapterNameToTextPatch"/> check
        /// (skip when the patch is absent, matching WF).
        /// </summary>
        public void Wipe(List<Address> list, Func<bool> chapterNameTextPrecondition = null)
        {
            if (Rom?.RomInfo == null) return;
            if (Rom.RomInfo.version != 8) return;

            // Mirror WF CheckAndShowPopupDialog(ChapterNameText): only wipe when the
            // patch is present / freshly installed. Default = headless patch check.
            bool ok = chapterNameTextPrecondition != null
                ? chapterNameTextPrecondition()
                : PatchDetection.SearchChapterNameToTextPatch(Rom);
            if (!ok) return;

            // Keep the last entry; wipe everything else.
            List<AddrResult> alist = ChapterTitleCore.MakeList(Rom);
            if (alist.Count <= 1) return;

            uint addr = alist[alist.Count - 1].addr;
            if (!U.isSafetyOffset(addr, Rom)) return;
            uint lastChapterNameImageAddr = Rom.u32(addr + 0);

            for (int i = 0; i < alist.Count; i++)
            {
                addr = alist[i].addr;
                uint a = Rom.u32(addr + 0);

                if (a != lastChapterNameImageAddr)
                {
                    Address.AddLZ77Pointer(list, addr + 0, "Chapter_Save",
                        false, Address.DataTypeEnum.LZ77IMG);
                    Rom.write_u32(addr + 0, lastChapterNameImageAddr, UndoData);
                }
                Address.AddLZ77Pointer(list, addr + 4, "Chapter_Number",
                    false, Address.DataTypeEnum.LZ77IMG);
                Address.AddLZ77Pointer(list, addr + 8, "Chapter_Title",
                    false, Address.DataTypeEnum.LZ77IMG);

                Rom.write_u32(addr + 4, 0, UndoData);
                Rom.write_u32(addr + 8, 0, UndoData);
            }
        }
    }

    /// <summary>
    /// Cross-platform Core port of WinForms <c>ToolTranslateROMWipeJPClassReelFont</c>.
    /// Repoints every OP-class JP-name font slot (except the first) at the first
    /// slot's image, freeing those LZ77 images for recycling. FE8J-only (version 8,
    /// multibyte, and the OP-class-reel font code signature present).
    /// </summary>
    public sealed class WipeJPClassReelFontHelper
    {
        readonly ROM Rom;
        readonly Undo.UndoData UndoData;

        public WipeJPClassReelFontHelper(ROM rom, Undo.UndoData undoData)
        {
            this.Rom = rom;
            this.UndoData = undoData;
        }

        public void Wipe(List<Address> list)
        {
            if (Rom?.RomInfo == null) return;
            if (Rom.RomInfo.version != 8) return;
            if (!Rom.RomInfo.is_multibyte) return;

            // The OP-class-reel font code must match the FE8J signature; otherwise
            // the table layout is unknown and wiping it is unsafe. isSafetyOffset
            // only checks addr < Data.Length, not addr+2, so guard the 16-bit read
            // explicitly to keep this truly no-throw. (Copilot review #1101.)
            if (!U.isSafetyOffset(0xB7890, Rom) || 0xB7890 + 2 > (uint)Rom.Data.Length) return;
            if (Rom.u16(0xB7890) != 0x4B00) return;

            // Keep the first entry; wipe everything else.
            List<AddrResult> alist = OPClassFontListCore.MakeList(Rom);
            if (alist.Count <= 1) return;

            uint addr = alist[0].addr;
            if (!U.isSafetyOffset(addr, Rom)) return;
            uint firstJpFontImageAddr = Rom.u32(addr + 0);

            for (int i = 0; i < alist.Count; i++)
            {
                addr = alist[i].addr;
                uint a = Rom.u32(addr + 0);

                if (a != firstJpFontImageAddr)
                {
                    Address.AddLZ77Pointer(list, addr + 0,
                        "OPClassFont " + U.To0xHexString(i),
                        false, Address.DataTypeEnum.LZ77IMG);
                    Rom.write_u32(addr + 0, firstJpFontImageAddr, UndoData);
                }
            }
        }
    }
}
