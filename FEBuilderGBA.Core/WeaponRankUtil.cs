namespace FEBuilderGBA.Core
{
    public static class WeaponRankUtil
    {
        /// <summary>
        /// Converts a raw weapon level value to a letter grade.
        /// FE GBA uses: 0=none, 1-30=E, 31-70=D, 71-120=C, 121-150=B, 151-180=A, 181+=S.
        /// </summary>
        public static string GetRankLetter(uint value)
        {
            if (value == 0) return "-";
            if (value < 31) return "E";
            if (value < 71) return "D";
            if (value < 121) return "C";
            if (value < 151) return "B";
            if (value < 181) return "A";
            return "S";
        }
    }
}
