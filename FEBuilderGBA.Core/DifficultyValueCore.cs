using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper for the packed u16 "Difficulty Settings" value
    /// that lives at offset 20 (W20) of each FE7/FE8 map setting struct.
    ///
    /// Mapping (parity with WinForms <c>MapSettingDifficultyForm</c>):
    /// <list type="bullet">
    ///   <item>bits 0..3   = EasyPenalty (0..15)   — Easy mode stat penalty</item>
    ///   <item>bits 4..7   = HardBoost    (0..15)   — Hard mode stat boost</item>
    ///   <item>bits 8..11  = NormalPenalty (0..15)  — Normal mode stat penalty</item>
    ///   <item>bits 12..15 = reserved (preserved on write)</item>
    /// </list>
    /// Single source of truth used by both <c>MapSettingDifficultyViewModel</c>
    /// (batch editor) and <c>MapSettingDifficultyDialogViewModel</c> (per-map
    /// dialog). Avoids the swap-bug that existed when both call-sites encoded
    /// the nibble layout independently.
    /// </summary>
    public static class DifficultyValueCore
    {
        /// <summary>
        /// Pack three nibbles into the WinForms packed difficulty word.
        /// Inputs are first clamped to <c>[0..15]</c> via <see cref="Math.Clamp(int,int,int)"/>
        /// (so negative values become 0 instead of wrapping to 15), then placed
        /// into their respective nibble slots. The upper nibble (bits 12..15)
        /// is left as zero by this helper — call <see cref="PackPreservingReserved"/>
        /// if you need to preserve the original ROM bits there.
        /// </summary>
        public static ushort Pack(int hardBoost, int normalPenalty, int easyPenalty)
        {
            // Clamp first so negative inputs don't roll over to 0xF via the `& 0xF` mask.
            int hardClamped = Math.Clamp(hardBoost, 0, 0xF);
            int normalClamped = Math.Clamp(normalPenalty, 0, 0xF);
            int easyClamped = Math.Clamp(easyPenalty, 0, 0xF);
            uint h = (uint)hardClamped;
            uint n = (uint)normalClamped;
            uint e = (uint)easyClamped;
            return (ushort)((h << 4) | (n << 8) | (e << 0));
        }

        /// <summary>
        /// Like <see cref="Pack"/> but preserves the high nibble (bits 12..15)
        /// from <paramref name="originalValue"/>. Used by Write paths so any
        /// reserved/unknown high bits already in ROM are not blanked out.
        /// </summary>
        public static ushort PackPreservingReserved(int hardBoost, int normalPenalty, int easyPenalty, ushort originalValue)
        {
            ushort packed = Pack(hardBoost, normalPenalty, easyPenalty);
            ushort reserved = (ushort)(originalValue & 0xF000);
            return (ushort)(packed | reserved);
        }

        /// <summary>
        /// Unpack a packed difficulty word into (HardBoost, NormalPenalty, EasyPenalty).
        /// </summary>
        public static (int HardBoost, int NormalPenalty, int EasyPenalty) Unpack(ushort value)
        {
            int hard = (value >> 4) & 0xF;
            int normal = (value >> 8) & 0xF;
            int easy = (value >> 0) & 0xF;
            return (hard, normal, easy);
        }

        /// <summary>
        /// Pretty-print a packed difficulty value as
        /// <c>Hard:+H Normal:-N Easy:-E</c> (matches WinForms output).
        /// </summary>
        public static string Format(ushort value)
        {
            var (h, n, e) = Unpack(value);
            return $"Hard:+{h} Normal:-{n} Easy:-{e}";
        }

        /// <summary>
        /// Returns true when the W20 packed difficulty word is meaningful for
        /// the given ROM. False for FE6 (different struct layout, no W20
        /// difficulty field) and when the map setting struct is too small to
        /// hold the word.
        /// </summary>
        public static bool IsSupported(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            if (rom.RomInfo.version == 6) return false;
            return rom.RomInfo.map_setting_datasize >= 22;
        }
    }
}
