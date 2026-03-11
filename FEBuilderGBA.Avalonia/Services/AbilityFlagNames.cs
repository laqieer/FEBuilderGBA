namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Provides human-readable names for ability flag bits in unit, class, and item data.
    /// Names vary slightly by ROM version but the bit positions are consistent.
    /// </summary>
    public static class AbilityFlagNames
    {
        // Unit ability byte 1 (offset 40 in unit data)
        public static string?[] UnitAbility1 => new[]
        {
            "Mounted",        // bit 0
            "Flying",         // bit 1
            "Canto",          // bit 2
            "Steal",          // bit 3
            "Lockpick",       // bit 4
            "Dance/Play",     // bit 5
            "Summon",         // bit 6
            "Lord"            // bit 7
        };

        public static string?[] UnitAbility2 => new[]
        {
            "Female",         // bit 0
            "Boss",           // bit 1
            "Lock 1",         // bit 2
            "Lock 2",         // bit 3
            "Lock 3",         // bit 4
            "Promoted",       // bit 5
            "Supply",         // bit 6
            "Lethality"       // bit 7
        };

        public static string?[] UnitAbility3 => new[]
        {
            "Ballista",       // bit 0
            "Pick",           // bit 1
            "Unrescuable",    // bit 2
            "Uncontrollable", // bit 3
            "Bit 4",          // bit 4
            "Bit 5",          // bit 5
            "Bit 6",          // bit 6
            "Bit 7"           // bit 7
        };

        public static string?[] UnitAbility4 => new[]
        {
            "Bit 0", "Bit 1", "Bit 2", "Bit 3",
            "Bit 4", "Bit 5", "Bit 6", "Bit 7"
        };

        // Item trait flags
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

        public static string?[] ClassAbility1 => new[]
        {
            "Mounted",        // bit 0
            "Flying",         // bit 1
            "Canto",          // bit 2
            "Steal",          // bit 3
            "Lockpick",       // bit 4
            "Dance/Play",     // bit 5
            "Summon",         // bit 6
            "Lord"            // bit 7
        };

        public static string?[] ClassAbility2 => new[]
        {
            "Female Only",    // bit 0
            "Boss",           // bit 1
            "Lock 1",         // bit 2
            "Lock 2",         // bit 3
            "Lock 3",         // bit 4
            "Promoted",       // bit 5
            "Supply",         // bit 6
            "Lethality"       // bit 7
        };
    }
}
