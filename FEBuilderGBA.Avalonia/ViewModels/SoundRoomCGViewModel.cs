using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Sound room CG viewer (FE7 only).
    /// WinForms: SoundRoomCGForm — block size 4, terminated by 0xFFFFFFFF.
    /// Each entry is a CG image ID (u32).</summary>
    public class SoundRoomCGViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });
        uint _currentAddr;
        bool _isLoaded;
        uint _cgId;
        string _cgDescription = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint CgId { get => _cgId; set => SetField(ref _cgId, value); }
        public string CgDescription { get => _cgDescription; set => SetField(ref _cgDescription, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.sound_room_cg_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint cgId = rom.u32(addr);
                if (cgId == 0xFFFFFFFF) break;

                string display = $"{i:D3} CG 0x{cgId:X04}";
                result.Add(new AddrResult(addr, display, cgId));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            CgId = values["D0"];
            CgDescription = $"CG Image ID: 0x{CgId:X04}";
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["D0"] = CgId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["CgId"] = $"0x{CgId:X08}",
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
                ["CgId@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }
    }
}
