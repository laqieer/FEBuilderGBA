using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ArenaEnemyWeaponViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        // ---- Basic weapon list state (arena_enemy_weapon_basic_pointer, 8 entries) ----
        uint _currentAddr;
        bool _canWrite;
        uint _weaponId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint WeaponId { get => _weaponId; set => SetField(ref _weaponId, value); }

        // ---- Rank-up weapon list state (arena_enemy_weapon_rankup_pointer, 26 entries) (#1465) ----
        uint _rankupCurrentAddr;
        bool _rankupCanWrite;
        uint _rankupWeaponId;

        public uint RankupCurrentAddr { get => _rankupCurrentAddr; set => SetField(ref _rankupCurrentAddr, value); }
        public bool RankupCanWrite { get => _rankupCanWrite; set => SetField(ref _rankupCanWrite, value); }
        public uint RankupWeaponId { get => _rankupWeaponId; set => SetField(ref _rankupWeaponId, value); }

        // ---------------------------------------------------------------
        // Basic list
        // ---------------------------------------------------------------

        public List<AddrResult> LoadArenaEnemyWeaponList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return ArenaEnemyWeaponCore.BuildBasicList(rom);
        }

        public void LoadArenaEnemyWeapon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            WeaponId = values["B0"];
            CanWrite = true;
        }

        public void WriteArenaEnemyWeapon()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint> { ["B0"] = WeaponId };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        /// <summary>Per-slot label + guidance + icon-type for the basic list (WF GetBasicTypeName).</summary>
        public (string Label, string Guidance, uint IconType) GetBasicTypeInfo(int index)
        {
            string label = ArenaEnemyWeaponCore.GetBasicTypeName(index, out string disp, out uint icon);
            return (label, disp, icon);
        }

        // ---------------------------------------------------------------
        // Rank-up list (#1465)
        // ---------------------------------------------------------------

        public List<AddrResult> LoadArenaEnemyWeaponRankupList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return ArenaEnemyWeaponCore.BuildRankupList(rom);
        }

        public void LoadArenaEnemyWeaponRankup(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            RankupCurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            RankupWeaponId = values["B0"];
            RankupCanWrite = true;
        }

        public void WriteArenaEnemyWeaponRankup()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || RankupCurrentAddr == 0) return;
            uint addr = RankupCurrentAddr;
            var values = new Dictionary<string, uint> { ["B0"] = RankupWeaponId };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        /// <summary>Per-slot label + guidance + icon-type for the rank-up list (WF GetRankupTypeName).</summary>
        public (string Label, string Guidance, uint IconType) GetRankupTypeInfo(int index)
        {
            string label = ArenaEnemyWeaponCore.GetRankupTypeName(index, out string disp, out uint icon);
            return (label, disp, icon);
        }

        public int GetListCount() => LoadArenaEnemyWeaponList().Count;
        public int GetRankupListCount() => LoadArenaEnemyWeaponRankupList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["WeaponId"] = $"0x{WeaponId:X02}",
                ["rankupAddr"] = $"0x{RankupCurrentAddr:X08}",
                ["RankupWeaponId"] = $"0x{RankupWeaponId:X02}",
                ["rankupCount"] = GetRankupListCount().ToString(),
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["WeaponId"] = "u8@0x00",
        };
    }
}
