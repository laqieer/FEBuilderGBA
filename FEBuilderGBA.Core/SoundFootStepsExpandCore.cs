// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform SoundFootSteps (per-class footstep-sound pointer table)
// Switch2 expansion + the FE8 PlaySoundStepByClass ASM hardcode fix.
//
// Extracted from FEBuilderGBA/SoundFootStepsForm.cs:85
// (SwitchListExpandsButton_Click) so both the legacy WinForms editor and the
// Avalonia view (#1449) share the SAME mutation — no Avalonia-side fork of the
// table-expansion + FE8-specific engine patch. The table-expansion mutation
// itself lives in the shared ItemUsagePointerCore.Switch2Expands; this helper
// adds the SoundFootSteps-only FE8 hardcode write so the engine reads the
// expanded table.
//
// Dependencies (NOT a pure function): the underlying
// ItemUsagePointerCore.Switch2Expands consumes CoreState.AppendBinaryData
// (FreeSpace allocation) + CoreState.Services (confirmation/error dialogs).
// See ItemUsagePointerCore for the full dependency contract.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Helpers for the WinForms <c>SoundFootStepsForm</c> ↔ Avalonia
    /// <c>SoundFootStepsViewerView</c> parity work (#1449). The per-class
    /// footstep-sound table is a Switch2 jump-table whose entry count derives
    /// from <c>u8(sound_foot_steps_switch2_address + 2) + 1</c>. Growing it to
    /// cover all classes (FE8 also needs an engine ASM hardcode fix so
    /// <c>PlaySoundStepByClass</c> reads the relocated table) was WinForms-only;
    /// this class hosts the shared mutation.
    /// </summary>
    public static class SoundFootStepsExpandCore
    {
        // FE8 PlaySoundStepByClass(足音) hardcode-fix patch bytes — written so
        // the engine reads the expanded/relocated footstep-sound table.
        // Verbatim from SoundFootStepsForm.SwitchListExpandsButton_Click.
        internal const uint FE8_HARDCODE_FIX_ADDR_MULTIBYTE = 0x7B198; // FE8J
        internal const uint FE8_HARDCODE_FIX_ADDR_SINGLEBYTE = 0x78D84; // FE8U
        internal static readonly byte[] FE8_HARDCODE_FIX_BYTES = new byte[] { 0x1C, 0xE0 };

        /// <summary>
        /// True when the SoundFootSteps Switch2 jump-table signature is present
        /// (i.e. the editor + List Expansion are usable). Thin wrapper over
        /// <see cref="ItemUsagePointerCore.IsSwitch2Enable"/> against the
        /// <c>sound_foot_steps_switch2_address</c> slot — mirrors WinForms
        /// <c>SoundFootStepsForm_Load</c> / <c>ReInit</c> gating.
        /// </summary>
        public static bool IsEnabled(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            return ItemUsagePointerCore.IsSwitch2Enable(
                rom, rom.RomInfo.sound_foot_steps_switch2_address);
        }

        /// <summary>
        /// Read the Switch2 metadata at <c>sound_foot_steps_switch2_address</c>
        /// and return <c>(startClassId, totalCount)</c> using the same
        /// <c>count + 1</c> convention WinForms <c>ReInit</c> uses
        /// (<c>u8(switch2 + 2) + 1</c>). Returns null when no Switch2 here.
        /// </summary>
        public static (uint Start, uint TotalCount)? ReadSwitch2(ROM rom)
        {
            if (rom?.RomInfo == null) return null;
            return ItemUsagePointerCore.ReadSwitch2(
                rom, rom.RomInfo.sound_foot_steps_switch2_address);
        }

        /// <summary>
        /// Expand the SoundFootSteps per-class footstep-sound table to
        /// <paramref name="newCount"/> entries (filling new slots with
        /// <paramref name="defaultJumpAddr"/>), then — on FE8 — apply the
        /// <c>PlaySoundStepByClass</c> ASM hardcode fix so the engine reads the
        /// relocated table. Both writes share <paramref name="undodata"/> so a
        /// single Push/Rollback covers them atomically. Mirrors WinForms
        /// <c>SoundFootStepsForm.SwitchListExpandsButton_Click</c> exactly.
        /// </summary>
        /// <returns>
        /// The new table address, or <see cref="U.NOT_FOUND"/> when the
        /// underlying Switch2 expansion failed (caller should roll back). When
        /// the expansion fails the FE8 hardcode fix is NOT written.
        /// </returns>
        public static uint Expand(
            ROM rom,
            uint newCount,
            uint defaultJumpAddr,
            Undo.UndoData undodata)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint newAddr = ItemUsagePointerCore.Switch2Expands(
                rom,
                rom.RomInfo.sound_foot_steps_pointer,
                rom.RomInfo.sound_foot_steps_switch2_address,
                newCount,
                defaultJumpAddr,
                undodata);

            if (newAddr == U.NOT_FOUND)
            {
                // Switch2Expands already surfaced the error + left ROM unchanged
                // (or its own writes are in the same undo the caller rolls back).
                return U.NOT_FOUND;
            }

            ApplyFe8HardcodeFix(rom, undodata);
            return newAddr;
        }

        /// <summary>
        /// Apply the FE8-only <c>PlaySoundStepByClass</c> ASM hardcode fix:
        /// write <c>{0x1c, 0xe0}</c> at <c>0x7B198</c> (FE8J / multibyte) or
        /// <c>0x78d84</c> (FE8U / single-byte). No-op for FE6/FE7. Public so the
        /// Core test can assert the byte writes independently of the FreeSpace
        /// allocation path. Mirrors WinForms lines 103-113.
        /// </summary>
        public static void ApplyFe8HardcodeFix(ROM rom, Undo.UndoData undodata)
        {
            if (rom?.RomInfo == null) return;
            if (rom.RomInfo.version != 8) return;

            uint addr = rom.RomInfo.is_multibyte
                ? FE8_HARDCODE_FIX_ADDR_MULTIBYTE
                : FE8_HARDCODE_FIX_ADDR_SINGLEBYTE;

            rom.write_range(addr, FE8_HARDCODE_FIX_BYTES, undodata);
        }
    }
}
