using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusOptionViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "W4", "W6", "W8", "W10", "W12", "W14", "W16", "W18", "W20", "W22", "W24", "W26", "W28", "W30", "W32", "W34", "D36", "D40" });

        uint _currentAddr;
        bool _isLoaded;
        uint _idTextId;
        uint _nameTextId;
        uint _helpTextId;
        uint _posX;
        uint _posY;
        uint _selectionText1;
        uint _selectionText2;
        uint _columns;
        uint _rows;
        uint _defaultTextId;
        uint _yesTextId;
        uint _minValue;
        uint _maxValue;
        uint _onOffText1;
        uint _onOffText2;
        uint _defaultValue;
        uint _optionType;
        uint _iconId;
        uint _asmPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint IdTextId { get => _idTextId; set => SetField(ref _idTextId, value); }
        public uint NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }
        public uint HelpTextId { get => _helpTextId; set => SetField(ref _helpTextId, value); }
        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint SelectionText1 { get => _selectionText1; set => SetField(ref _selectionText1, value); }
        public uint SelectionText2 { get => _selectionText2; set => SetField(ref _selectionText2, value); }
        public uint Columns { get => _columns; set => SetField(ref _columns, value); }
        public uint Rows { get => _rows; set => SetField(ref _rows, value); }
        public uint DefaultTextId { get => _defaultTextId; set => SetField(ref _defaultTextId, value); }
        public uint YesTextId { get => _yesTextId; set => SetField(ref _yesTextId, value); }
        public uint MinValue { get => _minValue; set => SetField(ref _minValue, value); }
        public uint MaxValue { get => _maxValue; set => SetField(ref _maxValue, value); }
        public uint OnOffText1 { get => _onOffText1; set => SetField(ref _onOffText1, value); }
        public uint OnOffText2 { get => _onOffText2; set => SetField(ref _onOffText2, value); }
        public uint DefaultValue { get => _defaultValue; set => SetField(ref _defaultValue, value); }
        public uint OptionType { get => _optionType; set => SetField(ref _optionType, value); }
        public uint IconId { get => _iconId; set => SetField(ref _iconId, value); }
        public uint AsmPointer { get => _asmPointer; set => SetField(ref _asmPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.status_game_option_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 44;
            var result = new List<AddrResult>();
            for (int i = 0; i < 64; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Validate: ASM pointer at offset 40 must be a valid pointer
                if (!U.isPointer(rom.u32(addr + 40))) break;

                // Get option name from NameTextId at offset 4
                uint nameTextId = rom.u16(addr + 4);
                string name = nameTextId > 0 ? NameResolver.GetTextById(nameTextId) : $"Option {i}";
                result.Add(new AddrResult(addr, $"0x{i:X2} {name}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 44 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            IdTextId = values["D0"];
            NameTextId = values["W4"];
            HelpTextId = values["W6"];
            PosX = values["W8"];
            PosY = values["W10"];
            SelectionText1 = values["W12"];
            SelectionText2 = values["W14"];
            Columns = values["W16"];
            Rows = values["W18"];
            DefaultTextId = values["W20"];
            YesTextId = values["W22"];
            MinValue = values["W24"];
            MaxValue = values["W26"];
            OnOffText1 = values["W28"];
            OnOffText2 = values["W30"];
            DefaultValue = values["W32"];
            OptionType = values["W34"];
            IconId = values["D36"];
            AsmPointer = values["D40"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 44 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = IdTextId,
                ["W4"] = NameTextId,
                ["W6"] = HelpTextId,
                ["W8"] = PosX,
                ["W10"] = PosY,
                ["W12"] = SelectionText1,
                ["W14"] = SelectionText2,
                ["W16"] = Columns,
                ["W18"] = Rows,
                ["W20"] = DefaultTextId,
                ["W22"] = YesTextId,
                ["W24"] = MinValue,
                ["W26"] = MaxValue,
                ["W28"] = OnOffText1,
                ["W30"] = OnOffText2,
                ["W32"] = DefaultValue,
                ["W34"] = OptionType,
                ["D36"] = IconId,
                ["D40"] = AsmPointer,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["IdTextId"] = IdTextId.ToString("X08"),
                ["NameTextId"] = NameTextId.ToString("X04"),
                ["HelpTextId"] = HelpTextId.ToString("X04"),
                ["PosX"] = PosX.ToString("X04"),
                ["PosY"] = PosY.ToString("X04"),
                ["SelectionText1"] = SelectionText1.ToString("X04"),
                ["SelectionText2"] = SelectionText2.ToString("X04"),
                ["Columns"] = Columns.ToString("X04"),
                ["Rows"] = Rows.ToString("X04"),
                ["DefaultTextId"] = DefaultTextId.ToString("X04"),
                ["YesTextId"] = YesTextId.ToString("X04"),
                ["MinValue"] = MinValue.ToString("X04"),
                ["MaxValue"] = MaxValue.ToString("X04"),
                ["OnOffText1"] = OnOffText1.ToString("X04"),
                ["OnOffText2"] = OnOffText2.ToString("X04"),
                ["DefaultValue"] = DefaultValue.ToString("X04"),
                ["OptionType"] = OptionType.ToString("X04"),
                ["IconId"] = IconId.ToString("X08"),
                ["AsmPointer"] = AsmPointer.ToString("X08"),
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
                ["u32@0x00_IdTextId"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04_NameTextId"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06_HelpTextId"] = $"0x{rom.u16(a + 6):X04}",
                ["u16@0x08_PosX"] = $"0x{rom.u16(a + 8):X04}",
                ["u16@0x0A_PosY"] = $"0x{rom.u16(a + 10):X04}",
                ["u16@0x0C_SelectionText1"] = $"0x{rom.u16(a + 12):X04}",
                ["u16@0x0E_SelectionText2"] = $"0x{rom.u16(a + 14):X04}",
                ["u16@0x10_Columns"] = $"0x{rom.u16(a + 16):X04}",
                ["u16@0x12_Rows"] = $"0x{rom.u16(a + 18):X04}",
                ["u16@0x14_DefaultTextId"] = $"0x{rom.u16(a + 20):X04}",
                ["u16@0x16_YesTextId"] = $"0x{rom.u16(a + 22):X04}",
                ["u16@0x18_MinValue"] = $"0x{rom.u16(a + 24):X04}",
                ["u16@0x1A_MaxValue"] = $"0x{rom.u16(a + 26):X04}",
                ["u16@0x1C_OnOffText1"] = $"0x{rom.u16(a + 28):X04}",
                ["u16@0x1E_OnOffText2"] = $"0x{rom.u16(a + 30):X04}",
                ["u16@0x20_DefaultValue"] = $"0x{rom.u16(a + 32):X04}",
                ["u16@0x22_OptionType"] = $"0x{rom.u16(a + 34):X04}",
                ["u32@0x24_IconId"] = $"0x{rom.u32(a + 36):X08}",
                ["u32@0x28_AsmPointer"] = $"0x{rom.u32(a + 40):X08}",
            };
        }
    }
}
