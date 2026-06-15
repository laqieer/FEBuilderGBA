using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportAttributeViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7" });

        uint _currentAddr;
        uint _affinityType;
        uint _attackBonus;
        uint _defenseBonus;
        uint _hitBonus;
        uint _avoidBonus;
        uint _critBonus;
        uint _critAvoidBonus;
        uint _unknown7;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint AffinityType { get => _affinityType; set => SetField(ref _affinityType, value); }
        public uint AttackBonus { get => _attackBonus; set => SetField(ref _attackBonus, value); }
        public uint DefenseBonus { get => _defenseBonus; set => SetField(ref _defenseBonus, value); }
        public uint HitBonus { get => _hitBonus; set => SetField(ref _hitBonus, value); }
        public uint AvoidBonus { get => _avoidBonus; set => SetField(ref _avoidBonus, value); }
        public uint CritBonus { get => _critBonus; set => SetField(ref _critBonus, value); }
        public uint CritAvoidBonus { get => _critAvoidBonus; set => SetField(ref _critAvoidBonus, value); }
        // B7: Unknown / padding
        public uint Unknown7 { get => _unknown7; set => SetField(ref _unknown7, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadSupportAttributeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_attribute_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Each entry is 8 bytes; iterate until first byte is 0
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 7 >= (uint)rom.Data.Length) break;

                uint v = rom.u8(addr);
                if (v == 0 && i > 0) break;

                string[] affinityNames = { "None", "Fire", "Thunder", "Wind", "Ice", "Dark", "Light", "Anima" };
                string affName = v < affinityNames.Length ? affinityNames[v] : $"0x{v:X02}";
                string name = $"{U.ToHexString(i + 1)} {affName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ---- #1149: decomp source-backed save-gate fields ----

        /// <summary>Snapshot of source-writable fields at load time (byte-offset keys, OrdinalIgnoreCase).</summary>
        Dictionary<string, uint> _loadedSourceFieldSnapshot = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        /// <summary>0-based entry id for the decomp source-backed writer. <see cref="U.NOT_FOUND"/> when unresolvable.</summary>
        public uint CurrentEntryId => SupportUnitNavigation.GetSupportAttributeEntryIdFromAddr(CoreState.ROM, CurrentAddr);

        /// <summary>All source-writable scalar fields keyed by lowercase byte-offset name (b0..b7 for 8-byte struct).</summary>
        public Dictionary<string, uint> CurrentSourceFieldMap()
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "b0", AffinityType }, { "b1", AttackBonus }, { "b2", DefenseBonus },
                { "b3", HitBonus }, { "b4", AvoidBonus }, { "b5", CritBonus },
                { "b6", CritAvoidBonus }, { "b7", Unknown7 },
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

        public void LoadSupportAttribute(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 7 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);

            AffinityType = values["B0"];
            AttackBonus = values["B1"];
            DefenseBonus = values["B2"];
            HitBonus = values["B3"];
            AvoidBonus = values["B4"];
            CritBonus = values["B5"];
            CritAvoidBonus = values["B6"];
            Unknown7 = values["B7"];

            CanWrite = true;
            RefreshSourceFieldSnapshot();
        }

        public void WriteSupportAttribute()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 7 >= (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = AffinityType, ["B1"] = AttackBonus, ["B2"] = DefenseBonus,
                ["B3"] = HitBonus, ["B4"] = AvoidBonus, ["B5"] = CritBonus,
                ["B6"] = CritAvoidBonus, ["B7"] = Unknown7,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadSupportAttributeList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AffinityType"] = $"0x{AffinityType:X02}",
                ["AttackBonus"] = $"0x{AttackBonus:X02}",
                ["DefenseBonus"] = $"0x{DefenseBonus:X02}",
                ["HitBonus"] = $"0x{HitBonus:X02}",
                ["AvoidBonus"] = $"0x{AvoidBonus:X02}",
                ["CritBonus"] = $"0x{CritBonus:X02}",
                ["CritAvoidBonus"] = $"0x{CritAvoidBonus:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["AffinityType"] = "u8@0x00",
                ["AttackBonus"] = "u8@0x01",
                ["DefenseBonus"] = "u8@0x02",
                ["HitBonus"] = "u8@0x03",
                ["AvoidBonus"] = "u8@0x04",
                ["CritBonus"] = "u8@0x05",
                ["CritAvoidBonus"] = "u8@0x06",
            };
        }
    }
}
