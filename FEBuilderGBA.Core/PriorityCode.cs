// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform port of the WinForms `PatchUtil.PRIORITY_CODE` / `SearchPriorityCode`
// pair (#536). The text-encoding priority code answers "given a ROM, which encoding
// should the engine prefer when both Huffman and UnHuffman patches are available?".
// It's pure ROM analysis - no Form / UI dependency - so it sits cleanly in Core.
//
// WinForms callers (`PatchUtil.PRIORITY_CODE` / `PatchUtil.SearchPriorityCode`) continue
// to work via the alias defined in WinForms `PatchUtil.cs`, which now delegates here.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Text-encoding priority bucket. Mirrors the original WinForms
    /// `PatchUtil.PRIORITY_CODE` enum (LAT1 = English / Latin-1, SJIS =
    /// multibyte JP, UTF8 = UTF-8 patch).
    /// </summary>
    public enum PRIORITY_CODE
    {
        LAT1,
        SJIS,
        UTF8,
    }

    /// <summary>
    /// Identifies which DrawFont* patch family is installed on the supplied ROM.
    /// Mirrors the WinForms `PatchUtil.draw_font_enum` enum.
    /// </summary>
    public enum DrawFontEnum
    {
        /// <summary>No DrawFont patch installed.</summary>
        NO,
        /// <summary>DrawMultiByte patch (FE7U / FE8U Japanese text rendering).</summary>
        DrawMultiByte,
        /// <summary>DrawSingleByte patch (FE7J / FE8J English text rendering).</summary>
        DrawSingleByte,
        /// <summary>DrawUTF8 patch (FE8U UTF-8 text rendering).</summary>
        DrawUTF8,
    }

    /// <summary>
    /// ROM-only helpers for resolving the text-encoding priority code.
    /// No Form / Program.ROM dependency - the caller supplies the ROM.
    ///
    /// Used by the ToolTranslateROM Core helpers (#536) and by anything that
    /// needs to know whether to encode strings as SJIS / UTF-8 / LAT1.
    /// </summary>
    public static class PriorityCodeUtil
    {
        struct PatchSignature
        {
            public string Name;
            public string Ver;
            public uint Addr;
            public byte[] Data;
        }

        static readonly PatchSignature[] DrawFontSignatures = new PatchSignature[]
        {
            new PatchSignature{ Name="DrawSingle", Ver="FE7J", Addr=0x56e2, Data=new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
            new PatchSignature{ Name="DrawSingle", Ver="FE8J", Addr=0x40c2, Data=new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
            new PatchSignature{ Name="DrawMulti",  Ver="FE7U", Addr=0x5BD6, Data=new byte[]{0x00, 0x00, 0x00, 0x4B, 0x9F, 0x46}},
            new PatchSignature{ Name="DrawMulti",  Ver="FE8U", Addr=0x44D2, Data=new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
            new PatchSignature{ Name="DrawUTF8",   Ver="FE7U", Addr=0x5B6A, Data=new byte[]{0x00, 0x00, 0x00, 0x4B, 0x18, 0x47}},
            new PatchSignature{ Name="DrawUTF8",   Ver="FE8U", Addr=0x44D2, Data=new byte[]{0x00, 0x00, 0x00, 0x4B, 0x18, 0x47}},
        };

        /// <summary>
        /// Determine which DrawFont patch (if any) is installed on the ROM.
        /// Returns <see cref="DrawFontEnum.NO"/> when no patch matches.
        /// </summary>
        public static DrawFontEnum SearchDrawFontPatch(ROM rom)
        {
            if (rom?.RomInfo == null) return DrawFontEnum.NO;

            string version = rom.RomInfo.VersionToFilename;
            foreach (PatchSignature sig in DrawFontSignatures)
            {
                if (sig.Ver != version) continue;

                byte[] data = rom.getBinaryData(sig.Addr, sig.Data.Length);
                if (U.memcmp(sig.Data, data) != 0) continue;

                if (sig.Name == "DrawSingle") return DrawFontEnum.DrawSingleByte;
                if (sig.Name == "DrawMulti")  return DrawFontEnum.DrawMultiByte;
                if (sig.Name == "DrawUTF8")   return DrawFontEnum.DrawUTF8;
            }
            return DrawFontEnum.NO;
        }

        /// <summary>
        /// Resolve the priority code for the given ROM. Multibyte ROMs always
        /// prefer SJIS; English ROMs depend on the installed DrawFont patch
        /// (multibyte / UTF8 / fallback to LAT1).
        /// </summary>
        public static PRIORITY_CODE SearchPriorityCode(ROM rom)
        {
            if (rom == null) return PRIORITY_CODE.SJIS;
            if (rom.RomInfo == null) return PRIORITY_CODE.SJIS;
            if (rom.RomInfo.is_multibyte) return PRIORITY_CODE.SJIS;

            DrawFontEnum dfe = SearchDrawFontPatch(rom);
            if (dfe == DrawFontEnum.DrawMultiByte) return PRIORITY_CODE.SJIS;
            if (dfe == DrawFontEnum.DrawUTF8)      return PRIORITY_CODE.UTF8;
            return PRIORITY_CODE.LAT1;
        }
    }
}
