// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform item-usage-pointer table enumeration + Switch2 expansion.
//
// Extracted from FEBuilderGBA/ItemUsagePointerForm.cs + PatchUtil.Switch2Expands
// so both the legacy WinForms editor and the new Avalonia view (#440 / #374)
// call into the same Core surface — no AV-side fork of the switch2 metadata
// read or the array-expansion ROM mutation.
//
// All methods are pure functions of a passed-in <see cref="ROM"/> instance
// (plus an <see cref="Undo.UndoData"/> for the mutating overload). The
// FreeSpace allocator is consumed through <see cref="CoreState.AppendBinaryData"/>
// which the WinForms host wires to InputFormRef.AppendBinaryData at startup.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Helpers for the WinForms `ItemUsagePointerForm` ↔ Avalonia
    /// `ItemUsagePointerViewerView` parity work (#440). Both sides
    /// expose 10 array kinds (usability check, effect-on-use, promotion1/2,
    /// staff1/2, statbooster1/2, errormessage, name_article); this class
    /// holds the shared dispatch table + the Switch2 expansion mutation
    /// that previously lived only in WinForms `PatchUtil.Switch2Expands`.
    /// </summary>
    public static class ItemUsagePointerCore
    {
        /// <summary>
        /// One of the 10 array-kind filters the WinForms FilterComboBox
        /// surfaces. Order MUST match the WinForms `FilterComboBox.Items`
        /// AddRange order in <c>ItemUsagePointerForm.Designer.cs</c> because
        /// the integer cast is stored in user-visible UI state.
        /// </summary>
        public enum FilterKind
        {
            Usability = 0,
            Effect = 1,
            Promotion1 = 2,
            Promotion2 = 3,
            Staff1 = 4,
            Staff2 = 5,
            StatBooster1 = 6,
            StatBooster2 = 7,
            ErrorMessage = 8,
            NameArticle = 9,
        }

        /// <summary>
        /// Static metadata about one array kind — paired pointer slot
        /// + switch2 metadata slot in the ROM. Both names are the
        /// `ROMFEINFO.<name>_pointer` / `<name>_switch2_address` lookups.
        /// </summary>
        public readonly struct FilterMetadata
        {
            public readonly FilterKind Kind;
            /// <summary>Display label (i18n key — translated via R._).</summary>
            public readonly string Label;
            /// <summary>Config-data filename prefix (e.g. <c>item_usability_array_</c>).</summary>
            public readonly string ConfigPrefix;

            public FilterMetadata(FilterKind kind, string label, string configPrefix)
            {
                Kind = kind;
                Label = label;
                ConfigPrefix = configPrefix;
            }
        }

        /// <summary>
        /// All 10 metadata rows in display order. Returned as a defensive
        /// copy each call so callers cannot mutate the static state.
        /// </summary>
        public static IReadOnlyList<FilterMetadata> GetAllFilters()
        {
            return new[]
            {
                new FilterMetadata(FilterKind.Usability,    "0=Usability check",                  "item_usability_array_"),
                new FilterMetadata(FilterKind.Effect,       "1=Effect-on-use",                    "item_effect_array_"),
                new FilterMetadata(FilterKind.Promotion1,   "2=Promotion (use)",                  "item_promotion1_array_"),
                new FilterMetadata(FilterKind.Promotion2,   "3=Promotion (check, FE7)",           "item_promotion2_array_"),
                new FilterMetadata(FilterKind.Staff1,       "4=Target selection (staff)",         "item_staff1_array_"),
                new FilterMetadata(FilterKind.Staff2,       "5=Staff effect",                     "item_staff2_array_"),
                new FilterMetadata(FilterKind.StatBooster1, "6=Stat Booster message",             "item_statbooster1_array_"),
                new FilterMetadata(FilterKind.StatBooster2, "7=Stat Booster + CC check",          "item_statbooster2_array_"),
                new FilterMetadata(FilterKind.ErrorMessage, "8=Use error message",                "item_errormessage_array_"),
                new FilterMetadata(FilterKind.NameArticle,  "9=Item name article (A/An/The)",     "item_name_article_"),
            };
        }

        /// <summary>
        /// Pointer slot in ROMFEINFO for the given filter. Mirrors
        /// WinForms <c>ItemUsagePointerForm.ReInit</c> switch-case.
        /// </summary>
        public static uint GetPointerSlot(ROM rom, FilterKind kind)
        {
            if (rom?.RomInfo == null) return 0;
            var info = rom.RomInfo;
            return kind switch
            {
                FilterKind.Usability    => info.item_usability_array_pointer,
                FilterKind.Effect       => info.item_effect_array_pointer,
                FilterKind.Promotion1   => info.item_promotion1_array_pointer,
                FilterKind.Promotion2   => info.item_promotion2_array_pointer,
                FilterKind.Staff1       => info.item_staff1_array_pointer,
                FilterKind.Staff2       => info.item_staff2_array_pointer,
                FilterKind.StatBooster1 => info.item_statbooster1_array_pointer,
                FilterKind.StatBooster2 => info.item_statbooster2_array_pointer,
                FilterKind.ErrorMessage => info.item_errormessage_array_pointer,
                FilterKind.NameArticle  => info.item_name_article_pointer,
                _ => 0,
            };
        }

        /// <summary>
        /// Switch2 metadata address in ROMFEINFO for the given filter.
        /// Layout at this address (per <c>PatchUtil.Switch2Expands</c>):
        ///   +0  start-item-id (u8 — `sub r0, #start`)
        ///   +1  sub opcode (must be 0x38..0x3D)
        ///   +2  count - 1 (u8 — `cmp r0, #count-1`) [may be +4 when extra
        ///       LDR is inserted by old compilers; see ExtraByte logic]
        ///   +3  cmp opcode (must be 0x28..0x2D)
        /// </summary>
        public static uint GetSwitchSlot(ROM rom, FilterKind kind)
        {
            if (rom?.RomInfo == null) return 0;
            var info = rom.RomInfo;
            return kind switch
            {
                FilterKind.Usability    => info.item_usability_array_switch2_address,
                FilterKind.Effect       => info.item_effect_array_switch2_address,
                FilterKind.Promotion1   => info.item_promotion1_array_switch2_address,
                FilterKind.Promotion2   => info.item_promotion2_array_switch2_address,
                FilterKind.Staff1       => info.item_staff1_array_switch2_address,
                FilterKind.Staff2       => info.item_staff2_array_switch2_address,
                FilterKind.StatBooster1 => info.item_statbooster1_array_switch2_address,
                FilterKind.StatBooster2 => info.item_statbooster2_array_switch2_address,
                FilterKind.ErrorMessage => info.item_errormessage_array_switch2_address,
                FilterKind.NameArticle  => info.item_name_article_switch2_address,
                _ => 0,
            };
        }

        /// <summary>
        /// Byte-pattern check for the Switch2 ASM signature at
        /// <paramref name="switchAddr"/>. Returns true when both the SUB
        /// (offset+1) and CMP (offset+3, with optional LDR slip) opcodes
        /// are present — mirrors WinForms <c>PatchUtil.IsSwitch2Enable</c>.
        /// </summary>
        public static bool IsSwitch2Enable(ROM rom, uint switchAddr)
        {
            if (rom == null) return false;
            if (switchAddr == 0) return false;
            if (!U.isSafetyOffset(switchAddr + 6, rom)) return false;

            // Some old compilers slip an LDR r2,[sp,#0x0] between the SUB
            // and the CMP — mirrors the WinForms ExtraByte handling.
            uint extraByte = 0;
            if (rom.u16(switchAddr + 2) == 0x9A00)
            {
                extraByte = 2;
            }

            uint subOp = rom.u8(switchAddr + 1);
            if (subOp < 0x38 || subOp > 0x3D) return false;

            uint cmpOp = rom.u8(switchAddr + 3 + extraByte);
            if (cmpOp < 0x28 || cmpOp > 0x2D) return false;

            return true;
        }

        /// <summary>
        /// Read the Switch2 metadata at <paramref name="switchAddr"/>
        /// and return <c>(start, count + 1)</c> — the same convention
        /// WinForms `ItemUsagePointerForm.ReInit` uses to call
        /// `ifr.ReInitPointer(pointer, count + 1)`. Returns null when
        /// the byte pattern is invalid (no Switch2 here).
        /// </summary>
        public static (uint Start, uint TotalCount)? ReadSwitch2(ROM rom, uint switchAddr)
        {
            if (!IsSwitch2Enable(rom, switchAddr)) return null;

            // WinForms `ItemUsagePointerForm.ReInit` reads the count at the
            // unadjusted `+2` offset (then calls `ReInitPointer(pointer,
            // count + 1)`). We mirror that read here so the list population
            // matches WF exactly. The LDR-slip variant (`+2 + extraByte`)
            // only matters for the ROM-mutating expansion path; that case
            // is handled inline in `Switch2Expands` rather than via a
            // separate overload, so callers consume this single helper for
            // both purposes.
            uint start = rom.u8(switchAddr + 0);
            uint countMinusOne = rom.u8(switchAddr + 2);
            uint totalCount = countMinusOne + 1u;
            return (start, totalCount);
        }

        /// <summary>
        /// Build the pointer-list rows for the given filter, mirroring
        /// WinForms `ItemUsagePointerForm.Init.callback` text formatting.
        /// Returns an empty list when the Switch2 metadata is absent
        /// (this version of FE doesn't support that array) or when
        /// the base pointer is unsafe.
        ///
        /// CRITICAL: uses the switch2 `count + 1` semantics — does NOT
        /// stop at first NULL/non-pointer entry. A NULL pointer between
        /// two valid entries stays in the list as a "Func=0x00000000" row.
        /// </summary>
        public static List<AddrResult> MakeRows(ROM rom, FilterKind kind)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint pointerSlot = GetPointerSlot(rom, kind);
            if (pointerSlot == 0) return result;
            // Make sure the pointer-slot itself is safe to dereference.
            if (!U.isSafetyOffset(pointerSlot, rom)) return result;

            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            uint switchAddr = GetSwitchSlot(rom, kind);
            var s2 = ReadSwitch2(rom, switchAddr);
            if (s2 == null) return result;

            uint startItemId = s2.Value.Start;
            uint totalCount = s2.Value.TotalCount;

            for (uint i = 0; i < totalCount; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint funcPtr = rom.u32(addr);
                uint itemId = startItemId + i;
                string name = U.ToHexString(itemId) + " Func=0x" + funcPtr.ToString("X08");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Expand the Switch2 array at <paramref name="switch2Addr"/> to
        /// <paramref name="newCount"/> entries, filling new entries with
        /// <paramref name="defaultJumpAddr"/>. Mirrors WinForms
        /// <c>PatchUtil.Switch2Expands</c> exactly — same opcode validation,
        /// same FreeSpace allocation path, same ROM byte writes.
        ///
        /// User-confirmation prompts are routed through
        /// <see cref="CoreState.Services"/> so Avalonia / headless callers
        /// can substitute their own UI seam. When `Services` is null we
        /// proceed without confirmation (test / batch-mode path).
        ///
        /// Returns the newly-allocated table address on success,
        /// <c>U.NOT_FOUND</c> on any validation / allocation failure.
        /// </summary>
        public static uint Switch2Expands(
            ROM rom,
            uint arrayPointer,
            uint switch2Addr,
            uint newCount,
            uint defaultJumpAddr,
            Undo.UndoData undodata)
        {
            if (rom == null) return U.NOT_FOUND;
            if (!IsSwitch2Enable(rom, switch2Addr))
            {
                // Caller should have gated on IsSwitch2Enable already.
                R.ShowStopError("Switch2 is not present at this address.");
                return U.NOT_FOUND;
            }
            if (CoreState.AppendBinaryData == null)
            {
                R.ShowStopError("CoreState.AppendBinaryData is not wired — "
                    + "cannot allocate free space for the new Switch2 table.");
                return U.NOT_FOUND;
            }

            uint pointeraddr = rom.p32(arrayPointer);

            uint extraByte = 0;
            if (rom.u16(switch2Addr + 2) == 0x9A00)
            {
                extraByte = 2;
            }

            uint start = rom.u8(switch2Addr + 0);
            uint count = rom.u8(switch2Addr + 2 + extraByte) + 1u;
            if (newCount <= start + count)
            {
                R.ShowStopError(
                    "Already large enough.\r\nrequested:{0} existing:{1}+{2}={3}",
                    U.To0xHexString(newCount), U.To0xHexString(start),
                    U.To0xHexString(count), U.To0xHexString(start + count));
                return U.NOT_FOUND;
            }

            // Opcode validation (defensive — IsSwitch2Enable already checked).
            uint op = rom.u8(switch2Addr + 1);
            if (op < 0x38 || op > 0x3D)
            {
                R.ShowStopError("Opcode rewritten by another patch — cannot expand.\r\nAddress:{0} Opcode:{1}",
                    U.To0xHexString(switch2Addr + 1), U.To0xHexString(op));
                return U.NOT_FOUND;
            }
            op = rom.u8(switch2Addr + 3 + extraByte);
            if (op < 0x28 || op > 0x2D)
            {
                R.ShowStopError("Opcode rewritten by another patch — cannot expand.\r\nAddress:{0} Opcode:{1}",
                    U.To0xHexString(switch2Addr + 3), U.To0xHexString(op));
                return U.NOT_FOUND;
            }

            // User confirmation (skip silently if no Services bound — tests).
            if (CoreState.Services != null)
            {
                string prompt = string.Format("Expand the array to {0}? This writes to ROM.",
                    U.To0xHexString(newCount));
                if (!CoreState.Services.ShowYesNo(prompt))
                {
                    return U.NOT_FOUND;
                }
            }

            byte[] dd = rom.getBinaryData(pointeraddr, count * 4);
            byte[] d = new byte[(newCount + 1) * 4];
            for (uint i = 0; i < start; i++)
            {
                U.write_p32(d, i * 4, defaultJumpAddr);
            }
            Array.Copy(dd, 0, d, start * 4, count * 4);
            for (uint i = start + count; i < newCount; i++)
            {
                U.write_p32(d, i * 4, defaultJumpAddr);
            }

            uint newaddr = CoreState.AppendBinaryData(d, undodata);
            if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;

            rom.write_p32(arrayPointer, newaddr, undodata);
            rom.write_u8(switch2Addr + 0, 0, undodata);
            rom.write_u8(switch2Addr + 2 + extraByte, newCount - 1u, undodata);

            return newaddr;
        }
    }
}
