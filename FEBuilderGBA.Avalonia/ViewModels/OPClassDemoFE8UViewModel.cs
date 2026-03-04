using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE8U variant).
    /// Data: op_class_demo_pointer, datasize=20, classId at offset 5.
    /// Validation: u8(addr+0x0F) must be &lt;= 6.
    /// </summary>
    public class OPClassDemoFE8UViewModel : ViewModelBase, IDataVerifiable
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
            // datasize=20, validate u8(addr+0xF) <= 6
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                uint animCheck = rom.u8(addr + 0x0F);
                if (animCheck > 6) break;

                uint classId = rom.u8(addr + 5);
                string name = U.ToHexString(classId) + " Class Demo (FE8U)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ClassId = rom.u8(addr + 5);
            AnimationType = rom.u8(addr + 6);
            BattleAnime = rom.u8(addr + 7);
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
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
            };
        }
    }
}
