namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Converts a raw weapon WEXP byte (0-255) to its in-game letter grade
    /// ("-", "E", "D", "C", "B", "A", "S"). The thresholds differ between FE6
    /// (Binding Blade) and FE7/FE8 (Blazing Blade / Sacred Stones) — see the
    /// version-aware overload. The WinForms `InputFormRef.GetWeaponClass(uint)`
    /// delegates to this helper so behavior stays identical across UIs.
    /// </summary>
    public static class WeaponRankUtil
    {
        /// <summary>
        /// Returns the letter grade for the given WEXP value using the default
        /// (FE7/FE8) thresholds: 0=none, 1-30=E, 31-70=D, 71-120=C, 121-180=B,
        /// 181-250=A, 251+=S. Existing callsites that don't know the ROM
        /// version stay backwards-compatible by hitting this overload.
        /// </summary>
        public static string GetRankLetter(uint value)
        {
            return GetRankLetter(value, 7);
        }

        /// <summary>
        /// Returns the letter grade for the given WEXP value, respecting the
        /// ROM-version-specific thresholds:
        /// - FE6 (romVersion=6): 1-50=E, 51-100=D, 101-150=C, 151-200=B, 201-250=A, 251+=S.
        /// - FE7/FE8 (romVersion=7 or 8 or anything else): 1-30=E, 31-70=D, 71-120=C, 121-180=B, 181-250=A, 251+=S.
        /// </summary>
        /// <param name="value">Raw WEXP byte (0-255).</param>
        /// <param name="romVersion">ROM game ID: 6=FE6 (Binding Blade), 7=FE7 (Blazing Blade), 8=FE8 (Sacred Stones).</param>
        public static string GetRankLetter(uint value, int romVersion)
        {
            if (value == 0) return "-";
            if (romVersion == 6)
            {
                // FE6 (Binding Blade) — thresholds match WinForms InputFormRef.GetWeaponClass.
                if (value <= 50) return "E";
                if (value <= 100) return "D";
                if (value <= 150) return "C";
                if (value <= 200) return "B";
                if (value <= 250) return "A";
                return "S";
            }
            // FE7 / FE8 default thresholds.
            if (value <= 30) return "E";
            if (value <= 70) return "D";
            if (value <= 120) return "C";
            if (value <= 180) return "B";
            if (value <= 250) return "A";
            return "S";
        }
    }
}
