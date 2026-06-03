// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform port of the PURE skill-text-ID recycle lookup from WinForms
// `SkillConfigSkillSystemForm.ConvertSkillTextIDWithRecrycle` (:841-944). This
// is a no-I/O, no-InputFormRef, no-ROM table: a fixed (skillId, textID) ->
// recycled textID map used by the SkillSystems bulk import so re-imported skill
// descriptions land on the canonical recycled text slots a FEBuilder ROM ships
// with. SLICE 2 of #923 / #885.
//
// The lookup is verbatim from WF (same constants, same order). It NEVER mutates
// the ROM and NEVER throws.

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure (skillId, textID) -> recycled-textID lookup, ported verbatim from
    /// WinForms <c>SkillConfigSkillSystemForm.ConvertSkillTextIDWithRecrycle</c>.
    /// </summary>
    public static class SkillConfigSkillTextIDRecycle
    {
        /// <summary>
        /// Map a (skillId, textID) pair to its recycled textID. Returns the input
        /// <paramref name="textID"/> unchanged when no recycle rule matches.
        /// </summary>
        public static uint Convert(uint skillId, uint textID)
        {
            //Vengeance
            if (skillId == 0x90 && textID == 0xEB1) return 0xF72;
            //Imbue
            if (skillId == 0x91 && textID == 0xEB0) return 0xF61;
            //DoubleLion
            if (skillId == 0x97 && textID == 0xEB9) return 0xF4C;
            //Shade
            if (skillId == 0x0C && textID == 0xE2B) return 0xF5F;
            //Glacies
            if (skillId == 0x32 && textID == 0xE52) return 0xF69;
            //GreatShild
            if (skillId == 0x6D && textID == 0xE8D) return 0xF70;
            //SkyBreaker
            if (skillId == 0x92 && textID == 0xEAF) return 0xF54;
            //BlueFlame
            if (skillId == 0x93 && textID == 0xEB2) return 0xF4B;
            //Gridmaster
            if (skillId == 0xF8 && textID == 0xF41) return 0xF6A;
            //Assassinate
            if (skillId == 0xF9 && textID == 0xF42) return 0xF48;
            //Corrosion
            if (skillId == 0x7D && textID == 0xE9C) return 0xF64;
            //ArcaneBalde
            if (skillId == 0x7E && textID == 0xE9D) return 0xF49;
            //KeepUp
            if (skillId == 0x79 && textID == 0xE78) return 0xF66;
            //KeepUp
            if (skillId == 0x79 && textID == 0xE9E) return 0xF66;
            //NatureRush
            if (skillId == 0xDA && textID == 0xF23) return 0xF67;
            //Resourcefull
            if (skillId == 0x58 && textID == 0xE78) return 0xF55;
            //Rightful Arch (元に戻す)
            if (skillId == 0x7F && textID == 0xF65) return 0xE9E;

            return textID;
        }
    }
}
