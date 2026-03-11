using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text dictionary browser ViewModel.
    /// WinForms: TextDicForm — record size 12 bytes, three sub-lists (main, chapter, title).
    /// B0 / J_0 = Title Index (references N2_AddressList).
    /// B1 / J_1 = Chapter Index (references N1_AddressList).
    /// W2 / J_2_TEXT = Text ID 1 (with L_2_TEXT_DICNAME1 display).
    /// W4 / J_4_TEXT = Text ID 2 (with L_4_TEXT display).
    /// W6 / J_6_FLAG = Flag 1.
    /// W8 / J_8_FLAG = Flag 2.
    /// B10 / J_10 = Unit ID (with L_10_UNIT / L_10_UNITICON display).
    /// B11 / J_11 = Class ID (with L_11_CLASS / L_11_CLASSICON display).
    /// </summary>
    public class TextDicViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _searchTerm = "";
        string _selectedEntry = "";
        ObservableCollection<string> _entries = new();
        uint _titleIndex, _chapterIndex, _textId1, _textId2, _flag1, _flag2, _unitId, _classId;
        string _titleName = "";
        string _chapterName = "";
        string _text1Preview = "";
        string _text2Preview = "";
        string _unitName = "";
        string _classNameDisplay = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SearchTerm { get => _searchTerm; set => SetField(ref _searchTerm, value); }
        public string SelectedEntry { get => _selectedEntry; set => SetField(ref _selectedEntry, value); }
        public ObservableCollection<string> Entries { get => _entries; set => SetField(ref _entries, value); }

        /// <summary>Title index (u8@0). WinForms: B0 / J_0, maps to N2_AddressList.</summary>
        public uint TitleIndex { get => _titleIndex; set => SetField(ref _titleIndex, value); }

        /// <summary>Chapter index (u8@1). WinForms: B1 / J_1, maps to N1_AddressList.</summary>
        public uint ChapterIndex { get => _chapterIndex; set => SetField(ref _chapterIndex, value); }

        /// <summary>Text ID 1 (u16@2). WinForms: W2 / J_2_TEXT with L_2_TEXT_DICNAME1.</summary>
        public uint TextId1 { get => _textId1; set => SetField(ref _textId1, value); }

        /// <summary>Text ID 2 (u16@4). WinForms: W4 / J_4_TEXT with L_4_TEXT.</summary>
        public uint TextId2 { get => _textId2; set => SetField(ref _textId2, value); }

        /// <summary>Flag 1 (u16@6). WinForms: W6 / J_6_FLAG.</summary>
        public uint Flag1 { get => _flag1; set => SetField(ref _flag1, value); }

        /// <summary>Flag 2 (u16@8). WinForms: W8 / J_8_FLAG.</summary>
        public uint Flag2 { get => _flag2; set => SetField(ref _flag2, value); }

        /// <summary>Unit ID (u8@10). WinForms: B10 / J_10 with L_10_UNIT / L_10_UNITICON.</summary>
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }

        /// <summary>Class ID (u8@11). WinForms: B11 / J_11 with L_11_CLASS / L_11_CLASSICON.</summary>
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }

        /// <summary>Resolved title name from N2_AddressList.</summary>
        public string TitleName { get => _titleName; set => SetField(ref _titleName, value); }

        /// <summary>Resolved chapter name from N1_AddressList.</summary>
        public string ChapterName { get => _chapterName; set => SetField(ref _chapterName, value); }

        public string Text1Preview { get => _text1Preview; set => SetField(ref _text1Preview, value); }
        public string Text2Preview { get => _text2Preview; set => SetField(ref _text2Preview, value); }
        public string UnitName { get => _unitName; set => SetField(ref _unitName, value); }
        public string ClassNameDisplay { get => _classNameDisplay; set => SetField(ref _classNameDisplay, value); }

        // Legacy aliases
        public uint B0 { get => TitleIndex; set => TitleIndex = value; }
        public uint B1 { get => ChapterIndex; set => ChapterIndex = value; }
        public uint W2 { get => TextId1; set => TextId1 = value; }
        public uint W4 { get => TextId2; set => TextId2 = value; }
        public uint W6 { get => Flag1; set => Flag1 = value; }
        public uint W8 { get => Flag2; set => Flag2 = value; }
        public uint B10 { get => UnitId; set => UnitId = value; }
        public uint B11 { get => ClassId; set => ClassId = value; }

        /// <summary>Build the main dictionary list from dic_main_pointer.</summary>
        public List<AddrResult> BuildList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            uint pointer = rom.RomInfo.dic_main_pointer;
            if (pointer == 0) return result;

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            const uint blockSize = 12;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint textId1 = rom.u16(addr + 2);
                uint textId2 = rom.u16(addr + 4);
                if (textId1 == 0 || textId2 == 0) break;

                string text = NameResolver.GetTextById(textId1);
                string display = $"0x{i:X2} {text}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 11 >= (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            TitleIndex = rom.u8(addr + 0);
            ChapterIndex = rom.u8(addr + 1);
            TextId1 = rom.u16(addr + 2);
            TextId2 = rom.u16(addr + 4);
            Flag1 = rom.u16(addr + 6);
            Flag2 = rom.u16(addr + 8);
            UnitId = rom.u8(addr + 10);
            ClassId = rom.u8(addr + 11);

            // Resolve names
            Text1Preview = TextId1 > 0 ? NameResolver.GetTextById(TextId1) : "";
            Text2Preview = TextId2 > 0 ? NameResolver.GetTextById(TextId2) : "";
            UnitName = NameResolver.GetUnitName(UnitId);
            ClassNameDisplay = NameResolver.GetClassName(ClassId);

            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 11 >= (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr + 0, (byte)TitleIndex);
            rom.write_u8(CurrentAddr + 1, (byte)ChapterIndex);
            rom.write_u16(CurrentAddr + 2, (ushort)TextId1);
            rom.write_u16(CurrentAddr + 4, (ushort)TextId2);
            rom.write_u16(CurrentAddr + 6, (ushort)Flag1);
            rom.write_u16(CurrentAddr + 8, (ushort)Flag2);
            rom.write_u8(CurrentAddr + 10, (byte)UnitId);
            rom.write_u8(CurrentAddr + 11, (byte)ClassId);
        }

        public int GetListCount() => BuildList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TitleIndex"] = $"0x{TitleIndex:X02}",
                ["ChapterIndex"] = $"0x{ChapterIndex:X02}",
                ["TextId1"] = $"0x{TextId1:X04}",
                ["TextId2"] = $"0x{TextId2:X04}",
                ["Flag1"] = $"0x{Flag1:X04}",
                ["Flag2"] = $"0x{Flag2:X04}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
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
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
