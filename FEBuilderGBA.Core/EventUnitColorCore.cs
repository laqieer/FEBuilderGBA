using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core helper for the event-script <c>UNIT_COLOR</c> argument
    /// (the "Unit Color" picker). Ports the pack/unpack + friendly-label logic of
    /// WinForms <c>EventUnitColorForm</c> (FEBuilderGBA/EventUnitColorForm.cs) and
    /// <c>InputFormRef.GetUNIT_COLOR</c> (FEBuilderGBA/InputFormRef.cs).
    ///
    /// <para>
    /// UNIT_COLOR packs four colour-override slots into a single value, one nibble
    /// each (low → high):
    /// <list type="bullet">
    ///   <item>nibble 0 (bits 0..3)   — Player / 自軍</item>
    ///   <item>nibble 1 (bits 4..7)   — Enemy  / 敵軍</item>
    ///   <item>nibble 2 (bits 8..11)  — NPC    / 友軍</item>
    ///   <item>nibble 3 (bits 12..15) — Fourth / 第4軍</item>
    /// </list>
    /// Each slot value: <c>0</c>=no change, <c>1</c>=blue, <c>2</c>=red, <c>3</c>=green,
    /// <c>4</c>=sepia.
    /// </para>
    ///
    /// <para>READ-ONLY / PURE — touches no ROM, never throws.</para>
    /// </summary>
    public static class EventUnitColorCore
    {
        /// <summary>Number of colour slots packed into one UNIT_COLOR value.</summary>
        public const int SlotCount = 4;

        /// <summary>Number of selectable colours per slot (0..4 inclusive).</summary>
        public const int ColorOptionCount = 5;

        /// <summary>
        /// Pack four slot values (Player, Enemy, NPC, Fourth) into a single
        /// UNIT_COLOR value: <c>a | (b&lt;&lt;4) | (c&lt;&lt;8) | (d&lt;&lt;12)</c>.
        /// Each slot is masked to a nibble so out-of-range input cannot corrupt
        /// adjacent slots. Mirrors WinForms <c>ApplyButton_Click</c>.
        /// </summary>
        public static uint Pack(uint player, uint enemy, uint npc, uint fourth)
        {
            return (player & 0xF)
                 | ((enemy & 0xF) << 4)
                 | ((npc & 0xF) << 8)
                 | ((fourth & 0xF) << 12);
        }

        /// <summary>
        /// Unpack a UNIT_COLOR value into its four slot nibbles.
        /// Mirrors WinForms <c>JumpTo</c> — but assigns the Fourth slot from
        /// nibble 3 (<c>value&gt;&gt;12</c>), correcting the WinForms line-60 bug
        /// that seeded the Fourth combo from the NPC nibble (<c>c</c>) so the
        /// round-trip <c>Pack(Unpack(v)) == v</c> holds for every value.
        /// </summary>
        public static (uint player, uint enemy, uint npc, uint fourth) Unpack(uint value)
        {
            uint a = value & 0xF;
            uint b = (value >> 4) & 0xF;
            uint c = (value >> 8) & 0xF;
            uint d = (value >> 12) & 0xF;
            return (a, b, c, d);
        }

        /// <summary>
        /// Human-readable summary of a packed UNIT_COLOR value
        /// (e.g. "Player→Blue, Enemy→Red"). Ports
        /// <c>InputFormRef.GetUNIT_COLOR</c> verbatim; uses <c>R._</c> so the
        /// existing translate entries are reused. Returns the "no change"
        /// sentence for <c>0</c>.
        /// </summary>
        public static string GetUNIT_COLOR(uint num)
        {
            if (num == 0)
            {
                return R._("変更せずに元の色で描画する");
            }
            string ret = "";

            string color;
            color = GetUNIT_COLORSub(num & 0xF);
            if (color != "")
            {
                ret += R._("自軍を{0}に", color);
            }
            color = GetUNIT_COLORSub((num >> 4) & 0xF);
            if (color != "")
            {
                if (ret != "") ret += ",";
                ret += R._("敵軍を{0}に", color);
            }
            color = GetUNIT_COLORSub((num >> 8) & 0xF);
            if (color != "")
            {
                if (ret != "") ret += ",";
                ret += R._("友軍を{0}に", color);
            }
            color = GetUNIT_COLORSub((num >> 12) & 0xF);
            if (color != "")
            {
                if (ret != "") ret += ",";
                ret += R._("第4軍を{0}に", color);
            }
            return ret;
        }

        /// <summary>
        /// Translate a single slot value (1..4) into its colour name; 0 / unknown
        /// returns "" (treated as "no change"). Ports
        /// <c>InputFormRef.GetUNIT_COLORSub</c>.
        /// </summary>
        public static string GetUNIT_COLORSub(uint num)
        {
            switch (num)
            {
                case 0x01:
                    return R._("青");
                case 0x02:
                    return R._("赤");
                case 0x03:
                    return R._("緑");
                case 0x04:
                    return R._("セピア");
            }
            return "";
        }
    }
}
