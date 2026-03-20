namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Provides human-readable names for ability flag bits in unit, class, and item data.
    /// Labels are sourced from config/data/unitclass_checkbox_FE{6,7,8}.en.txt.
    /// The config format is {byte}{bit}=Label where byte=1-4 and bit=1-8 map to
    /// bit 0 (0x01) through bit 7 (0x80).
    /// Unit and class share the same ability flag definitions; bytes 1-2 are identical
    /// across all ROM versions, while bytes 3-4 differ between FE6, FE7, and FE8.
    /// </summary>
    public static class AbilityFlagNames
    {
        // --- Ability byte 1 (same for all versions, unit and class) ---
        // Config: 01=Mounted Aid Calc, 02=Canto, 03=Steal, 04=Thief Skill,
        //         05=Dance (Dancer), 06=Play (Bard), 07=Critical Boost, 08=Ballista Access
        public static readonly string?[] Ability1 = new[]
        {
            "Mounted Aid Calc",  // bit 0 (0x01) — sets mounted rescue calculation
            "Canto",             // bit 1 (0x02) — move after action
            "Steal",             // bit 2 (0x04) — steal ability
            "Thief Skill",       // bit 3 (0x08) — lockpick + fog vision
            "Dance (Dancer)",    // bit 4 (0x10) — dance command
            "Play (Bard)",       // bit 5 (0x20) — play command
            "Critical Boost",    // bit 6 (0x40) — +15/30% crit
            "Ballista Access"    // bit 7 (0x80) — use ballistae
        };

        // --- Ability byte 2 (same for all versions, unit and class) ---
        // Config: 11=Promoted, 12=Supply, 13=Cavalry Icon, 14=Wyvern Icon,
        //         15=Pegasus Icon, 16=Lord, 17=Female, 18=Boss
        public static readonly string?[] Ability2 = new[]
        {
            "Promoted",          // bit 0 (0x01) — promoted class
            "Supply",            // bit 1 (0x02) — transport unit
            "Cavalry Icon",      // bit 2 (0x04) — cavalry rescue icon
            "Wyvern Icon",       // bit 3 (0x08) — wyvern rescue icon
            "Pegasus Icon",      // bit 4 (0x10) — pegasus rescue icon
            "Lord",              // bit 5 (0x20) — lord unit
            "Female",            // bit 6 (0x40) — female status
            "Boss"               // bit 7 (0x80) — boss marker
        };

        // --- Ability byte 3 (version-specific) ---

        // FE6 byte 3: 21=Roy Lock, 22=Myrmidon/SM, 23=Manakete, 24=Zephiel Lock,
        //             25=Disable Select, 26=Tri Attack 1, 27=Tri Attack 2, 28=NPC
        public static readonly string?[] Ability3_FE6 = new[]
        {
            "Roy Lock",              // bit 0 (0x01)
            "Myrmidon/Swordmaster",  // bit 1 (0x02)
            "Manakete Lock",         // bit 2 (0x04)
            "Zephiel Lock",          // bit 3 (0x08)
            "Disable Unit Select",   // bit 4 (0x10)
            "Triangle Attack 1",     // bit 5 (0x20)
            "Triangle Attack 2",     // bit 6 (0x40)
            "NPC"                    // bit 7 (0x80)
        };

        // FE7 byte 3: 21=Weapon Lock 1, 22=Myrmidon/SM, 23=Manakete, 24=Morphs,
        //             25=Disable Select, 26=Tri Attack 1, 27=Tri Attack 2, 28=Usage prohibited
        public static readonly string?[] Ability3_FE7 = new[]
        {
            "Weapon Lock 1",         // bit 0 (0x01)
            "Myrmidon/Swordmaster",  // bit 1 (0x02)
            "Manakete Lock",         // bit 2 (0x04)
            "Morphs",                // bit 3 (0x08)
            "Disable Unit Select",   // bit 4 (0x10)
            "Triangle Attack 1",     // bit 5 (0x20)
            "Triangle Attack 2",     // bit 6 (0x40)
            "Usage prohibited"       // bit 7 (0x80)
        };

        // FE8 byte 3: 21=Weapon Lock 1, 22=Myrmidon/SM, 23=Monster Weapons, 24=Max Level 10,
        //             25=Disable Select, 26=Tri Attack 1, 27=Tri Attack 2, 28=NPC
        public static readonly string?[] Ability3_FE8 = new[]
        {
            "Weapon Lock 1",         // bit 0 (0x01)
            "Myrmidon/Swordmaster",  // bit 1 (0x02)
            "Monster Weapons",       // bit 2 (0x04)
            "Max Level 10",          // bit 3 (0x08)
            "Disable Unit Select",   // bit 4 (0x10)
            "Triangle Attack 1",     // bit 5 (0x20)
            "Triangle Attack 2",     // bit 6 (0x40)
            "NPC"                    // bit 7 (0x80)
        };

        // --- Ability byte 4 (version-specific) ---

        // FE6 byte 4: 31=Disable Exp Gain, 32-38=??
        public static readonly string?[] Ability4_FE6 = new[]
        {
            "Disable Exp Gain",  // bit 0 (0x01)
            "Bit 1",             // bit 1 (0x02)
            "Bit 2",             // bit 2 (0x04)
            "Bit 3",             // bit 3 (0x08)
            "Bit 4",             // bit 4 (0x10)
            "Bit 5",             // bit 5 (0x20)
            "Bit 6",             // bit 6 (0x40)
            "Bit 7"              // bit 7 (0x80)
        };

        // FE7 byte 4: 31=Disable Exp Gain, 32=Silencer/Lethality, 33=Magic Seal,
        //             34=Droppable Item, 35=Eliwood Lock, 36=Hector Lock, 37=Lyn Lock, 38=Athos Lock
        public static readonly string?[] Ability4_FE7 = new[]
        {
            "Disable Exp Gain",      // bit 0 (0x01)
            "Silencer/Lethality",    // bit 1 (0x02)
            "Magic Seal",            // bit 2 (0x04)
            "Droppable Item",        // bit 3 (0x08)
            "Eliwood Lock",          // bit 4 (0x10)
            "Hector Lock",           // bit 5 (0x20)
            "Lyndis Lock",           // bit 6 (0x40)
            "Athos Lock"             // bit 7 (0x80)
        };

        // FE8 byte 4: 31=Do Not Grant Exp, 32=Silencer/Lethality, 33=Magic Seal,
        //             34=Summoning, 35=Eirika Lock, 36=Ephraim Lock, 37=Lyn Lock (Unused), 38=Athos Lock (Unused)
        public static readonly string?[] Ability4_FE8 = new[]
        {
            "Do Not Grant Exp",      // bit 0 (0x01)
            "Silencer/Lethality",    // bit 1 (0x02)
            "Magic Seal",            // bit 2 (0x04)
            "Summoning",             // bit 3 (0x08)
            "Eirika Lock",           // bit 4 (0x10)
            "Ephraim Lock",          // bit 5 (0x20)
            "Lyn Lock (Unused)",     // bit 6 (0x40)
            "Athos Lock (Unused)"    // bit 7 (0x80)
        };

        /// <summary>
        /// Get ability flag names for a specific byte index and ROM version.
        /// Bytes 1-2 are the same across all versions. Bytes 3-4 vary by version.
        /// </summary>
        /// <param name="version">ROM version: 6=FE6, 7=FE7, 8=FE8</param>
        /// <param name="byteIndex">1-based byte index (1-4)</param>
        public static string?[] GetAbilityNames(int version, int byteIndex)
        {
            return byteIndex switch
            {
                1 => Ability1,
                2 => Ability2,
                3 => version switch
                {
                    6 => Ability3_FE6,
                    7 => Ability3_FE7,
                    _ => Ability3_FE8,
                },
                4 => version switch
                {
                    6 => Ability4_FE6,
                    7 => Ability4_FE7,
                    _ => Ability4_FE8,
                },
                _ => Ability1,
            };
        }

        // --- Backward compatibility aliases (used by Unit and Class editors) ---
        // These default to FE8 for bytes 3-4, which is the most common version.
        public static string?[] UnitAbility1 => Ability1;
        public static string?[] UnitAbility2 => Ability2;
        public static string?[] UnitAbility3 => Ability3_FE8;
        public static string?[] UnitAbility4 => Ability4_FE8;

        public static string?[] ClassAbility1 => Ability1;
        public static string?[] ClassAbility2 => Ability2;

        // Item trait flags (unchanged — these are correct as documented)
        public static string?[] ItemTrait1 => new[]
        {
            "Weapon",         // bit 0
            "Magic",          // bit 1
            "Staff",          // bit 2
            "Unbreakable",    // bit 3
            "Unsellable",     // bit 4
            "Brave",          // bit 5
            "Magic Damage",   // bit 6
            "Uncounterable"   // bit 7
        };

        public static string?[] ItemTrait2 => new[]
        {
            "Reverse Tri.",   // bit 0
            "Hammerne",       // bit 1
            "Lock 1",         // bit 2
            "Lock 2",         // bit 3
            "Lock 3",         // bit 4
            "Dragon",         // bit 5
            "Monster",        // bit 6
            "Bit 7"           // bit 7
        };
    }
}
