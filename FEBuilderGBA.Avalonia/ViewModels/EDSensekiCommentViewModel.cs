using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDSensekiCommentViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "D12" });

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _unitId, _conversationText1, _conversationText2, _conversationText3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ConversationText1 { get => _conversationText1; set => SetField(ref _conversationText1, value); }
        public uint ConversationText2 { get => _conversationText2; set => SetField(ref _conversationText2, value); }
        public uint ConversationText3 { get => _conversationText3; set => SetField(ref _conversationText3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.senseki_comment_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 16);
                if (addr + 16 > (uint)rom.Data.Length) break;

                if (rom.u16(addr) == 0x0) break;

                uint uid = rom.u32(addr + 0);
                string name = U.ToHexString(uid) + " Senseki Comment";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 15 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            UnitId = v["D0"];
            ConversationText1 = v["D4"];
            ConversationText2 = v["D8"];
            ConversationText3 = v["D12"];

            IsLoaded = true;
            CanWrite = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 16 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = UnitId, ["D4"] = ConversationText1,
                ["D8"] = ConversationText2, ["D12"] = ConversationText3,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X08}",
                ["ConversationText1"] = $"0x{ConversationText1:X08}",
                ["ConversationText2"] = $"0x{ConversationText2:X08}",
                ["ConversationText3"] = $"0x{ConversationText3:X08}",
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
                ["u32@0x00_UnitId"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_ConversationText1"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_ConversationText2"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_ConversationText3"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
