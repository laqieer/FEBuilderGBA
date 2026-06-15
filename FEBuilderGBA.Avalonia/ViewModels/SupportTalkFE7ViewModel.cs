using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportTalkFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "D4", "D8", "D12", "B16", "B17", "B18", "B19" });

        uint _currentAddr;
        bool _isLoaded;
        uint _supportPartner1, _supportPartner2;       // B0, B1
        uint _textC, _textB, _textA;                    // D4, D8, D12 (u32)
        uint _songC, _songB, _songA, _padding;          // B16-B19

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SupportPartner1 { get => _supportPartner1; set => SetField(ref _supportPartner1, value); }
        public uint SupportPartner2 { get => _supportPartner2; set => SetField(ref _supportPartner2, value); }
        public uint TextC { get => _textC; set => SetField(ref _textC, value); }
        public uint TextB { get => _textB; set => SetField(ref _textB, value); }
        public uint TextA { get => _textA; set => SetField(ref _textA, value); }
        public uint SongC { get => _songC; set => SetField(ref _songC, value); }
        public uint SongB { get => _songB; set => SetField(ref _songB, value); }
        public uint SongA { get => _songA; set => SetField(ref _songA, value); }
        public uint Padding { get => _padding; set => SetField(ref _padding, value); }

        public List<AddrResult> LoadSupportTalkList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = 20;
            var result = new List<AddrResult>();
            int emptyCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0)
                {
                    emptyCount++;
                    if (emptyCount >= 10) break;
                    continue;
                }
                emptyCount = 0;

                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 1);
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

        /// <summary>Block size for FE7 support_talks (20 bytes).</summary>
        const uint BLOCK_SIZE = 20;

        /// <summary>0-based entry id for the decomp source-backed writer. <see cref="U.NOT_FOUND"/> when unresolvable.</summary>
        public uint CurrentEntryId => SupportUnitNavigation.GetSupportTalkEntryIdFromAddr(CoreState.ROM, CurrentAddr, BLOCK_SIZE);

        /// <summary>All source-writable scalar fields keyed by lowercase byte-offset name (b0, b1, d4, d8, d12, b16..b19 for FE7 20-byte struct).</summary>
        public Dictionary<string, uint> CurrentSourceFieldMap()
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "b0",  SupportPartner1 }, { "b1",  SupportPartner2 },
                { "d4",  TextC }, { "d8",  TextB }, { "d12", TextA },
                { "b16", SongC }, { "b17", SongB }, { "b18", SongA }, { "b19", Padding },
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

            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            SupportPartner1 = v["B0"];
            SupportPartner2 = v["B1"];
            TextC = v["D4"];
            TextB = v["D8"];
            TextA = v["D12"];
            SongC = v["B16"];
            SongB = v["B17"];
            SongA = v["B18"];
            Padding = v["B19"];

            IsLoaded = true;
            RefreshSourceFieldSnapshot();
        }

        public void WriteSupportTalk()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 20 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = SupportPartner1, ["B1"] = SupportPartner2,
                ["D4"] = TextC, ["D8"] = TextB, ["D12"] = TextA,
                ["B16"] = SongC, ["B17"] = SongB, ["B18"] = SongA, ["B19"] = Padding,
            };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        /// <summary>
        /// #358 — find the support-talk row whose two unit-id bytes match
        /// <paramref name="uid1"/> and <paramref name="uid2"/> in either order.
        /// FE7 layout: u8 at offset 0 and u8 at offset 1.
        /// Mirrors WinForms <c>SupportTalkFE7Form.JumpTo(unit1, unit2)</c>.
        /// </summary>
        public uint? FindAddrForUnitPair(uint uid1, uint uid2)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return null;
            uint dataSize = 20;
            int emptyCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint first = rom.u16(addr);
                if (first == 0)
                {
                    emptyCount++;
                    if (emptyCount >= 10) break;
                    continue;
                }
                emptyCount = 0;
                uint d1 = rom.u8(addr + 0);
                uint d2 = rom.u8(addr + 1);
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
                ["TextC"] = $"0x{TextC:X08}",
                ["TextB"] = $"0x{TextB:X08}",
                ["TextA"] = $"0x{TextA:X08}",
                ["SongC"] = $"0x{SongC:X02}",
                ["SongB"] = $"0x{SongB:X02}",
                ["SongA"] = $"0x{SongA:X02}",
                ["Padding"] = $"0x{Padding:X02}",
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
                ["SupportPartner2@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["TextC@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["TextB@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["TextA@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["SongC@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["SongB@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["SongA@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["Padding@0x13"] = $"0x{rom.u8(a + 19):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["SupportPartner1"] = "SupportPartner1@0x00",
            ["SupportPartner2"] = "SupportPartner2@0x01",
            ["TextC"] = "TextC@0x04",
            ["TextB"] = "TextB@0x08",
            ["TextA"] = "TextA@0x0C",
            ["SongC"] = "SongC@0x10",
            ["SongB"] = "SongB@0x11",
            ["SongA"] = "SongA@0x12",
            ["Padding"] = "Padding@0x13",
        };
    }
}
