using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Tactician affinity editor (FE7 only).
    /// WinForms: TacticianAffinityFE7 — record size 4 bytes.
    /// D0 / L_0_ATTRIBUTE = Affinity ID (u32@0).
    /// B4 / L_4_ID_PLUS1 = Index+1 display field.
    /// L_0_TEXT_NAME1 = unit name, L_2_TEXT_DETAIL3 = detail text, L_5_CLASS = class name.
    /// L_0_ATTRIBUTEICON = affinity icon, Explain = birth month / blood type description.
    /// </summary>
    public class TacticianAffinityFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _affinityId;
        byte _indexPlus1;
        string _affinityName = "";
        string _explanation = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Affinity ID (u32@0). WinForms: D0 / L_0_ATTRIBUTE.</summary>
        public uint AffinityId { get => _affinityId; set => SetField(ref _affinityId, value); }

        /// <summary>Index+1 field (u8@4). WinForms: B4 inside L_4_ID_PLUS1 panel.</summary>
        public byte IndexPlus1 { get => _indexPlus1; set => SetField(ref _indexPlus1, value); }

        /// <summary>Resolved affinity name. WinForms: L_0_ATTRIBUTE label text.</summary>
        public string AffinityName { get => _affinityName; set => SetField(ref _affinityName, value); }

        /// <summary>Birth month / blood type explanation. WinForms: Explain TextBox.</summary>
        public string Explanation { get => _explanation; set => SetField(ref _explanation, value); }

        // Legacy aliases for backward compat
        public uint D0 { get => AffinityId; set => AffinityId = value; }
        public byte B4 { get => IndexPlus1; set => IndexPlus1 = value; }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.tactician_affinity_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            int maxCount = rom.RomInfo.is_multibyte ? 48 : 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint affinityId = rom.u32(addr + 0);
                string name = U.ToHexString((uint)i) + " " + U.ToHexString(affinityId);
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            AffinityId = rom.u32(addr + 0);
            // B4 is at addr+4 but may be outside this record for some layouts
            if (addr + 5 <= (uint)rom.Data.Length)
                IndexPlus1 = (byte)rom.u8(addr + 4);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr + 0, AffinityId);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AffinityId"] = $"0x{AffinityId:X08}",
                ["IndexPlus1"] = $"0x{IndexPlus1:X02}",
                ["AffinityName"] = AffinityName,
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
            };
        }
    }
}
