using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SupportTalkViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B2", "W4", "W6", "W8", "W10", "W12", "W14" });

        uint _currentAddr;
        uint _supportPartner1;  // B0 - Support Partner 1
        uint _supportPartner2;  // B2 - Support Partner 2
        uint _textIdC;          // W4 - C Support Text
        uint _textIdB;          // W6 - B Support Text
        uint _textIdA;          // W8 - A Support Text
        uint _songC;            // W10 - C Support Song
        uint _songB;            // W12 - B Support Song
        uint _songA;            // W14 - A Support Song
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SupportPartner1 { get => _supportPartner1; set => SetField(ref _supportPartner1, value); }
        public uint SupportPartner2 { get => _supportPartner2; set => SetField(ref _supportPartner2, value); }
        public uint TextIdC { get => _textIdC; set => SetField(ref _textIdC, value); }
        public uint TextIdB { get => _textIdB; set => SetField(ref _textIdB, value); }
        public uint TextIdA { get => _textIdA; set => SetField(ref _textIdA, value); }
        public uint SongC { get => _songC; set => SetField(ref _songC, value); }
        public uint SongB { get => _songB; set => SetField(ref _songB, value); }
        public uint SongA { get => _songA; set => SetField(ref _songA, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadSupportTalkList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Each entry is 16 bytes; stop on 0xFFFF or empty
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 16);
                if (addr + 15 >= (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, 16 * 10)) break;

                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 2);
                // uid1/uid2 are 1-based ROM-stored unit IDs (WinForms convention).
                // Use the 1-based resolver so each name matches the partner ID. (#653)
                string n1 = NameResolver.GetUnitNameByOneBasedId(uid1);
                string n2 = NameResolver.GetUnitNameByOneBasedId(uid2);
                string name = $"{U.ToHexString(i)} {n1} (0x{uid1:X02}) & {n2} (0x{uid2:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ---- #1149: decomp source-backed save-gate fields ----

        /// <summary>Snapshot of source-writable fields at load time (byte-offset keys, OrdinalIgnoreCase).</summary>
        Dictionary<string, uint> _loadedSourceFieldSnapshot = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Block size for FE8 support_talks (16 bytes).</summary>
        const uint BLOCK_SIZE = 16;

        /// <summary>0-based entry id for the decomp source-backed writer. <see cref="U.NOT_FOUND"/> when unresolvable.</summary>
        public uint CurrentEntryId => SupportUnitNavigation.GetSupportTalkEntryIdFromAddr(CoreState.ROM, CurrentAddr, BLOCK_SIZE);

        /// <summary>All source-writable scalar fields keyed by lowercase byte-offset name (b0, b2, w4..w14 for FE8 16-byte struct).</summary>
        public Dictionary<string, uint> CurrentSourceFieldMap()
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "b0",  SupportPartner1 }, { "b2",  SupportPartner2 },
                { "w4",  TextIdC }, { "w6",  TextIdB }, { "w8",  TextIdA },
                { "w10", SongC },   { "w12", SongB },   { "w14", SongA },
            };
        }

        /// <summary>Returns ONLY fields whose value differs from the load-time snapshot (#1149).</summary>
        public IReadOnlyDictionary<string, uint> BuildSourceFieldDict()
        {
            var current = CurrentSourceFieldMap();
            var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in current)
            {
                if (!_loadedSourceFieldSnapshot.TryGetValue(kv.Key, out uint baseline) || baseline != kv.Value)
                    changed[kv.Key] = kv.Value;
            }
            return changed;
        }

        /// <summary>Re-baseline the snapshot to current values after a successful source-backed write.</summary>
        public void RefreshSourceFieldSnapshot()
        {
            _loadedSourceFieldSnapshot = CurrentSourceFieldMap();
        }

        public void LoadSupportTalk(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 15 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);

            SupportPartner1 = v["B0"];
            SupportPartner2 = v["B2"];
            TextIdC = v["W4"];
            TextIdB = v["W6"];
            TextIdA = v["W8"];
            SongC = v["W10"];
            SongB = v["W12"];
            SongA = v["W14"];

            IsLoaded = true;
            RefreshSourceFieldSnapshot();
        }

        public void WriteSupportTalk()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 16 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = SupportPartner1, ["B2"] = SupportPartner2,
                ["W4"] = TextIdC, ["W6"] = TextIdB, ["W8"] = TextIdA,
                ["W10"] = SongC, ["W12"] = SongB, ["W14"] = SongA,
            };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        /// <summary>
        /// #358 — find the support-talk row whose two unit-id bytes match
        /// <paramref name="uid1"/> and <paramref name="uid2"/> in either order.
        /// FE8 layout: u8 at offset 0 and u8 at offset 2 (byte 1 is reserved).
        /// Mirrors WinForms <c>SupportTalkForm.JumpTo(unit1, unit2)</c>; the
        /// WinForms version reads u16 at +0 and +2, but for ROMs with the
        /// expected padding (byte 1 = 0) the result is the same byte value
        /// as u8 — and u8 is the correct field width for the unit ID.
        /// </summary>
        public uint? FindAddrForUnitPair(uint uid1, uint uid2)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return null;
            uint dataSize = 16;
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint first = rom.u16(addr);
                if (first == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, 16 * 10)) break;
                uint d1 = rom.u8(addr + 0);
                uint d2 = rom.u8(addr + 2);
                if ((d1 == uid1 && d2 == uid2) || (d1 == uid2 && d2 == uid1))
                    return addr;
            }
            return null;
        }

        public int GetListCount() => LoadSupportTalkList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SupportPartner1"] = $"0x{SupportPartner1:X02}",
                ["SupportPartner2"] = $"0x{SupportPartner2:X02}",
                ["TextIdC"] = $"0x{TextIdC:X04}",
                ["TextIdB"] = $"0x{TextIdB:X04}",
                ["TextIdA"] = $"0x{TextIdA:X04}",
                ["SongC"] = $"0x{SongC:X04}",
                ["SongB"] = $"0x{SongB:X04}",
                ["SongA"] = $"0x{SongA:X04}",
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
                ["SupportPartner1@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["SupportPartner2@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["TextIdC@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["TextIdB@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["TextIdA@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["SongC@0x0A"] = $"0x{rom.u16(a + 10):X04}",
                ["SongB@0x0C"] = $"0x{rom.u16(a + 12):X04}",
                ["SongA@0x0E"] = $"0x{rom.u16(a + 14):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["SupportPartner1"] = "SupportPartner1@0x00",
                ["SupportPartner2"] = "SupportPartner2@0x02",
                ["TextIdC"] = "TextIdC@0x04",
                ["TextIdB"] = "TextIdB@0x06",
                ["TextIdA"] = "TextIdA@0x08",
                ["SongC"] = "SongC@0x0A",
                ["SongB"] = "SongB@0x0C",
                ["SongA"] = "SongA@0x0E",
            };
        }
    }
}
