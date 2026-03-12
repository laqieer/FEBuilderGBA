using System.Collections.Generic;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Sound room viewer for FE6.
    /// WinForms: SoundRoomFE6Form — block size = sound_room_datasize, terminated by 0xFFFFFFFF.
    /// Fields: BgmId (D0), SongNameTextId (D4), DescriptionTextId (D8).</summary>
    public class SoundRoomFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8" });
        uint _currentAddr;
        bool _isLoaded;
        uint _bgmId;
        uint _songNameTextId;
        uint _descriptionTextId;
        string _songNamePreview = "";
        string _descriptionPreview = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint BgmId { get => _bgmId; set => SetField(ref _bgmId, value); }
        public uint SongNameTextId { get => _songNameTextId; set => SetField(ref _songNameTextId, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public string SongNamePreview { get => _songNamePreview; set => SetField(ref _songNamePreview, value); }
        public string DescriptionPreview { get => _descriptionPreview; set => SetField(ref _descriptionPreview, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.sound_room_pointer;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (pointer == 0 || dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0xFFFFFFFF) break;
                // Check for empty block after 10 entries
                if (i > 10 && IsEmptyBlock(rom, addr, dataSize * 10)) break;

                string songName = TryDecodeSongName(rom, addr);
                string display = $"{(i + 1):D3} {songName}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        static bool IsEmptyBlock(ROM rom, uint addr, uint length)
        {
            uint end = addr + length;
            if (end > (uint)rom.Data.Length) end = (uint)rom.Data.Length;
            for (uint a = addr; a < end; a++)
            {
                if (rom.Data[a] != 0x00 && rom.Data[a] != 0xFF) return false;
            }
            return true;
        }

        static string TryDecodeSongName(ROM rom, uint addr)
        {
            try
            {
                // SongNameTextId is at offset 4 for most versions
                uint textId = rom.u32(addr + 4);
                if (textId > 0 && textId < 0xFFFF)
                    return NameResolver.GetTextById(textId);
            }
            catch { }
            return $"Song {rom.u32(addr):X}";
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            BgmId = values["D0"];
            SongNameTextId = values["D4"];
            DescriptionTextId = values["D8"];

            SongNamePreview = SongNameTextId > 0 && SongNameTextId < 0xFFFF
                ? NameResolver.GetTextById(SongNameTextId) : "";
            DescriptionPreview = DescriptionTextId > 0 && DescriptionTextId < 0xFFFF
                ? NameResolver.GetTextById(DescriptionTextId) : "";

            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["D0"] = BgmId, ["D4"] = SongNameTextId, ["D8"] = DescriptionTextId,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BgmId"] = $"0x{BgmId:X08}",
                ["SongNameTextId"] = $"0x{SongNameTextId:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X08}",
                ["SongName"] = SongNamePreview,
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
