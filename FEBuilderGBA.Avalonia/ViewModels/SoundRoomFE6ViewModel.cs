using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundRoomFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Sound Room (FE6)", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _bgmId;
        uint _songNameTextId;
        uint _descriptionTextId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>BGM / Song ID (D0).</summary>
        public uint BgmId { get => _bgmId; set => SetField(ref _bgmId, value); }
        /// <summary>Text ID for the song name (D4).</summary>
        public uint SongNameTextId { get => _songNameTextId; set => SetField(ref _songNameTextId, value); }
        /// <summary>Text ID for the description (D8).</summary>
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            BgmId = rom.u32(addr + 0);
            SongNameTextId = rom.u32(addr + 4);
            DescriptionTextId = rom.u32(addr + 8);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            rom.write_u32(a + 0, BgmId);
            rom.write_u32(a + 4, SongNameTextId);
            rom.write_u32(a + 8, DescriptionTextId);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BgmId"] = $"0x{BgmId:X08}",
                ["SongNameTextId"] = $"0x{SongNameTextId:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X08}",
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
                ["BgmId@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["SongNameTextId@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["DescriptionTextId@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
