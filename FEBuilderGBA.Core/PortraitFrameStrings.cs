// SPDX-License-Identifier: GPL-3.0-or-later
namespace FEBuilderGBA
{
    /// <summary>
    /// WF portrait wizard mode strings indexed by frame number 0-10.
    /// Mirrors <c>ImagePortraitImporterForm.GenPreviewMainChar</c> (lines
    /// 261-316), which assigns <c>X_STATUS.Text</c> per frame index. Used by
    /// the Avalonia wizard's per-frame status label (#707 Slice A).
    ///
    /// Frame mapping (exact WF parity — frame 6 is intentionally different
    /// from the other mouth frames in WF):
    ///   0 = Normal (通常時)               -- default else branch
    ///   1 = Half-blink (半目)
    ///   2 = Closed eyes (とじ目)
    ///   3 = Mouth 1 (口1)
    ///   4 = Mouth 2 (口2)
    ///   5 = Mouth 3 (口3)
    ///   6 = Status screen Mouth 4 (ステータス画面 口4)  -- special WF label
    ///   7 = Mouth 5 (口5)
    ///   8 = Mouth 6 (口6)
    ///   9 = Mouth 7 (口7)
    ///   10 = Position check (位置確認用)
    /// </summary>
    public static class PortraitFrameStrings
    {
        /// <summary>
        /// Returns the WF mode string for a given frame index (0-10).
        /// Returns "?" for out-of-range frames.
        /// </summary>
        public static string GetWfModeString(int frame) => frame switch
        {
            0 => "Normal (通常時)",
            1 => "Half-blink (半目)",
            2 => "Closed eyes (とじ目)",
            3 => "Mouth 1 (口1)",
            4 => "Mouth 2 (口2)",
            5 => "Mouth 3 (口3)",
            // WF parity: GenPreviewMainChar sets frame 6 to "ステータス画面 口4"
            // (Status screen Mouth 4), NOT plain "口4". Keep the special
            // label so the Avalonia wizard matches WF byte-for-byte.
            6 => "Status screen Mouth 4 (ステータス画面 口4)",
            7 => "Mouth 5 (口5)",
            8 => "Mouth 6 (口6)",
            9 => "Mouth 7 (口7)",
            10 => "Position check (位置確認用)",
            _ => "?"
        };
    }
}
