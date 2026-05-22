using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemWeaponTriangleViewerViewModel : ViewModelBase, IDataVerifiable
    {
        // Field map for EditorFormRef.
        // Byte 0/1 = weapon-type IDs (unsigned, range 0..255).
        // Byte 2/3 = atk/hit bonuses (SIGNED sbyte, range -128..127).
        // Mirrors WinForms `b2`/`b3` lowercase naming which `InputFormRef.RomToUI`
        // casts to sbyte. Issue #370.
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "S2", "S3" });

        uint _currentAddr;
        uint _weaponType1;
        uint _weaponType2;
        int _bonus;
        int _penalty;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint WeaponType1 { get => _weaponType1; set => SetField(ref _weaponType1, value); }
        public uint WeaponType2 { get => _weaponType2; set => SetField(ref _weaponType2, value); }
        public int Bonus { get => _bonus; set => SetField(ref _bonus, value); }
        public int Penalty { get => _penalty; set => SetField(ref _penalty, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadItemWeaponTriangleList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_cornered_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 255) break;

                uint w1 = rom.u8(addr);
                uint w2 = rom.u8(addr + 1);
                // Match WinForms `DrawWeaponTypeIcon2AndText` label format:
                //   "{weapon1Hex} {weapon1Name} -> {weapon2Hex} {weapon2Name}"
                // The prefix MUST start with the weapon-type ID (not the row index)
                // so any DrawWeaponTypeIcon-style parser that uses U.atoh(text)
                // gets the correct icon. Issue #370.
                string n1 = WeaponTypeNames.Get(w1);
                string n2 = WeaponTypeNames.Get(w2);
                string name = $"{U.ToHexString(w1)} {n1} -> {U.ToHexString(w2)} {n2}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemWeaponTriangle(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            WeaponType1 = values["B0"];
            WeaponType2 = values["B1"];
            // S2/S3 are sign-extended into uint by EditorFormRef; cast back to int
            // to preserve the negative value (e.g. 0xF1 -> 0xFFFFFFF1 -> -15).
            Bonus = (int)values["S2"];
            Penalty = (int)values["S3"];

            CanWrite = true;
        }

        public void WriteItemWeaponTriangle()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = WeaponType1,
                ["B1"] = WeaponType2,
                // EditorFormRef.WriteFields for SByte masks to byte; uint cast of
                // negative int gives sign-extended uint which the writer truncates.
                ["S2"] = unchecked((uint)Bonus),
                ["S3"] = unchecked((uint)Penalty),
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadItemWeaponTriangleList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            // For signed fields, display decimal and the byte-masked hex so the
            // report does NOT emit 32-bit sign-extended values like 0xFFFFFFF1.
            // Issue #370 (Copilot review item 2).
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["WeaponType1"] = $"0x{WeaponType1:X02}",
                ["WeaponType2"] = $"0x{WeaponType2:X02}",
                ["Bonus"] = $"{Bonus} (0x{(byte)Bonus:X02})",
                ["Penalty"] = $"{Penalty} (0x{(byte)Penalty:X02})",
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
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["WeaponType1"] = "u8@0x00",
                ["WeaponType2"] = "u8@0x01",
                ["Bonus"] = "s8@0x02",
                ["Penalty"] = "s8@0x03",
            };
        }

        /// <summary>
        /// Maps weapon-type IDs to short English names matching the WinForms
        /// `InputFormRef.GetWeaponTypeName` semantic (translated to ASCII).
        /// </summary>
        internal static class WeaponTypeNames
        {
            public static string Get(uint type) => type switch
            {
                0x00 => "Sword",
                0x01 => "Lance",
                0x02 => "Axe",
                0x03 => "Bow",
                0x04 => "Staff",
                0x05 => "Anima",
                0x06 => "Light",
                0x07 => "Dark",
                0x09 => "Item",
                0x0B => "DragonStone",
                0x0C => "Ring",
                0x11 => "FireStone",
                0x12 => "DanceRing",
                _ => $"0x{type:X02}",
            };
        }
    }
}
