using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7 Japanese / FE6 variant).
    /// Data: op_class_demo_pointer, datasize=32, classId at offset 15.
    /// Used by FE6 (MainFE6Form opens OPClassDemoFE7Form).
    /// </summary>
    public class OPClassDemoFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _classId;
        uint _animationType;
        uint _battleAnime;
        string _unavailableMessage = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AnimationType { get => _animationType; set => SetField(ref _animationType, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_demo_pointer;
            if (baseAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                IsLoaded = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                IsLoaded = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // datasize=32, up to 0x42 entries
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                uint classId = rom.u8(addr + 15);
                string name = U.ToHexString(classId) + " Class Demo (FE7)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ClassId = rom.u8(addr + 15);
            AnimationType = rom.u8(addr + 16);
            BattleAnime = rom.u8(addr + 17);
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AnimationType"] = $"0x{AnimationType:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
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
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
            };
        }
    }
}
