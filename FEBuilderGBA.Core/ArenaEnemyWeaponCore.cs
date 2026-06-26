// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free Core helper for the Arena Enemy Weapon editor (#1465).
    ///
    /// Ports the two-list semantics of WinForms <c>ArenaEnemyWeaponForm</c>:
    ///   - Basic weapon list:  <c>arena_enemy_weapon_basic_pointer</c>, stride 1, 8 entries.
    ///   - Rank-up weapon list: <c>arena_enemy_weapon_rankup_pointer</c>, stride 1, 0x1A (26) entries.
    ///
    /// Both tables are fixed-size single-byte item-id arrays. The per-slot label /
    /// guidance / icon-type strings are verbatim ports of WF
    /// <c>GetBasicTypeName</c> / <c>GetRankupTypeName</c> so the Avalonia editor can
    /// surface the same context the WinForms form shows.
    ///
    /// READ-ONLY: this class never mutates the ROM. The single slot byte is written
    /// by the editor's existing undo-tracked write path.
    /// </summary>
    public static class ArenaEnemyWeaponCore
    {
        /// <summary>Number of basic weapon slots (WF Init: i &lt; 8).</summary>
        public const int BasicCount = 8;

        /// <summary>Number of rank-up weapon slots (WF N_Init: i &lt; 0x1A).</summary>
        public const int RankupCount = 0x1A;

        /// <summary>
        /// Basic weapon list builder. Mirrors WF <c>Init</c>:
        /// pointer = <c>arena_enemy_weapon_basic_pointer</c>, stride 1, fixed 8 entries.
        /// Address/value reads are scoped to the passed <paramref name="rom"/>; the
        /// display NAME comes from <c>NameResolver.GetItemName</c>, which resolves item
        /// names via the ambient <c>CoreState.ROM</c> (id-keyed shared cache). The
        /// editor only opens against the active ROM, so callers must keep
        /// <c>CoreState.ROM</c> equal to <paramref name="rom"/> for correct names.
        /// </summary>
        public static List<AddrResult> BuildBasicList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint ptr = rom.RomInfo.arena_enemy_weapon_basic_pointer;
            if (ptr == 0) return result;
            // Guard the full 4-byte pointer slot before p32 (u32 throws via
            // check_safety when ptr is within the last 3 bytes of the ROM), and
            // validate the resolved base against the PASSED rom (not CoreState.ROM).
            if (ptr + 4 > (uint)rom.Data.Length) return result;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < BasicCount; i++)
            {
                uint addr = baseAddr + i * 1;
                if (addr + 1 > (uint)rom.Data.Length) break;

                uint itemid = rom.u8(addr);
                string typeName = GetBasicTypeName((int)i, out _, out _);
                string name = U.ToHexString(itemid) + " " + NameResolver.GetItemName(itemid) + " (" + typeName + ")";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Rank-up weapon list builder. Mirrors WF <c>N_Init</c>:
        /// pointer = <c>arena_enemy_weapon_rankup_pointer</c>, stride 1, fixed 0x1A (26) entries.
        /// Same name-resolution contract as <see cref="BuildBasicList"/>: address/value
        /// reads are scoped to <paramref name="rom"/>, but item NAMES resolve via the
        /// ambient <c>CoreState.ROM</c>, which must match <paramref name="rom"/>.
        /// </summary>
        public static List<AddrResult> BuildRankupList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint ptr = rom.RomInfo.arena_enemy_weapon_rankup_pointer;
            if (ptr == 0) return result;
            // Guard the full 4-byte pointer slot before p32 (u32 throws via
            // check_safety when ptr is within the last 3 bytes of the ROM), and
            // validate the resolved base against the PASSED rom (not CoreState.ROM).
            if (ptr + 4 > (uint)rom.Data.Length) return result;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < RankupCount; i++)
            {
                uint addr = baseAddr + i * 1;
                if (addr + 1 > (uint)rom.Data.Length) break;

                uint itemid = rom.u8(addr);
                string typeName = GetRankupTypeName((int)i, out _, out _);
                string name = U.ToHexString(itemid) + " " + NameResolver.GetItemName(itemid) + " (" + typeName + ")";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Per-slot label + guidance + icon-type for the basic weapon list.
        /// Verbatim port of WF <c>ArenaEnemyWeaponForm.GetBasicTypeName</c>.
        /// </summary>
        public static string GetBasicTypeName(int number, out string out_disp, out uint icontype)
        {
            if (number == 0x00)
            {
                icontype = 0;
                out_disp = R._("剣の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:剣");
            }
            if (number == 0x01)
            {
                icontype = 1;
                out_disp = R._("槍の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:槍");
            }
            if (number == 0x02)
            {
                icontype = 2;
                out_disp = R._("斧の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:斧");
            }
            if (number == 0x03)
            {
                icontype = 3;
                out_disp = R._("弓の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:弓");
            }
            if (number == 0x04)
            {
                icontype = 4;
                out_disp = R._("杖の基本となる武器です。(闘技場では利用できないので、0x00である必要があります)");
                return R._("基本武器:杖");
            }
            if (number == 0x05)
            {
                icontype = 5;
                out_disp = R._("理魔法の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:理魔法");
            }
            if (number == 0x06)
            {
                icontype = 6;
                out_disp = R._("光魔法の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:光魔法");
            }
            //if (number == 0x07)
            {
                icontype = 7;
                out_disp = R._("闇魔法の基本となる武器です。誰でも使えるEランク武器を設定してください。");
                return R._("基本武器:闇魔法");
            }
        }

        /// <summary>
        /// Per-slot label + guidance + icon-type for the rank-up weapon list.
        /// Verbatim port of WF <c>ArenaEnemyWeaponForm.GetRankupTypeName</c>
        /// (entries 0x00..0x19; 0xFF-icon rows are separators / terminator).
        /// </summary>
        public static string GetRankupTypeName(int number, out string out_disp, out uint icontype)
        {
            if (number == 0x00)
            {
                icontype = 0;
                out_disp = R._("基本武器:剣と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:剣0");
            }
            if (number == 0x01)
            {
                icontype = 0;
                out_disp = R._("中ランクの剣アイテムを設定してください。");
                return R._("ランクアップ:剣1");
            }
            if (number == 0x02)
            {
                icontype = 0;
                out_disp = R._("最大ランクの剣アイテムを設定してください。");
                return R._("ランクアップ:剣2");
            }
            if (number == 0x03)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x04)
            {
                icontype = 1;
                out_disp = R._("基本武器:槍と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:槍0");
            }
            if (number == 0x05)
            {
                icontype = 1;
                out_disp = R._("中ランクの槍アイテムを設定してください。");
                return R._("ランクアップ:槍1");
            }
            if (number == 0x06)
            {
                icontype = 1;
                out_disp = R._("最大ランクの槍アイテムを設定してください。");
                return R._("ランクアップ:槍2");
            }
            if (number == 0x07)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x08)
            {
                icontype = 2;
                out_disp = R._("基本武器:斧と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:斧0");
            }
            if (number == 0x09)
            {
                icontype = 2;
                out_disp = R._("中ランクの斧アイテムを設定してください。");
                return R._("ランクアップ:斧1");
            }
            if (number == 0x0A)
            {
                icontype = 2;
                out_disp = R._("最大ランクの斧アイテムを設定してください。");
                return R._("ランクアップ:斧2");
            }
            if (number == 0x0B)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x0C)
            {
                icontype = 3;
                out_disp = R._("基本武器:弓と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:弓0");
            }
            if (number == 0x0D)
            {
                icontype = 3;
                out_disp = R._("中ランクの弓アイテムを設定してください。");
                return R._("ランクアップ:弓1");
            }
            if (number == 0x0E)
            {
                icontype = 3;
                out_disp = R._("最大ランクの弓アイテムを設定してください。");
                return R._("ランクアップ:弓2");
            }
            if (number == 0x0F)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x10)
            {
                icontype = 5;
                out_disp = R._("基本武器:理魔法と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:理魔法0");
            }
            if (number == 0x11)
            {
                icontype = 5;
                out_disp = R._("中ランクの理アイテムを設定してください。");
                return R._("ランクアップ:理魔法1");
            }
            if (number == 0x12)
            {
                icontype = 5;
                out_disp = R._("最大ランクの理魔法アイテムを設定してください。");
                return R._("ランクアップ:理魔法2");
            }
            if (number == 0x13)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x14)
            {
                icontype = 6;
                out_disp = R._("基本武器:光魔法と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:光魔法0");
            }
            if (number == 0x15)
            {
                icontype = 6;
                out_disp = R._("最大ランクの光魔法アイテムを設定してください。");
                return R._("ランクアップ:光魔法1");
            }
            if (number == 0x16)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            if (number == 0x17)
            {
                icontype = 7;
                out_disp = R._("基本武器:闇魔法と同じ武器にしてください。そうしないと、探索に失敗します。");
                return R._("ランクアップ:闇魔法0");
            }
            if (number == 0x18)
            {
                icontype = 0xFF;
                out_disp = R._("区切りです。0x00にしてください");
                return R._("区切り 0x00");
            }
            //number == 0x19
            {
                icontype = 0xFF;
                out_disp = R._("終端です。必ず0xFFにしてください。");
                return R._("終端 0xFF");
            }
        }
    }
}
