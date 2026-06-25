using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia Custom Battle Animation editor.
    ///
    /// FE7-only TWO-level pointer-table navigation (parity with WinForms
    /// <c>UnitCustomBattleAnimeForm</c> — the source of truth):
    /// <list type="number">
    ///   <item><b>Pointer table</b> (WinForms N2 IFR): base = <c>p32(unit_custom_battle_anime_pointer)</c>,
    ///   stride 4, rule <c>i==0 || isPointer(u32)</c>. Each row is a u32 <i>pointer</i> to a
    ///   per-class weapon-anime list — NOT a record. One entry per class.</item>
    ///   <item><b>Inner weapon-anime list</b> (WinForms main IFR): on selecting a table slot,
    ///   <c>inner = p32(slot)</c> (the SECOND dereference), stride 4, terminate when
    ///   <c>u32==0</c>, fields u8 B0 / u8 B1 / u16 W2. Read/Write happens ONLY on these rows.</item>
    /// </list>
    ///
    /// #1412 RELEASE-BLOCKER fix: the previous flat-list implementation did a single
    /// <c>p32(pointer)</c> and treated each pointer-table slot as a 4-byte weapon-anime record,
    /// writing B0/B1/W2 back over the pointer slots and silently corrupting the FE7 per-class
    /// custom-battle-anime pointer table. FE6/FE8 short-circuit safely (pointer == 0).
    /// </summary>
    public class UnitCustomBattleAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "W2" });

        // ---- Per-record edit state (an INNER weapon-anime row) ----
        uint _currentAddr;
        bool _isLoaded;
        uint _weaponType;
        uint _special;
        uint _animeNumber;

        // ---- Inner-list window (for the defensive write guard) ----
        uint _innerBase;   // p32 of the currently selected pointer-table slot
        int _innerCount;   // number of weapon-anime records in the inner list

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        public uint Special { get => _special; set => SetField(ref _special, value); }
        public uint AnimeNumber { get => _animeNumber; set => SetField(ref _animeNumber, value); }

        /// <summary>Base of the inner weapon-anime list currently shown (p32 of the selected slot).</summary>
        public uint InnerBase => _innerBase;
        /// <summary>Number of records in the inner weapon-anime list currently shown.</summary>
        public int InnerCount => _innerCount;

        // ===================================================================
        // Level 1 — the POINTER TABLE (top list)
        // ===================================================================

        /// <summary>
        /// Build the top list = the FE7 custom-battle-anime POINTER TABLE.
        /// base = <c>p32(unit_custom_battle_anime_pointer)</c>; stride 4; rule
        /// <c>i==0 || isPointer(u32(addr))</c> (WinForms N2_Init). Each <see cref="AddrResult.addr"/>
        /// is a pointer-table SLOT (base + i*4); <see cref="AddrResult.tag"/> is the class index.
        /// Returns empty for FE6/FE8 (pointer == 0) — the safe short-circuit.
        /// </summary>
        public List<AddrResult> LoadPointerTable()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            uint pointer = rom.RomInfo.unit_custom_battle_anime_pointer;
            if (pointer == 0) return result;

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            const uint blockSize = 4;
            for (int i = 0; i < 256; i++)
            {
                uint slot = baseAddr + (uint)(i * blockSize);
                if (slot + blockSize > (uint)rom.Data.Length) break;

                // WinForms N2 rule: i==0 ? true : isPointer(u32(addr)).
                if (i != 0 && !U.isPointer(rom.u32(slot))) break;

                // WinForms label: the unit name + lower/upper-class marker for this class index
                // (UnitFE7Form.GetNameWhereCustomBattleAnime, ported into NameResolver — keeps the
                // unit-table scan out of this VM so the per-record raw report stays the source of truth).
                string label = NameResolver.GetCustomBattleAnimeName(rom, (uint)i);
                string name = label.Length == 0 ? $"0x{i:X2}" : $"0x{i:X2} {label}";
                result.Add(new AddrResult(slot, name, (uint)i));
            }
            return result;
        }

        // ===================================================================
        // Level 2 — the INNER weapon-anime list (drilled in from a slot)
        // ===================================================================

        /// <summary>
        /// Build the inner weapon-anime list for a selected pointer-table SLOT.
        /// <c>inner = p32(slotAddr)</c> (the SECOND dereference); stride 4; terminate when
        /// <c>u32==0</c> (WinForms main Init rule); fields u8 B0 / u8 B1 / u16 W2.
        /// Records the inner window so <see cref="WriteEntry"/> can refuse to write a table slot.
        /// </summary>
        public List<AddrResult> LoadInnerList(uint slotAddr)
        {
            var result = new List<AddrResult>();
            _innerBase = 0;
            _innerCount = 0;

            ROM rom = CoreState.ROM;
            if (rom == null) return result;
            if (slotAddr == 0) return result;
            if (slotAddr + 4 > (uint)rom.Data.Length) return result;

            uint inner = rom.p32(slotAddr);
            if (!U.isSafetyOffset(inner, rom)) return result;
            _innerBase = inner;

            const uint blockSize = 4;
            for (int i = 0; i < 256; i++)
            {
                uint addr = inner + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0) break; // "00まで検索" terminator

                uint wt = rom.u8(addr);
                uint animeNum = rom.u16(addr + 2);
                result.Add(new AddrResult(addr, $"0x{i:X2} Weapon={wt:X2} Anim={animeNum:X4}", (uint)i));
            }
            _innerCount = result.Count;
            return result;
        }

        /// <summary>True when <paramref name="addr"/> is a record inside the current inner list window.</summary>
        public bool IsInnerAddress(uint addr)
        {
            if (_innerBase == 0 || _innerCount == 0) return false;
            if (addr < _innerBase) return false;
            uint end = _innerBase + (uint)(_innerCount * 4);
            return addr + 4 <= end && (addr - _innerBase) % 4 == 0;
        }

        // ===================================================================
        // Per-record read / write (INNER rows only)
        // ===================================================================

        public void LoadEntry(uint addr)
        {
            IsLoaded = false;
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;
            // Defensive: only an inner weapon-anime record may be loaded into the editor.
            if (!IsInnerAddress(addr)) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            WeaponType = values["B0"];
            Special = values["B1"];
            AnimeNumber = values["W2"];
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 4 > (uint)rom.Data.Length) return;
            // RELEASE-BLOCKER guard (#1412): NEVER write over a pointer-table slot.
            // Only inner weapon-anime records are writable.
            if (!IsInnerAddress(addr)) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = WeaponType,
                ["B1"] = Special,
                ["W2"] = AnimeNumber,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        /// <summary>
        /// <see cref="IDataVerifiable"/> contract: the top-level list count = the pointer-table row
        /// count (one per class). Matches the canonical
        /// <c>ListParityHelper.BuildUnitCustomBattleAnimeList</c> shape — the top list IS the list.
        /// </summary>
        public int GetListCount() => LoadPointerTable().Count;

        /// <summary>
        /// Resolve the pointer-table SLOT that owns an inner weapon-anime <paramref name="innerAddr"/>:
        /// the slot whose <c>p32</c> base..base+count*4 range contains it. Returns 0 if none.
        /// Drives <c>NavigateTo</c> for inner-row jumps that land under a class other than the
        /// currently selected one. Deterministic: returns the FIRST matching slot (shared inner lists
        /// resolve to their first owner).
        /// </summary>
        public uint FindOwningSlot(uint innerAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || innerAddr == 0) return 0;

            foreach (var slotRow in LoadPointerTable())
            {
                uint inner = rom.p32(slotRow.addr);
                if (!U.isSafetyOffset(inner, rom)) continue;
                // Count the inner records for this slot.
                int count = 0;
                for (int j = 0; j < 256; j++)
                {
                    uint a = inner + (uint)(j * 4);
                    if (a + 4 > (uint)rom.Data.Length) break;
                    if (rom.u32(a) == 0) break;
                    count++;
                }
                uint end = inner + (uint)(count * 4);
                if (innerAddr >= inner && innerAddr + 4 <= end && (innerAddr - inner) % 4 == 0)
                    return slotRow.addr;
            }
            return 0;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0_WeaponType"] = $"0x{WeaponType:X02}",
                ["B1_Special"] = $"0x{Special:X02}",
                ["W2_AnimeNumber"] = $"0x{AnimeNumber:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_WeaponType"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Special"] = $"0x{rom.u8(a + 1):X02}",
                ["u16@0x02_AnimeNumber"] = $"0x{rom.u16(a + 2):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["B0_WeaponType"] = "u8@0x00_WeaponType",
            ["B1_Special"] = "u8@0x01_Special",
            ["W2_AnimeNumber"] = "u16@0x02_AnimeNumber",
        };
    }
}
