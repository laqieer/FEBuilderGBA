using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitCustomBattleAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "W2" });

        uint _currentAddr;
        bool _isLoaded;
        uint _weaponType;
        uint _special;
        uint _animeNumber;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        public uint Special { get => _special; set => SetField(ref _special, value); }
        public uint AnimeNumber { get => _animeNumber; set => SetField(ref _animeNumber, value); }

        /// <summary>Build list from a given base address (set via JumpTo/NavigateTo).
        /// Each entry is 4 bytes: WeaponType (u8), Special (u8), AnimeNumber (u16).
        /// Terminated when u32(addr) == 0.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            _baseAddr = baseAddr;
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null || baseAddr == 0) return result;

            const uint blockSize = 4;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0) break;

                uint wt = rom.u8(addr);
                uint animeNum = rom.u16(addr + 2);
                result.Add(new AddrResult(addr, $"0x{i:X2} Weapon={wt:X2} Anim={animeNum:X4}", (uint)i));
            }
            return result;
        }
        uint _baseAddr;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // If navigated to with a base address, use that
            if (_baseAddr != 0) return BuildList(_baseAddr);

            // Otherwise try unit_custom_battle_anime_pointer
            uint pointer = rom.RomInfo.unit_custom_battle_anime_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();
            return BuildList(baseAddr);
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;
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

            var values = new Dictionary<string, uint>
            {
                ["B0"] = WeaponType,
                ["B1"] = Special,
                ["W2"] = AnimeNumber,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

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
    }
}
